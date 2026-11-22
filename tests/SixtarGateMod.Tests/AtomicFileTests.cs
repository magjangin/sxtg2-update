using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sxtg-atomic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* 임시 폴더 정리 실패는 시험 결과와 무관 */ }
    }

    [Fact]
    public void WriteAllText_CreatesParentFolderAndLeavesNoTempFile()
    {
        string path = Path.Combine(_dir, "sub", "state.txt");
        AtomicFile.WriteAllText(path, "a=1");
        Assert.Equal("a=1", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFile_WithoutBom()
    {
        string path = Path.Combine(_dir, "x.txt");
        AtomicFile.WriteAllText(path, "old");
        AtomicFile.WriteAllText(path, "한글");
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]); // UTF-8 BOM 없음
        Assert.Equal("한글", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllLines_WritesEveryLine()
    {
        string path = Path.Combine(_dir, "lines.txt");
        AtomicFile.WriteAllLines(path, new[] { "k1=v1", "k2=v2" });
        Assert.Equal(new[] { "k1=v1", "k2=v2" }, File.ReadAllLines(path));
    }
}
