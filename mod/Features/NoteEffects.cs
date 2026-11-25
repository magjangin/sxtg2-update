using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// sxtg2 [노트 속도 카오스] and [히든 / 페이드] on the renewal renderer. Visual only, judgement is untouched.
///
/// StartrailPatternRenderer.PositioningNote computes, per frame and note,
///   head = (headWeight - weight) * _scrollSpeed * _noteDistanceInterval   -> NoteObject.set_HeadPosition
///   tail = (tailWeight - weight) * _scrollSpeed * _noteDistanceInterval   -> NoteObject.set_NoteLength
/// and spawns a note once headWeight &lt;= weight + renderRange. Scaling head and tail by one factor changes a note's
/// fall speed while it still reaches 0 at its timing; renderRange is widened by 1/Min so slow notes don't pop in.
/// Hidden / Fade map head to a percentage of renderRange's height (judgement line 0, spawn edge 100) and fade the
/// note through a CanvasGroup. Long notes fade with their head.
/// </summary>
public static class NoteEffects
{
    private static StartrailPatternRenderer? _renderer;
    private static float _baseRenderRange;
    private static readonly Dictionary<IntPtr, float> NoteFactor = new();
    private static readonly Dictionary<int, float> LaneFactor = new();
    private static readonly Dictionary<IntPtr, int> NoteLane = new();
    private static readonly Dictionary<IntPtr, Cover> Covers = new();
    private static float _laneScale; // _scrollSpeed * _noteDistanceInterval, refreshed once per frame
    private static System.Random _random = new();

    [HarmonyPatch(typeof(StartrailPatternRenderer), nameof(StartrailPatternRenderer.Register))]
    public static class StartrailPatternRenderer_Register_Patch
    {
        public static void Postfix(StartrailPatternRenderer __instance)
        {
            ModConfig.ReloadIfChanged();
            // 플레이 준비 시점(노트가 나오기 전)을 함께 쓴다: 커스텀 노트 스프라이트를 이때 올림
            CustomNoteSkin.PrepareForPlay();
            NoteFactor.Clear();
            LaneFactor.Clear();
            NoteLane.Clear();
            RestoreAndClearCovers();
            _random = new System.Random();

            if (_renderer == null || _renderer.Pointer != __instance.Pointer)
                _baseRenderRange = __instance.renderRange;
            _renderer = __instance;

            float range = _baseRenderRange;
            if (ModConfig.NoteSpeedChaos && ModConfig.NoteSpeedChaosMin < 1f)
                range = _baseRenderRange / ModConfig.NoteSpeedChaosMin;
            __instance.renderRange = range;

            bool needed = ModConfig.NoteSpeedChaos || ModConfig.AnyLaneCover;
            if (needed)
                _laneScale = __instance._scrollSpeed * __instance._noteDistanceInterval;
            try { SetNotePatches(needed); }
            catch (Exception ex) { Melon<Core>.Logger.Error($"[NoteEffects] NoteObject 위치 훅 부착/해제 실패: {ex.Message}"); }
            if (needed)
                Melon<Core>.Logger.Msg($"[NoteEffects] SpeedChaos={ModConfig.NoteSpeedChaos} ({ModConfig.NoteSpeedChaosMin}-{ModConfig.NoteSpeedChaosMax}, perLane={ModConfig.NoteSpeedChaosPerLane}), " +
                    $"Hidden={ModConfig.Hidden}%, FadeIn={ModConfig.FadeIn}, FadeOut={ModConfig.FadeOut}, 표시 범위 {_baseRenderRange:F2} -> {range:F2}");
        }
    }

    /// <summary>Called once per frame: reads the renderer's scroll scale once instead of once per visible note.</summary>
    public static void OnFrame()
    {
        if (_notePatchesActive && ModConfig.AnyLaneCover && _renderer is not null)
            _laneScale = _renderer._scrollSpeed * _renderer._noteDistanceInterval;
    }

    public static void EndPlay()
    {
        try { SetNotePatches(false); }
        catch (Exception ex) { Core.DebugLog($"[NoteEffects] 동적 패치 해제 실패(무시): {ex.Message}"); }
        NoteFactor.Clear();
        LaneFactor.Clear();
        NoteLane.Clear();
        RestoreAndClearCovers();
        _renderer = null;
    }

    private static void RestoreAndClearCovers()
    {
        foreach (var c in Covers.Values)
        {
            if (c.Group != null && c.Alpha != 1f)
            {
                try { c.Group.alpha = 1f; }
                catch (Exception) { } // 씬 전환으로 이미 파괴된 CanvasGroup: 복원할 것이 없음
            }
        }
        Covers.Clear();
    }

    private static float Lerp(float min, float max) => min + (max - min) * (float)_random.NextDouble();

    // NoteObject._tickData의 네이티브 오프셋(처음 한 번 조회, 못 구하면 -1 → 인터롭 필드 읽기로 대체).
    // 인터롭 필드 읽기(note._tickData)는 호출마다 il2cpp_field_get_offset과 새 래퍼(GC 핸들 할당 + 종료자)를 만들어,
    // 노트별 속도 카오스에서는 보이는 노트마다 매 프레임 두 번(머리·길이 setter) 그 비용이 들었다.
    private static int _tickFieldOffset = int.MinValue;

    private static IntPtr TickPointer(NoteObject note, IntPtr notePtr)
    {
        if (_tickFieldOffset == int.MinValue)
        {
            int off = LambdaTranslator.GetIl2CppFieldOffset(typeof(NoteObject), "_tickData");
            // 객체 헤더(klass + monitor, 0x10) 뒤의 그럴듯한 범위만 믿는다. 아니면 인터롭 읽기로.
            _tickFieldOffset = off >= 0x10 && off < 0x1000 ? off : -1;
            if (_tickFieldOffset < 0)
                Melon<Core>.Logger.Warning("[NoteEffects] NoteObject._tickData 오프셋을 찾지 못해 느린 경로(인터롭 필드 읽기)로 동작");
        }
        if (_tickFieldOffset > 0)
            return System.Runtime.InteropServices.Marshal.ReadIntPtr(notePtr, _tickFieldOffset);
        var tick = note._tickData;
        return tick is not null ? tick.Pointer : IntPtr.Zero;
    }

    private static float Factor(NoteObject note, IntPtr notePtr)
    {
        if (!ModConfig.NoteSpeedChaos) return 1f;
        float min = ModConfig.NoteSpeedChaosMin, max = ModConfig.NoteSpeedChaosMax;

        if (ModConfig.NoteSpeedChaosPerLane)
        {
            // Notes are pooled per lane (one parent anchor per lane), so a NoteObject never changes lane: resolve
            // its lane anchor once instead of walking transform.parent every frame.
            if (!NoteLane.TryGetValue(notePtr, out int key))
            {
                var parent = note.transform.parent;
                NoteLane[notePtr] = key = parent is not null ? parent.GetInstanceID() : 0;
            }
            if (!LaneFactor.TryGetValue(key, out float f)) LaneFactor[key] = f = Lerp(min, max);
            return f;
        }

        // Pooled NoteObjects are reused for other notes: key by the note's tick, not by the object.
        IntPtr tick = TickPointer(note, notePtr);
        IntPtr id = tick != IntPtr.Zero ? tick : notePtr;
        if (!NoteFactor.TryGetValue(id, out float g)) NoteFactor[id] = g = Lerp(min, max);
        return g;
    }

    // The two NoteObject setter hooks run for every visible note every frame. Each IL2CPP Harmony detour costs a
    // trampoline plus a managed wrapper allocation, so they are only attached while a play actually uses
    // speed chaos / hidden / fade (no [HarmonyPatch] attribute: applied from SetNotePatches).
    private static bool _notePatchesActive;
    private static bool _unsafeWarned;

    private static MethodBase? HeadSetter() => AccessTools.PropertySetter(typeof(NoteObject), nameof(NoteObject.HeadPosition));
    private static MethodBase? LengthSetter() => AccessTools.PropertySetter(typeof(NoteObject), nameof(NoteObject.NoteLength));

    /// <summary>Lets the boot-time ICF scan cover these two dynamic targets too (Core registers them before patching).</summary>
    public static void RegisterDynamicTargets()
    {
        PatchResilience.DynamicPatchTargets.Add(HeadSetter);
        PatchResilience.DynamicPatchTargets.Add(LengthSetter);
    }

    private static void SetNotePatches(bool active)
    {
        if (active == _notePatchesActive) return;
        var harmony = Melon<Core>.Instance.HarmonyInstance;
        var head = HeadSetter();
        var length = LengthSetter();
        if (active)
        {
            // Small setters are the usual victims of identical-code folding; never detour a shared body.
            foreach (var m in new[] { head, length })
            {
                string reason = "대상 없음";
                if (m != null && PatchResilience.IsSafeToPatchNow(m, out reason)) continue;
                if (!_unsafeWarned)
                {
                    _unsafeWarned = true;
                    Melon<Core>.Logger.Warning($"[NoteEffects] 노트 위치 훅을 붙일 수 없어 속도 카오스/히든/페이드를 끔: {reason}");
                }
                return;
            }
            // 게임 업데이트로 인터롭 멤버·시그니처가 바뀌었으면 매 노트·매 프레임 트램펄린 오류/크래시가 나므로 붙이지 않음
            string? broken = PatchPreflight.Check(typeof(NoteObject_SetHeadPosition_Patch))
                             ?? PatchPreflight.Check(typeof(NoteObject_SetNoteLength_Patch));
            if (broken == null)
            {
                var sigWarnings = new List<string>();
                broken = PatchSignatureCheck.Check(typeof(NoteObject_SetHeadPosition_Patch), new MethodBase[] { head! }, sigWarnings)
                      ?? PatchSignatureCheck.Check(typeof(NoteObject_SetNoteLength_Patch), new MethodBase[] { length! }, sigWarnings);
                foreach (var w in sigWarnings)
                    Melon<Core>.Logger.Warning($"[NoteEffects] 시그니처 경고: {w}");
            }
            if (broken != null)
            {
                if (!_unsafeWarned)
                {
                    _unsafeWarned = true;
                    Melon<Core>.Logger.Error($"[NoteEffects] 사전 검사(JIT·시그니처) 실패로 속도 카오스/히든/페이드를 끔: {broken}");
                }
                return;
            }
            harmony.Patch(head, prefix: new HarmonyMethod(typeof(NoteObject_SetHeadPosition_Patch), nameof(NoteObject_SetHeadPosition_Patch.Prefix)));
            try
            {
                harmony.Patch(length, prefix: new HarmonyMethod(typeof(NoteObject_SetNoteLength_Patch), nameof(NoteObject_SetNoteLength_Patch.Prefix)));
            }
            catch
            {
                try { harmony.Unpatch(head, HarmonyPatchType.Prefix, harmony.Id); } catch (Exception ex) { Core.DebugLog($"[NoteEffects] HeadPosition 패치 해제 실패(무시): {ex.Message}"); }
                throw;
            }
        }
        else
        {
            if (head != null) harmony.Unpatch(head, HarmonyPatchType.Prefix, harmony.Id);
            if (length != null) harmony.Unpatch(length, HarmonyPatchType.Prefix, harmony.Id);
        }
        _notePatchesActive = active;
        Core.DebugLog($"[NoteEffects] NoteObject 위치 훅 {(active ? "부착" : "해제")}");
    }

    public static class NoteObject_SetHeadPosition_Patch
    {
        public static void Prefix(NoteObject __instance, ref float value)
        {
            if (!PlayAssist.IsPlaying) return;
            // Pointer는 GC 핸들 조회(네이티브 호출)라 노트·프레임마다 한 번만 읽어 넘긴다(전에는 최대 4번).
            IntPtr ptr = __instance.Pointer;
            if (ModConfig.NoteSpeedChaos) value *= Factor(__instance, ptr);
            ApplyLaneCover(__instance, ptr, value);
        }
    }

    public static class NoteObject_SetNoteLength_Patch
    {
        public static void Prefix(NoteObject __instance, ref float value)
        {
            if (PlayAssist.IsPlaying && ModConfig.NoteSpeedChaos) value *= Factor(__instance, __instance.Pointer);
        }
    }

    private sealed class Cover
    {
        public CanvasGroup Group = null!; // cleared with Covers on every Register (pooled notes live for one play)
        public float Alpha = -1f;
    }

    private static void ApplyLaneCover(NoteObject note, IntPtr notePtr, float head)
    {
        bool active = ModConfig.AnyLaneCover;
        // 속도 카오스만 쓰는 플레이: 가린 노트가 하나도 없으면 노트·프레임마다 사전 조회도 하지 않음
        if (!active && Covers.Count == 0) return;

        Cover? cover = null;
        if (!Covers.TryGetValue(notePtr, out cover))
        {
            if (!active) return; // never touched, nothing to restore
            var group = note.gameObject.GetComponent<CanvasGroup>() ?? note.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Covers[notePtr] = cover = new Cover { Group = group };
        }

        float alpha = 1f;
        if (active)
        {
            float full = _baseRenderRange * _laneScale;
            if (full > 1f)
            {
                float pct = Math.Clamp(head / full * 100f, 0f, 100f);
                if (ModConfig.Hidden > 0f)
                {
                    float soft = ModConfig.LaneCoverSoftness / full * 100f;
                    alpha = Math.Min(alpha, soft > 0.01f ? Overlay.Clamp01((pct - ModConfig.Hidden) / soft + 0.5f) : (pct >= ModConfig.Hidden ? 1f : 0f));
                }
                if (ModConfig.FadeIn)
                    alpha = Math.Min(alpha, Overlay.Clamp01((ModConfig.FadeInStart - pct) / ModConfig.FadeInLength));
                if (ModConfig.FadeOut)
                    alpha = Math.Min(alpha, Overlay.Clamp01((pct - (ModConfig.FadeOutStart - ModConfig.FadeOutLength)) / ModConfig.FadeOutLength));
            }
        }

        if (cover.Alpha == alpha) return; // most notes sit fully visible or fully hidden: skip the IL2CPP call
        cover.Group.alpha = alpha;
        cover.Alpha = alpha;
    }
}
