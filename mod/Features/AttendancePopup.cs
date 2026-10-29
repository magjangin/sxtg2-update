using HarmonyLib;
using MelonLoader;
using Il2CppSixtarGate.ModeSelect;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// config SkipAttendancePopup: the mode select screen no longer opens the daily attendance (출석 보상) popup by itself.
///
/// AttendanceController.Initialize starts WaitUpdateCheck: state 0-2 wait for the update check and load the attendance
/// table and today's reward into _attendanceTableData / _attendanceReward, state 3 clears _loadingAttendanceData and,
/// when the reward is receivable, logs "receivable attendance reward!!!" and opens AttendanceRewardPopup. Only state 3
/// is skipped, so the data stays loaded and the game's own OpenAttendancePopup still works. Nothing is sent to the
/// server: the reward is simply not claimed. MoveNext has its own native body (no identical-code folding).
/// </summary>
public static class AttendancePopup
{
    private static bool _logged;

    [HarmonyPatch(typeof(AttendanceController._WaitUpdateCheck_d__6), "MoveNext")]
    public static class AttendanceController_WaitUpdateCheck_Patch
    {
        public static bool Prefix(AttendanceController._WaitUpdateCheck_d__6 __instance, ref bool __result)
        {
            if (__instance.__1__state != 3) return true;
            ModConfig.ReloadIfChanged();
            if (!ModConfig.SkipAttendancePopup) return true;

            __instance.__1__state = -1;
            var controller = __instance.__4__this;
            if (controller != null) controller._loadingAttendanceData = null;
            __result = false;
            if (!_logged)
            {
                _logged = true;
                Melon<Core>.Logger.Msg("[Attendance] Skipped the automatic attendance reward popup (SkipAttendancePopup=1)");
            }
            return false;
        }
    }
}
