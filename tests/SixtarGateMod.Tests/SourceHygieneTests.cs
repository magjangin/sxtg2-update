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

    [Fact]
    public void CoreDeclaresHarmonyDontPatchAll()
    {
        string core = File.ReadAllText(Path.Combine(RepoRoot(), "mod", "Core.cs"));
        Assert.Contains("[assembly: HarmonyDontPatchAll]", core);
    }
}
