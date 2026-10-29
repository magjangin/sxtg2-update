using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Il2Cpp;
using Il2CppRhythmGame;
using Il2CppRhythmGame.MusicSelect;
using Il2CppRhythmGame.SixtarServer;
using Il2CppSixtarGate;
using Il2CppSixtarGate.MusicSelect;
using Il2CppSixtarGate.Play;
using Il2CppSixtarithm;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SixtarGateMod.Loaders;
using SixtarGateMod.Managers;
using SixtarGateMod.Models;
using SixtarGateMod.Features;

[assembly: MelonInfo(typeof(SixtarGateMod.Core), "SixtarGateMod", "2.2.0", "화영왕 (Hwa-young-wang)")]
[assembly: MelonGame("Lyrebird Studio", "Sixtar Gate STARTRAIL")]
// OnInitializeMelon calls PatchAll itself; without this MelonLoader auto-patches too and every hook runs twice.
[assembly: HarmonyDontPatchAll]

namespace SixtarGateMod;

public class Core : MelonMod
{
    public static bool UnlockAllTracks { get; set; } = true;
    public static bool BlockServerUpload { get; set; } = true;
    public static bool SuppressMaintenancePopup { get; set; } = true;
    public static bool IsCustomTrackActive { get; set; } = false;
    /// <summary>EPlayStyle.LIGHT (Lunar, 5 lanes) vs STANDARD (Solar, 7 lanes) of the song being played.</summary>
    public static bool CurrentPlayStyleIsLunar { get; private set; }

    public const string CUSTOM_CATEGORY_NAME = "CUSTOM";

    /// <summary>Routine per-selection / per-note messages; printed only with DebugLog=1 (console output is not free).</summary>
    public static void DebugLog(string message)
    {
        if (ModConfig.DebugLog) Melon<Core>.Logger.Msg(message);
    }
    /// <summary>Chart for the CUSTOM tab's placeholder track (no album in hwa/): one blue note, ~128 s long.</summary>
    private const string DefaultBms = "#BPM 160\n#00211:01000000\n#08511:00000000\n";

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("==========================================================");
        LoggerInstance.Msg(" Sixtar Gate: STARTRAIL Custom Chart & Media Loader v2.2.0");
        LoggerInstance.Msg(" Author: 화영왕 (Hwa-young-wang)                            ");
        LoggerInstance.Msg("==========================================================");

        CustomTrackManager.Initialize();
        ModConfig.Initialize(MelonLoader.Utils.MelonEnvironment.GameRootDirectory);
        CustomNoteSkin.Initialize(MelonLoader.Utils.MelonEnvironment.GameRootDirectory);

        LoggerInstance.Msg("[Harmony] Explicitly applying all patches...");
        // Patch class by class: with a single PatchAll, one missing target aborts every patch after it.
        int applied = 0, failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Core).Assembly))
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try
            {
                HarmonyInstance.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                LoggerInstance.Error($"[Harmony] Patch '{type.Name}' failed: {ex.Message}");
            }
        }
        if (failed == 0)
            LoggerInstance.Msg($"[Harmony] All patches applied successfully! ({applied})");
        else
            LoggerInstance.Warning($"[Harmony] {applied} patches applied, {failed} failed");
    }

    // sxtg2 overlays (judgment bar, key viewer). IMGUI drawing is stripped from this game build
    // ("Method unstripping failed"), so they draw on a pooled uGUI canvas once per frame instead of OnGUI.
    private bool _overlayErrorLogged;

    // The overlays belong to the play scene only; leaving it (result screen, music select) ends the session.
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"[Scene] Loaded '{sceneName}'");
        if (!string.Equals(sceneName, "Renewal_Play", StringComparison.OrdinalIgnoreCase))
            PlayAssist.EndPlay();
        CustomMarathon.OnSceneLoaded(sceneName);
    }

    public override void OnLateUpdate()
    {
        try
        {
            NoteEffects.OnFrame();
            if (!Overlay.Begin()) return;
            float now = Time.time;
            CustomMarathon.Update(now);
            JudgmentBar.Draw(now);
            CustomMarathon.Draw(now);
            KeyViewer.Draw();
            Overlay.End();
        }
        catch (Exception ex)
        {
            if (_overlayErrorLogged) return;
            _overlayErrorLogged = true;
            LoggerInstance.Warning($"[Overlay] Draw error (further errors suppressed): {ex}");
        }
    }

    #region --- Dedicated CUSTOM Category & Custom 1-Note Chart Injection ---

    // 1. CategorySelectController: Register "CUSTOM" category to category tabs list
    [HarmonyPatch(typeof(CategorySelectController), "LoadData")]
    public static class CategorySelectController_LoadData_Patch
    {
        public static void Postfix()
        {
            try
            {
                var categories = CategorySelectController.Categories;
                if (categories != null && !categories.Contains(CUSTOM_CATEGORY_NAME))
                {
                    categories.Add(CUSTOM_CATEGORY_NAME);
                    Melon<Core>.Logger.Msg($"[CustomCategory] Successfully added '{CUSTOM_CATEGORY_NAME}' to CategorySelectController.Categories!");
                }
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomCategory] Error adding category to CategorySelectController: {ex}");
            }
        }
    }

    // 2. RhythmGameUtils: Also register "CUSTOM" to legacy/utility category dictionary and selectable array
    [HarmonyPatch(typeof(RhythmGameUtils), nameof(RhythmGameUtils.GetSelectableCategoty))]
    public static class RhythmGameUtils_GetSelectableCategoty_Patch
    {
        public static void Postfix(ref Il2CppStringArray __result)
        {
            if (__result == null) return;
            for (int i = 0; i < __result.Length; i++)
            {
                if (__result[i] == CUSTOM_CATEGORY_NAME) return;
            }

            var newArr = new Il2CppStringArray(__result.Length + 1);
            for (int i = 0; i < __result.Length; i++)
            {
                newArr[i] = __result[i];
            }
            newArr[__result.Length] = CUSTOM_CATEGORY_NAME;
            __result = newArr;
        }
    }

    // 3. TrackSelectController.GetTracks: Feed custom tracks ONLY when category is "CUSTOM"
    [HarmonyPatch(typeof(TrackSelectController), nameof(TrackSelectController.GetTracks), new Type[] { typeof(string) })]
    public static class TrackSelectController_GetTracks_Patch
    {
        private static Il2CppSystem.Collections.Generic.List<ITrack>? cachedCustomTracks = null;
        public static Il2CppSystem.Collections.Generic.List<ITrack>? CachedCustomTracks => cachedCustomTracks;

        public static bool Prefix(TrackSelectController __instance, string category, ref Il2CppSystem.Collections.Generic.List<ITrack> __result)
        {
            if (string.Equals(category, CUSTOM_CATEGORY_NAME, StringComparison.OrdinalIgnoreCase))
            {
                if (cachedCustomTracks == null)
                {
                    var allTracks = __instance.GetAllTracks();
                    ITrack? template = null;
                    if (allTracks != null)
                    {
                        for (int i = 0; i < allTracks.Count; i++)
                        {
                            var t = allTracks[i];
                            if (t != null && t.DisplayName == "Starting Point")
                            {
                                template = t;
                                break;
                            }
                        }
                        if (template == null && allTracks.Count > 0)
                        {
                            template = allTracks[0];
                        }
                    }

                    if (template != null)
                    {
                        cachedCustomTracks = CustomTrackManager.CreateCustomTrackList(template);
                        Melon<Core>.Logger.Msg($"[CustomCategory] Populated {cachedCustomTracks.Count} custom track(s) in CUSTOM tab!");
                        MediaOverrideManager.PreloadAll(CustomTrackManager.LoadedAlbums);
                    }
                }

                __result = cachedCustomTracks ?? new Il2CppSystem.Collections.Generic.List<ITrack>();
                return false; // Skip original filtering, return our isolated custom list
            }

            // For ALL TRACKS or any official category: Let original code execute untouched.
            // Custom tracks will NOT appear in ALL TRACKS or other categories!
            return true;
        }
    }

    // 4. In-Game Play Trigger & Custom Chart Injection
    [HarmonyPatch(typeof(SixtarGlobal), nameof(SixtarGlobal.GotoPlayScene), new Type[] { typeof(ITrack), typeof(ELevels), typeof(EPlayStyle) })]
    public static class SixtarGlobal_GotoPlayScene_Patch
    {
        public static void Prefix(ITrack track, ELevels level, EPlayStyle playStyle)
        {
            CurrentPlayStyleIsLunar = playStyle == EPlayStyle.LIGHT;
            KeyViewer.ResetBindings();
            if (track != null && (track.Category == CUSTOM_CATEGORY_NAME || track.TrackID.StartsWith("CUSTOM_") || track.DisplayName.Contains("(Custom 1-Note)")))
            {
                IsCustomTrackActive = true;
                if (CustomTrackManager.AlbumsByTrackId.TryGetValue(track.TrackID, out var album))
                {
                    CustomTrackManager.CurrentSelectedAlbum = album;
                    CustomMusicSelect.LastCustomTrackId = track.TrackID;
                    Melon<Core>.Logger.Msg($"[CustomChart] GotoPlayScene: Selected Custom Album '{album.Title}' by '{album.Artist}' (BMS: {album.BmsPath})");
                }
                else
                {
                    CustomTrackManager.CurrentSelectedAlbum = null;
                    Melon<Core>.Logger.Msg($"[CustomChart] GotoPlayScene: Selected Default 1-Note Custom Track ({track.DisplayName})");
                }
            }
            else
            {
                IsCustomTrackActive = false;
                CustomTrackManager.CurrentSelectedAlbum = null;
                Melon<Core>.Logger.Msg($"[CustomChart] GotoPlayScene: Normal Track selected ({track?.DisplayName}). Custom mode inactive.");
            }
            if (track != null) CustomMarathon.OnPlayStarting(track, level, playStyle, IsCustomTrackActive);
        }
    }

    // GotoPlayScene loads the track's asset bundle, then b__12 -> LoadAsset<TextAsset>(trackID) -> b__13 calls
    // RuntimeManager.LoadBank(ta) and looks up "SXGTBIN_{pattern}" -> b__14 stores the pattern text and moves to Renewal_Play.
    // A custom trackID has no bank TextAsset, so b__13 throws NullReferenceException inside LoadBank and loading stalls.
    // For custom tracks skip both lookups and hand b__14 a TextAsset holding the album BMS directly.
    [HarmonyPatch(typeof(SixtarGlobal.__c__DisplayClass54_0), nameof(SixtarGlobal.__c__DisplayClass54_0._GotoPlayScene_b__12))]
    public static class SixtarGlobal_GotoPlayScene_LoadBundle_Patch
    {
        public static bool Prefix(SixtarGlobal.__c__DisplayClass54_0 __instance)
        {
            if (!IsCustomTrackActive) return true;
            var track = __instance.track;
            if (track == null) return true;

            string bmsContent = DefaultBms;
            var album = CustomTrackManager.CurrentSelectedAlbum;
            if (album != null && album.HasCustomBms)
            {
                try { bmsContent = CustomTrackManager.LoadChartText(album); }
                catch (Exception ex) { Melon<Core>.Logger.Error($"[CustomChart] Failed reading BMS for play scene: {ex.Message}"); }
            }

            try
            {
                if (__instance.args != null)
                    __instance.args._BankName_k__BackingField = track.TrackID;
                Melon<Core>.Logger.Msg($"[CustomChart] Skipping FMOD bank/SXGTBIN lookup for '{track.TrackID}', moving to Renewal_Play with local BMS ({bmsContent.Length} chars)");
                __instance._GotoPlayScene_b__14(new TextAsset(bmsContent));
                return false;
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomChart] Custom play-scene bootstrap failed: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SixtarGlobal), nameof(SixtarGlobal.GotoMusicSelect))]
    public static class SixtarGlobal_GotoMusicSelect_Patch
    {
        // Marathon: the result screen leaves through GotoMusicSelect; start the next queued custom song instead.
        public static bool Prefix() => !CustomMarathon.TryContinue();

        public static void Postfix()
        {
            // Postfixes still run when the prefix skipped the original: keep the state GotoPlayScene just set.
            if (CustomMarathon.DivertingMusicSelect) { CustomMarathon.DivertingMusicSelect = false; return; }
            IsCustomTrackActive = false;
            CustomTrackManager.CurrentSelectedAlbum = null;
        }
    }

    [HarmonyPatch(typeof(MikazukiConverter), nameof(MikazukiConverter.Parse), new Type[] { typeof(string) })]
    public static class MikazukiConverter_Parse_Patch
    {
        public static bool Prefix(ref Pattern __result)
        {
            if (IsCustomTrackActive)
            {
                var bms = new BMSConverter();
                string bmsContent = "";

                var album = CustomTrackManager.CurrentSelectedAlbum;
                if (album != null && album.HasCustomBms)
                {
                    try
                    {
                        bmsContent = CustomTrackManager.LoadChartText(album);
                        Melon<Core>.Logger.Msg($"[CustomChart] Injected real BMS file '{album.BmsPath}' ({bmsContent.Length} chars)");
                    }
                    catch (Exception ex)
                    {
                        Melon<Core>.Logger.Error($"[CustomChart] Failed reading BMS file: {ex.Message}. Falling back to 1-Note BMS.");
                    }
                }

                if (string.IsNullOrEmpty(bmsContent))
                {
                    bmsContent = DefaultBms;
                    Melon<Core>.Logger.Msg("[CustomChart] Using default 1-Note BMS.");
                }

                __result = bms.Parse(bmsContent);
                Melon<Core>.Logger.Msg($"[CustomChart] Parsed BMS Pattern: Markers={__result?.Markers?.Count}, Blocks={__result?.Blocks?.Count}");
                return false;
            }
            return true;
        }
    }

    // 5. FinishProcessor Hook: Extend track play duration for custom tracks
    [HarmonyPatch(typeof(FinishProcessor), nameof(FinishProcessor.Initialize))]
    public static class FinishProcessor_Initialize_Patch
    {
        public static void Postfix(FinishProcessor __instance, IPatternPlayer player)
        {
            if (IsCustomTrackActive)
            {
                float customDuration = CustomSongLength();
                if (customDuration <= 0f) customDuration = 125.0f;

                __instance.TrackEndTiming = customDuration;
                __instance.LatestNoteTiming = customDuration - 3.0f;

                Melon<Core>.Logger.Msg($"[FinishProcessor] Custom track initialized: TrackEndTiming = {__instance.TrackEndTiming:F1}s");
            }
        }
    }

    // 5b. Song length for custom tracks. FinishProcessor.UpdatePatternTime finishes once
    // patternTime >= TrackEndOffset + TrackEndTiming + _finishOffset, and TrackEndTiming is (re)assigned after
    // Initialize from SoundModuleForFMOD.GetGamePlayAudioLength(), i.e. the length of the FMOD event on the Game
    // channel. With CustomAudioManager that event is the generic programmer-sound event, not the song, so the play
    // ended ~186 s into a 294 s track. (FinishProcessor.set_TrackEndTiming is folded with 10 other setters: not patchable.)
    // album.Duration is the chart's last note + 3 s; a song whose audio/BGA runs past its last note (하얀 그리움: last
    // note 237.5 s, audio 257.8 s) ended right after the notes, so take the longer of that and the streaming song.
    private static float CustomSongLength()
    {
        var album = CustomTrackManager.CurrentSelectedAlbum;
        if (!IsCustomTrackActive || album == null) return 0f;
        return Math.Max(album.Duration, CustomAudioManager.GetStreamLength(ISoundModule.SoundChannel.Game));
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.GetGamePlayAudioLength))]
    public static class SoundModuleForFMOD_GetGamePlayAudioLength_Patch
    {
        public static void Postfix(ref float __result)
        {
            float length = CustomSongLength();
            if (length > 0f) __result = length;
        }
    }

    [HarmonyPatch(typeof(FinishProcessor), "UpdatePatternTime")]
    public static class FinishProcessor_UpdatePatternTime_Patch
    {
        private static bool _logged;

        public static void Prefix(FinishProcessor __instance)
        {
            // Finish fires at TrackEndOffset + TrackEndTiming + _finishOffset: only the sum has to reach the song length.
            float length = CustomSongLength();
            float offset = __instance.TrackEndOffset;
            if (length <= 0f || offset + __instance.TrackEndTiming >= length - 0.05f) return;
            if (!_logged)
            {
                _logged = true;
                Melon<Core>.Logger.Msg($"[FinishProcessor] End timing was offset {offset:F1}s + timing {__instance.TrackEndTiming:F1}s; extending to song length {length:F1}s");
            }
            __instance.TrackEndTiming = Math.Max(0f, length - offset);
        }
    }

    // 6. BackgroundUIProcessor Hook: Override BGA and Jacket with custom album media.
    // The game's BGA path is VideoClip-only: <Initialize>b__2(VideoClip) assigns bgaPlayer.clip (null for a custom ID,
    // which also flips the player back to clip mode), and PlayBGA/PauseBGA/IsVideoPlayable all bail out when clip == null.
    // So for a custom BGA re-apply the URL after that callback and run PlayBGA/PauseBGA ourselves.
    private static bool IsCustomBga(BackgroundUIProcessor p)
    {
        var album = CustomTrackManager.CurrentSelectedAlbum;
        return IsCustomTrackActive && album != null && album.HasCustomBga && p != null && p.bgaPlayer != null;
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor), nameof(BackgroundUIProcessor.Initialize))]
    public static class BackgroundUIProcessor_Initialize_Patch
    {
        public static void Postfix(BackgroundUIProcessor __instance)
        {
            ApplyCurrentAlbumJacketDeferred(__instance.jacket, "Play.background jacket");
            if (IsCustomBga(__instance))
                MediaOverrideManager.OverrideBGA(__instance.bgaPlayer, CustomTrackManager.CurrentSelectedAlbum!);
        }
    }

    [HarmonyPatch(typeof(BackgroundUIProcessor.__c__DisplayClass11_0), "_Initialize_b__2")]
    public static class BackgroundUIProcessor_VideoCallback_Patch
    {
        public static void Postfix(BackgroundUIProcessor.__c__DisplayClass11_0 __instance)
        {
            var p = __instance.__4__this;
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
            DebugLog($"[MediaOverride] PlayBGA (custom) prepared={player.isPrepared}, delay={delay:F2}s");
            return false;
        }

        private static System.Collections.IEnumerator PlayAfter(VideoPlayer player, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (player != null) player.Play();
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

    // 7. MusicSelect Jacket Hook: Show Custom Jacket in Center TrackInfoViewer.
    // Jackets decode asynchronously (MediaOverrideManager.RequestAlbumSprite): a synchronous LoadImage of a 1080p PNG
    // costs ~50-100 ms of main thread per song, which stuttered every selection change.
    [HarmonyPatch(typeof(TrackInfoViewer), nameof(TrackInfoViewer.Set), new Type[] { typeof(ITrack), typeof(TrackSelectArgs) })]
    public static class TrackInfoViewer_Set_Patch
    {
        private static string? _shownTrackId;

        public static void Postfix(TrackInfoViewer __instance, ITrack track, TrackSelectArgs args)
        {
            if (track == null) return;
            _shownTrackId = track.TrackID;

            var album = CustomTrackManager.FindAlbum(track.TrackID);
            if (album == null || !album.HasCustomJacket) return;

            var img = __instance.jacket;
            if (img == null)
            {
                Melon<Core>.Logger.Warning("[MusicSelect] Could not find jacket Image component in TrackInfoViewer");
                return;
            }

            string trackId = track.TrackID;
            MediaOverrideManager.RequestAlbumSprite(album, sprite =>
            {
                // The selection may have moved on while the PNG was decoding.
                if (img == null || _shownTrackId != trackId) return;
                img.sprite = sprite;
                img.color = White;
                img.enabled = true;
                DebugLog($"[MusicSelect] Center jacket '{album.Title}'");
            });
        }
    }

    // 8. Renewal list rows (TrackObject.thumbnail, the square left of each title). TrackObject.LoadThumbnail is inlined
    // into SetTrack; its async bundle callback <LoadThumbnail>b__1 assigns the bundle sprite (null for a custom ID,
    // leaving an empty box), so re-apply after both. Each row costs one dictionary lookup for official songs.
    private static void ApplyListThumbnail(TrackObject? owner, ITrack? track)
    {
        var thumb = owner?.thumbnail;
        if (thumb == null || track == null) return;
        var album = CustomTrackManager.FindAlbum(track.TrackID);
        if (album == null || !album.HasCustomJacket) return;
        MediaOverrideManager.RequestAlbumSprite(album, sprite =>
        {
            if (thumb == null) return;
            thumb.sprite = sprite;
            thumb.color = White;
            thumb.enabled = true;
        });
    }

    [HarmonyPatch(typeof(TrackObject.__c__DisplayClass18_0), nameof(TrackObject.__c__DisplayClass18_0._LoadThumbnail_b__1))]
    public static class TrackObject_LoadThumbnailCallback_Patch
    {
        public static void Postfix(TrackObject.__c__DisplayClass18_0 __instance) => ApplyListThumbnail(__instance.__4__this, __instance.track);
    }

    [HarmonyPatch(typeof(TrackObject), nameof(TrackObject.SetTrack), new Type[] { typeof(ITrack) })]
    public static class TrackObject_SetTrack_Patch
    {
        public static void Postfix(TrackObject __instance, ITrack track) => ApplyListThumbnail(__instance, track);
    }

    // 10. Play-flow jackets (loading screen, pause popup, result screen).
    // Each screen loads "{trackID}_jacket" / "Thumbnail" sprites from the track's asset bundle and assigns them in an
    // async callback; for a custom trackID the bundle hands back null, leaving a white box. Re-apply after each callback.
    private static readonly Color White = Overlay.Rgba(1f, 1f, 1f, 1f);

    private static void ApplyCurrentAlbumJacket(Image? img, string where)
    {
        if (img == null || !IsCustomTrackActive) return;
        var album = CustomTrackManager.CurrentSelectedAlbum;
        if (album == null || !album.HasCustomJacket) return;
        var sprite = MediaOverrideManager.GetOrCreateAlbumSprite(album);
        if (sprite == null) return;
        img.sprite = sprite;
        img.color = White;
        img.enabled = true;
        DebugLog($"[Jacket] Applied custom jacket to {where}");
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager), "OnLoadResource_Travel")]
    public static class PlayLoading_OnLoadResource_Patch
    {
        public static void Postfix(Il2CppSixtarGate.PlayLoading.PlayLoadingManager __instance)
        {
            ApplyCurrentAlbumJacket(__instance.jacketFull, "PlayLoading.jacketFull");
            ApplyCurrentAlbumJacket(__instance.trackThumbnail, "PlayLoading.trackThumbnail");
        }
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager.__c__DisplayClass34_0), "_OnLoadResource_Travel_b__2")]
    public static class PlayLoading_JacketCallback_Patch
    {
        public static void Postfix(Il2CppSixtarGate.PlayLoading.PlayLoadingManager.__c__DisplayClass34_0 __instance)
            => ApplyCurrentAlbumJacket(__instance.__4__this?.jacketFull, "PlayLoading.jacketFull (callback)");
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.PlayLoading.PlayLoadingManager.__c__DisplayClass34_0), "_OnLoadResource_Travel_b__4")]
    public static class PlayLoading_ThumbnailCallback_Patch
    {
        public static void Postfix(Il2CppSixtarGate.PlayLoading.PlayLoadingManager.__c__DisplayClass34_0 __instance)
            => ApplyCurrentAlbumJacket(__instance.__4__this?.trackThumbnail, "PlayLoading.trackThumbnail (callback)");
    }

    // Identical-code folding: TrackInfoViewerModule._Set_b__3_1 shares its native body with 11 other sprite
    // callbacks (profile icon, unlock list, ...), and BackgroundUIProcessor's <Initialize>b__1 with 2 inventory
    // thumbnail callbacks, so patching them would run our Postfix with a foreign `this`. Instead re-apply the
    // jacket a few times after the async bundle callback has had a chance to assign null.
    private static void ApplyCurrentAlbumJacketDeferred(Image? img, string where)
    {
        if (img == null || !IsCustomTrackActive) return;
        ApplyCurrentAlbumJacket(img, where);
        MelonCoroutines.Start(ReapplyJacket(img, where));
    }

    private static System.Collections.IEnumerator ReapplyJacket(Image img, string where)
    {
        foreach (float delay in new[] { 0.1f, 0.4f, 1.0f })
        {
            yield return new WaitForSecondsRealtime(delay);
            if (img == null || !IsCustomTrackActive) yield break;
            var sprite = CustomTrackManager.CurrentSelectedAlbum is { HasCustomJacket: true } album ? MediaOverrideManager.GetOrCreateAlbumSprite(album) : null;
            if (sprite != null && img.sprite != sprite)
            {
                img.sprite = sprite;
                img.color = White;
                img.enabled = true;
                DebugLog($"[Jacket] Re-applied custom jacket to {where} after async load");
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

    [HarmonyPatch(typeof(Il2CppSixtarGate.Result.TrackInfoModule.__c__DisplayClass13_0), "_Show_b__3")]
    public static class ResultTrackInfo_Callback3_Patch
    {
        public static void Postfix(Il2CppSixtarGate.Result.TrackInfoModule.__c__DisplayClass13_0 __instance) => ApplyResultJackets(__instance.__4__this);
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.Result.TrackInfoModule.__c__DisplayClass13_0), "_Show_b__4")]
    public static class ResultTrackInfo_Callback4_Patch
    {
        public static void Postfix(Il2CppSixtarGate.Result.TrackInfoModule.__c__DisplayClass13_0 __instance) => ApplyResultJackets(__instance.__4__this);
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.Result.TravelBackgroundModule.__c__DisplayClass5_0), "_InitializeModule_b__1")]
    public static class ResultBackground_JacketCallback_Patch
    {
        public static void Postfix(Il2CppSixtarGate.Result.TravelBackgroundModule.__c__DisplayClass5_0 __instance)
            => ApplyCurrentAlbumJacket(__instance.__4__this?.jacket, "Result.background jacket");
    }

    // 9. Preview & In-Game BGM: route custom track audio names to the album's local audio file.
    // MusicSelect.SoundController plays ISoundModule.Play("preview_" + trackID, BGM); no FMOD event exists
    // for "event:/preview_CUSTOM_...", so without this the preview is silent (or keeps the previous track).
    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Play), new Type[] { typeof(string), typeof(ISoundModule.SoundChannel), typeof(float) })]
    public static class SoundModuleForFMOD_Play_Patch
    {
        public static bool Prefix(SoundModuleForFMOD __instance, string audioName, ISoundModule.SoundChannel channel, float position)
        {
            if (!CustomAudioManager.TryResolve(audioName, out var album, out var filePath, out var isPreview))
                return true;

            DebugLog($"[CustomAudio] Play('{audioName}', {channel}, {position:F2}) -> {(isPreview ? "preview" : "bgm")} of '{album!.Title}'");
            try
            {
                if (filePath == null)
                {
                    Melon<Core>.Logger.Warning($"[CustomAudio] No audio file in '{album.AlbumFolder}'. Stopping channel {channel}.");
                    __instance.Stop(channel);
                    return false;
                }

                CustomAudioManager.PlayLocal(__instance, filePath, channel, position);
                return false;
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomAudio] Local playback failed, falling back to original Play: {ex}");
                return true;
            }
        }
    }

    #endregion

    #region --- 1. All Songs & DLC Unlock Hooks ---

    /// <summary>Shared prefix body: report "owned / unlocked" and skip the original check.</summary>
    private static bool Unlocked(ref bool result)
    {
        if (!UnlockAllTracks) return true;
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

    #endregion

    #region --- 2. Offline Guard & Server Sync Block ---

    [HarmonyPatch(typeof(ServerMaintenanceNotice), nameof(ServerMaintenanceNotice.Show), new Type[] { typeof(string) })]
    public static class ServerMaintenanceNotice_Show_Patch
    {
        public static bool Prefix(string language)
        {
            if (SuppressMaintenancePopup)
            {
                Melon<Core>.Logger.Msg($"[OfflineGuard] Suppressed ServerMaintenanceNotice popup (Language: {language})");
                return false; // Prevent popup and quit logic
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.PostHighScoreData), new Type[] { typeof(PlayResult), typeof(Il2CppSystem.Action<int>) })]
    public static class PostHighScoreData_Patch
    {
        public static bool Prefix(PlayResult pr, Il2CppSystem.Action<int> onComplete)
        {
            if (BlockServerUpload)
            {
                Melon<Core>.Logger.Msg("[OfflineGuard] Prevented PostHighScoreData from sending to server. Emulating local success.");
                onComplete?.Invoke(1); // Emulate rank 1 or success response
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetHighScoreList), new Type[] { typeof(TrackWithLevel), typeof(Il2CppSystem.Action<ScoreList>) })]
    public static class GetHighScoreList_Patch
    {
        public static bool Prefix(TrackWithLevel twl, Il2CppSystem.Action<ScoreList> callback)
        {
            if (BlockServerUpload)
            {
                Melon<Core>.Logger.Msg("[OfflineGuard] Blocked GetHighScoreList server query. Returning offline local score list.");
                var emptyList = new ScoreList();
                callback?.Invoke(emptyList);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Il2CppSixtarGate.Web.BackEndRankingService.TravelHighScoreRequest), nameof(Il2CppSixtarGate.Web.BackEndRankingService.TravelHighScoreRequest.Start))]
    public static class BackEnd_TravelHighScoreRequest_Patch
    {
        public static bool Prefix()
        {
            if (BlockServerUpload)
            {
                Melon<Core>.Logger.Msg("[OfflineGuard] Prevented BackEnd TravelHighScoreRequest from uploading to BACKND server.");
                return false;
            }
            return true;
        }
    }

    #endregion
}
