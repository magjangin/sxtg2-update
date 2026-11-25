using System;
using System.Collections.Generic;
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
        /// <summary>이 풀 노트에 마지막으로 입힌 스킨(없으면 프리팹 그대로).</summary>
        public Skin? Applied;
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

    private static IntPtr _rendererPtr, _spawnRendererPtr;
    private static IngameUIRenderer? _ui;
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
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} {m.Name} 델리게이트 바인딩 실패: {ex.Message}");
            }
        }
        else
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} IngameUIRenderer.GetNoteReferance/GetNoteReference 메서드를 찾지 못함");
        }
        // 직접 호출 폴백을 두면 게임 업데이트로 오타가 수정됐을 때 PatchPreflight가 공유 스폰 훅 전체를 탈락시키므로 null 반환
        return (_, _) => null;
    }

    private static string SuffixOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (var s in Suffixes)
            if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return s;
        return "";
    }

    private static Image[] ImagesOf(RectTransform? part)
    {
        if (part is null) return Array.Empty<Image>();
        var src = part.GetComponentsInChildren<Image>(true);
        if (src is null || src.Length == 0) return Array.Empty<Image>();
        var arr = new Image[src.Length];
        for (int i = 0; i < src.Length; i++) arr[i] = src[i];
        return arr;
    }

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
        IntPtr uiPtr = ui.Pointer;
        if (uiPtr == _rendererPtr) return;
        SkinsBySuffix.Clear();
        InstanceParts.Clear();
        LaneSuffix.Clear();

        var prefabs = ui.notePackage?.noteObjects;
        if (prefabs == null) return;
        _rendererPtr = uiPtr;
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

    /// <summary>
    /// 플레이 끝(PlayAssist.EndPlay): 다음 플레이의 렌더러가 GC로 비운 같은 주소에 생겨도 지난 플레이의 UI 렌더러·목록을
    /// 쓰지 않게 기억을 지운다. 노트 Image 목록(InstanceParts)도 플레이마다 새 풀이므로 함께 비운다.
    /// </summary>
    public static void EndPlay()
    {
        _rendererPtr = _spawnRendererPtr = IntPtr.Zero;
        _ui = null;
        InstanceParts.Clear();
        SkinsBySuffix.Clear();
        LaneSuffix.Clear();
    }

    /// <summary>
    /// 스폰마다 Image 하나에 setter 5개(IL2CPP 호출)를 불렀다. 배치 속성(type·preserveAspect)은 게임이 플레이 중
    /// 바꾸지 않으므로, 이 노트에 지난번 입힌 모양(<paramref name="prev"/>)과 같으면 건너뛴다. 스프라이트·색·재질은
    /// 게임이 다른 이유로 바꿀 수 있어 항상 다시 쓴다(2026-11-25).
    /// </summary>
    private static void Copy(Look[] from, Look[]? prev, Image[] to)
    {
        int n = Math.Min(from.Length, to.Length);
        for (int i = 0; i < n; i++)
        {
            var src = from[i];
            var dst = to[i];
            if (!src.Valid || dst is null) continue; // dst는 이 플레이의 풀 노트 Image(GetComponentsInChildren 결과)
            bool known = prev != null && i < prev.Length && prev[i].Valid;
            dst.sprite = src.Sprite;
            if (!known || prev![i].Type != src.Type) dst.type = src.Type;
            dst.color = src.Color;
            dst.material = src.Material;
            if (!known || prev![i].PreserveAspect != src.PreserveAspect) dst.preserveAspect = src.PreserveAspect;
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
            // 렌더러·UI 렌더러는 플레이마다 하나: 노트마다 UIRenderer 속성(IL2CPP 호출)을 읽지 않게 렌더러 포인터로 기억
            // Pointer는 GC 핸들 조회(네이티브 호출): 스폰마다 한 번씩만 읽는다
            IntPtr rendererPtr = renderer.Pointer;
            if (rendererPtr != _spawnRendererPtr)
            {
                _spawnRendererPtr = rendererPtr;
                _ui = renderer.UIRenderer;
            }
            var ui = _ui;
            if (ui is null) return; // 관리 코드 비교(노트마다 IL2CPP 호출 없음): _ui는 플레이마다 새로 읽고 EndPlay에서 비움
            EnsureCatalog(ui);

            // The lane prefab is fixed for the whole play; resolve its suffix once per lane.
            if (!LaneSuffix.TryGetValue(index, out var suffix))
            {
                var reference = GetNoteRef(ui, index);
                LaneSuffix[index] = suffix = reference != null ? SuffixOf(reference.name) : "";
            }
            if (!SkinsBySuffix.TryGetValue(suffix, out var skins) || skins.Count == 0) return;

            IntPtr notePtr = note.Pointer;
            if (!InstanceParts.TryGetValue(notePtr, out var target))
                InstanceParts[notePtr] = target = PartsOf(note);

            var source = skins[Rng.Next(skins.Count)];
            var prev = target.Applied;
            Copy(source.Head, prev?.Head, target.Head);
            Copy(source.Body, prev?.Body, target.Body);
            Copy(source.Hold, prev?.Hold, target.Hold);
            Copy(source.Tail, prev?.Tail, target.Tail);
            target.Applied = source;
        }
        catch (Exception ex)
        {
            if (_warned) return;
            _warned = true;
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 적용 실패 (이후 같은 오류는 생략): {ex.Message}");
        }
    }
}
