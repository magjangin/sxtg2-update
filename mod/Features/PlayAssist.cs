using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2CppSixtarGate;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// Renewal play scene (Renewal_Play / StartrailPatternPlayer) hooks for the sxtg2 [플레이] options and the
/// judge / key-press feed used by the judgment bar and key viewer.
///
/// Every patched address here was checked against script.json for identical-code folding: tiny getters and
/// setters (e.g. JudgeProcessor.set_IsAutoJudge, shared by 44 methods) must not be patched, only called.
/// </summary>
public static class PlayAssist
{
    public static JudgeProcessor? CurrentJudge { get; private set; }
    public static bool IsPlaying => CurrentJudge != null;

    /// <summary>Judge window per EJudge id in ms (BlueStar, WhiteStar, YellowStar, RedStar), read from JudgeRange.</summary>
    public static readonly float[] RangeMs = { 36f, 85f, 136f, 187f };

    /// <summary>Pressed state per SixtarGate.Play.LaneIndex (LL, L, R, RR, Gate, LT, RT).</summary>
    public static readonly bool[] LanePressed = new bool[7];

    public static void EndPlay()
    {
        // Keep _currentJudgePtr: if the same processor is still judging (e.g. an additive scene load ended the
        // session by mistake) EnsureSession re-arms it without subscribing OnNoteJudged a second time.
        CurrentJudge = null;
        Array.Clear(LanePressed, 0, LanePressed.Length);
    }

    // A new JudgeProcessor is built for every play (and restart). A Harmony patch on its constructor never fires in
    // this IL2CPP build, so set up lazily from the first instance call of each new processor (pointer changes).
    private static IntPtr _currentJudgePtr;

    private static void EnsureSession(JudgeProcessor jp)
    {
        if (jp == null) return;
        if (jp.Pointer == _currentJudgePtr)
        {
            if (CurrentJudge == null) CurrentJudge = jp;
            return;
        }
        _currentJudgePtr = jp.Pointer;
        try
        {
            ModConfig.ReloadIfChanged();
            CurrentJudge = jp;
            Array.Clear(LanePressed, 0, LanePressed.Length);
            CustomNoteSkin.ResetForNewPlay();
            ReadJudgeRanges(jp);

            Action<IJudgeProcessor.ISixtarJudgeData> onJudged = OnNoteJudged;
            jp.add_OnNoteJudged(onJudged);

            Melon<Core>.Logger.Msg($"[PlayAssist] Play started: AutoPlay={ModConfig.AutoPlay}, AllPerfect={ModConfig.AllPerfect}, " +
                $"MaxScore={ModConfig.MaxScore:0}, ranges(ms)={string.Join("/", RangeMs)}");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[PlayAssist] JudgeProcessor setup failed: {ex}");
        }
    }

    private static void ReadJudgeRanges(JudgeProcessor jp)
    {
        var timings = jp._range?._timings;
        if (timings == null) return;
        for (int i = 0; i < timings.Count; i++)
        {
            var unit = timings[i];
            if (unit != null && unit.id >= 0 && unit.id < RangeMs.Length && unit.range > 0f)
                RangeMs[unit.id] = unit.range * 1000f;
        }
    }

    private static void OnNoteJudged(IJudgeProcessor.ISixtarJudgeData data)
    {
        try
        {
            // Long notes report every tick; only the head tick carries a real input offset.
            var ticks = data?.TickJudgeList;
            if (data == null || ticks == null || ticks.Count != 1) return;
            JudgmentBar.RegisterHit(data.DeltaTime, (int)data.Taken);        }
        catch { }
    }

    // AutoPlay: the renewal JudgeProcessor already has an auto-judge path (ProcessAutoJudge, judgeID 0, delta 0).
    [HarmonyPatch(typeof(JudgeProcessor), "Judge")]
    public static class JudgeProcessor_Judge_Patch
    {
        public static void Prefix(JudgeProcessor __instance)
        {
            EnsureSession(__instance);
            if (ModConfig.AutoPlay && !__instance.IsAutoJudge)
                __instance.IsAutoJudge = true;
        }
    }

    // AllPerfect: every tick result (hit, auto or Missbreak's judgeID 3) funnels through OnTickEvent before it is
    // appended to the note's TickJudgeList and broadcast, so forcing judgeID 0 here turns it into BlueStar everywhere.
    [HarmonyPatch(typeof(JudgeProcessor), "OnTickEvent")]
    public static class JudgeProcessor_OnTickEvent_Patch
    {
        public static void Prefix(JudgeProcessor __instance, JudgeResult result)
        {
            EnsureSession(__instance);
            if (ModConfig.AllPerfect && result != null)
                result.judgeID = 0;
        }
    }

    // Key viewer feed: LaneComposer press/release events land here (lane = LaneIndex value).
    [HarmonyPatch(typeof(JudgeProcessor), "SetPressedState")]
    public static class JudgeProcessor_SetPressedState_Patch
    {
        public static void Postfix(JudgeProcessor __instance, int lane, bool pressed)
        {
            EnsureSession(__instance);
            if (lane >= 0 && lane < LanePressed.Length)
                LanePressed[lane] = pressed;
        }
    }

    /// <summary>Score at a full combo of BLUESTARs: MaxScore, or the game's 1,000,000.</summary>
    private static int MaxScoreTotal => ModConfig.IsMaxScoreCustom ? (int)Math.Floor(ModConfig.MaxScore) : ScoreProcessor.MAX_SCORE;

    // UpdateScore computes floor(fraction / notes * 1000000.0) natively (no IL to transpile), then raises
    // OnReverseScoreUpdate (1000000 - points lost so far, for the game's ScoreInvert option) and OnScoreUpdate.
    // The delegates are swapped for the duration of the call:
    //   MaxScore: both values scaled to MaxScore.
    //   ScoreInvertCountdown: the reverse value becomes MaxScore - score (counts down by every point earned and
    //   reaches 0 at a perfect end) and is raised from the postfix, after OnScoreUpdate gave this call's score.
    [HarmonyPatch(typeof(ScoreProcessor), nameof(ScoreProcessor.UpdateScore))]
    public static class ScoreProcessor_UpdateScore_Patch
    {
        private static IntPtr _cachedScorePtr, _cachedReversePtr;
        private static Il2CppSystem.Action<int>? _cachedScoreWrapper, _cachedReverseWrapper, _noOp;
        private static int _lastScore;
        private static bool _scored;

        private static int Scale(int v) => ModConfig.IsMaxScoreCustom ? (int)Math.Floor(v * (double)ModConfig.MaxScore / 1000000.0) : v;

        public static void Prefix(ScoreProcessor __instance, out (Il2CppSystem.Action<int>? score, Il2CppSystem.Action<int>? reverse) __state)
        {
            __state = (null, null);
            if (!ModConfig.IsMaxScoreCustom && !ModConfig.ScoreInvertCountdown) return;

            var score = __instance.OnScoreUpdate;
            var reverse = __instance.OnReverseScoreUpdate;
            __state = (score, reverse);
            _scored = false;
            if (score != null) __instance.OnScoreUpdate = ScoreWrapper(score);
            if (reverse != null)
                __instance.OnReverseScoreUpdate = ModConfig.ScoreInvertCountdown ? NoOp() : ReverseWrapper(reverse);
        }

        public static void Postfix(ScoreProcessor __instance, (Il2CppSystem.Action<int>? score, Il2CppSystem.Action<int>? reverse) __state)
        {
            if (__state.score != null) __instance.OnScoreUpdate = __state.score;
            if (__state.reverse != null) __instance.OnReverseScoreUpdate = __state.reverse;
            if (ModConfig.ScoreInvertCountdown && __state.reverse != null && _scored)
                __state.reverse.Invoke(Math.Max(0, MaxScoreTotal - _lastScore));
        }

        private static Il2CppSystem.Action<int> ScoreWrapper(Il2CppSystem.Action<int> original)
        {
            if (_cachedScoreWrapper != null && _cachedScorePtr == original.Pointer) return _cachedScoreWrapper;
            Action<int> wrapper = v =>
            {
                _lastScore = Scale(v);
                _scored = true;
                original.Invoke(_lastScore);
            };
            _cachedScoreWrapper = wrapper;
            _cachedScorePtr = original.Pointer;
            return _cachedScoreWrapper;
        }

        private static Il2CppSystem.Action<int> ReverseWrapper(Il2CppSystem.Action<int> original)
        {
            if (_cachedReverseWrapper != null && _cachedReversePtr == original.Pointer) return _cachedReverseWrapper;
            Action<int> wrapper = v => original.Invoke(Scale(v));
            _cachedReverseWrapper = wrapper;
            _cachedReversePtr = original.Pointer;
            return _cachedReverseWrapper;
        }

        private static Il2CppSystem.Action<int> NoOp()
        {
            if (_noOp == null)
            {
                Action<int> nothing = _ => { };
                _noOp = nothing;
            }
            return _noOp;
        }
    }

    // MaxScore with the game's ScoreInvert option: SubscribeObserver builds the ReverseScoreObserver inline with a
    // hard-coded start of 1000000 (score.targetNumber = 1000000f, StartUpdater(0, 0)); the scaled
    // OnReverseScoreUpdate only arrives with the first judged note, so the counter started at 1,000,000.
    [HarmonyPatch(typeof(GearObject), nameof(GearObject.SubscribeObserver))]
    public static class GearObject_SubscribeObserver_Patch
    {
        public static void Postfix(GearObject __instance)
        {
            var score = __instance.score;
            if (!ModConfig.IsMaxScoreCustom || score == null || Math.Abs(score.targetNumber - ScoreProcessor.MAX_SCORE) > 0.5f) return;
            score.targetNumber = MaxScoreTotal;
            score.StartUpdater(0f, 0f);
        }
    }

    // BlockSave: PlayRecordModule writes the local best-score / play-count files after the result screen.
    [HarmonyPatch(typeof(PlayRecordModule), nameof(PlayRecordModule.RecordPlayData))]
    public static class PlayRecordModule_RecordPlayData_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!ModConfig.ShouldBlockRecord) return true;
            Melon<Core>.Logger.Msg("[PlayAssist] Blocked local play record save (BlockSave/AutoPlay/AllPerfect/MaxScore)");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayRecordModule), nameof(PlayRecordModule.RecordCoursePlayData))]
    public static class PlayRecordModule_RecordCoursePlayData_Patch
    {
        public static bool Prefix() => !ModConfig.ShouldBlockRecord;
    }

    [HarmonyPatch(typeof(PlayRecordModule), nameof(PlayRecordModule.RecordLicensePlayData))]
    public static class PlayRecordModule_RecordLicensePlayData_Patch
    {
        public static bool Prefix() => !ModConfig.ShouldBlockRecord;
    }

    [HarmonyPatch(typeof(SixtarGlobal), nameof(SixtarGlobal.GotoMusicSelect))]
    public static class SixtarGlobal_GotoMusicSelect_EndPlay_Patch
    {
        public static void Postfix() => EndPlay();
    }
}
