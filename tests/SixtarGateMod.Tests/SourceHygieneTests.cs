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
}
