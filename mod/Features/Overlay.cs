using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace SixtarGateMod.Features;

/// <summary>Screen rectangle in IMGUI pixels (origin top-left). Plain managed math, unlike UnityEngine.Rect.</summary>
public readonly record struct Box(float X, float Y, float W, float H)
{
    public float XMax => X + W;
    public float YMax => Y + H;
}

/// <summary>
/// Immediate-mode drawing on a uGUI overlay canvas. The game build strips the IMGUI drawing methods
/// (GUI.DrawTexture / GUI.Label fail with "Method unstripping failed"), so the sxtg2 overlays draw through pooled
/// Image / Text objects instead. Call Begin() once per frame, then Rect/Label, then End() to hide the rest.
///
/// Cost model: in this interop build every UnityEngine value-type helper (new Rect/Color/Vector2, Rect.x, Mathf.*,
/// Color.white, ==) is an il2cpp_runtime_invoke that boxes its result, so overlay code uses Box / Rgba / MathF only.
/// Each pooled object keeps a managed copy of what it shows and only crosses into IL2CPP when that changes, and
/// sibling order (= draw order) is rewritten only on frames whose draw sequence differs from the previous one.
/// </summary>
public static class Overlay
{
    private sealed class Item
    {
        public readonly Graphic Graphic;
        public readonly RectTransform Rt;
        public readonly Image? Image;
        public readonly Text? Text;
        public readonly Outline? Outline;
        public bool Active;
        public Box Box;
        public int Layout = -1;
        public Color Color = Rgba(-1f, -1f, -1f, -1f);
        public Sprite? Sprite;
        public string? Label;
        public int FontSize;
        public TextAnchor Anchor = (TextAnchor)(-1);
        public bool? OutlineOn;

        public Item(Graphic g, Image? image, Text? text, Outline? outline)
        {
            Graphic = g;
            Rt = g.rectTransform;
            Image = image;
            Text = text;
            Outline = outline;
            Active = true; // GameObject starts active when created by NewChild; keep managed state in sync so Hide() can deactivate unused items.
        }
    }

    private static GameObject? _root;
    private static RectTransform? _rootRect;
    private static readonly List<Item> Images = new(), Texts = new(), Drawn = new(), LastOrder = new();
    private static int _imageIndex, _textIndex, _layout;
    private static float _scale = 1f, _screenH;
    private static Font? _font;
    private static Sprite? _whiteSprite;
    private static bool _failed;

    public static float ScreenWidth { get; private set; }
    public static float ScreenHeight => _screenH;

    public static Color Rgba(float r, float g, float b, float a) => new() { r = r, g = g, b = b, a = a };
    public static Color WithAlpha(Color c, float a) => Rgba(c.r, c.g, c.b, a);
    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    public static bool Begin()
    {
        if (_failed) return false;
        try
        {
            if (_root == null) Create();
            _imageIndex = _textIndex = 0;
            Drawn.Clear();
            float scale = _rootRect!.localScale.x;
            if (scale <= 0f) scale = 1f;
            float w = Screen.width, h = Screen.height;
            if (scale != _scale || h != _screenH || w != ScreenWidth) { _scale = scale; _screenH = h; ScreenWidth = w; _layout++; }
            return true;
        }
        catch (Exception ex)
        {
            _failed = true;
            Melon<Core>.Logger.Error($"[Overlay] Could not create overlay canvas: {ex}");
            return false;
        }
    }

    public static void End()
    {
        if (_root == null) return;
        Hide(Images, _imageIndex);
        Hide(Texts, _textIndex);

        bool same = Drawn.Count == LastOrder.Count;
        for (int i = 0; same && i < Drawn.Count; i++) same = ReferenceEquals(Drawn[i], LastOrder[i]);
        if (same) return;
        foreach (var item in Drawn) item.Rt.SetAsLastSibling();
        LastOrder.Clear();
        LastOrder.AddRange(Drawn);
    }

    public static void Rect(Box b, Color color, Sprite? sprite = null)
    {
        if (_root == null) return;
        if (_imageIndex == Images.Count)
        {
            var img = NewChild("Rect").AddComponent<Image>();
            img.raycastTarget = false;
            Images.Add(new Item(img, img, null, null));
        }
        var it = Images[_imageIndex++];
        sprite ??= _whiteSprite;
        if (!ReferenceEquals(it.Sprite, sprite)) { it.Image!.sprite = sprite; it.Sprite = sprite; }
        Show(it, b, color);
    }

    public static void Label(Box b, string text, Color color, int fontSize, TextAnchor anchor, bool outline)
    {
        if (_root == null || _font == null) return;
        if (_textIndex == Texts.Count)
        {
            var go = NewChild("Label");
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.AddComponent<Outline>();
            o.effectDistance = new Vector2 { x = 1.5f, y = -1.5f };
            Texts.Add(new Item(t, null, t, o));
        }
        var it = Texts[_textIndex++];
        var txt = it.Text!;
        if (it.Label != text) { txt.text = text; it.Label = text; }
        if (it.FontSize != fontSize) { txt.fontSize = fontSize; it.FontSize = fontSize; }
        if (it.Anchor != anchor) { txt.alignment = anchor; it.Anchor = anchor; }
        if (it.OutlineOn != outline) { it.Outline!.enabled = outline; it.OutlineOn = outline; }
        if (outline && it.Color.a != color.a) it.Outline!.effectColor = Rgba(0f, 0f, 0f, 0.8f * color.a);
        Show(it, b, color);
    }

    /// <summary>White sprite whose alpha follows a texture mask (capsule / diamond shapes).</summary>
    public static void HideAll()
    {
        if (_root == null) return;
        Hide(Images, 0);
        Hide(Texts, 0);
        Drawn.Clear();
    }

    public static Sprite MakeSprite(Texture2D tex)
    {
        var s = Sprite.Create(tex, new UnityEngine.Rect { m_XMin = 0f, m_YMin = 0f, m_Width = tex.width, m_Height = tex.height }, new Vector2 { x = 0.5f, y = 0.5f });
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return s;
    }

    private static void Show(Item it, Box b, Color color)
    {
        if (it.Layout != _layout || it.Box != b)
        {
            it.Rt.anchoredPosition = new Vector2 { x = b.X / _scale, y = (_screenH - b.Y - b.H) / _scale };
            it.Rt.sizeDelta = new Vector2 { x = b.W / _scale, y = b.H / _scale };
            it.Box = b;
            it.Layout = _layout;
        }
        var c = it.Color;
        if (c.r != color.r || c.g != color.g || c.b != color.b || c.a != color.a) { it.Graphic.color = color; it.Color = color; }
        if (!it.Active) { it.Graphic.gameObject.SetActive(true); it.Active = true; }
        Drawn.Add(it);
    }

    private static void Hide(List<Item> items, int from)
    {
        for (int i = from; i < items.Count; i++)
        {
            if (!items[i].Active) continue;
            items[i].Graphic.gameObject.SetActive(false);
            items[i].Active = false;
        }
    }

    private static void Create()
    {
        _root = new GameObject("SixtarGateModOverlay");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        _rootRect = _root.GetComponent<RectTransform>();

        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Rgba(1f, 1f, 1f, 1f));
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _whiteSprite = MakeSprite(tex);

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_font == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            if (fonts != null && fonts.Length > 0) _font = fonts[0];
        }
        Melon<Core>.Logger.Msg($"[Overlay] Created overlay canvas (font: {(_font != null ? _font.name : "none")})");
    }

    private static GameObject NewChild(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root!.transform, false);
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.anchorMin = default;
        rt.anchorMax = default;
        rt.pivot = default;
        return go;
    }
}
