using System;
using System.Collections.Generic;
using System.Linq;
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
/// instances of that lane prefab, so its postfix (shared with CustomNoteSkin) copies the head / body / hold / tail
/// image looks of a random prefab with the same suffix onto the instance. Pooled instances are re-skinned on every spawn.
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

    /// <summary>
    /// 프리팹 Image의 모양 값. 프리팹은 플레이 중 바뀌지 않으므로 목록을 만들 때 한 번 읽어 둔다.
    /// 전에는 노트가 나올 때마다 원본 Image 속성 5개를 다시 읽어 IL2CPP 호출이 두 배였다.
    /// </summary>
    private readonly record struct Look(bool Valid, Sprite? Sprite, Image.Type Type, Color Color, Material? Material, bool PreserveAspect);

    private sealed class Skin
    {
        public Look[] Head = Array.Empty<Look>(), Body = Array.Empty<Look>(), Hold = Array.Empty<Look>(), Tail = Array.Empty<Look>();
    }

    private static IntPtr _rendererPtr;
    private static readonly Dictionary<string, List<Skin>> SkinsBySuffix = new();
    private static readonly Dictionary<IntPtr, Parts> InstanceParts = new();
    private static readonly Dictionary<LaneIndex, string> LaneSuffix = new();
    private static readonly System.Random Rng = new();
    private static bool _warned;

    // Typo-tolerant fallback if GetNoteReferance is renamed to GetNoteReference in a future game update.
    private static readonly Func<IngameUIRenderer, LaneIndex, NoteObject?> GetNoteRef = BindGetNoteReference();

    private static Func<IngameUIRenderer, LaneIndex, NoteObject?> BindGetNoteReference()
    {
        var m = PatchResilience.ResolveMethod(typeof(IngameUIRenderer), new[] { typeof(LaneIndex) }, "GetNoteReferance", "GetNoteReference");
        if (m != null)
        {
            try
            {
                return (Func<IngameUIRenderer, LaneIndex, NoteObject?>)Delegate.CreateDelegate(
                    typeof(Func<IngameUIRenderer, LaneIndex, NoteObject?>), m);
            }
            catch { }
        }
        return (ui, lane) => ui.GetNoteReferance(lane);
    }

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

    private static Look[] LooksOf(Image[] images)
    {
        var looks = new Look[images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            // null 칸(Valid=false)은 대상 쪽 같은 칸을 건너뜀(전과 같은 짝 맞춤)
            looks[i] = img == null ? default : new Look(true, img.sprite, img.type, img.color, img.material, img.preserveAspect);
        }
        return looks;
    }

    private static Skin SkinOf(NoteObject prefab)
    {
        var parts = PartsOf(prefab);
        return new Skin { Head = LooksOf(parts.Head), Body = LooksOf(parts.Body), Hold = LooksOf(parts.Hold), Tail = LooksOf(parts.Tail) };
    }

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
            if (!SkinsBySuffix.TryGetValue(suffix, out var list)) SkinsBySuffix[suffix] = list = new List<Skin>();
            list.Add(SkinOf(prefab));
            names.Add(name);
        }
        Melon<Core>.Logger.Msg($"{LOG_PREFIX} 노트 프리팹 {names.Count}개: {string.Join(", ", names)}");
    }

    private static void Copy(Look[] from, Image[] to)
    {
        int n = Math.Min(from.Length, to.Length);
        for (int i = 0; i < n; i++)
        {
            var src = from[i];
            var dst = to[i];
            if (!src.Valid || dst == null) continue;
            dst.sprite = src.Sprite;
            dst.type = src.Type;
            dst.color = src.Color;
            dst.material = src.Material;
            dst.preserveAspect = src.PreserveAspect;
        }
    }

    /// <summary>
    /// 노트 스폰 훅(<see cref="CustomNoteSkin.StartrailPatternRenderer_GetNoteObejct_Patch"/>)에서 RandomNoteSkin이
    /// 켜져 있을 때 불린다. 풀에서 다시 나온 노트도 매번 새로 입힌다.
    /// </summary>
    public static void OnNoteSpawned(StartrailPatternRenderer renderer, LaneIndex index, NoteObject note)
    {
        try
        {
            var ui = renderer.UIRenderer;
            if (ui == null) return;
            EnsureCatalog(ui);

            // The lane prefab is fixed for the whole play; resolve its suffix once per lane.
            if (!LaneSuffix.TryGetValue(index, out var suffix))
            {
                var reference = GetNoteRef(ui, index);
                LaneSuffix[index] = suffix = reference != null ? SuffixOf(reference.name) : "";
            }
            if (!SkinsBySuffix.TryGetValue(suffix, out var skins) || skins.Count == 0) return;

            if (!InstanceParts.TryGetValue(note.Pointer, out var target))
                InstanceParts[note.Pointer] = target = PartsOf(note);

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
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 적용 실패 (이후 같은 오류는 생략): {ex.Message}");
        }
    }
}
