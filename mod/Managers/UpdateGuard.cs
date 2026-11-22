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
/// 3. 게임 파일 지문을 지난 실행과 비교(2026-11-16)하고, 커스텀 곡을 결과 화면까지 게임 내부 예외 없이 플레이하면
///    그 지문을 "이 PC에서 실기 확인됨"으로 기록(2026-11-18). 소스의 <see cref="VerifiedBuilds"/>를 고치지 않아도
///    새 빌드를 실기로 확인한 뒤에는 "검증 안 됨" 경고가 사라진다. 게임 파일이 하나라도 바뀌면 기록은 버려진다.
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

    /// <summary>
    /// LambdaTranslator·IcfScanner 캐시 무효화용 통합 지문.
    /// mtime 대신 PE 링크 시각과 global-metadata.dat 헤더 해시를 써서 폴더 복사 시 불필요한 재스캔을 막고,
    /// 크기가 같은 바이너리/메타데이터 교체도 즉시 감지한다.
    /// </summary>
    public static string BinaryFingerprint
    {
        get
        {
            string build = _current.TryGetValue("buildId", out var b) ? b : (CurrentBuildId ?? "?");
            string gaSize = _current.TryGetValue("gameAssemblySize", out var gs) ? gs : "?";
            string gaStamp = _current.TryGetValue("gameAssemblyStamp", out var gt) ? gt : "?";
            string metaHead = _current.TryGetValue("metadataHeader", out var mh) ? mh : "?";
            return $"{build}|{gaSize}|{gaStamp}|{metaHead}";
        }
    }

    /// <summary>소스의 검증 목록에 있는 빌드인지(지문 변경과 무관).</summary>
    private static bool ListVerified => CurrentBuildId != null && VerifiedBuilds.Contains(CurrentBuildId);

    /// <summary>검증 목록에 있고 지문이 그대로이거나, 지난 실행과 지문이 완전히 같고 그 지문에 실기 확인 기록이 있음.</summary>
    private static bool IsVerified => (ListVerified && !_fingerprintChanged) || _locallyVerified != null;

    public static void Initialize(string gameRoot)
    {
        _gameRoot = gameRoot;
        string gaInfo = ReportBuild(gameRoot);
        CheckFingerprint(gameRoot);
        LogBuildVerdict(gaInfo);
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
    /// <summary>지난 기록과 지문이 완전히 같을 때 그 기록의 실기 확인 값("날짜 곡"), 아니면 null.</summary>
    private static string? _locallyVerified;
    /// <summary>이번 실행에서 지문을 저장했는지(= 패치가 전부 붙고 필수 패치가 안 빠짐). 실기 확인 기록의 전제.</summary>
    private static bool _stateSaved;
    private static int _appliedPatches;

    private static void CheckFingerprint(string gameRoot)
    {
        try
        {
            _current = ReadFingerprint(gameRoot);
            var previous = BuildFingerprint.Parse(File.Exists(StatePath(gameRoot)) ? File.ReadAllLines(StatePath(gameRoot)) : null);
            if (previous == null)
            {
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 지문 기록 없음(첫 실행): 패치가 모두 붙으면 이번 실행의 게임 파일 지문을 기록합니다.");
                return;
            }
            if (previous.TryGetValue(BuildFingerprint.VerifiedPlayKey, out var vp) && vp.Length > 0
                && BuildFingerprint.SameBuild(previous, _current))
                _locallyVerified = vp;
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
        string? meta = null;
        try
        {
            foreach (var dataDir in Directory.GetDirectories(gameRoot, "*_Data"))
            {
                string f = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                if (File.Exists(f)) { meta = f; break; }
            }
        }
        catch (Exception ex) { Core.DebugLog($"[UpdateGuard] global-metadata.dat 위치 검색 실패: {ex.Message}"); }
        d["metadataSize"] = meta == null ? "?" : SizeOf(meta);
        BuildFingerprint.MetadataHeader(meta == null ? Array.Empty<byte>() : ReadHead(meta, 0x1000), out string metaVersion, out string metaHeader);
        d["metadataVersion"] = metaVersion;
        d["metadataHeader"] = metaHeader;
        d["gameAssemblyStamp"] = BuildFingerprint.PeTimestamp(ReadHead(Path.Combine(gameRoot, "GameAssembly.dll"), 0x1000));
        d["unityPlayerStamp"] = BuildFingerprint.PeTimestamp(ReadHead(Path.Combine(gameRoot, "UnityPlayer.dll"), 0x1000));
        return d;
    }

    private static string SizeOf(string file)
    {
        try { var fi = new FileInfo(file); return fi.Exists ? fi.Length.ToString() : "?"; }
        catch { return "?"; }
    }

    /// <summary>파일 앞 <paramref name="max"/>바이트(헤더만, 수 KB라 부팅 비용 무시 가능). 못 읽으면 빈 배열.</summary>
    private static byte[] ReadHead(string file, int max)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[(int)Math.Min(max, fs.Length)];
            int n = 0;
            while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
            return n == buf.Length ? buf : buf.AsSpan(0, n).ToArray();
        }
        catch { return Array.Empty<byte>(); }
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
            bool verified = IsVerified;
            string verdict;
            if (criticalMissing > 0)
                verdict = $"문제 있음: 필수 패치 {criticalMissing}개가 빠졌습니다. 게임 업데이트로 모드가 깨졌을 가능성이 큽니다 (모드 수정 필요)";
            else if (!allApplied)
                verdict = $"일부 패치 빠짐 (ICF {skippedIcf}, 사전 JIT {skippedPreflight}, 실패 {failed}): 해당 기능만 동작하지 않습니다";
            else if (ListVerified && !_fingerprintChanged)
                verdict = "정상 (검증된 빌드, 패치 전부 적용" + (_locallyVerified != null ? $", 이 PC 실기 확인: {_locallyVerified})" : ")");
            else if (_locallyVerified != null)
                verdict = $"정상 (이 PC에서 실기 플레이로 확인된 빌드: {_locallyVerified}, 패치 전부 적용)";
            else
                verdict = "패치는 전부 붙었지만 이 빌드의 동작은 검증되지 않았습니다: 커스텀 곡 1곡을 결과 화면까지 플레이해 확인하세요" +
                          (_fingerprintChanged ? $" (달라진 것: {_fingerprintChangeNote})" : "");

            if (criticalMissing > 0 || !allApplied || !verified)
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 업데이트 점검 결과: {verdict}");
            else
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 업데이트 점검 결과: {verdict}");

            _appliedPatches = applied;
            if (_gameRoot != null && criticalMissing == 0 && allApplied && _current.Count > 0)
            {
                // 지문이 그대로면 실기 확인 기록을 이어받고, 바뀌었으면(_locallyVerified == null) 버린다.
                SaveState(_gameRoot, applied, _locallyVerified);
                _stateSaved = true;
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 업데이트 점검 요약 실패: {ex.Message}");
        }
    }

    private static void SaveState(string gameRoot, int applied, string? verifiedPlay)
    {
        var lines = BuildFingerprint.Format(_current, applied, DateTime.Now, verifiedPlay);
        AtomicFile.WriteAllLines(StatePath(gameRoot), lines);
    }

    /// <summary>빌드 ID를 정하고(매니페스트 + GameAssembly 크기 대조) 어긋남만 경고한다. 검증 여부 줄은 지문을 본 뒤
    /// <see cref="LogBuildVerdict"/>가 찍는다(이 PC의 실기 확인 기록이 있으면 "검증 안 됨" 경고를 내지 않으려고).</summary>
    private static string ReportBuild(string gameRoot)
    {
        string gaInfo = "";
        try
        {
            // {steamapps}\common\{게임} → {steamapps}\appmanifest_1802720.acf
            string? steamapps = Directory.GetParent(gameRoot)?.Parent?.FullName;
            string acf = steamapps == null ? "" : Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
            var ga = new FileInfo(Path.Combine(gameRoot, "GameAssembly.dll"));
            gaInfo = ga.Exists ? $"GameAssembly {ga.Length:N0}바이트, {ga.LastWriteTime:yyyy-MM-dd HH:mm}" : "GameAssembly 없음";
            string? sizeMatch = null;
            if (ga.Exists)
                foreach (var kv in VerifiedGameAssemblySizes)
                    if (kv.Value == ga.Length) sizeMatch = kv.Key;

            if (!File.Exists(acf))
            {
                // DepotDownloader 등 매니페스트 없는 설치: 바이너리 크기로만 판별
                CurrentBuildId = sizeMatch;
                gaInfo = $"Steam 매니페스트 없음 → GameAssembly 크기로 판별, {gaInfo}";
                return gaInfo;
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
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 빌드 확인 실패: {ex.Message}");
        }
        return gaInfo;
    }

    private static void LogBuildVerdict(string gaInfo)
    {
        if (ListVerified && !_fingerprintChanged)
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} 게임 빌드 {CurrentBuildId} (실기 검증된 빌드, {gaInfo})");
        else if (_locallyVerified != null)
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} 게임 빌드 {CurrentBuildId ?? "?"} ({gaInfo}): 검증 목록에는 없지만 이 PC에서 실기 플레이로 확인된 지문 ({_locallyVerified})");
        else
            Melon<Core>.Logger.Warning(
                $"{LOG_PREFIX} 검증되지 않은 게임 빌드 {CurrentBuildId ?? "?"} ({gaInfo}). " +
                $"검증된 빌드: {string.Join(", ", VerifiedBuilds)}. " +
                "커스텀 곡 1곡을 결과 화면까지 플레이하면(게임 내부 예외 없이) 이 PC에 확인으로 기록됩니다. 개발 중이면 tools/version_diff/check_update.ps1로 변경점을 확인하세요.");
    }

    // ---- 실기 플레이 확인 기록 (2026-11-18) ----
    // 씬 흐름(실측): PlayLoading → Renewal_Play → Loading → Result. 플레이 중 그만두면 Renewal_Play → Loading → MusicSelect.
    // 커스텀 곡으로 Renewal_Play에 들어간 뒤 Result에 닿을 때까지 게임 내부 예외(Exception/Assert 로그)가 0건이면
    // 문서의 "커스텀 곡 1곡을 결과 화면까지 플레이" 확인과 같은 것으로 보고 지문 파일에 기록한다.
    // 처음 판(77036d2)은 검증 목록에 이미 있는 빌드면 로그도 없이 건너뛰어서, 실제로 플레이해도 결과를 알 수 없었다
    // (2026-11-18 실기). 지금은 검증 목록과 무관하게 항상 기록하고 판정을 로그로 남긴다.

    private static int _unityExceptionCount;
    private static readonly VerifiedPlayTracker _playTracker = new();
    private static bool _recordedThisRun;

    /// <summary>Core.OnSceneWasLoaded에서 매 씬마다 호출.</summary>
    public static void OnSceneLoaded(string sceneName, bool customTrackActive, string? trackTitle)
    {
        try
        {
            var check = _playTracker.OnScene(sceneName, customTrackActive, System.Threading.Volatile.Read(ref _unityExceptionCount), out int during);
            if (check == PlayCheck.None) return;
            string title = string.IsNullOrWhiteSpace(trackTitle) ? "(제목 없음)" : trackTitle!;
            if (check == PlayCheck.ExceptionsDuring)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 커스텀 곡 '{title}' 플레이 중 게임 내부 예외 {during}건: 이 빌드를 실기 확인으로 기록하지 않습니다 (위의 '게임 내부 예외' 줄 참조).");
                return;
            }
            if (!_stateSaved || _gameRoot == null)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 커스텀 곡 '{title}'이 결과 화면까지 갔지만, 이번 실행은 패치가 일부 빠져서 실기 확인으로 기록하지 않습니다.");
                return;
            }
            if (_recordedThisRun)
            {
                Core.DebugLog($"{LOG_PREFIX} 커스텀 곡 '{title}' 결과 화면 도달 (이번 실행은 이미 기록함)");
                return;
            }

            string note = $"{DateTime.Now:yyyy-MM-dd HH:mm} {title}";
            SaveState(_gameRoot, _appliedPatches, note);
            _recordedThisRun = true;
            _locallyVerified = note;
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} 커스텀 곡 '{title}'을 결과 화면까지 게임 내부 예외 없이 플레이: 빌드 {CurrentBuildId ?? "?"}의 지문을 이 PC에서 실기 확인된 것으로 기록 ({StateFileName}의 {BuildFingerprint.VerifiedPlayKey}=)" +
                                   (ListVerified ? "" : ". 개발 중이면 UpdateGuard.VerifiedBuilds와 AGENTS.md에도 적으세요."));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 실기 확인 기록 실패: {ex.Message}");
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
        System.Threading.Interlocked.Increment(ref _unityExceptionCount); // 같은 예외 반복·상한 초과로 출력을 생략해도 세기는 한다(실기 확인 기록 판정용)
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
                                        (!IsVerified ? "\n    (검증되지 않은 빌드에서 발생)" : ""));
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
