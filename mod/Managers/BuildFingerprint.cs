using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SixtarGateMod.Managers;

/// <summary>
/// 게임 파일 지문(빌드 ID·파일 크기·PE 링크 시각·메타데이터 헤더)의 비교와 저장 형식. MelonLoader·IL2CPP에 의존하지 않는
/// 순수 함수라 <c>tools/lambdascan</c>에서 오프라인으로 시험한다. 읽고 쓰는 쪽은 <see cref="UpdateGuard"/>.
/// </summary>
public static class BuildFingerprint
{
    /// <summary>
    /// 지문 항목: 키, 표시 이름, 바뀌면 위험한지(인터롭 형식·필드 오프셋에 영향).
    /// 크기만으로는 크기가 같은 변경을 못 잡아서(2026-11-16 한계) 2026-11-18에 PE 헤더의 링크 시각(TimeDateStamp)과
    /// 메타데이터 헤더(버전 + 섹션 오프셋·크기 표의 해시)를 더했다. 옛 기록에 없는 키는 비교하지 않는다.
    /// </summary>
    public static readonly (string Key, string Label, bool Risky)[] Keys =
    {
        ("buildId", "Steam 빌드 ID", false),
        ("gameAssemblySize", "GameAssembly.dll 크기", false),
        ("gameAssemblyStamp", "GameAssembly.dll 링크 시각(PE)", false),
        ("metadataSize", "global-metadata.dat 크기", true),
        ("metadataVersion", "global-metadata.dat 버전", true),
        ("metadataHeader", "global-metadata.dat 헤더(섹션 표)", true),
        ("unityPlayerSize", "UnityPlayer.dll 크기", true),
        ("unityPlayerStamp", "UnityPlayer.dll 링크 시각(PE)", true),
    };

    /// <summary>이 지문의 빌드를 실기 플레이(커스텀 곡 → 결과 화면, 게임 내부 예외 0)로 확인한 기록. 값은 "날짜 곡 제목".</summary>
    public const string VerifiedPlayKey = "verifiedPlay";

    /// <summary>지난 기록과 지금을 비교해 달라진 항목 설명을 돌려준다. 한쪽이 "?"(못 읽음)거나 없으면 그 항목은 비교하지 않는다.</summary>
    public static List<string> Diff(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current, out bool risky)
    {
        risky = false;
        var changes = new List<string>();
        foreach (var (key, label, isRisky) in Keys)
        {
            if (!current.TryGetValue(key, out string? now) || !previous.TryGetValue(key, out string? before)) continue;
            if (now == "?" || before == "?" || now == before) continue;
            changes.Add($"{label} {before} → {now}");
            if (isRisky) risky = true;
        }
        return changes;
    }

    /// <summary>
    /// 실기 확인 기록을 이어받아도 되는지: 지문 키가 양쪽에 모두 있고 전부 같아야 한다(<see cref="Diff"/>와 달리 빠진 키를 봐주지 않음).
    /// "?"끼리는 같다고 보되(매니페스트 없는 설치의 buildId 등), 바이너리를 직접 가리키는 키는 반드시 읽혀 있어야 한다.
    /// </summary>
    public static bool SameBuild(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
    {
        foreach (var (key, _, _) in Keys)
        {
            if (!current.TryGetValue(key, out string? now) || !previous.TryGetValue(key, out string? before)) return false;
            if (now != before) return false;
        }
        foreach (var key in new[] { "gameAssemblySize", "gameAssemblyStamp", "metadataHeader" })
            if (current[key] == "?") return false;
        return true;
    }

    /// <summary>key=value 줄(# 주석 허용)을 읽는다. 항목이 하나도 없으면 null.</summary>
    public static Dictionary<string, string>? Parse(IEnumerable<string>? lines)
    {
        if (lines == null) return null;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return d.Count == 0 ? null : d;
    }

    /// <summary>저장할 줄(주석 + 지문 키들 + 부가 정보 + 실기 확인 기록).</summary>
    public static List<string> Format(IReadOnlyDictionary<string, string> current, int patchesApplied, DateTime savedAt, string? verifiedPlay = null)
    {
        var lines = new List<string> { "# 모드가 지난 실행에서 본 게임 파일 지문 (자동 생성, 지우면 다음 실행에 다시 기록)" };
        foreach (var (key, _, _) in Keys)
            if (current.TryGetValue(key, out var v)) lines.Add($"{key}={v}");
        lines.Add($"patchesApplied={patchesApplied}");
        lines.Add($"savedAt={savedAt:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(verifiedPlay))
            lines.Add($"{VerifiedPlayKey}={verifiedPlay!.Replace('\r', ' ').Replace('\n', ' ').Trim()}");
        return lines;
    }

    /// <summary>PE 파일 앞부분(최소 0x200바이트 권장)에서 COFF 헤더의 TimeDateStamp를 8자리 16진수로. 못 읽으면 "?".
    /// MSVC 재현 빌드(/Brepro)면 시각 대신 내용 해시가 들어가지만, 어느 쪽이든 내용이 바뀌면 값이 바뀐다.</summary>
    public static string PeTimestamp(ReadOnlySpan<byte> head)
    {
        if (head.Length < 0x40 || head[0] != (byte)'M' || head[1] != (byte)'Z') return "?";
        int pe = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(0x3C, 4));
        if (pe < 0x40 || pe > head.Length - 12) return "?";
        if (BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(pe, 4)) != 0x00004550) return "?"; // "PE\0\0"
        return BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(pe + 8, 4)).ToString("X8");
    }

    /// <summary>
    /// global-metadata.dat 헤더: 0 = sanity(0xFAB11BAF), 4 = 버전(이 빌드 31), 8부터 (오프셋, 크기) 쌍이 이어지고 첫 오프셋이
    /// 곧 헤더 길이(v31은 0x100). 버전과 헤더 전체의 FNV-1a 64비트 해시를 돌려준다. 섹션 하나라도 크기가 바뀌면 해시가 바뀐다
    /// (같은 크기 안에서 내용만 바뀐 문자열 등은 못 잡음). 형식이 다르면 "?".
    /// </summary>
    public static void MetadataHeader(ReadOnlySpan<byte> head, out string version, out string headerHash)
    {
        version = "?";
        headerHash = "?";
        if (head.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(0, 4)) != 0xFAB11BAF) return;
        version = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(4, 4)).ToString();
        int len = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(8, 4));
        if (len < 12 || len > head.Length) len = Math.Min(head.Length, 0x100);
        headerHash = Fnv1a64(head.Slice(0, len)).ToString("X16");
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
        return h;
    }
}

/// <summary>커스텀 곡 플레이 확인 판정 결과.</summary>
public enum PlayCheck
{
    /// <summary>판정할 것 없음(플레이 시작, 로딩 씬, 공식 곡, 중간에 그만둠 등).</summary>
    None,
    /// <summary>커스텀 곡으로 Renewal_Play → Result를 게임 내부 예외 0건으로 지남.</summary>
    Passed,
    /// <summary>Result에 닿았지만 그 사이 게임 내부 예외가 있었음.</summary>
    ExceptionsDuring,
}

/// <summary>
/// 씬 순서로 "커스텀 곡을 결과 화면까지 예외 없이 플레이"를 판정하는 순수 상태 기계(<see cref="UpdateGuard"/>가 사용,
/// <c>tools/lambdascan</c>에서 실측 씬 순서로 시험). 실측 흐름: PlayLoading → Renewal_Play → Loading → Result,
/// 중간에 그만두면 Renewal_Play → Loading → MusicSelect.
/// </summary>
public sealed class VerifiedPlayTracker
{
    private int _start = -1; // Renewal_Play 진입 때의 예외 개수, -1 = 진행 중인 커스텀 플레이 없음

    public PlayCheck OnScene(string sceneName, bool customTrackActive, int exceptionCount, out int exceptionsDuring)
    {
        exceptionsDuring = 0;
        if (string.Equals(sceneName, "Renewal_Play", StringComparison.OrdinalIgnoreCase))
        {
            _start = customTrackActive ? exceptionCount : -1;
            return PlayCheck.None;
        }
        if (string.Equals(sceneName, "Loading", StringComparison.OrdinalIgnoreCase)) return PlayCheck.None; // 플레이 → 결과 사이
        int start = _start;
        _start = -1;
        if (start < 0 || !customTrackActive || !string.Equals(sceneName, "Result", StringComparison.OrdinalIgnoreCase))
            return PlayCheck.None;
        exceptionsDuring = exceptionCount - start;
        return exceptionsDuring > 0 ? PlayCheck.ExceptionsDuring : PlayCheck.Passed;
    }
}
