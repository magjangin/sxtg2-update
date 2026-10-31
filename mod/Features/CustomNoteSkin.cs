using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppSixtarGate.Play;
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
    public static bool HasSprites => Sprites.Count > 0;

    public static void Initialize(string gameFolder)
    {
        FolderPath = Path.Combine(gameFolder, "CustomNotes");
        try
        {
            Directory.CreateDirectory(FolderPath);
            foreach (var file in Directory.GetFiles(FolderPath, "*.png"))
            {
                var sprite = MediaOverrideManager.LoadSpriteFromFile(file);
                if (sprite == null) continue;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite.texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Sprites[Path.GetFileNameWithoutExtension(file)] = sprite;
            }
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} Loaded {Sprites.Count} sprite(s) from {FolderPath}: {string.Join(", ", Sprites.Keys)}");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Failed loading CustomNotes: {ex.Message}");
        }
    }

    public static void ResetForNewPlay() => Skinned.Clear();

    private static Sprite? Get(string name) => Sprites.TryGetValue(name, out var s) ? s : null;

    // LaneIndex: LL 0, L 1, R 2, RR 3, Gate 4, LT 5, RT 6.
    private static string ColorOf(int lane) => lane switch { 5 or 6 => "Red", 4 => "Gate", _ => "Blue" };

    private static Sprite? HeadSprite(string color) => Get(color) ?? (color == "Gate" ? Get("Blue") : null);
    private static Sprite? PartSprite(string color, string part) => Get(color + part) ?? Get(part + color) ?? Get(part);

    // Typo-tolerant: resolves "GetNoteObejct" or "GetNoteObject" if fixed in a future update.
    [HarmonyPatch]
    public static class StartrailPatternRenderer_GetNoteObejct_Patch
    {
        public static MethodBase? TargetMethod()
            => PatchResilience.ResolveMethod(typeof(StartrailPatternRenderer), "GetNoteObejct", "GetNoteObject");

        public static void Postfix(LaneIndex index, NoteObject __result)
        {
            if (!ModConfig.CustomNotes || ModConfig.RandomNoteSkin || !HasSprites || __result == null) return;
            if (!Skinned.Add(__result.Pointer)) return; // pooled object already skinned this play

            try
            {
                string color = ColorOf((int)index);
                Apply(__result.headImage, HeadSprite(color));
                Apply(__result.tailImage, PartSprite(color, "Tail"));
                var hold = PartSprite(color, "Hold");
                Apply(__result.bodyImage, hold);
                Apply(__result.holdImage, hold);
                if (Skinned.Count == 1)
                    Melon<Core>.Logger.Msg($"{LOG_PREFIX} Applying custom sprites to notes (first: lane {index}, '{__result.gameObject.name}')");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} Apply failed: {ex.Message}");
            }
        }

        private static void Apply(RectTransform? target, Sprite? sprite)
        {
            if (target == null || sprite == null) return;
            var img = target.GetComponent<Image>() ?? target.GetComponentInChildren<Image>(true);
            if (img != null) img.sprite = sprite;
        }
    }
}
