using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSixtarGate.Play;
using MelonLoader;
using SixtarGateMod.Managers;
using UnityEngine;
using UnityEngine.Video;

namespace SixtarGateMod.Features;

/// <summary>
/// In-game BGA URL override. The game's BackgroundUIProcessor only supports VideoClip assets from
/// Track/{id} bundles and bails out of PlayBGA/PauseBGA/IsVideoPlayable whenever clip == null.
/// </summary>
public static class CustomBga
{
    internal static bool IsCustomBga(BackgroundUIProcessor p)
    {
        var album = CustomTrackManager.CurrentSelectedAlbum;
        return Core.IsCustomTrackActive && album != null && album.HasCustomBga && p != null && p.bgaPlayer != null;
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor), nameof(BackgroundUIProcessor.Initialize))]
    public static class BackgroundUIProcessor_Initialize_Patch
    {
        public static void Postfix(BackgroundUIProcessor __instance)
        {
            CustomJackets.ApplyCurrentAlbumJacketDeferred(__instance.jacket, "Play.background jacket");
            if (IsCustomBga(__instance))
                MediaOverrideManager.OverrideBGA(__instance.bgaPlayer, CustomTrackManager.CurrentSelectedAlbum!);
        }
    }

    [HarmonyPatch]
    public static class BackgroundUIProcessor_VideoCallback_Patch
    {
        private static Func<Il2CppObjectBase, BackgroundUIProcessor?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(BackgroundUIProcessor), "_Initialize_b__", typeof(VideoClip), out _getOwner,
                accessedOwnerFieldName: "bgaPlayer");

        public static void Postfix(Il2CppObjectBase __instance)
        {
            var p = _getOwner?.Invoke(__instance);
            if (p != null && IsCustomBga(p))
                MediaOverrideManager.OverrideBGA(p.bgaPlayer, CustomTrackManager.CurrentSelectedAlbum!);
        }
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor), nameof(BackgroundUIProcessor.IsVideoPlayable), MethodType.Getter)]
    public static class BackgroundUIProcessor_IsVideoPlayable_Patch
    {
        public static void Postfix(BackgroundUIProcessor __instance, ref bool __result)
        {
            if (!__result && IsCustomBga(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor), nameof(BackgroundUIProcessor.PlayBGA))]
    public static class BackgroundUIProcessor_PlayBGA_Patch
    {
        public static bool Prefix(BackgroundUIProcessor __instance)
        {
            if (!IsCustomBga(__instance)) return true;

            // Same as the original body minus the clip == null early return.
            __instance._isPlayStarted = true;
            if (__instance._isReservedPause)
            {
                __instance._isReservedPause = false;
                __instance._reservedDelayTime = Time.time - __instance._reservedPauseTime;
                return false;
            }

            if (__instance.jacket != null)
                __instance.jacket.gameObject.SetActive(false);

            float delay = __instance._reservedDelayTime;
            var player = __instance.bgaPlayer;
            if (delay > 0f)
            {
                __instance._reservedDelayTime = 0f;
                MelonCoroutines.Start(PlayAfter(player, delay));
            }
            else
            {
                player.Play();
            }
            Core.DebugLog($"[MediaOverride] 커스텀 BGA 재생: 준비됨={player.isPrepared}, 지연={delay:F2}초");
            return false;
        }

        private static System.Collections.IEnumerator PlayAfter(VideoPlayer player, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (player != null && player.isActiveAndEnabled && Core.IsCustomTrackActive)
                player.Play();
        }
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor), nameof(BackgroundUIProcessor.PauseBGA))]
    public static class BackgroundUIProcessor_PauseBGA_Patch
    {
        public static bool Prefix(BackgroundUIProcessor __instance)
        {
            if (!IsCustomBga(__instance)) return true;

            if (!__instance._isPlayStarted)
            {
                __instance._isReservedPause = true;
                __instance._reservedPauseTime = Time.time;
                return false;
            }
            __instance.bgaPlayer.Pause();
            return false;
        }
    }
}
