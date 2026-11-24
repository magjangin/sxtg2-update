using System;
using System.Reflection;
using HarmonyLib;
using Il2CppRhythmGame;
using Il2CppSixtarGate;
using Il2CppSixtarGate.MusicSelect;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using SixtarGateMod.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace SixtarGateMod.Features;

/// <summary>
/// Custom jacket and thumbnail overrides across MusicSelect (center viewer + list rows),
/// PlayLoading, Pause popup, Play background, and Result screen.
/// </summary>
public static class CustomJackets
{
    private static readonly float[] ReapplyDelays = { 0.1f, 0.4f, 1.0f };

    private static void SetJacketImage(Image img, Sprite sprite)
    {
        img.sprite = sprite;
        img.color = Overlay.White;
        img.enabled = true;
    }

    public static void ClearRowCache() => _rowTrackIds.Clear();

    // MusicSelect Jacket Hook: Show Custom Jacket in Center TrackInfoViewer.
    // Jackets decode asynchronously (MediaOverrideManager.RequestAlbumSprite): a synchronous LoadImage of a 1080p PNG
    // costs ~50-100 ms of main thread per song, which stuttered every selection change.
    [HarmonyPatch(typeof(TrackInfoViewer), nameof(TrackInfoViewer.Set), new Type[] { typeof(ITrack), typeof(TrackSelectArgs) })]
    public static class TrackInfoViewer_Set_Patch
    {
        private static string? _shownTrackId;

        public static void Postfix(TrackInfoViewer __instance, ITrack track, TrackSelectArgs args)
        {
            if (track == null) return;
            string? trackId = track.TrackID;
            _shownTrackId = trackId;

            var album = CustomTrackManager.FindAlbum(trackId);
            if (album == null || !album.HasCustomJacket) return;

            var img = __instance.jacket;
            if (img == null)
            {
                Melon<Core>.Logger.Warning("[MusicSelect] TrackInfoViewer에서 자켓 Image 컴포넌트를 찾지 못함");
                return;
            }

            MediaOverrideManager.RequestAlbumSprite(album, sprite =>
            {
                // The selection may have moved on while the PNG was decoding.
                if (img == null || _shownTrackId != trackId) return;
                SetJacketImage(img, sprite);
                Core.DebugLog($"[MusicSelect] 가운데 자켓 표시: '{album.Title}'");
            });
        }
    }

    // Renewal list rows (TrackObject.thumbnail, the square left of each title). TrackObject.LoadThumbnail is inlined
    // into SetTrack; its async bundle callback <LoadThumbnail>b__1 assigns the bundle sprite (null for a custom ID,
    // leaving an empty box), so re-apply after both. Each row costs one dictionary lookup for official songs.
    private static readonly Dictionary<IntPtr, string> _rowTrackIds = new();

    private static void ApplyListThumbnail(TrackObject? owner, ITrack? track)
    {
        // 목록을 스크롤하면 행마다 두 번(SetTrack + 번들 콜백) 불린다. 공식 곡 행은 곡 ID 기록과 사전 조회만 하고 끝내도록
        // thumbnail 필드 읽기(래퍼 생성)와 Unity null 비교(IL2CPP 호출)는 커스텀 자켓이 있을 때만 한다(2026-11-23).
        if (owner is null || track is null) return;
        string trackId = track.TrackID ?? "";
        IntPtr ownerPtr = owner.Pointer;
        _rowTrackIds[ownerPtr] = trackId;
        var album = CustomTrackManager.FindAlbum(trackId);
        if (album == null || !album.HasCustomJacket) return;
        var thumb = owner.thumbnail;
        if (thumb == null) return;
        MediaOverrideManager.RequestAlbumSprite(album, sprite =>
        {
            if (owner == null || thumb == null) return;
            if (!_rowTrackIds.TryGetValue(ownerPtr, out var cur) || cur != trackId) return;
            SetJacketImage(thumb, sprite);
        });
    }

    [HarmonyPatch]
    public static class TrackObject_LoadThumbnailCallback_Patch
    {
        private static Func<Il2CppObjectBase, TrackObject?>? _getOwner;
        private static Func<Il2CppObjectBase, ITrack?>? _getTrack;

        public static MethodBase? TargetMethod()
        {
            var m = PatchResilience.FindClosureCallback(typeof(TrackObject), "_LoadThumbnail_b__", typeof(Sprite), out _getOwner,
                requiredClosureMembers: new[] { "track" });
            if (m?.DeclaringType is Type closureType)
                _getTrack = PatchResilience.CreateGetter<ITrack>(closureType, "track");
            return (_getOwner != null && _getTrack != null) ? m : null;
        }

        public static void Postfix(Il2CppObjectBase __instance)
            => ApplyListThumbnail(_getOwner?.Invoke(__instance), _getTrack?.Invoke(__instance));
    }

    [HarmonyPatch(typeof(TrackObject), nameof(TrackObject.SetTrack), new Type[] { typeof(ITrack) })]
    public static class TrackObject_SetTrack_Patch
    {
        public static void Postfix(TrackObject __instance, ITrack track) => ApplyListThumbnail(__instance, track);
    }

    private static void ApplyCurrentAlbumJacket(Image? img, string where)
    {
        if (img == null || !Core.IsCustomTrackActive) return;
        var album = CustomTrackManager.CurrentSelectedAlbum;
        if (album == null || !album.HasCustomJacket) return;
        var sprite = MediaOverrideManager.GetOrCreateAlbumSprite(album);
        if (sprite == null) return;
        SetJacketImage(img, sprite);
        Core.DebugLog($"[Jacket] 커스텀 자켓 적용: {where}");
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager), nameof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager.OnLoadResource_Travel))]
    public static class PlayLoading_OnLoadResource_Patch
    {
        public static void Postfix(Il2CppSixtarGate.PlayLoading.PlayLoadingManager __instance)
        {
            ApplyCurrentAlbumJacketDeferred(__instance.jacketFull, "PlayLoading.jacketFull");
            ApplyCurrentAlbumJacketDeferred(__instance.trackThumbnail, "PlayLoading.trackThumbnail");
        }
    }

    [HarmonyPatch]
    [IcfShared(2, "Outer Space 판 DisplayClass35_0의 같은 람다와 공유. 두 클로저의 <>4__this 위치가 같아 어느 쪽 this로 실행돼도 안전")]
    public static class PlayLoading_JacketCallback_Patch
    {
        private static Func<Il2CppObjectBase, Il2CppSixtarGate.PlayLoading.PlayLoadingManager?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager), "_OnLoadResource_Travel_b__", typeof(Sprite), out _getOwner,
                matchIndex: 0, accessedOwnerFieldName: "jacketFull");

        public static void Postfix(Il2CppObjectBase __instance)
            => ApplyCurrentAlbumJacket(_getOwner?.Invoke(__instance)?.jacketFull, "PlayLoading.jacketFull (callback)");
    }

    [HarmonyPatch]
    [IcfShared(2, "Outer Space 판 DisplayClass35_0의 같은 람다와 공유. 두 클로저의 <>4__this 위치가 같아 어느 쪽 this로 실행돼도 안전")]
    public static class PlayLoading_ThumbnailCallback_Patch
    {
        private static Func<Il2CppObjectBase, Il2CppSixtarGate.PlayLoading.PlayLoadingManager?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager), "_OnLoadResource_Travel_b__", typeof(Sprite), out _getOwner,
                matchIndex: 1, accessedOwnerFieldName: "trackThumbnail");

        public static void Postfix(Il2CppObjectBase __instance)
            => ApplyCurrentAlbumJacket(_getOwner?.Invoke(__instance)?.trackThumbnail, "PlayLoading.trackThumbnail (callback)");
    }

    // Identical-code folding: TrackInfoViewerModule._Set_b__3_1 shares its native body with 11 other sprite
    // callbacks (profile icon, unlock list, ...), and BackgroundUIProcessor's <Initialize>b__1 with 2 inventory
    // thumbnail callbacks, so patching them would run our Postfix with a foreign `this`. Instead re-apply the
    // jacket a few times after the async bundle callback has had a chance to assign null.
    internal static void ApplyCurrentAlbumJacketDeferred(Image? img, string where)
    {
        if (img == null || !Core.IsCustomTrackActive) return;
        var album = CustomTrackManager.CurrentSelectedAlbum;
        if (album == null || !album.HasCustomJacket) return;
        ApplyCurrentAlbumJacket(img, where);
        MelonCoroutines.Start(ReapplyJacket(img, where));
    }

    private static System.Collections.IEnumerator ReapplyJacket(Image img, string where)
    {
        string? trackId = CustomTrackManager.CurrentSelectedAlbum?.TrackId;
        if (string.IsNullOrEmpty(trackId)) yield break;
        foreach (float delay in ReapplyDelays)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (img == null || !Core.IsCustomTrackActive) yield break;
            var album = CustomTrackManager.CurrentSelectedAlbum;
            if (album == null || !album.HasCustomJacket || album.TrackId != trackId) yield break;
            var sprite = MediaOverrideManager.GetOrCreateAlbumSprite(album);
            if (sprite != null && img.sprite != sprite)
            {
                SetJacketImage(img, sprite);
                Core.DebugLog($"[Jacket] 비동기 로드 후 커스텀 자켓 다시 적용: {where}");
            }
        }
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.Play.Pause.TrackInfoViewerModule), nameof(Il2CppSixtarGate.Play.Pause.TrackInfoViewerModule.Set))]
    public static class PauseTrackInfo_Set_Patch
    {
        public static void Postfix(Il2CppSixtarGate.Play.Pause.TrackInfoViewerModule __instance)
            => ApplyCurrentAlbumJacketDeferred(__instance.jacket, "Pause.jacket");
    }

    private static void ApplyResultJackets(Il2CppSixtarGate.Result.TrackInfoModule? module)
    {
        ApplyCurrentAlbumJacket(module?.jacket, "Result.jacket");
        ApplyCurrentAlbumJacket(module?.thumbnail, "Result.thumbnail");
    }

    [HarmonyPatch]
    public static class ResultTrackInfo_Callback3_Patch
    {
        private static Func<Il2CppObjectBase, Il2CppSixtarGate.Result.TrackInfoModule?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(Il2CppSixtarGate.Result.TrackInfoModule), "_Show_b__", typeof(Sprite), out _getOwner,
                matchIndex: 0, accessedOwnerFieldName: "jacket");

        public static void Postfix(Il2CppObjectBase __instance) => ApplyResultJackets(_getOwner?.Invoke(__instance));
    }

    [HarmonyPatch]
    public static class ResultTrackInfo_Callback4_Patch
    {
        private static Func<Il2CppObjectBase, Il2CppSixtarGate.Result.TrackInfoModule?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(Il2CppSixtarGate.Result.TrackInfoModule), "_Show_b__", typeof(Sprite), out _getOwner,
                matchIndex: 1, accessedOwnerFieldName: "thumbnail");

        public static void Postfix(Il2CppObjectBase __instance) => ApplyResultJackets(_getOwner?.Invoke(__instance));
    }

    [HarmonyPatch]
    public static class ResultBackground_JacketCallback_Patch
    {
        private static Func<Il2CppObjectBase, Il2CppSixtarGate.Result.TravelBackgroundModule?>? _getOwner;

        public static MethodBase? TargetMethod()
            => PatchResilience.FindClosureCallback(
                typeof(Il2CppSixtarGate.Result.TravelBackgroundModule), "_InitializeModule_b__", typeof(Sprite), out _getOwner,
                matchIndex: 0, accessedOwnerFieldName: "jacket");

        public static void Postfix(Il2CppObjectBase __instance)
            => ApplyCurrentAlbumJacket(_getOwner?.Invoke(__instance)?.jacket, "Result.background jacket");
    }
}
