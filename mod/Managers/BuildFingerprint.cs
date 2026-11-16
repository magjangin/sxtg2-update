using System;
using System.Collections.Generic;

namespace SixtarGateMod.Managers;

/// <summary>
/// 게임 파일 지문(빌드 ID·파일 크기)의 비교와 저장 형식. MelonLoader·IL2CPP에 의존하지 않는 순수 함수라
/// <c>tools/lambdascan</c>에서 오프라인으로 시험한다. 읽고 쓰는 쪽은 <see cref="UpdateGuard"/>.
/// </summary>
public static class BuildFingerprint
{
    /// <summary>지문 항목: 키, 표시 이름, 바뀌면 위험한지(인터롭 형식·필드 오프셋에 영향).</summary>
    public static readonly (string Key, string Label, bool Risky)[] Keys =
    {
        ("buildId", "Steam 빌드 ID", false),
        ("gameAssemblySize", "GameAssembly.dll 크기", false),
        ("metadataSize", "global-metadata.dat 크기", true),
        ("unityPlayerSize", "UnityPlayer.dll 크기", true),
    };

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

    /// <summary>저장할 줄(주석 + 지문 키들 + 부가 정보).</summary>
    public static List<string> Format(IReadOnlyDictionary<string, string> current, int patchesApplied, DateTime savedAt)
    {
        var lines = new List<string> { "# 모드가 지난 실행에서 본 게임 파일 지문 (자동 생성, 지우면 다음 실행에 다시 기록)" };
        foreach (var (key, _, _) in Keys)
            if (current.TryGetValue(key, out var v)) lines.Add($"{key}={v}");
        lines.Add($"patchesApplied={patchesApplied}");
        lines.Add($"savedAt={savedAt:yyyy-MM-dd HH:mm:ss}");
        return lines;
    }
}
