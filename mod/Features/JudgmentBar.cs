using System;
using System.Collections.Generic;
using UnityEngine;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// sxtg2 judgment bar, drawn on the uGUI Overlay (IMGUI is stripped in this build). Hits come from JudgeProcessor.OnNoteJudged via PlayAssist.
/// Renewal JudgeResult.deltaTime = inputTime - noteTime (negative = early), so the offset is negated to keep
/// sxtg2's convention: positive = FAST, drawn above / right of the center line.
/// </summary>
public static class JudgmentBar
{
    private struct HitTick
    {
        public float offsetMs;
        public float timeAdded;
        public Color color;
    }

    private const float Duration = 1.5f;
    private static readonly List<HitTick> HitHistory = new();
    private static readonly string[] JudgeNames = { "BLUESTAR", "WHITESTAR", "YELLOWSTAR", "REDSTAR" };

    private static float _lastHitOffsetMs;
    private static float _lastHitTime = -999f;
    private static Color _lastHitColor = Color.white;
    private static int _lastHitJudge = -1;

    private static readonly Dictionary<(int, int, int), Sprite> ShapeCache = new();

    private static Color JudgeColor(int judge) => judge switch
    {
        0 => new Color(0.3f, 0.65f, 1f),
        1 => new Color(0.95f, 0.95f, 0.95f),
        2 => new Color(1f, 0.85f, 0.2f),
        3 => new Color(0.95f, 0.25f, 0.25f),
        _ => new Color(0.7f, 0.7f, 0.7f),
    };

    public static void RegisterHit(float deltaTimeSeconds, int judge)
    {
        float offsetMs = -deltaTimeSeconds * 1000f;
        var color = JudgeColor(judge);
        _lastHitOffsetMs = offsetMs;
        _lastHitTime = Time.time;
        _lastHitColor = color;
        _lastHitJudge = judge;
        HitHistory.Add(new HitTick { offsetMs = offsetMs, timeAdded = Time.time, color = color });
    }

    public static void Draw()
    {
        if (!ModConfig.EnableJudgmentBar || !PlayAssist.IsPlaying) return;

        float now = Time.time;
        HitHistory.RemoveAll(t => now - t.timeAdded > Duration);

        float screenW = Screen.width;
        float screenH = Screen.height;
        bool vertical = ModConfig.JudgmentBarVertical;
        int shape = ModConfig.JudgmentBarShape;
        int rangeShape = ModConfig.JudgmentBarRangeShape == -1 ? shape : ModConfig.JudgmentBarRangeShape;
        bool left = ModConfig.JudgmentBarSide.Equals("Left", StringComparison.OrdinalIgnoreCase);
        bool right = ModConfig.JudgmentBarSide.Equals("Right", StringComparison.OrdinalIgnoreCase);

        float barW = vertical ? 24f : 300f;
        float barH = vertical ? 300f : 24f;
        float centerX = left ? (vertical ? 60f : 60f + barW / 2f)
                      : right ? (vertical ? screenW - 60f : screenW - 60f - barW / 2f)
                      : (vertical ? 60f : screenW / 2f);
        float centerY = screenH / 2f;

        var ranges = PlayAssist.RangeMs;
        float maxMs = ranges[3];
        float pxPerMs = (vertical ? barH / 2f : barW / 2f) / maxMs;

        DrawShape(shape, new Rect(centerX - barW / 2f, centerY - barH / 2f, barW, barH), new Color(0.08f, 0.08f, 0.08f, 0.65f));
        DrawRangeBox(centerX, centerY, barW, barH, vertical, ranges[2] * 2f * pxPerMs, new Color(0.65f, 0.55f, 0.12f, 0.18f), rangeShape);
        DrawRangeBox(centerX, centerY, barW, barH, vertical, ranges[1] * 2f * pxPerMs, new Color(0.75f, 0.75f, 0.75f, 0.2f), rangeShape);
        DrawRangeBox(centerX, centerY, barW, barH, vertical, ranges[0] * 2f * pxPerMs, new Color(0.2f, 0.5f, 0.85f, 0.32f), rangeShape);

        if (vertical)
            DrawColorRect(new Rect(centerX - barW / 2f - 2f, centerY - 1f, barW + 4f, 2f), Color.white);
        else
            DrawColorRect(new Rect(centerX - 1f, centerY - barH / 2f - 2f, 2f, barH + 4f), Color.white);

        foreach (var tick in HitHistory)
        {
            float off = Mathf.Clamp(tick.offsetMs, -maxMs, maxMs);
            float alpha = Mathf.Clamp01(1f - (now - tick.timeAdded) / Duration) * 0.9f;
            var c = new Color(tick.color.r, tick.color.g, tick.color.b, alpha);
            if (vertical)
                DrawColorRect(new Rect(centerX - barW / 2f - 1f, centerY - off * pxPerMs - 1f, barW + 2f, 2f), c);
            else
                DrawColorRect(new Rect(centerX + off * pxPerMs - 1f, centerY - barH / 2f - 1f, 2f, barH + 2f), c);
        }

        float sinceLast = now - _lastHitTime;
        if (sinceLast >= Duration) return;

        string sign = _lastHitOffsetMs >= 0f ? "+" : "";
        string judgeName = _lastHitJudge >= 0 && _lastHitJudge < JudgeNames.Length ? JudgeNames[_lastHitJudge] : "?";
        string fastSlow = _lastHitOffsetMs >= 0f ? "FAST" : "SLOW";
        string text = $"{sign}{_lastHitOffsetMs:F1} ms · {judgeName} ({fastSlow})";
        float fade = Mathf.Clamp01(1f - sinceLast / Duration);

        bool labelOnLeft = vertical && right;
        Rect labelRect = vertical
            ? (labelOnLeft ? new Rect(centerX - barW / 2f - 270f, centerY - 12f, 260f, 24f) : new Rect(centerX + barW / 2f + 10f, centerY - 12f, 260f, 24f))
            : new Rect(centerX - 90f, centerY - barH / 2f - 28f, 260f, 24f);
        Overlay.Label(labelRect, text, new Color(_lastHitColor.r, _lastHitColor.g, _lastHitColor.b, fade), 16,
            labelOnLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, outline: true);
    }

    internal static void DrawColorRect(Rect rect, Color color) => Overlay.Rect(rect, color);

    private static void DrawRangeBox(float cx, float cy, float barW, float barH, bool vertical, float size, Color color, int shape)
    {
        var rect = vertical
            ? new Rect(cx - barW / 2f + 1f, cy - size / 2f, barW - 2f, size)
            : new Rect(cx - size / 2f, cy - barH / 2f + 1f, size, barH - 2f);
        DrawShape(shape, rect, color);
    }

    private static void DrawShape(int shape, Rect rect, Color color)
    {
        int w = Mathf.RoundToInt(rect.width);
        int h = Mathf.RoundToInt(rect.height);
        if (shape == 0 || w < 2 || h < 2)
        {
            DrawColorRect(rect, color);
            return;
        }
        Overlay.Rect(rect, color, GetShapeSprite(shape, w, h));
    }

    // 1 = capsule (distance to the center segment), 2 = diamond.
    private static Sprite GetShapeSprite(int shape, int w, int h)
    {
        var key = (shape, w, h);
        if (ShapeCache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (ShapeCache.Count > 64) ShapeCache.Clear();

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float radius = Mathf.Min(w, h) / 2f;
        float ax, ay, bx, by;
        if (w >= h) { ax = radius; ay = h / 2f; bx = w - radius; by = h / 2f; }
        else { ax = w / 2f; ay = radius; bx = w / 2f; by = h - radius; }
        float hw = w / 2f, hh = h / 2f, diag = Mathf.Sqrt(hw * hw + hh * hh);

        for (int y = 0; y < h; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f;
                float dist = shape == 1
                    ? DistanceToSegment(px, py, ax, ay, bx, by) - radius
                    : (hh * Mathf.Abs(px - hw) + hw * Mathf.Abs(py - hh) - hw * hh) / diag;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - dist)));
            }
        }
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var sprite = Overlay.MakeSprite(tex);
        ShapeCache[key] = sprite;
        return sprite;
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0.0001f ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2) : 0f;
        float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
        return Mathf.Sqrt(ex * ex + ey * ey);
    }
}
