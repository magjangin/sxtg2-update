using System;
using System.Collections.Generic;
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
            NoteFactor.Clear();
            LaneFactor.Clear();
            NoteLane.Clear();
            Covers.Clear();
            _random = new System.Random();

            if (_renderer == null || _renderer.Pointer != __instance.Pointer)
                _baseRenderRange = __instance.renderRange;
            _renderer = __instance;
            _laneScale = __instance._scrollSpeed * __instance._noteDistanceInterval;

            float range = _baseRenderRange;
            if (ModConfig.NoteSpeedChaos && ModConfig.NoteSpeedChaosMin < 1f)
                range = _baseRenderRange / ModConfig.NoteSpeedChaosMin;
            __instance.renderRange = range;

            bool needed = ModConfig.NoteSpeedChaos || ModConfig.AnyLaneCover;
            try { SetNotePatches(needed); }
            catch (Exception ex) { Melon<Core>.Logger.Error($"[NoteEffects] Could not (un)patch NoteObject setters: {ex.Message}"); }
            if (needed)
                Melon<Core>.Logger.Msg($"[NoteEffects] SpeedChaos={ModConfig.NoteSpeedChaos} ({ModConfig.NoteSpeedChaosMin}-{ModConfig.NoteSpeedChaosMax}, perLane={ModConfig.NoteSpeedChaosPerLane}), " +
                    $"Hidden={ModConfig.Hidden}%, FadeIn={ModConfig.FadeIn}, FadeOut={ModConfig.FadeOut}, renderRange {_baseRenderRange:F2} -> {range:F2}");
        }
    }

    /// <summary>Called once per frame: reads the renderer's scroll scale once instead of once per visible note.</summary>
    public static void OnFrame()
    {
        if (_notePatchesActive && ModConfig.AnyLaneCover && _renderer != null)
            _laneScale = _renderer._scrollSpeed * _renderer._noteDistanceInterval;
    }

    private static float Lerp(float min, float max) => min + (max - min) * (float)_random.NextDouble();

    private static float Factor(NoteObject note)
    {
        if (!ModConfig.NoteSpeedChaos) return 1f;
        float min = ModConfig.NoteSpeedChaosMin, max = ModConfig.NoteSpeedChaosMax;

        if (ModConfig.NoteSpeedChaosPerLane)
        {
            // Notes are pooled per lane (one parent anchor per lane), so a NoteObject never changes lane: resolve
            // its lane anchor once instead of walking transform.parent every frame.
            if (!NoteLane.TryGetValue(note.Pointer, out int key))
            {
                var parent = note.transform.parent;
                NoteLane[note.Pointer] = key = parent != null ? parent.GetInstanceID() : 0;
            }
            if (!LaneFactor.TryGetValue(key, out float f)) LaneFactor[key] = f = Lerp(min, max);
            return f;
        }

        // Pooled NoteObjects are reused for other notes: key by the note's tick, not by the object.
        var tick = note._tickData;
        IntPtr id = tick != null ? tick.Pointer : note.Pointer;
        if (!NoteFactor.TryGetValue(id, out float g)) NoteFactor[id] = g = Lerp(min, max);
        return g;
    }

    // The two NoteObject setter hooks run for every visible note every frame. Each IL2CPP Harmony detour costs a
    // trampoline plus a managed wrapper allocation, so they are only attached while a play actually uses
    // speed chaos / hidden / fade (no [HarmonyPatch] attribute: applied from SetNotePatches).
    private static bool _notePatchesActive;

    private static void SetNotePatches(bool active)
    {
        if (active == _notePatchesActive) return;
        var harmony = Melon<Core>.Instance.HarmonyInstance;
        var head = AccessTools.PropertySetter(typeof(NoteObject), nameof(NoteObject.HeadPosition));
        var length = AccessTools.PropertySetter(typeof(NoteObject), nameof(NoteObject.NoteLength));
        if (active)
        {
            harmony.Patch(head, prefix: new HarmonyMethod(typeof(NoteObject_SetHeadPosition_Patch), nameof(NoteObject_SetHeadPosition_Patch.Prefix)));
            harmony.Patch(length, prefix: new HarmonyMethod(typeof(NoteObject_SetNoteLength_Patch), nameof(NoteObject_SetNoteLength_Patch.Prefix)));
        }
        else
        {
            harmony.Unpatch(head, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(length, HarmonyPatchType.Prefix, harmony.Id);
        }
        _notePatchesActive = active;
        Core.DebugLog($"[NoteEffects] NoteObject position hooks {(active ? "attached" : "detached")}");
    }

    public static class NoteObject_SetHeadPosition_Patch
    {
        public static void Prefix(NoteObject __instance, ref float value)
        {
            if (!PlayAssist.IsPlaying) return;
            if (ModConfig.NoteSpeedChaos) value *= Factor(__instance);
            ApplyLaneCover(__instance, value);
        }
    }

    public static class NoteObject_SetNoteLength_Patch
    {
        public static void Prefix(NoteObject __instance, ref float value)
        {
            if (PlayAssist.IsPlaying && ModConfig.NoteSpeedChaos) value *= Factor(__instance);
        }
    }

    private sealed class Cover
    {
        public CanvasGroup Group = null!; // cleared with Covers on every Register (pooled notes live for one play)
        public float Alpha = -1f;
    }

    private static void ApplyLaneCover(NoteObject note, float head)
    {
        bool active = ModConfig.AnyLaneCover;
        if (!active && !Covers.ContainsKey(note.Pointer)) return; // never touched, nothing to restore

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
                    alpha = Math.Min(alpha, soft > 0.01f ? Clamp01((pct - ModConfig.Hidden) / soft + 0.5f) : (pct >= ModConfig.Hidden ? 1f : 0f));
                }
                if (ModConfig.FadeIn)
                    alpha = Math.Min(alpha, Clamp01((ModConfig.FadeInStart - pct) / ModConfig.FadeInLength));
                if (ModConfig.FadeOut)
                    alpha = Math.Min(alpha, Clamp01((pct - (ModConfig.FadeOutStart - ModConfig.FadeOutLength)) / ModConfig.FadeOutLength));
            }
        }

        if (!Covers.TryGetValue(note.Pointer, out var cover))
        {
            var group = note.gameObject.GetComponent<CanvasGroup>() ?? note.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Covers[note.Pointer] = cover = new Cover { Group = group };
        }
        if (cover.Alpha == alpha) return; // most notes sit fully visible or fully hidden: skip the IL2CPP call
        cover.Group.alpha = alpha;
        cover.Alpha = alpha;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
