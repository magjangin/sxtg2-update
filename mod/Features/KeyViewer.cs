using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2Cpp;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// sxtg2 key viewer, drawn on the uGUI Overlay (IMGUI is stripped in this build). Pressed state comes from JudgeProcessor.SetPressedState (PlayAssist.LanePressed),
/// key labels from the game's own InputSystem bindings (InputListener.Instance.playActionMap).
/// Display order keeps sxtg2's layout with GATE in the middle.
/// </summary>
public static class KeyViewer
{
    // SixtarGate.Play.LaneIndex values: LL 0, L 1, R 2, RR 3, Gate 4, LT 5, RT 6.
    private static readonly int[] SolarOrder = { 5, 0, 1, 4, 2, 3, 6 };
    private static readonly int[] LunarOrder = { 0, 1, 4, 2, 3 };
    private const int GateLane = 4;

    private static readonly string[] Labels = new string[7];
    private static bool _bound;

    public static void ResetBindings() { _bound = false; _bindAttempts = 0; }

    public static void Draw()
    {
        if (!ModConfig.EnableKeyViewer || !PlayAssist.IsPlaying) return;
        if (!_bound) TryBindLabels();

        int[] order = Core.CurrentPlayStyleIsLunar ? LunarOrder : SolarOrder;
        const float size = 44f, gap = 6f;
        float total = order.Length * size + (order.Length - 1) * gap;
        float x0 = (Screen.width - total) / 2f;
        float y = Screen.height - 56f - size;

        for (int i = 0; i < order.Length; i++)
        {
            int lane = order[i];
            var rect = new Rect(x0 + i * (size + gap), y, size, size);
            bool pressed = PlayAssist.LanePressed[lane];
            Color normal = ModConfig.KeyViewerNormalColor;
            Color down = lane == GateLane ? ModConfig.KeyViewerGatePressedColor : ModConfig.KeyViewerPressedColor;

            JudgmentBar.DrawColorRect(rect, pressed ? down : normal);
            DrawBorder(rect, pressed ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f));
            Overlay.Label(rect, Labels[lane] ?? "-", pressed ? new Color(0.05f, 0.05f, 0.05f, 1f) : new Color(1f, 1f, 1f, 0.9f),
                15, TextAnchor.MiddleCenter, outline: false);
        }
    }

    private static readonly string[] LaneNames = { "LL", "L", "R", "RR", "GATE", "LT", "RT" };
    private static int _bindAttempts;

    private static void TryBindLabels()
    {
        try
        {
            // Renewal play scene: the global InputListenerForUnityNewInputSystem module owns one SixtarInputAction
            // whose PlayAction map holds the lane bindings for both Solar and Lunar.
            var renewal = UnityEngine.Object.FindObjectOfType<Il2CppSixtarGate.InputListenerForUnityNewInputSystem>()?._sixtarInputAction;
            if (renewal != null)
            {
                Labels[0] = Label(renewal.m_PlayAction_LaneLL);
                Labels[1] = Label(renewal.m_PlayAction_LaneL);
                Labels[2] = Label(renewal.m_PlayAction_LaneR);
                Labels[3] = Label(renewal.m_PlayAction_LaneRR);
                Labels[4] = Label(renewal.m_PlayAction_LaneGate);
                Labels[5] = Label(renewal.m_PlayAction_LaneLT);
                Labels[6] = Label(renewal.m_PlayAction_LaneRT);
                _bound = true;
                Melon<Core>.Logger.Msg($"[KeyViewer] Key labels: {string.Join(" ", Labels)} (SixtarInputAction)");
                return;
            }

            // Legacy play scene: InputListener.playActionMap (separate Solar / Lunar maps).
            var listener = InputListener.Instance;
            if (listener == null) listener = UnityEngine.Object.FindObjectOfType<InputListener>();
            var map = listener?.playActionMap;
            if (map == null)
            {
                // Retry for ~2 s (input may be wired a little after the judge starts), then fall back to lane names.
                if (++_bindAttempts < 120) return;
                Array.Copy(LaneNames, Labels, Labels.Length);
                _bound = true;
                Melon<Core>.Logger.Warning("[KeyViewer] No input bindings found (SixtarInputAction / InputListener); showing lane names");
                return;
            }
            bool lunar = Core.CurrentPlayStyleIsLunar;
            Labels[0] = Label(lunar ? map.m_Lunar_LaneLL : map.m_Solar_LaneLL);
            Labels[1] = Label(lunar ? map.m_Lunar_LaneL : map.m_Solar_LaneL);
            Labels[2] = Label(lunar ? map.m_Lunar_LaneR : map.m_Solar_LaneR);
            Labels[3] = Label(lunar ? map.m_Lunar_LaneRR : map.m_Solar_LaneRR);
            Labels[4] = Label(lunar ? map.m_Lunar_Gate : map.m_Solar_Gate);
            Labels[5] = Label(map.m_Solar_LaneLT);
            Labels[6] = Label(map.m_Solar_LaneRT);
            _bound = true;
            Melon<Core>.Logger.Msg($"[KeyViewer] Key labels: {string.Join(" ", Labels)} ({(lunar ? "Lunar" : "Solar")})");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"[KeyViewer] Could not read key bindings: {ex.Message}");
            for (int i = 0; i < Labels.Length; i++) Labels[i] ??= "-";
            _bound = true;
        }
    }

    private static string Label(InputAction? action)
    {
        if (action == null) return "-";
        string s = InputActionRebindingExtensions.GetBindingDisplayString(action, (InputBinding.DisplayStringOptions)0, null) ?? "";
        int bar = s.IndexOf(" | ", StringComparison.Ordinal);
        if (bar >= 0) s = s.Substring(0, bar);
        s = s.Trim();
        return s switch
        {
            "" => "-",
            "Space" => "SPC",
            "Left Shift" => "LSFT",
            "Right Shift" => "RSFT",
            "Left Ctrl" or "Left Control" => "LCTL",
            "Right Ctrl" or "Right Control" => "RCTL",
            "Left Alt" => "LALT",
            "Right Alt" => "RALT",
            "Up Arrow" => "↑",
            "Down Arrow" => "↓",
            "Left Arrow" => "←",
            "Right Arrow" => "→",
            _ => s.Length > 4 ? s.Substring(0, 4).ToUpperInvariant() : s.ToUpperInvariant(),
        };
    }

    private static void DrawBorder(Rect r, Color c)
    {
        JudgmentBar.DrawColorRect(new Rect(r.x, r.y, r.width, 1.5f), c);
        JudgmentBar.DrawColorRect(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), c);
        JudgmentBar.DrawColorRect(new Rect(r.x, r.y, 1.5f, r.height), c);
        JudgmentBar.DrawColorRect(new Rect(r.xMax - 1.5f, r.y, 1.5f, r.height), c);
    }
}
