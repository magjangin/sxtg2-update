using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSixtarGate.ModeSelect;
using MelonLoader;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// config SkipAttendancePopup: the mode select screen no longer opens the daily attendance (출석 보상) popup by itself.
///
/// AttendanceController.Initialize starts WaitUpdateCheck: state 0-2 wait for the update check and load the attendance
/// table and today's reward into _attendanceTableData / _attendanceReward, state 3 clears _loadingAttendanceData and,
/// when the reward is receivable, logs "receivable attendance reward!!!" and opens AttendanceRewardPopup. Only state 3
/// is skipped, so the data stays loaded and the game's own OpenAttendancePopup still works. Nothing is sent to the
/// server: the reward is simply not claimed.
/// Resolved structurally via PatchResilience so state-machine ordinal changes (_d__6 -> _d__7) never break it.
/// </summary>
public static class AttendancePopup
{
    private static bool _logged;
    private static bool _errorLogged;

    [HarmonyPatch]
    public static class AttendanceController_WaitUpdateCheck_Patch
    {
        private static Func<Il2CppObjectBase, int>? _getState;
        private static Action<Il2CppObjectBase, int>? _setState;
        private static Func<Il2CppObjectBase, AttendanceController?>? _getOwner;

        public static MethodBase? TargetMethod()
        {
            var moveNext = PatchResilience.FindCoroutineMoveNext(
                typeof(AttendanceController), "_WaitUpdateCheck_d__", out _getState, out _setState);
            if (moveNext?.DeclaringType is Type smType)
            {
                _getOwner = PatchResilience.CreateGetter<AttendanceController>(smType, "__4__this", "<>4__this");
                if (_getOwner == null)
                    Melon<Core>.Logger.Warning($"[Attendance] {smType.Name}에서 소유자(__4__this) 접근자를 찾지 못함 — 수동 출석 팝업 열기에 영향이 있을 수 있음");
            }
            return moveNext;
        }

        public static bool Prefix(Il2CppObjectBase __instance, ref bool __result)
        {
            if (__instance == null || _getState == null) return true;
            try
            {
                if (_getState(__instance) != 3) return true;
                ModConfig.ReloadIfChanged();
                if (!ModConfig.SkipAttendancePopup) return true;

                _setState?.Invoke(__instance, -1);
                var controller = _getOwner?.Invoke(__instance);
                if (controller != null) controller._loadingAttendanceData = null;
                __result = false;
                if (!_logged)
                {
                    _logged = true;
                    Melon<Core>.Logger.Msg("[Attendance] 출석 보상 팝업 자동 표시를 건너뜀 (SkipAttendancePopup=1)");
                }
                return false;
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Melon<Core>.Logger.Warning($"[Attendance] 출석 팝업 생략 처리 실패(원본 실행으로 폴백): {ex.Message}");
                }
                return true;
            }
        }
    }
}
