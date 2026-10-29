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
    private static readonly Dictionary<IntPtr, CanvasGroup> Groups = new();
    private static System.Random _random = new();

    [HarmonyPatch(typeof(StartrailPatternRenderer), nameof(StartrailPatternRenderer.Register))]
    public static class StartrailPatternRenderer_Register_Patch
    {
        public static void Postfix(StartrailPatternRenderer __instance)
        {
            ModConfig.ReloadIfChanged();
            NoteFactor.Clear();
            LaneFactor.Clear();
            Groups.Clear();
            _random = new System.Random();

            if (_renderer == null || _renderer.Pointer != __instance.Pointer)
                _baseRenderRange = __instance.renderRange;
            _renderer = __instance;

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

    private static float Factor(NoteObject note)
    {
        if (!ModConfig.NoteSpeedChaos) return 1f;
        float min = ModConfig.NoteSpeedChaosMin, max = ModConfig.NoteSpeedChaosMax;

        if (ModConfig.NoteSpeedChaosPerLane)
        {
            // Notes of one lane share a parent anchor (LaneComposer.GetNoteAnchor) and keep their order.
            var parent = note.transform.parent;
            int key = parent != null ? parent.GetInstanceID() : 0;
            if (!LaneFactor.TryGetValue(key, out float f))
                LaneFactor[key] = f = Mathf.Lerp(min, max, (float)_random.NextDouble());
            return f;
        }

        // Pooled NoteObjects are reused for other notes: key by the note's tick, not by the object.
        var tick = note._tickData;
        IntPtr id = tick != null ? tick.Pointer : note.Pointer;
        if (!NoteFactor.TryGetValue(id, out float g))
            NoteFactor[id] = g = Mathf.Lerp(min, max, (float)_random.NextDouble());
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

    private static void ApplyLaneCover(NoteObject note, float head)
    {
        bool active = ModConfig.AnyLaneCover;
        if (!active && !Groups.ContainsKey(note.Pointer)) return; // never touched, nothing to restore

        float alpha = 1f;
        if (active && _renderer != null)
        {
            float full = _baseRenderRange * _renderer._scrollSpeed * _renderer._noteDistanceInterval;
            if (full > 1f)
            {
                float pct = Mathf.Clamp(head / full * 100f, 0f, 100f);
                if (ModConfig.Hidden > 0f)
                {
                    float soft = ModConfig.LaneCoverSoftness / full * 100f;
                    alpha = Mathf.Min(alpha, soft > 0.01f ? Mathf.Clamp01((pct - ModConfig.Hidden) / soft + 0.5f) : (pct >= ModConfig.Hidden ? 1f : 0f));
                }
                if (ModConfig.FadeIn)
                    alpha = Mathf.Min(alpha, Mathf.Clamp01((ModConfig.FadeInStart - pct) / ModConfig.FadeInLength));
                if (ModConfig.FadeOut)
                    alpha = Mathf.Min(alpha, Mathf.Clamp01((pct - (ModConfig.FadeOutStart - ModConfig.FadeOutLength)) / ModConfig.FadeOutLength));
            }
        }

        if (!Groups.TryGetValue(note.Pointer, out var group) || group == null)
        {
            group = note.gameObject.GetComponent<CanvasGroup>() ?? note.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Groups[note.Pointer] = group;
        }
        group.alpha = alpha;
    }
}
