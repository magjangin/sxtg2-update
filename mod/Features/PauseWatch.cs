using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// 일시정지 메뉴 상태 추적: 메뉴가 열려 있는 동안 판정바·키뷰어(Overlay)를 숨긴다.
///
/// CommonPauseProcessor는 _pauseTime이 음수면 "일시정지 아님"이다. 일시정지는 _pauseTime에 Time.time을 넣고
/// PausePopup.Set/Open을 부르며, Resume이 다시 음수로 돌리고 resumeDelay 카운트다운을 시작한다.
/// 메뉴를 여는 경로는 셋이다: Pause(), Pause(bool), 그리고 일시정지 키 입력 람다 &lt;SetInput&gt;b__27_0 — 마지막은
/// Pause() 본문을 인라인으로 품고 있어 Pause 훅을 거치지 않는다(2026-11-27 실기: Esc로 연 메뉴에서 오버레이가 남음).
/// 그래서 경로를 훅으로 쫓지 않고, 플레이마다 불리는 Initialize에서 처리기 포인터만 기억해 매 프레임 _pauseTime을
/// Marshal로 직접 읽는다(IL2CPP 호출·래퍼 할당 없음, NoteEffects.TickPointer와 같은 방식). 포인터는 씬 경계
/// (PlayAssist.EndPlay)에서 지운다.
///
/// 진단: 곡 시작 직후 일시정지가 자동으로 열리는 원인을 가리기 위해, 메뉴가 열릴 때(PausePopup.Open — 세 경로의
/// 공통 지점)와 Pause()/Pause(bool) 호출, 플레이어의 창 포커스 변화를 한 줄씩 남긴다(매 프레임 경로 아님).
/// "메뉴 열림" 직전에 Pause 호출 로그가 없으면 일시정지 키 입력 경로다.
/// 게임은 곡 시작 전 포커스를 잃으면(OnApplicationFocus(false), _startTime &lt; 0) Started 이벤트에 StartAndPause를
/// 걸어 시작하자마자 일시정지한다.
/// </summary>
public static class PauseWatch
{
    private static IntPtr _processor;
    private static int _pauseTimeOffset = int.MinValue;

    public static void Reset() => _processor = IntPtr.Zero;

    /// <summary>일시정지 메뉴가 열려 있음(Resume 전).</summary>
    public static bool IsPaused
    {
        get
        {
            if (_processor == IntPtr.Zero || _pauseTimeOffset <= 0) return false;
            float t = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_processor, _pauseTimeOffset));
            return t >= 0f;
        }
    }

    private static void Remember(CommonPauseProcessor p)
    {
        if (p == null) return;
        if (_pauseTimeOffset == int.MinValue)
        {
            int off = LambdaTranslator.GetIl2CppFieldOffset(typeof(CommonPauseProcessor), "_pauseTime");
            _pauseTimeOffset = off >= 0x10 && off < 0x1000 ? off : -1;
            if (_pauseTimeOffset < 0)
                Melon<Core>.Logger.Warning("[Pause] CommonPauseProcessor._pauseTime 오프셋을 찾지 못해 일시정지 중 오버레이 숨김을 쓸 수 없음");
        }
        _processor = p.Pointer;
    }

    private static string When()
        => $"t={Time.time:0.00}s, 창 포커스={Application.isFocused}, 커스텀 곡={Core.IsCustomTrackActive}";

    [HarmonyPatch(typeof(CommonPauseProcessor), nameof(CommonPauseProcessor.Initialize))]
    public static class CommonPauseProcessor_Initialize_Patch
    {
        public static void Postfix(CommonPauseProcessor __instance)
        {
            try { Remember(__instance); }
            catch (Exception ex) { Melon<Core>.Logger.Warning($"[Pause] 일시정지 처리기 기억 실패: {ex.Message}"); }
        }
    }

    private static void OnPauseCall(CommonPauseProcessor p, string how)
    {
        try
        {
            if (p == null) return;
            if (_processor == IntPtr.Zero) Remember(p); // Initialize를 놓친 경우 대비
            if (p._pauseTime >= 0f) return; // 이미 일시정지면 원본은 아무것도 안 함
            Melon<Core>.Logger.Msg($"[Pause] {how} 호출 ({When()})");
        }
        catch (Exception ex) { Melon<Core>.Logger.Warning($"[Pause] 일시정지 호출 로그 실패: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CommonPauseProcessor), nameof(CommonPauseProcessor.Pause), new Type[0])]
    public static class CommonPauseProcessor_Pause_Patch
    {
        public static void Prefix(CommonPauseProcessor __instance) => OnPauseCall(__instance, "Pause()");
    }

    [HarmonyPatch(typeof(CommonPauseProcessor), nameof(CommonPauseProcessor.Pause), new[] { typeof(bool) })]
    public static class CommonPauseProcessor_PauseBool_Patch
    {
        public static void Prefix(CommonPauseProcessor __instance, bool popupOpen)
            => OnPauseCall(__instance, popupOpen ? "Pause(팝업)" : "Pause(팝업 없음)");
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.Play.Pause.PausePopup), nameof(Il2CppSixtarGate.Play.Pause.PausePopup.Open))]
    public static class PausePopup_Open_Patch
    {
        public static void Prefix()
        {
            try { Melon<Core>.Logger.Msg($"[Pause] 일시정지 메뉴 열림 ({When()})"); }
            catch (Exception ex) { Core.DebugLog($"[Pause] 메뉴 열림 로그 실패: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(StartrailPatternPlayer), "OnApplicationFocus")]
    public static class StartrailPatternPlayer_OnApplicationFocus_Patch
    {
        public static void Prefix(StartrailPatternPlayer __instance, bool focus)
        {
            try
            {
                // 씬에 플레이어 컴포넌트가 여러 개(실기 4개) 있어 같은 변화가 여러 번 온다: 실제 재생 중인 플레이어
                // (_args가 있는 것)만 남긴다.
                if (__instance == null || __instance._args == null) return;
                Melon<Core>.Logger.Msg($"[Pause] 창 포커스 {(focus ? "얻음" : "잃음")} " +
                    $"(t={Time.time:0.00}s, 곡 시작 전={__instance._startTime < 0f}, 커스텀 곡={Core.IsCustomTrackActive})");
            }
            catch (Exception ex) { Core.DebugLog($"[Pause] 포커스 로그 실패: {ex.Message}"); }
        }
    }
}
