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
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SixtarGateMod.Loaders;
using SixtarGateMod.Managers;
using SixtarGateMod.Models;
using SixtarGateMod.Features;

[assembly: MelonInfo(typeof(SixtarGateMod.Core), "SixtarGateMod", "2.3.0", "화영왕 (Hwa-young-wang)")]
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
        var ver = typeof(Core).Assembly.GetName().Version;
        string verText = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.3.0";
        LoggerInstance.Msg("==========================================================");
        LoggerInstance.Msg($" Sixtar Gate: STARTRAIL 커스텀 차트 & 미디어 로더 v{verText}");
        LoggerInstance.Msg(" 제작: 화영왕 (Hwa-young-wang)");
        LoggerInstance.Msg("==========================================================");

        string gameRoot = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
        UpdateGuard.Initialize(gameRoot);
        LambdaTranslator.Initialize(gameRoot);
        CustomTrackManager.Initialize(gameRoot);
        ModConfig.Initialize(gameRoot);
        CustomNoteSkin.Initialize(gameRoot);
        NoteEffects.RegisterDynamicTargets();

        LoggerInstance.Msg("[Harmony] 패치 적용 중...");
        try
        {
            PatchResilience.ApplyAllPatches(HarmonyInstance, LoggerInstance, gameRoot);
        }
        finally
        {
            // hwa 스캔은 Harmony 적용(2~3초)과 겹쳐 작업 스레드에서 돌았다: 결과와 로그를 여기서 받는다.
            CustomTrackManager.CompleteScan();
        }
    }

    public override void OnDeinitializeMelon() => CustomAudioManager.StopAll();

    // sxtg2 overlays (judgment bar, key viewer). IMGUI drawing is stripped from this game build
    // ("Method unstripping failed"), so they draw on a pooled uGUI canvas once per frame instead of OnGUI.
    private bool _overlayErrorLogged;
    private bool _inRenewalPlay;
    private bool _overlayIdle;

    // The overlays belong to the play scene only; leaving it (result screen, music select) ends the session.
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"[Scene] 씬 로드: '{sceneName}'");
        _inRenewalPlay = string.Equals(sceneName, "Renewal_Play", StringComparison.OrdinalIgnoreCase);
        if (!_inRenewalPlay)
        {
            bool definiteBoundary = string.Equals(sceneName, "Result", StringComparison.OrdinalIgnoreCase)
                || sceneName.IndexOf("MusicSelect", StringComparison.OrdinalIgnoreCase) >= 0
                || sceneName.IndexOf("PlayLoading", StringComparison.OrdinalIgnoreCase) >= 0;
            PlayAssist.EndPlay(resetSessionPointer: definiteBoundary);
        }
        CustomMarathon.OnSceneLoaded(sceneName);
        // 커스텀 곡 Renewal_Play → Result를 게임 내부 예외 없이 지나면 이 빌드를 실기 확인으로 기록
        // (IsCustomTrackActive는 결과 화면을 떠나는 GotoMusicSelect Postfix까지 유지됨)
        UpdateGuard.OnSceneLoaded(sceneName, IsCustomTrackActive,
            CustomTrackManager.CurrentSelectedAlbum?.Title ?? (IsCustomTrackActive ? "기본 1노트 곡" : null));
    }

    public override void OnLateUpdate()
    {
        // Also check PlayAssist.IsPlaying so overlays keep working if a future update renames the play scene.
        if (!_inRenewalPlay && !PlayAssist.IsPlaying && !CustomMarathon.IsShowingResultBanner) return;
        try
        {
            NoteEffects.OnFrame();
            // 판정바·키뷰어를 둘 다 끄고 마라톤 배너도 없으면 그릴 것이 없다: 캔버스 생성과 매 프레임
            // 화면 크기·배율 조회(IL2CPP 호출 3번)를 건너뛰고, 전에 그린 것만 한 번 숨긴다.
            bool playOverlay = PlayAssist.IsPlaying && (ModConfig.EnableJudgmentBar || ModConfig.EnableKeyViewer);
            if (!playOverlay && !CustomMarathon.IsShowingResultBanner)
            {
                if (!_overlayIdle) { Overlay.HideAll(); _overlayIdle = true; }
                return;
            }
            _overlayIdle = false;
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
            LoggerInstance.Warning($"[Overlay] 그리기 오류 (이후 같은 오류는 생략): {ex}");
        }
    }

    #region --- Dedicated CUSTOM Category & Custom 1-Note Chart Injection ---

    // 1. CategorySelectController: Register "CUSTOM" category to category tabs list
    [HarmonyPatch(typeof(CategorySelectController), "LoadData")]
    [CriticalPatch("곡 선택 상단 CUSTOM 카테고리 탭 생성")]
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
                    Melon<Core>.Logger.Msg($"[CustomCategory] 카테고리 목록에 '{CUSTOM_CATEGORY_NAME}' 탭 추가 완료");
                }
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomCategory] 카테고리 추가 중 오류: {ex}");
            }
        }
    }

    // 2. RhythmGameUtils: Also register "CUSTOM" to legacy/utility category dictionary and selectable array.
    // Typo-tolerant: resolves "GetSelectableCategoty" or "GetSelectableCategory" if fixed in a future update.
    [HarmonyPatch]
    [CriticalPatch("선택 가능 카테고리 목록에 CUSTOM 탭 등록")]
    public static class RhythmGameUtils_GetSelectableCategoty_Patch
    {
        public static MethodBase? TargetMethod()
            => PatchResilience.ResolveMethod(typeof(RhythmGameUtils), "GetSelectableCategoty", "GetSelectableCategory");

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
    [CriticalPatch("CUSTOM 카테고리 커스텀 곡 목록 반환")]
    public static class TrackSelectController_GetTracks_Patch
    {
        private static Il2CppSystem.Collections.Generic.List<ITrack>? cachedCustomTracks = null;
        public static Il2CppSystem.Collections.Generic.List<ITrack>? CachedCustomTracks => cachedCustomTracks;

        public static bool Prefix(TrackSelectController __instance, string category, ref Il2CppSystem.Collections.Generic.List<ITrack> __result)
        {
            if (string.Equals(category, CUSTOM_CATEGORY_NAME, StringComparison.OrdinalIgnoreCase))
            {
                if (CustomTrackManager.RefreshIfChanged())
                    cachedCustomTracks = null;

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
                        Melon<Core>.Logger.Msg($"[CustomCategory] CUSTOM 탭에 커스텀 곡 {cachedCustomTracks.Count}곡 등록");
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
    [CriticalPatch("커스텀 곡 플레이 활성화 및 앨범 선택 상태 설정")]
    public static class SixtarGlobal_GotoPlayScene_Patch
    {
        public static void Prefix(ITrack track, ELevels level, EPlayStyle playStyle)
        {
            CurrentPlayStyleIsLunar = playStyle == EPlayStyle.LIGHT;
            PlayAssist.EndPlay(resetSessionPointer: true);
            // Null-safe: an exception thrown from this prefix would abort GotoPlayScene itself (no song starts).
            if (track != null && (track.Category == CUSTOM_CATEGORY_NAME
                                  || (track.TrackID?.StartsWith("CUSTOM_", StringComparison.Ordinal) ?? false)))
            {
                IsCustomTrackActive = true;
                if (CustomTrackManager.FindAlbum(track.TrackID) is { } album)
                {
                    CustomTrackManager.CurrentSelectedAlbum = album;
                    CustomMusicSelect.LastCustomTrackId = track.TrackID;
                    Melon<Core>.Logger.Msg($"[CustomChart] 커스텀 곡 선택: '{album.Title}' - '{album.Artist}' (BMS: {album.BmsPath})");
                }
                else
                {
                    CustomTrackManager.CurrentSelectedAlbum = null;
                    Melon<Core>.Logger.Msg($"[CustomChart] 기본 1노트 커스텀 곡 선택 ({track.DisplayName})");
                }
            }
            else
            {
                IsCustomTrackActive = false;
                CustomTrackManager.CurrentSelectedAlbum = null;
                Melon<Core>.Logger.Msg($"[CustomChart] 일반 곡 선택 ({track?.DisplayName}), 커스텀 모드 꺼짐");
            }
            if (track != null) CustomMarathon.OnPlayStarting(track, level, playStyle, IsCustomTrackActive);
        }
    }

    // GotoPlayScene loads the track's asset bundle, then b__12 -> LoadAsset<TextAsset>(trackID) -> b__13 calls
    // RuntimeManager.LoadBank(ta) and looks up "SXGTBIN_{pattern}" -> b__14 stores the pattern text and moves to Renewal_Play.
    // A custom trackID has no bank TextAsset, so b__13 throws NullReferenceException inside LoadBank and loading stalls.
    // For custom tracks skip both lookups and hand b__14 a TextAsset holding the album BMS directly.
    // Resolved structurally via PatchResilience so closure/lambda ordinal changes across game updates never break it.
    [HarmonyPatch]
    [CriticalPatch("커스텀 곡이 로딩 화면에서 멈춤 (FMOD 뱅크 조회 NRE)")]
    public static class SixtarGlobal_GotoPlayScene_LoadBundle_Patch
    {
        private static Func<Il2CppObjectBase, ITrack?>? _getTrack;
        private static Func<Il2CppObjectBase, Il2CppObjectBase?>? _getArgs;
        private static Action<Il2CppObjectBase, string>? _setBankName;
        private static Action<Il2CppObjectBase, TextAsset>? _invokePatternReady;

        public static MethodBase? TargetMethod()
        {
            // track/args를 캡처한 클로저의 번들 콜백만(다른 클로저에 같은 시그니처 람다가 생겨도 헷갈리지 않게)
            var target = PatchResilience.FindClosureMethodCapturing(
                typeof(SixtarGlobal), "_GotoPlayScene_b__", new[] { "track", "args" }, typeof(ISixtarAssetBundle));
            if (target?.DeclaringType is Type closureType)
            {
                _getTrack = PatchResilience.CreateGetter<ITrack>(closureType, "track");
                _getArgs = PatchResilience.CreateGetter<Il2CppObjectBase>(closureType, "args");
                var argsProp = PatchResilience.FindProperty(closureType, typeof(Il2CppObjectBase), "args");
                if (argsProp != null)
                    _setBankName = PatchResilience.CreateSetter<string>(argsProp.PropertyType, "BankName", "_BankName_k__BackingField");
                var patternMethod = PatchResilience.FindMethodInType(closureType, "_GotoPlayScene_b__", typeof(TextAsset));
                if (patternMethod != null)
                    _invokePatternReady = PatchResilience.CreateAction<TextAsset>(closureType, patternMethod);
            }
            // 대상은 찾았는데 필요한 접근자가 없으면 Prefix가 매번 원본으로 넘겨 커스텀 곡이 로딩에서 멈춘다.
            // 조용히 붙이지 말고 패치 실패로 보고(필수 패치 빠짐 오류로 드러남).
            if (target != null && (_getTrack == null || _invokePatternReady == null))
            {
                Melon<Core>.Logger.Error(
                    $"[CustomChart] {target.DeclaringType?.Name}.{target.Name}에서 track 접근자({_getTrack != null}) 또는 " +
                    $"패턴 콜백(TextAsset 람다, {_invokePatternReady != null})을 찾지 못함");
                return null;
            }
            return target;
        }

        public static bool Prefix(Il2CppObjectBase __instance)
        {
            if (!IsCustomTrackActive || _getTrack == null || _invokePatternReady == null) return true;
            var track = _getTrack(__instance);
            if (track == null) return true;

            string bmsContent = DefaultBms;
            var album = CustomTrackManager.CurrentSelectedAlbum;
            if (album != null && album.HasCustomBms)
            {
                try { bmsContent = CustomTrackManager.LoadChartText(album); }
                catch (Exception ex) { Melon<Core>.Logger.Error($"[CustomChart] 플레이 씬용 BMS 읽기 실패: {ex.Message}"); }
            }

            try
            {
                var args = _getArgs?.Invoke(__instance);
                if (args != null)
                    _setBankName?.Invoke(args, track.TrackID);
                Melon<Core>.Logger.Msg($"[CustomChart] '{track.TrackID}'의 FMOD 뱅크/SXGTBIN 조회를 건너뛰고 로컬 BMS({bmsContent.Length}자)로 Renewal_Play 진입");
                _invokePatternReady(__instance, new TextAsset(bmsContent));
                return false;
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomChart] 커스텀 곡 플레이 씬 진입 실패: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SixtarGlobal), nameof(SixtarGlobal.GotoMusicSelect))]
    [CriticalPatch("곡 선택 복귀 시 커스텀 곡 활성 상태 및 플레이 세션 정리")]
    public static class SixtarGlobal_GotoMusicSelect_Patch
    {
        // Marathon: the result screen leaves through GotoMusicSelect; start the next queued custom song instead.
        public static bool Prefix() => !CustomMarathon.TryContinue();

        public static void Postfix()
        {
            // 플레이 세션 정리(오버레이·노트 효과·판정바·키뷰어). 전에는 PlayAssist에 같은 대상의 패치 클래스가
            // 따로 있어 Harmony 적용이 한 번 더 들었음. 마라톤으로 다음 곡에 넘어갈 때도 원래처럼 정리한다.
            PlayAssist.EndPlay(resetSessionPointer: true);
            // Postfixes still run when the prefix skipped the original: keep the state GotoPlayScene just set.
            if (CustomMarathon.DivertingMusicSelect) { CustomMarathon.DivertingMusicSelect = false; return; }
            IsCustomTrackActive = false;
            CustomTrackManager.CurrentSelectedAlbum = null;
        }
    }

    [HarmonyPatch(typeof(MikazukiConverter), nameof(MikazukiConverter.Parse), new Type[] { typeof(string) })]
    [CriticalPatch("커스텀 차트 BMS 파싱 주입")]
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
                        Melon<Core>.Logger.Msg($"[CustomChart] BMS 파일 주입: '{album.BmsPath}' ({bmsContent.Length}자)");
                    }
                    catch (Exception ex)
                    {
                        Melon<Core>.Logger.Error($"[CustomChart] BMS 파일 읽기 실패: {ex.Message}. 1노트 BMS로 대체");
                    }
                }

                if (string.IsNullOrEmpty(bmsContent))
                {
                    bmsContent = DefaultBms;
                    Melon<Core>.Logger.Msg("[CustomChart] 기본 1노트 BMS 사용");
                }

                try
                {
                    __result = bms.Parse(bmsContent);
                }
                catch (Exception ex)
                {
                    // A chart the game's BMSConverter rejects (unknown channel -> KeyNotFoundException, ...) used to
                    // escape into PatternConverter and leave the loading screen stuck. Play the 1-note chart instead.
                    Melon<Core>.Logger.Error($"[CustomChart] 게임 BMS 해석기가 차트를 거부함 ({ex.GetType().Name}: {ex.Message}). 1노트 BMS로 대체");
                    __result = new BMSConverter().Parse(DefaultBms);
                }
                Melon<Core>.Logger.Msg($"[CustomChart] BMS 패턴 해석 완료: 마커 {__result?.Markers?.Count}개, 블록 {__result?.Blocks?.Count}개");
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
            FinishProcessor_UpdatePatternTime_Patch.InvalidateCache();
            if (IsCustomTrackActive)
            {
                float customDuration = CustomSongLength();
                if (customDuration <= 0f) customDuration = 125.0f;

                __instance.TrackEndTiming = customDuration;
                __instance.LatestNoteTiming = customDuration - 3.0f;

                Melon<Core>.Logger.Msg($"[FinishProcessor] 커스텀 곡 종료 시점 설정: TrackEndTiming = {__instance.TrackEndTiming:F1}초");
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
            if (length > 0f)
            {
                __result = length;
                FinishProcessor_UpdatePatternTime_Patch.InvalidateCache();
            }
        }
    }

    [HarmonyPatch(typeof(FinishProcessor), "UpdatePatternTime")]
    public static class FinishProcessor_UpdatePatternTime_Patch
    {
        private static IntPtr _verifiedInstance;
        private static float _verifiedLength;
        private static IntPtr _loggedFor;

        public static void InvalidateCache() => _verifiedInstance = IntPtr.Zero;

        // Runs every frame of every play: bail out before touching the IL2CPP properties when not a custom song
        // or once the current FinishProcessor instance has already been verified for this song length.
        public static void Prefix(FinishProcessor __instance)
        {
            if (!IsCustomTrackActive) return;
            float length = CustomSongLength();
            if (length <= 0f) return;

            IntPtr ptr = __instance.Pointer;
            if (ptr == _verifiedInstance && Math.Abs(length - _verifiedLength) < 0.01f) return;

            // Finish fires at TrackEndOffset + TrackEndTiming + _finishOffset: only the sum has to reach the song length.
            float offset = __instance.TrackEndOffset;
            float current = __instance.TrackEndTiming;
            if (offset + current < length - 0.05f)
            {
                if (_loggedFor != ptr) // once per play (a new FinishProcessor per play)
                {
                    _loggedFor = ptr;
                    Melon<Core>.Logger.Msg($"[FinishProcessor] 종료 시점이 오프셋 {offset:F1}초 + {current:F1}초라서 곡 길이 {length:F1}초로 연장");
                }
                __instance.TrackEndTiming = Math.Max(0f, length - offset);
            }
            _verifiedInstance = ptr;
            _verifiedLength = length;
        }
    }

    // BGA patches live in Features/CustomBga.cs, jacket/thumbnail patches in Features/CustomJackets.cs.

    // 9. Preview & In-Game BGM: route custom track audio names to the album's local audio file.
    // MusicSelect.SoundController plays ISoundModule.Play("preview_" + trackID, BGM); no FMOD event exists
    // for "event:/preview_CUSTOM_...", so without this the preview is silent (or keeps the previous track).
    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Play), new Type[] { typeof(string), typeof(ISoundModule.SoundChannel), typeof(float) })]
    [CriticalPatch("커스텀 곡 FMOD 오디오 스트림 재생")]
    public static class SoundModuleForFMOD_Play_Patch
    {
        public static bool Prefix(SoundModuleForFMOD __instance, string audioName, ISoundModule.SoundChannel channel, float position)
        {
            if (channel == ISoundModule.SoundChannel.Game)
                FinishProcessor_UpdatePatternTime_Patch.InvalidateCache();
            if (!CustomAudioManager.TryResolve(audioName, out var album, out var filePath, out var isPreview))
            {
                CustomAudioManager.StopLocal(channel);
                return true;
            }

            DebugLog($"[CustomAudio] Play('{audioName}', {channel}, {position:F2}) -> '{album!.Title}'의 {(isPreview ? "프리뷰" : "본곡")}");
            try
            {
                if (filePath == null)
                {
                    Melon<Core>.Logger.Warning($"[CustomAudio] '{album.AlbumFolder}'에 음원 파일이 없음. {channel} 채널 정지");
                    __instance.Stop(channel);
                    return false;
                }

                CustomAudioManager.PlayLocal(__instance, filePath, channel, position);
                return false;
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Error($"[CustomAudio] 로컬 음원 재생 실패, 원래 재생 함수로 대체: {ex}");
                return true;
            }
        }
    }

    #endregion

    // All Songs & DLC Unlock hooks are in Features/Unlock.cs.
    // Offline Guard & Server Sync Block patches are consolidated in Features/OfflineBypass.cs.
}
