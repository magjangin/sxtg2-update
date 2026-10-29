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
    private readonly record struct HitTick(float OffsetMs, float TimeAdded, Color Color);

    private const float Duration = 1.5f;
    private static readonly List<HitTick> HitHistory = new();
    private static readonly string[] JudgeNames = { "BLUESTAR", "WHITESTAR", "YELLOWSTAR", "REDSTAR" };
    private static readonly Color[] JudgeColors =
    {
        Overlay.Rgba(0.3f, 0.65f, 1f, 1f), Overlay.Rgba(0.95f, 0.95f, 0.95f, 1f),
        Overlay.Rgba(1f, 0.85f, 0.2f, 1f), Overlay.Rgba(0.95f, 0.25f, 0.25f, 1f),
    };
    private static readonly Color Background = Overlay.Rgba(0.08f, 0.08f, 0.08f, 0.65f);
    private static readonly Color YellowRange = Overlay.Rgba(0.65f, 0.55f, 0.12f, 0.18f);
    private static readonly Color WhiteRange = Overlay.Rgba(0.75f, 0.75f, 0.75f, 0.2f);
    private static readonly Color BlueRange = Overlay.Rgba(0.2f, 0.5f, 0.85f, 0.32f);
    private static readonly Color CenterLine = Overlay.Rgba(1f, 1f, 1f, 1f);

    private static float _lastHitTime = -999f;
    private static Color _lastHitColor;
    private static string _lastHitText = "";

    private static readonly Dictionary<(int, int, int), Sprite> ShapeCache = new();

    public static void RegisterHit(float deltaTimeSeconds, int judge)
    {
        float offsetMs = -deltaTimeSeconds * 1000f;
        float now = Time.time;
        var color = judge >= 0 && judge < JudgeColors.Length ? JudgeColors[judge] : Overlay.Rgba(0.7f, 0.7f, 0.7f, 1f);
        string name = judge >= 0 && judge < JudgeNames.Length ? JudgeNames[judge] : "?";
        _lastHitTime = now;
        _lastHitColor = color;
        _lastHitText = $"{(offsetMs >= 0f ? "+" : "")}{offsetMs:F1} ms · {name} ({(offsetMs >= 0f ? "FAST" : "SLOW")})";
        HitHistory.Add(new HitTick(offsetMs, now, color));
    }

    public static void Draw(float now)
    {
        if (!ModConfig.EnableJudgmentBar || !PlayAssist.IsPlaying) return;

        // Ticks are appended in time order, so the expired ones are always at the front.
        int expired = 0;
        while (expired < HitHistory.Count && now - HitHistory[expired].TimeAdded > Duration) expired++;
        if (expired > 0) HitHistory.RemoveRange(0, expired);

        float screenW = Overlay.ScreenWidth, screenH = Overlay.ScreenHeight;
        bool vertical = ModConfig.JudgmentBarVertical;
        int shape = ModConfig.JudgmentBarShape;
        int rangeShape = ModConfig.JudgmentBarRangeShape == -1 ? shape : ModConfig.JudgmentBarRangeShape;
        bool left = ModConfig.JudgmentBarSide.Equals("Left", StringComparison.OrdinalIgnoreCase);
        bool right = ModConfig.JudgmentBarSide.Equals("Right", StringComparison.OrdinalIgnoreCase);

        float barW = vertical ? 24f : 300f;
        float barH = vertical ? 300f : 24f;
        float cx = left ? (vertical ? 60f : 60f + barW / 2f)
                 : right ? (vertical ? screenW - 60f : screenW - 60f - barW / 2f)
                 : (vertical ? 60f : screenW / 2f);
        float cy = screenH / 2f;

        var ranges = PlayAssist.RangeMs;
        float maxMs = ranges[3];
        float pxPerMs = (vertical ? barH / 2f : barW / 2f) / maxMs;

        DrawShape(shape, new Box(cx - barW / 2f, cy - barH / 2f, barW, barH), Background);
        DrawRangeBox(cx, cy, barW, barH, vertical, ranges[2] * 2f * pxPerMs, YellowRange, rangeShape);
        DrawRangeBox(cx, cy, barW, barH, vertical, ranges[1] * 2f * pxPerMs, WhiteRange, rangeShape);
        DrawRangeBox(cx, cy, barW, barH, vertical, ranges[0] * 2f * pxPerMs, BlueRange, rangeShape);

        Overlay.Rect(vertical ? new Box(cx - barW / 2f - 2f, cy - 1f, barW + 4f, 2f) : new Box(cx - 1f, cy - barH / 2f - 2f, 2f, barH + 4f), CenterLine);

        foreach (var tick in HitHistory)
        {
            float off = Math.Clamp(tick.OffsetMs, -maxMs, maxMs) * pxPerMs;
            var c = Overlay.WithAlpha(tick.Color, Overlay.Clamp01(1f - (now - tick.TimeAdded) / Duration) * 0.9f);
            Overlay.Rect(vertical ? new Box(cx - barW / 2f - 1f, cy - off - 1f, barW + 2f, 2f) : new Box(cx + off - 1f, cy - barH / 2f - 1f, 2f, barH + 2f), c);
        }

        float sinceLast = now - _lastHitTime;
        if (sinceLast >= Duration) return;

        bool labelOnLeft = vertical && right;
        var label = !vertical ? new Box(cx - 90f, cy - barH / 2f - 28f, 260f, 24f)
                  : labelOnLeft ? new Box(cx - barW / 2f - 270f, cy - 12f, 260f, 24f)
                  : new Box(cx + barW / 2f + 10f, cy - 12f, 260f, 24f);
        Overlay.Label(label, _lastHitText, Overlay.WithAlpha(_lastHitColor, Overlay.Clamp01(1f - sinceLast / Duration)), 16,
            labelOnLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, outline: true);
    }

    private static void DrawRangeBox(float cx, float cy, float barW, float barH, bool vertical, float size, Color color, int shape)
        => DrawShape(shape, vertical ? new Box(cx - barW / 2f + 1f, cy - size / 2f, barW - 2f, size) : new Box(cx - size / 2f, cy - barH / 2f + 1f, size, barH - 2f), color);

    private static void DrawShape(int shape, Box b, Color color)
    {
        int w = (int)MathF.Round(b.W), h = (int)MathF.Round(b.H);
        Overlay.Rect(b, color, shape == 0 || w < 2 || h < 2 ? null : GetShapeSprite(shape, w, h));
    }

    // 1 = capsule (distance to the center segment), 2 = diamond. Built as raw RGBA: Texture2D.SetPixel per pixel
    // would be one IL2CPP call each.
    private static Sprite? GetShapeSprite(int shape, int w, int h)
    {
        var key = (shape, w, h);
        if (ShapeCache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (ShapeCache.Count > 64) ShapeCache.Clear();

        float radius = Math.Min(w, h) / 2f;
        float ax, ay, bx, by;
        if (w >= h) { ax = radius; ay = h / 2f; bx = w - radius; by = h / 2f; }
        else { ax = w / 2f; ay = radius; bx = w / 2f; by = h - radius; }
        float hw = w / 2f, hh = h / 2f, diag = MathF.Sqrt(hw * hw + hh * hh);

        var rgba = new byte[w * h * 4];
        for (int y = 0, i = 0; y < h; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < w; x++, i += 4)
            {
                float px = x + 0.5f;
                float dist = shape == 1
                    ? DistanceToSegment(px, py, ax, ay, bx, by) - radius
                    : (hh * MathF.Abs(px - hw) + hw * MathF.Abs(py - hh) - hw * hh) / diag;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                rgba[i + 3] = (byte)(Overlay.Clamp01(0.5f - dist) * 255f + 0.5f);
            }
        }
        var tex = MediaOverrideManager.TextureFromRgba(w, h, rgba);
        if (tex == null) return null;
        tex.wrapMode = TextureWrapMode.Clamp;
        var sprite = Overlay.MakeSprite(tex);
        ShapeCache[key] = sprite;
        return sprite;
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0.0001f ? Overlay.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2) : 0f;
        float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
        return MathF.Sqrt(ex * ex + ey * ey);
    }
}
