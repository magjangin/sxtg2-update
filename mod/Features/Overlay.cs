using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace SixtarGateMod.Features;

/// <summary>
/// Immediate-mode drawing on a uGUI overlay canvas. The game build strips the IMGUI drawing methods
/// (GUI.DrawTexture / GUI.Label fail with "Method unstripping failed"), so the sxtg2 overlays draw through pooled
/// Image / Text objects instead. Coordinates are IMGUI-style screen pixels (origin top-left).
/// Call Begin() once per frame, then Rect/Label, then End() to hide whatever was not drawn this frame.
/// Pooled objects are reused in call order, so every call moves its object to the end of the sibling list:
/// otherwise a pooled Image created after a Text (e.g. a key box once more hit lines exist) renders over the label.
/// </summary>
public static class Overlay
{
    private static GameObject? _root;
    private static RectTransform? _rootRect;
    private static readonly List<Image> Images = new();
    private static readonly List<Text> Texts = new();
    private static int _imageIndex, _textIndex;
    private static Font? _font;
    private static Sprite? _whiteSprite;
    private static bool _failed;

    public static bool Begin()
    {
        if (_failed) return false;
        try
        {
            if (_root == null) Create();
            _imageIndex = 0;
            _textIndex = 0;
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
        for (int i = _imageIndex; i < Images.Count; i++)
            if (Images[i].gameObject.activeSelf) Images[i].gameObject.SetActive(false);
        for (int i = _textIndex; i < Texts.Count; i++)
            if (Texts[i].gameObject.activeSelf) Texts[i].gameObject.SetActive(false);
    }

    public static void Rect(Rect r, Color color, Sprite? sprite = null)
    {
        if (_root == null) return;
        Image img;
        if (_imageIndex < Images.Count) img = Images[_imageIndex];
        else
        {
            img = NewChild("Rect").AddComponent<Image>();
            img.raycastTarget = false;
            Images.Add(img);
        }
        _imageIndex++;
        img.sprite = sprite ?? _whiteSprite;
        img.color = color;
        Place(img.rectTransform, r);
        img.transform.SetAsLastSibling();
        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
    }

    public static void Label(Rect r, string text, Color color, int fontSize, TextAnchor anchor, bool outline)
    {
        if (_root == null || _font == null) return;
        Text t;
        if (_textIndex < Texts.Count) t = Texts[_textIndex];
        else
        {
            var go = NewChild("Label");
            t = go.AddComponent<Text>();
            t.font = _font;
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.8f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            Texts.Add(t);
        }
        _textIndex++;
        t.text = text;
        t.color = color;
        t.fontSize = fontSize;
        t.alignment = anchor;
        var outlineComp = t.GetComponent<Outline>();
        if (outlineComp != null)
        {
            outlineComp.enabled = outline;
            outlineComp.effectColor = new Color(0f, 0f, 0f, 0.8f * color.a);
        }
        Place(t.rectTransform, r);
        t.transform.SetAsLastSibling();
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
    }

    /// <summary>White sprite whose alpha follows a texture mask (capsule / diamond shapes).</summary>
    public static Sprite MakeSprite(Texture2D tex)
    {
        var s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return s;
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
        tex.SetPixel(0, 0, Color.white);
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
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        return go;
    }

    // IMGUI rect (top-left origin, screen px) -> anchored position from the bottom-left of the overlay canvas.
    private static void Place(RectTransform rt, Rect r)
    {
        float scale = _rootRect != null && _rootRect.localScale.x > 0f ? _rootRect.localScale.x : 1f;
        rt.anchoredPosition = new Vector2(r.x / scale, (Screen.height - r.y - r.height) / scale);
        rt.sizeDelta = new Vector2(r.width / scale, r.height / scale);
    }
}
