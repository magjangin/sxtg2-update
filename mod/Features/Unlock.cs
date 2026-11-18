using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppSixtarGate;
using Il2CppSixtarGate.MusicSelect;

namespace SixtarGateMod.Features;

/// <summary>Client-side full track &amp; DLC unlock overrides.</summary>
public static class Unlock
{
    /// <summary>Shared prefix body: report "owned / unlocked" and skip the original check (also OfflineBypass's ItemHelper).</summary>
    internal static bool Unlocked(ref bool result)
    {
        if (!Core.UnlockAllTracks) return true;
        result = true;
        return false;
    }

    [HarmonyPatch(typeof(ItemUnlockChecker), nameof(ItemUnlockChecker.IsUnlocked), new Type[] { typeof(string) })]
    public static class ItemUnlockChecker_IsUnlocked_Patch { public static bool Prefix(ref bool __result) => Unlocked(ref __result); }

    // Skips the Steam DLC ownership check.
    [HarmonyPatch(typeof(ItemUnlockChecker), nameof(ItemUnlockChecker.IsDLCUnlocked), new Type[] { typeof(string) })]
    public static class ItemUnlockChecker_IsDLCUnlocked_Patch { public static bool Prefix(ref bool __result) => Unlocked(ref __result); }

    [HarmonyPatch(typeof(ItemProvideModule), nameof(ItemProvideModule.IsOwned), new Type[] { typeof(string) })]
    public static class ItemProvideModule_IsOwned_Patch { public static bool Prefix(ref bool __result) => Unlocked(ref __result); }

    [HarmonyPatch(typeof(Il2Cpp.TrackData), nameof(Il2Cpp.TrackData.IsOwned))]
    public static class Global_TrackData_IsOwned_Patch { public static bool Prefix(ref bool __result) => Unlocked(ref __result); }

    // Typed targets, not AccessTools.TypeByName: that scans every assembly and logs a yellow ReflectionTypeLoadException
    // warning for UnityEngine.CoreModule (LightProbesQueryDisposeJob) each time.
    [HarmonyPatch(typeof(TrackLoader.TrackData), nameof(TrackLoader.TrackData.IsOwned), MethodType.Getter)]
    public static class TrackLoader_TrackData_IsOwned_Patch { public static bool Prefix(ref bool __result) => Unlocked(ref __result); }
}
