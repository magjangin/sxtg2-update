using System;
using System.Collections.Generic;
using System.IO;

namespace SixtarGateMod.Managers;

/// <summary>
/// hwa 폴더 변경 감지 지문(순수 파일 시스템 계산, Unity 의존 없음 → 단위 테스트 대상).
/// 스캔(<see cref="CustomTrackManager"/> 작업 스레드)과 CUSTOM 탭 진입 때 변경 확인이 같은 값을 내야 한다:
/// 전에는 두 곳에 같은 공식을 따로 적어, 한쪽만 고치면 탭에 들어갈 때마다 전체 재스캔하거나 변경을 놓칠 수 있었다.
/// 값은 프로세스 안에서만 비교한다(문자열 해시가 실행마다 달라 파일에 저장하지 않음).
/// </summary>
public static class HwaStamp
{
    /// <summary>
    /// 폴더 안 파일들의 이름·크기·수정 시각을 순서와 무관하게 접는다(파일 시스템마다 열거 순서가 달라서).
    /// 폴더 자체의 수정 시각은 넣지 않는다: Windows는 하위 폴더 시각을 부모 폴더 색인에서 늦게 갱신해, 열거로 얻은
    /// 값이 실제보다 뒤처진다(실측 약 1ms). 전에는 그 값을 넣어 스캔(작업 스레드)과 탭 진입 확인이 서로 다른 지문을
    /// 내서 곡 정보를 고친 뒤 한 번 더 "변경 감지 → 재스캔"이 되고 그 폴더의 증분 재사용도 깨졌다(2026-11-26,
    /// HwaStampTests). 파일 추가·삭제·이름 변경·내용 수정은 파일 목록만으로 모두 잡힌다.
    /// </summary>
    public static long FoldFiles(IEnumerable<FileInfo> files)
    {
        long sum = 0, xor = 0;
        foreach (var f in files)
        {
            long h = StringComparer.OrdinalIgnoreCase.GetHashCode(f.Name);
            h = h * 1_000_003 + f.Length;
            h = h * 1_000_003 + f.LastWriteTimeUtc.Ticks;
            sum += h;
            xor ^= h;
        }
        return sum * 31 + xor;
    }

    /// <summary>루트 폴더(곡 폴더로도 쓰임)의 파일 지문과 하위 폴더 수로 시작.</summary>
    public static long Begin(long rootFold, int subDirCount) => ((17L * 31) + rootFold) * 31 + subDirCount;

    /// <summary>하위 곡 폴더 하나를 더한다. 하위 폴더는 이름 순(대소문자 무시)으로 넣어야 한다.</summary>
    public static long AddFolder(long acc, string folderName, long fold)
        => (acc * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(folderName)) * 31 + fold;

    /// <summary>하위 폴더를 이름 순(대소문자 무시)으로 정렬한 배열.</summary>
    public static DirectoryInfo[] SortedSubDirs(DirectoryInfo root)
    {
        var dirs = root.GetDirectories();
        Array.Sort(dirs, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return dirs;
    }

    /// <summary>폴더 전체 지문(스캔 없이 변경 여부만 볼 때). 스캔이 쓰는 <see cref="Begin"/>/<see cref="AddFolder"/>와 같은 값.</summary>
    public static long Compute(string hwaFolder)
    {
        var root = new DirectoryInfo(hwaFolder);
        var dirs = SortedSubDirs(root);
        long acc = Begin(FoldFiles(root.EnumerateFiles()), dirs.Length);
        foreach (var dir in dirs)
            acc = AddFolder(acc, dir.Name, FoldFiles(dir.EnumerateFiles()));
        return acc;
    }
}
