using System.Text.RegularExpressions;
using Xunit;

namespace SixtarGateMod.Tests;

/// <summary>소스 규칙을 테스트로 고정한다(2026-11-22: 빈 catch 12곳을 정리한 뒤 다시 늘지 않게).</summary>
public class SourceHygieneTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "mod")) && Directory.Exists(Path.Combine(dir.FullName, "tests"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("저장소 루트를 찾지 못함");
    }

    private static IEnumerable<string> ModSources()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        return Directory.EnumerateFiles(Path.Combine(RepoRoot(), "mod"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep));
    }

    // 빈 catch는 (1) 예외 필터로 좁히거나 (2) 본문에 이유 주석을 넣거나 (3) 같은 줄 끝에 이유 주석을 달아야 한다.
    private static readonly Regex EmptyCatch = new(@"catch\s*(\(\s*Exception\s*\))?\s*\{\s*\}(?!\s*//)", RegexOptions.Compiled);

    [Fact]
    public void NoUndocumentedEmptyCatchBlocks()
    {
        var offenders = new List<string>();
        foreach (var file in ModSources())
        {
            string text = File.ReadAllText(file);
            foreach (Match m in EmptyCatch.Matches(text))
            {
                int line = text.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{line}");
            }
        }
        Assert.True(offenders.Count == 0, "이유 없는 빈 catch: " + string.Join(", ", offenders));
    }

    [Fact]
    public void VersionIsTheSameInCsprojAndMelonInfo()
    {
        string root = RepoRoot();
        string csproj = File.ReadAllText(Path.Combine(root, "mod", "SixtarGateMod.csproj"));
        string core = File.ReadAllText(Path.Combine(root, "mod", "Core.cs"));
        string v1 = Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        string v2 = Regex.Match(core, "MelonInfo\\(typeof\\(SixtarGateMod\\.Core\\), \"SixtarGateMod\", \"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(v1);
        Assert.Equal(v1, v2);
    }

    // AGENTS.md에 기록된 IL2CPP/MelonLoader 금지 패턴이 코드에 다시 들어오지 않게 고정한다.
    private static readonly (string Name, Regex Pattern)[] ForbiddenPatterns =
    {
        ("AccessTools.TypeByName (ReflectionTypeLoadException 경고 유발)", new Regex(@"\bAccessTools\.TypeByName\s*\(", RegexOptions.Compiled)),
        ("UnityEngine.Mathf (il2cpp_runtime_invoke + boxing 유발, MathF/Math 사용)", new Regex(@"\bMathf\.", RegexOptions.Compiled)),
        ("new Color(...) (il2cpp_runtime_invoke 유발, Overlay.Rgba 사용)", new Regex(@"\bnew\s+(UnityEngine\.)?Color\s*\(", RegexOptions.Compiled)),
        ("new Rect(...) (il2cpp_runtime_invoke 유발, Box 또는 객체 이니셜라이저 사용)", new Regex(@"\bnew\s+(UnityEngine\.)?Rect\s*\(", RegexOptions.Compiled)),
        ("OnGUI (이 빌드에서 IMGUI unstripping 실패)", new Regex(@"\bvoid\s+OnGUI\s*\(", RegexOptions.Compiled)),
        ("Harmony.PatchAll (ApplyAllPatches 우회·이중 패치 방지)", new Regex(@"\.PatchAll\s*\(", RegexOptions.Compiled)),
    };

    [Fact]
    public void NoForbiddenIl2CppOrMelonPatterns()
    {
        var offenders = new List<string>();
        foreach (var file in ModSources())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                string code = comment >= 0 ? line.Substring(0, comment) : line;
                foreach (var (name, rx) in ForbiddenPatterns)
                {
                    if (rx.IsMatch(code))
                        offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1} ({name})");
                }
            }
        }
        Assert.True(offenders.Count == 0, "금지 패턴 발견:\n" + string.Join("\n", offenders));
    }

    // 2026-11-25: CUSTOM 탭은 커스텀 곡 플레이 경로의 패치가 모두 붙었을 때만 만든다(Core.CustomPlayReady).
    // 그 목록의 패치가 [CriticalPatch]가 아니면 빠져도 "필수 패치 빠짐" 오류 없이 탭만 조용히 사라지므로 함께 고정한다.
    [Fact]
    public void CustomPlayGatePatchesAreCritical()
    {
        var sources = ModSources().ToDictionary(f => f, File.ReadAllText);
        string core = sources.First(kv => kv.Key.EndsWith(Path.DirectorySeparatorChar + "Core.cs")).Value;
        var gate = Regex.Match(core, @"MissingOf\(([^;]*)\);", RegexOptions.Singleline);
        Assert.True(gate.Success, "Core.CustomPlayReady의 MissingOf(...) 목록을 찾지 못함");
        var names = Regex.Matches(gate.Groups[1].Value, @"typeof\((?:\w+\.)*(\w+)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.True(names.Count >= 4, "게이트 목록이 너무 짧음: " + string.Join(", ", names));

        var notCritical = new List<string>();
        foreach (var name in names)
        {
            var decl = new Regex(@"\[CriticalPatch\([^\]]*\)\]\s*public static class " + Regex.Escape(name) + @"\b");
            if (!sources.Values.Any(text => decl.IsMatch(text))) notCritical.Add(name);
        }
        Assert.True(notCritical.Count == 0, "[CriticalPatch]가 없는 게이트 패치: " + string.Join(", ", notCritical));
    }

    [Fact]
    public void CoreDeclaresHarmonyDontPatchAll()
    {
        string core = File.ReadAllText(Path.Combine(RepoRoot(), "mod", "Core.cs"));
        Assert.Contains("[assembly: HarmonyDontPatchAll]", core);
    }
}
