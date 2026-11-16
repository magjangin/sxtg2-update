using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;

namespace SixtarGateMod.Managers;

/// <summary>
/// 게임 업데이트 대비 실행 시 점검.
/// 1. Steam 빌드 ID(appmanifest)를 읽어 실기 검증된 빌드인지 알림.
/// 2. Unity 내부 예외(Debug.LogException, 코루틴 예외 등)를 MelonLoader 로그로 전달.
///    게임 내부 NRE는 원래 Player.log에만 찍혀서, 오프라인 우회가 세션 모듈 초기화를 깨뜨린
///    BackndChat NRE를 1주일 동안 놓쳤음(2026-10-31 ~ 11-07).
/// </summary>
public static class UpdateGuard
{
    private const string LOG_PREFIX = "[UpdateGuard]";
    private const string SteamAppId = "1802720";

    /// <summary>커스텀 곡 플레이 ~ 결과 화면까지 실기로 확인한 Steam 빌드.</summary>
    private static readonly HashSet<string> VerifiedBuilds = new(StringComparer.Ordinal)
    {
        "26218612", // 2026-10-29 (오프라인 우회 도입 전)
        "26455233", // 2026-11-07 ver 1.1.007 / Release 1 (1027)
    };

    /// <summary>
    /// 검증된 빌드의 GameAssembly.dll 크기(바이트). .acf의 buildid는 DepotDownloader 설치나 업데이트 차단용 수정으로
    /// 실제 바이너리와 어긋날 수 있어 크기로 한 번 더 확인한다. 크기 기록이 없는 빌드(26218612)는 .acf만 믿는다.
    /// </summary>
    private static readonly Dictionary<string, long> VerifiedGameAssemblySizes = new(StringComparer.Ordinal)
    {
        ["26455233"] = 64_716_288,
    };

    private const int MaxForwardedMessages = 30;
    private static readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private static int _suppressed;
    private static Application.LogCallback? _callback; // GC에 수집되지 않게 참조 유지

    public static string? CurrentBuildId { get; private set; }

    public static void Initialize(string gameRoot)
    {
        _gameRoot = gameRoot;
        ReportBuild(gameRoot);
        CheckFingerprint(gameRoot);
        HookUnityLog();
    }

    // ---- 지난 실행과의 지문 비교 (2026-11-16) ----
    // .acf buildid는 수정·DepotDownloader 설치로 실제 바이너리와 어긋날 수 있고, 검증 목록은 소스에 박혀 있어 새 빌드를
    // 알 수 없다. 그래서 실제 파일(GameAssembly.dll, global-metadata.dat, UnityPlayer.dll)의 크기를 지난 실행과 비교해
    // "게임이 바뀐 뒤 첫 실행"을 직접 알아챈다. global-metadata.dat이 바뀌면 인터롭 형식·필드 오프셋이 달라질 수 있어
    // GameAssembly.dll만 바뀐 경우(11-06 업데이트)보다 위험하다.

    private const string StateFileName = "SixtarGateMod.BuildState.txt";
    private static string? _gameRoot;
    private static bool _fingerprintChanged;
    private static string _fingerprintChangeNote = "";
    private static Dictionary<string, string> _current = new(StringComparer.Ordinal);

    private static void CheckFingerprint(string gameRoot)
    {
        try
        {
            _current = ReadFingerprint(gameRoot);
            var previous = BuildFingerprint.Parse(File.Exists(StatePath(gameRoot)) ? File.ReadAllLines(StatePath(gameRoot)) : null);
            if (previous == null)
            {
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 지문 기록 없음(첫 실행): 패치가 모두 붙으면 이번 실행의 파일 크기를 기록합니다.");
                return;
            }
            var changes = BuildFingerprint.Diff(previous, _current, out bool risky);
            if (changes.Count == 0) return;

            _fingerprintChanged = true;
            _fingerprintChangeNote = string.Join("; ", changes);
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 지난 실행과 달라진 것: {_fingerprintChangeNote}");
            if (risky)
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 메타데이터/Unity 런타임이 바뀌었습니다: 인터롭 형식·필드 오프셋이 달라졌을 수 있습니다. " +
                                           "패치 결과 요약과 [Harmony/Preflight]·[ICFGuard] 경고를 확인하고 tools/version_diff/check_update.ps1을 돌려 보세요.");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 지문 비교 실패: {ex.Message}");
        }
    }

    private static string StatePath(string gameRoot) => Path.Combine(gameRoot, "UserData", StateFileName);

    private static Dictionary<string, string> ReadFingerprint(string gameRoot)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buildId"] = CurrentBuildId ?? "?",
            ["gameAssemblySize"] = SizeOf(Path.Combine(gameRoot, "GameAssembly.dll")),
            ["unityPlayerSize"] = SizeOf(Path.Combine(gameRoot, "UnityPlayer.dll")),
        };
        // 메타데이터는 {게임}_Data\il2cpp_data\Metadata\global-metadata.dat. 폴더 이름이 실행 파일 이름에서 오므로 *_Data를 훑는다.
        string meta = "?";
        try
        {
            foreach (var dataDir in Directory.GetDirectories(gameRoot, "*_Data"))
            {
                string f = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                if (File.Exists(f)) { meta = SizeOf(f); break; }
            }
        }
        catch { }
        d["metadataSize"] = meta;
        return d;
    }

    private static string SizeOf(string file)
    {
        try { var fi = new FileInfo(file); return fi.Exists ? fi.Length.ToString() : "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// 패치 적용이 끝난 뒤 판정 한 줄을 찍고 이번 지문을 저장한다(PatchResilience.ApplyAllPatches 끝에서 호출).
    /// 지문은 패치가 모두 붙었을 때만 새로 기록한다: 일부가 빠진 상태를 기준으로 삼으면 다음 부팅에서 경고가 사라진다.
    /// </summary>
    public static void ReportPatchSummary(int applied, int skippedIcf, int skippedPreflight, int failed, int criticalMissing)
    {
        try
        {
            bool allApplied = skippedIcf == 0 && skippedPreflight == 0 && failed == 0;
            bool verified = CurrentBuildId != null && VerifiedBuilds.Contains(CurrentBuildId);
            string verdict;
            if (criticalMissing > 0)
                verdict = $"문제 있음: 필수 패치 {criticalMissing}개가 빠졌습니다. 게임 업데이트로 모드가 깨졌을 가능성이 큽니다 (모드 수정 필요)";
            else if (!allApplied)
                verdict = $"일부 패치 빠짐 (ICF {skippedIcf}, 사전 JIT {skippedPreflight}, 실패 {failed}): 해당 기능만 동작하지 않습니다";
            else if (verified && !_fingerprintChanged)
                verdict = "정상 (검증된 빌드, 패치 전부 적용)";
            else
                verdict = "패치는 전부 붙었지만 이 빌드의 동작은 검증되지 않았습니다: 커스텀 곡 1곡을 결과 화면까지 플레이해 확인하세요" +
                          (_fingerprintChanged ? $" (달라진 것: {_fingerprintChangeNote})" : "");

            if (criticalMissing > 0 || !allApplied || !verified || _fingerprintChanged)
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 업데이트 점검 결과: {verdict}");
            else
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 업데이트 점검 결과: {verdict}");

            if (_gameRoot != null && criticalMissing == 0 && allApplied && _current.Count > 0)
                SaveState(_gameRoot, applied);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 업데이트 점검 요약 실패: {ex.Message}");
        }
    }

    private static void SaveState(string gameRoot, int applied)
    {
        string path = StatePath(gameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = BuildFingerprint.Format(_current, applied, DateTime.Now);
        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines);
        File.Move(tmp, path, true);
    }

    private static void ReportBuild(string gameRoot)
    {
        try
        {
            // {steamapps}\common\{게임} → {steamapps}\appmanifest_1802720.acf
            string? steamapps = Directory.GetParent(gameRoot)?.Parent?.FullName;
            string acf = steamapps == null ? "" : Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
            var ga = new FileInfo(Path.Combine(gameRoot, "GameAssembly.dll"));
            string gaInfo = ga.Exists ? $"GameAssembly {ga.Length:N0}바이트, {ga.LastWriteTime:yyyy-MM-dd HH:mm}" : "GameAssembly 없음";
            string? sizeMatch = null;
            if (ga.Exists)
                foreach (var kv in VerifiedGameAssemblySizes)
                    if (kv.Value == ga.Length) sizeMatch = kv.Key;

            if (!File.Exists(acf))
            {
                // DepotDownloader 등 매니페스트 없는 설치: 바이너리 크기로만 판별
                CurrentBuildId = sizeMatch;
                if (sizeMatch != null)
                    Melon<Core>.Logger.Msg($"{LOG_PREFIX} Steam 매니페스트 없음, GameAssembly 크기로 빌드 {sizeMatch} 확인 (실기 검증된 빌드, {gaInfo})");
                else
                    Melon<Core>.Logger.Warning($"{LOG_PREFIX} Steam 매니페스트({acf})가 없고 GameAssembly 크기도 검증된 빌드와 다름 ({gaInfo}). 검증되지 않은 빌드로 봅니다.");
                return;
            }

            var m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
            CurrentBuildId = m.Success ? m.Groups[1].Value : null;

            // .acf가 검증된 빌드라고 해도 바이너리 크기가 기록과 다르면 실제로는 다른 빌드
            if (CurrentBuildId != null && ga.Exists
                && VerifiedGameAssemblySizes.TryGetValue(CurrentBuildId, out long expected) && expected != ga.Length)
            {
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} 매니페스트는 빌드 {CurrentBuildId}인데 GameAssembly 크기가 기록({expected:N0}바이트)과 다름 ({gaInfo}). " +
                    "매니페스트를 고쳤거나 게임 파일이 바뀐 것입니다.");
                CurrentBuildId = sizeMatch ?? CurrentBuildId + "?"; // "?"를 붙여 검증 목록과 안 맞게 함
            }

            if (CurrentBuildId != null && VerifiedBuilds.Contains(CurrentBuildId))
            {
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 게임 빌드 {CurrentBuildId} (실기 검증된 빌드, {gaInfo})");
            }
            else
            {
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} 검증되지 않은 게임 빌드 {CurrentBuildId ?? "?"} ({gaInfo}). " +
                    $"검증된 빌드: {string.Join(", ", VerifiedBuilds)}. " +
                    "커스텀 곡 1곡을 결과 화면까지 플레이해 보고, 개발 중이면 tools/version_diff/check_update.ps1로 변경점을 확인하세요.");
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 빌드 확인 실패: {ex.Message}");
        }
    }

    private static void HookUnityLog()
    {
        try
        {
            _callback = new Action<string, string, LogType>(OnUnityLog);
            Application.add_logMessageReceived(_callback);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} Unity 로그 연결 실패 (게임 내부 예외는 Player.log에서 확인): {ex.Message}");
        }
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Assert) return;
        try
        {
            string firstFrame = FirstLine(stackTrace);
            string key = condition + "|" + firstFrame;
            if (_seen.Contains(key)) return; // 같은 예외는 한 번만
            // 상한을 넘으면 더 기억하지 않음(전에는 생략하면서도 계속 Add해서, 메시지마다 내용이 다른 예외가 쏟아지면 집합이 끝없이 커졌음)
            if (_seen.Count >= MaxForwardedMessages)
            {
                if (_suppressed++ == 0)
                    Melon<Core>.Logger.Warning($"{LOG_PREFIX} 게임 내부 예외가 {MaxForwardedMessages}종을 넘어 이후는 생략 (Player.log 참조)");
                return;
            }
            _seen.Add(key);
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 게임 내부 예외: {condition}" +
                                        (firstFrame.Length > 0 ? $"\n    위치: {firstFrame}" : "") +
                                        (CurrentBuildId != null && !VerifiedBuilds.Contains(CurrentBuildId) ? "\n    (검증되지 않은 빌드에서 발생)" : ""));
        }
        catch
        {
            // 로그 콜백 안에서는 절대 예외를 내보내지 않음
        }
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return (i < 0 ? s : s.Substring(0, i)).Trim();
    }
}
