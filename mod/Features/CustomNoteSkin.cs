using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Loaders;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// sxtg2 custom note skin: PNGs in {game}\CustomNotes replace the renewal NoteObject images.
///   Blue.png  -> LL / L / R / RR heads      Red.png -> LT / RT heads      Gate.png -> Gate head (falls back to Blue)
///   Tail.png / BlueTail.png / RedTail.png  -> long-note end cap (tailImage)
///   Hold.png / BlueHold.png / RedHold.png  -> long-note body (bodyImage, holdImage)
/// Hook: StartrailPatternRenderer.GetNoteObejct(LaneIndex, Transform) — notes are pooled per lane, so the lane
/// passed in is the note's lane. (NoteObject.SetTickData is shared by 102 folded methods and must not be patched.)
/// </summary>
public static class CustomNoteSkin
{
    private const string LOG_PREFIX = "[CustomNoteSkin]";
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<IntPtr> Skinned = new();
    public static string FolderPath { get; private set; } = "";

    /// <summary>작업 스레드에서 해독한 PNG(실패하면 Rgba null → 메인 스레드에서 LoadImage로 다시).</summary>
    private sealed record Decoded(string Name, string Path, int Width, int Height, byte[]? Rgba);
    private static System.Threading.Tasks.Task<List<Decoded>>? _decode;
    private static bool _loaded;

    /// <summary>
    /// PNG 해독은 부팅 때 작업 스레드에서 시작하고, 텍스처 올리기는 커스텀 노트를 쓰는 첫 플레이의 준비 때(<see cref="PrepareForPlay"/>) 한다.
    /// 전에는 부팅 메인 스레드에서 LoadImage로 동기 해독해 2장에 약 0.27초가 들었고, CustomNotes=0이어도 했다.
    /// </summary>
    public static void Initialize(string gameFolder)
    {
        FolderPath = Path.Combine(gameFolder, "CustomNotes");
        try
        {
            Directory.CreateDirectory(FolderPath);
            string[] files = Directory.GetFiles(FolderPath, "*.png");
            if (files.Length > 0)
                _decode = System.Threading.Tasks.Task.Run(() => DecodeAll(files));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} CustomNotes 로드 실패: {ex.Message}");
        }
    }

    // 작업 스레드: 파일 읽기와 PngDecoder만(Unity 호출 없음). 해독 못 하는 PNG(16비트·인터레이스)는 Rgba를 비워 둔다.
    private static List<Decoded> DecodeAll(string[] files)
    {
        var list = new List<Decoded>(files.Length);
        foreach (var file in files)
        {
            byte[]? rgba = null;
            int w = 0, h = 0;
            try
            {
                if (PngDecoder.TryDecode(File.ReadAllBytes(file), 0, out w, out h, out var pixels)) rgba = pixels;
            }
            catch { }
            list.Add(new Decoded(Path.GetFileNameWithoutExtension(file), file, w, h, rgba));
        }
        return list;
    }

    /// <summary>
    /// 플레이 준비(StartrailPatternRenderer.Register, 노트가 나오기 전)에서 부른다: 커스텀 노트를 쓸 플레이면 스프라이트를
    /// 미리 올려, 텍스처 첫 생성 비용이 첫 노트가 나오는 순간에 걸리지 않게 한다.
    /// </summary>
    public static void PrepareForPlay()
    {
        ResetForNewPlay();
        if (!_loaded && ModConfig.CustomNotes && !ModConfig.RandomNoteSkin) EnsureLoaded();
    }

    /// <summary>해독해 둔 PNG를 스프라이트로 올린다(처음 한 번, 메인 스레드). 스프라이트가 하나라도 있으면 true.</summary>
    private static bool EnsureLoaded()
    {
        if (_loaded) return Sprites.Count > 0;
        _loaded = true;
        var decode = _decode;
        _decode = null;
        if (decode == null) return false;
        try
        {
            foreach (var d in decode.Result)
            {
                Sprite? sprite = null;
                if (d.Rgba != null && MediaOverrideManager.TextureFromRgba(d.Width, d.Height, d.Rgba) is { } tex)
                {
                    try { sprite = Overlay.MakeSprite(tex); }
                    catch { sprite = null; }
                    if (sprite == null) UnityEngine.Object.Destroy(tex);
                }
                sprite ??= MediaOverrideManager.LoadSpriteFromFile(d.Path);
                if (sprite != null) Sprites[d.Name] = sprite;
            }
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} {FolderPath}에서 스프라이트 {Sprites.Count}개 로드: {string.Join(", ", Sprites.Keys)}");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} CustomNotes 로드 실패: {ex.GetBaseException().Message}");
        }
        return Sprites.Count > 0;
    }

    public static void ResetForNewPlay() => Skinned.Clear();

    private static Sprite? Get(string name) => Sprites.TryGetValue(name, out var s) ? s : null;

    // LaneIndex: LL 0, L 1, R 2, RR 3, Gate 4, LT 5, RT 6.
    private static string ColorOf(int lane) => lane switch { 5 or 6 => "Red", 4 => "Gate", _ => "Blue" };

    private static Sprite? HeadSprite(string color) => Get(color) ?? (color == "Gate" ? Get("Blue") : null);
    private static Sprite? PartSprite(string color, string part) => Get(color + part) ?? Get(part + color) ?? Get(part);

    /// <summary>
    /// 노트 스폰 훅 하나로 커스텀 PNG 스킨과 랜덤 스킨을 함께 처리한다. 전에는 두 기능이 같은 대상
    /// (GetNoteObejct)에 패치 클래스를 따로 붙여 Harmony 적용이 한 번 더 들고, 노트마다 Postfix 두 개가 불렸다.
    /// 두 기능은 동시에 쓰이지 않는다(RandomNoteSkin을 켜면 CustomNotes는 쓰이지 않음).
    /// Typo-tolerant: resolves "GetNoteObejct" or "GetNoteObject" if fixed in a future update.
    /// </summary>
    [HarmonyPatch]
    public static class StartrailPatternRenderer_GetNoteObejct_Patch
    {
        public static MethodBase? TargetMethod()
            => PatchResilience.ResolveMethod(typeof(StartrailPatternRenderer), "GetNoteObejct", "GetNoteObject");

        public static void Postfix(StartrailPatternRenderer __instance, LaneIndex index, NoteObject __result)
        {
            if (__result == null) return;
            if (ModConfig.RandomNoteSkin) RandomNoteSkin.OnNoteSpawned(__instance, index, __result);
            else if (ModConfig.CustomNotes && EnsureLoaded()) ApplyCustom(index, __result);
        }
    }

    private static void ApplyCustom(LaneIndex index, NoteObject note)
    {
        if (!Skinned.Add(note.Pointer)) return; // pooled object already skinned this play

        try
        {
            string color = ColorOf((int)index);
            Apply(note.headImage, HeadSprite(color));
            Apply(note.tailImage, PartSprite(color, "Tail"));
            var hold = PartSprite(color, "Hold");
            Apply(note.bodyImage, hold);
            Apply(note.holdImage, hold);
            if (Skinned.Count == 1)
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 노트에 커스텀 스프라이트 적용 시작 (첫 노트: 레인 {index}, '{note.gameObject.name}')");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 적용 실패: {ex.Message}");
        }
    }

    private static void Apply(RectTransform? target, Sprite? sprite)
    {
        if (target == null || sprite == null) return;
        var img = target.GetComponent<Image>() ?? target.GetComponentInChildren<Image>(true);
        if (img != null) img.sprite = sprite;
    }
}
