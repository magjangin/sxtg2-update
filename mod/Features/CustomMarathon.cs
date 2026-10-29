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
        if (sceneName == "Result") { _resultShownAt = Time.time; _autoAdvanced = false; }
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

    public static void Update()
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
        if (auto > 0f && !_autoAdvanced && Time.time - _resultShownAt >= auto)
        {
            _autoAdvanced = true;
            SixtarGlobal.GotoMusicSelect(); // diverted to the next song by Core's prefix
        }
    }

    /// <summary>Result-screen banner, drawn on the Overlay.</summary>
    public static void Draw()
    {
        if (!IsActive || _resultShownAt < 0f) return;
        string next = Queue.Count > 0 ? Queue[0].DisplayName : "-";
        string auto = ModConfig.CustomMarathonAutoNext > 0f
            ? $"  ·  auto {Mathf.Max(0f, ModConfig.CustomMarathonAutoNext - (Time.time - _resultShownAt)):F0}s"
            : "";
        var rect = new Rect(Screen.width / 2f - 320f, 18f, 640f, 34f);
        Overlay.Rect(rect, new Color(0.05f, 0.05f, 0.08f, 0.75f));
        Overlay.Label(rect, $"MARATHON {_played}/{_total}  ·  NEXT: {next}  ·  [Enter] next  [Esc] stop{auto}",
            new Color(1f, 1f, 1f, 0.95f), 16, TextAnchor.MiddleCenter, outline: true);
    }
}
