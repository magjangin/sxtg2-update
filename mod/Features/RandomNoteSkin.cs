using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// RandomNoteSkin: every note that appears borrows its images from a random note skin of the game.
///
/// IngameUIRenderer.notePackage holds the NoteObject prefabs of every skin, named "{skin}" (blue),
/// "{skin}_red" and "{skin}_gate"; SetupNoteReferance / GetNoteReferance(lane) pick one per lane from the skin
/// option and the per-lane NoteTypeLane* options. StartrailPatternRenderer.GetNoteObejct hands out pooled (or new)
/// instances of that lane prefab, so its postfix copies the head / body / hold / tail images of a random prefab with
/// the same suffix onto the instance. Pooled instances are re-skinned on every spawn.
/// </summary>
public static class RandomNoteSkin
{
    private const string LOG_PREFIX = "[RandomNoteSkin]";
    private static readonly string[] Suffixes = { "_red", "_gate" };
    private static readonly string[] Effectors = { "barline", "startline", "gatealarm" };

    private sealed class Parts
    {
        public Image[] Head = Array.Empty<Image>(), Body = Array.Empty<Image>(), Hold = Array.Empty<Image>(), Tail = Array.Empty<Image>();
    }

    private static IntPtr _rendererPtr;
    private static readonly Dictionary<string, List<Parts>> SkinsBySuffix = new();
    private static readonly Dictionary<IntPtr, Parts> InstanceParts = new();
    private static readonly Dictionary<LaneIndex, string> LaneSuffix = new();
    private static readonly System.Random Rng = new();
    private static bool _warned;

    private static string SuffixOf(string name)
    {
        foreach (var s in Suffixes)
            if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return s;
        return "";
    }

    private static Image[] ImagesOf(RectTransform? part)
        => part == null ? Array.Empty<Image>() : part.GetComponentsInChildren<Image>(true).ToArray();

    private static Parts PartsOf(NoteObject note) => new()
    {
        Head = ImagesOf(note.headImage),
        Body = ImagesOf(note.bodyImage),
        Hold = ImagesOf(note.holdImage),
        Tail = ImagesOf(note.tailImage),
    };

    /// <summary>Catalog the skins once per play (a new IngameUIRenderer comes with every play scene).</summary>
    private static void EnsureCatalog(IngameUIRenderer ui)
    {
        if (ui.Pointer == _rendererPtr) return;
        _rendererPtr = ui.Pointer;
        SkinsBySuffix.Clear();
        InstanceParts.Clear();
        LaneSuffix.Clear();

        var prefabs = ui.notePackage?.noteObjects;
        if (prefabs == null) return;
        var names = new List<string>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            if (prefab == null) continue;
            string name = prefab.name;
            bool effector = false;
            foreach (var e in Effectors) effector |= name.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0;
            if (effector) continue;

            string suffix = SuffixOf(name);
            if (!SkinsBySuffix.TryGetValue(suffix, out var list)) SkinsBySuffix[suffix] = list = new List<Parts>();
            list.Add(PartsOf(prefab));
            names.Add(name);
        }
        Melon<Core>.Logger.Msg($"{LOG_PREFIX} {names.Count} note prefabs: {string.Join(", ", names)}");
    }

    private static void Copy(Image[] from, Image[] to)
    {
        int n = Math.Min(from.Length, to.Length);
        for (int i = 0; i < n; i++)
        {
            var src = from[i];
            var dst = to[i];
            if (src == null || dst == null) continue;
            dst.sprite = src.sprite;
            dst.type = src.type;
            dst.color = src.color;
            dst.material = src.material;
            dst.preserveAspect = src.preserveAspect;
        }
    }

    // Registered after CustomNoteSkin's postfix (Priority.Low) — when RandomNoteSkin is on, custom PNGs are not used.
    [HarmonyPatch(typeof(StartrailPatternRenderer), "GetNoteObejct")]
    public static class StartrailPatternRenderer_GetNoteObejct_Patch
    {
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(StartrailPatternRenderer __instance, LaneIndex index, NoteObject __result)
        {
            if (!ModConfig.RandomNoteSkin || __result == null) return;
            try
            {
                var ui = __instance.UIRenderer;
                if (ui == null) return;
                EnsureCatalog(ui);

                // The lane prefab is fixed for the whole play; resolve its suffix once per lane.
                if (!LaneSuffix.TryGetValue(index, out var suffix))
                {
                    var reference = ui.GetNoteReferance(index);
                    LaneSuffix[index] = suffix = reference != null ? SuffixOf(reference.name) : "";
                }
                if (!SkinsBySuffix.TryGetValue(suffix, out var skins) || skins.Count == 0) return;

                if (!InstanceParts.TryGetValue(__result.Pointer, out var target))
                    InstanceParts[__result.Pointer] = target = PartsOf(__result);

                var source = skins[Rng.Next(skins.Count)];
                Copy(source.Head, target.Head);
                Copy(source.Body, target.Body);
                Copy(source.Hold, target.Hold);
                Copy(source.Tail, target.Tail);
            }
            catch (Exception ex)
            {
                if (_warned) return;
                _warned = true;
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} Apply failed (further errors suppressed): {ex.Message}");
            }
        }
    }
}
