using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2Cpp;
using Il2CppSixtarGate;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// sxtg2 [커스텀 곡 마라톤]: starting a custom song queues every other custom song in random order; leaving the
/// result screen (Enter, or CustomMarathonAutoNext seconds) starts the next one instead of returning to the song
/// list, Esc on the result screen ends the run. The result screen leaves through SixtarGlobal.GotoMusicSelect,
/// which Core diverts through <see cref="TryContinue"/>.
/// </summary>
public static class CustomMarathon
{
    private static readonly List<ITrack> Queue = new();
    private static int _played, _total;
    private static ELevels _level;
    private static EPlayStyle _style;
    private static bool _startingNext;
    private static float _resultShownAt = -1f;
    private static bool _autoAdvanced;

    public static bool IsActive { get; private set; }

    /// <summary>Set while TryContinue starts the next song, so GotoMusicSelect's postfixes keep the custom-track state.</summary>
    public static bool DivertingMusicSelect { get; set; }

    /// <summary>Called from the GotoPlayScene prefix for every play start.</summary>
    public static void OnPlayStarting(ITrack track, ELevels level, EPlayStyle style, bool isCustom)
    {
        _level = level;
        _style = style;
        _resultShownAt = -1f;
        if (_startingNext) { _startingNext = false; return; }

        IsActive = false;
        Queue.Clear();
        if (!ModConfig.CustomMarathon || !isCustom) return;

        var all = Core.TrackSelectController_GetTracks_Patch.CachedCustomTracks;
        if (all == null || all.Count < 2) return;

        var rng = new System.Random();
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t != null && t.TrackID != track.TrackID) Queue.Add(t);
        }
        for (int i = Queue.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (Queue[i], Queue[j]) = (Queue[j], Queue[i]);
        }
        _played = 1;
        _total = Queue.Count + 1;
        IsActive = Queue.Count > 0;
        if (IsActive)
            Melon<Core>.Logger.Msg($"[Marathon] Started with '{track.DisplayName}': {_total} custom songs");
    }

    public static void OnSceneLoaded(string sceneName)
    {
        if (!IsActive) return;
        if (sceneName == "Result") { _resultShownAt = Time.time; _autoAdvanced = false; _banner = ""; }
    }

    /// <summary>GotoMusicSelect prefix: returns true when the next marathon song was started instead.</summary>
    public static bool TryContinue()
    {
        if (!IsActive) return false;
        if (Queue.Count == 0)
        {
            Melon<Core>.Logger.Msg("[Marathon] All custom songs played, back to the song list");
            IsActive = false;
            return false;
        }

        var next = Queue[0];
        Queue.RemoveAt(0);
        _played++;
        Melon<Core>.Logger.Msg($"[Marathon] Next ({_played}/{_total}): '{next.DisplayName}'");
        _startingNext = true;
        DivertingMusicSelect = true;
        SixtarGlobal.GotoPlayScene(next, _level, _style);
        return true;
    }

    public static void Update(float now)
    {
        if (!IsActive || _resultShownAt < 0f) return;
        try
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                IsActive = false;
                Queue.Clear();
                Melon<Core>.Logger.Msg("[Marathon] Stopped by Esc on the result screen");
                return;
            }
        }
        catch { }

        float auto = ModConfig.CustomMarathonAutoNext;
        if (auto > 0f && !_autoAdvanced && now - _resultShownAt >= auto)
        {
            _autoAdvanced = true;
            SixtarGlobal.GotoMusicSelect(); // diverted to the next song by Core's prefix
        }
    }

    private static readonly Color BannerBack = Overlay.Rgba(0.05f, 0.05f, 0.08f, 0.75f), BannerText = Overlay.Rgba(1f, 1f, 1f, 0.95f);
    private static int _bannerSecond = -1;
    private static string _banner = "";

    /// <summary>Result-screen banner, drawn on the Overlay. The text is rebuilt only when the countdown second changes.</summary>
    public static void Draw(float now)
    {
        if (!IsActive || _resultShownAt < 0f) return;
        float autoNext = ModConfig.CustomMarathonAutoNext;
        int second = autoNext > 0f ? (int)MathF.Ceiling(Math.Max(0f, autoNext - (now - _resultShownAt))) : -1;
        if (second != _bannerSecond || _banner.Length == 0)
        {
            _bannerSecond = second;
            string next = Queue.Count > 0 ? Queue[0].DisplayName : "-";
            _banner = $"MARATHON {_played}/{_total}  ·  NEXT: {next}  ·  [Enter] next  [Esc] stop{(second >= 0 ? $"  ·  auto {second}s" : "")}";
        }
        var box = new Box(Overlay.ScreenWidth / 2f - 320f, 18f, 640f, 34f);
        Overlay.Rect(box, BannerBack);
        Overlay.Label(box, _banner, BannerText, 16, TextAnchor.MiddleCenter, outline: true);
    }
}
