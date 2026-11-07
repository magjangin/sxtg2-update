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

    private const int MaxForwardedMessages = 30;
    private static readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private static int _suppressed;
    private static Application.LogCallback? _callback; // GC에 수집되지 않게 참조 유지

    public static string? CurrentBuildId { get; private set; }

    public static void Initialize(string gameRoot)
    {
        ReportBuild(gameRoot);
        HookUnityLog();
    }

    private static void ReportBuild(string gameRoot)
    {
        try
        {
            // {steamapps}\common\{게임} → {steamapps}\appmanifest_1802720.acf
            string? steamapps = Directory.GetParent(gameRoot)?.Parent?.FullName;
            string acf = steamapps == null ? "" : Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(acf))
            {
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} Steam 매니페스트를 찾지 못해 빌드 확인을 건너뜀 ({acf})");
                return;
            }

            var m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
            CurrentBuildId = m.Success ? m.Groups[1].Value : null;
            var ga = new FileInfo(Path.Combine(gameRoot, "GameAssembly.dll"));
            string gaInfo = ga.Exists ? $"GameAssembly {ga.LastWriteTime:yyyy-MM-dd HH:mm}" : "GameAssembly 없음";

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
            if (!_seen.Add(key)) return; // 같은 예외는 한 번만
            if (_seen.Count > MaxForwardedMessages)
            {
                if (_suppressed++ == 0)
                    Melon<Core>.Logger.Warning($"{LOG_PREFIX} 게임 내부 예외가 {MaxForwardedMessages}종을 넘어 이후는 생략 (Player.log 참조)");
                return;
            }
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
