using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class HwaStampTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sxtg-hwa-" + Guid.NewGuid().ToString("N"));

    public HwaStampTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "B song"));
        Directory.CreateDirectory(Path.Combine(_dir, "a song"));
        File.WriteAllText(Path.Combine(_dir, "a song", "chart.bms"), "#BPM 150");
        File.WriteAllText(Path.Combine(_dir, "a song", "info.txt"), "title: A");
        File.WriteAllText(Path.Combine(_dir, "B song", "chart.bms"), "#BPM 180");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* 임시 폴더 정리 실패는 시험 결과와 무관 */ }
    }

    /// <summary>CustomTrackManager 스캔이 계산하는 방식(정렬된 GetFiles + Begin/AddFolder) 그대로.</summary>
    private long ScanStyle()
    {
        var root = new DirectoryInfo(_dir);
        var dirs = HwaStamp.SortedSubDirs(root);
        long acc = 0;
        var folders = new List<DirectoryInfo> { root };
        folders.AddRange(dirs);
        for (int i = 0; i < folders.Count; i++)
        {
            var files = folders[i].GetFiles();
            Array.Sort(files, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));
            long fold = HwaStamp.FoldFiles(files);
            acc = i == 0 ? HwaStamp.Begin(fold, dirs.Length) : HwaStamp.AddFolder(acc, folders[i].Name, fold);
        }
        return acc;
    }

    [Fact]
    public void ScanAndChangeCheck_AgreeOnUnchangedFolder()
    {
        // 다르면 CUSTOM 탭에 들어갈 때마다 "변경 감지 → 전체 재스캔"이 된다
        Assert.Equal(HwaStamp.Compute(_dir), ScanStyle());
        Assert.Equal(HwaStamp.Compute(_dir), HwaStamp.Compute(_dir));
    }

    [Fact]
    public void InPlaceEdit_ChangesStamp()
    {
        long before = HwaStamp.Compute(_dir);
        string info = Path.Combine(_dir, "a song", "info.txt");
        File.WriteAllText(info, "title: A (edited)"); // 크기 변경
        File.SetLastWriteTimeUtc(info, DateTime.UtcNow.AddMinutes(1));
        Assert.NotEqual(before, HwaStamp.Compute(_dir));
    }

    [Fact]
    public void SameSizeEditWithNewTime_ChangesStamp()
    {
        string chart = Path.Combine(_dir, "B song", "chart.bms");
        File.SetLastWriteTimeUtc(chart, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        long before = HwaStamp.Compute(_dir);
        File.WriteAllText(chart, "#BPM 190"); // 같은 크기
        File.SetLastWriteTimeUtc(chart, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(before, HwaStamp.Compute(_dir));
    }

    [Fact]
    public void NewSongFolder_ChangesStamp()
    {
        long before = HwaStamp.Compute(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "c song"));
        Assert.NotEqual(before, HwaStamp.Compute(_dir));
    }

    [Fact]
    public void SubDirs_AreSortedCaseInsensitive()
    {
        var names = HwaStamp.SortedSubDirs(new DirectoryInfo(_dir)).Select(d => d.Name).ToArray();
        Assert.Equal(new[] { "a song", "B song" }, names);
    }
}
