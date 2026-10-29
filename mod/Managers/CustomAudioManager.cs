using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using Il2CppFMOD;
using Il2CppFMODUnity;
using Il2CppRhythmGame;
using Il2CppSixtarGate;
using SixtarGateMod.Models;

namespace SixtarGateMod.Managers;

/// <summary>
/// Plays hwa album audio through FMOD Core as a stream (System.createStream + playSound on the master group).
///
/// v2.1 used SoundModuleForFMOD.PlayLocalSound (a programmer-sound Studio event that createSound()s the path), but
/// that event has a fixed ~3 min timeline: longer songs went silent mid-play and its length leaked into
/// GetGamePlayAudioLength (early finish). A Core channel has no such limit. The channel is kept outside
/// SoundModuleForFMOD._soundChannels, so its Pause / Resume / Stop / GetTimelinePosition (all ContainsKey-guarded,
/// all unique native bodies) are extended below to drive it, and so are set_CurrectionVolume / ChangeVolume.
/// </summary>
public static class CustomAudioManager
{
    private const string LOG_PREFIX = "[CustomAudio]";
    private const string PREVIEW_PREFIX = "preview_";

    private sealed class LocalStream
    {
        public Sound Sound;
        public Channel Channel;
        public float LengthSeconds;
    }

    /// <summary>Length of the local file streaming on a channel (0 when none), e.g. the song on the Game channel.</summary>
    public static float GetStreamLength(ISoundModule.SoundChannel channel)
        => Streams.TryGetValue(channel, out var s) ? s.LengthSeconds : 0f;

    private static readonly Dictionary<ISoundModule.SoundChannel, LocalStream> Streams = new();

    /// <summary>
    /// Resolves an ISoundModule audio name ("preview_{trackID}" or "{trackID}") to a custom album file.
    /// Returns false when the name does not belong to a custom album, so the original Play runs.
    /// </summary>
    public static bool TryResolve(string audioName, out CustomAlbumInfo? album, out string? filePath, out bool isPreview)
    {
        album = null;
        filePath = null;
        isPreview = false;
        if (string.IsNullOrEmpty(audioName))
            return false;

        string trackId = audioName;
        if (trackId.StartsWith(PREVIEW_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            trackId = trackId.Substring(PREVIEW_PREFIX.Length);
            isPreview = true;
        }

        if (!trackId.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase))
            return false;

        album = CustomTrackManager.FindAlbum(trackId);
        if (album == null)
            return false;

        filePath = isPreview && album.HasCustomPreview ? album.PreviewPath : (album.HasCustomBgm ? album.BgmPath : null);
        if (filePath != null)
            filePath = Path.GetFullPath(filePath);
        return true;
    }

    public static void PlayLocal(SoundModuleForFMOD module, string filePath, ISoundModule.SoundChannel channel, float position)
    {
        module.Stop(channel); // also stops our previous stream on this channel (Stop prefix below)

        var core = RuntimeManager.CoreSystem;
        var result = core.createStream(filePath, MODE.CREATESTREAM | MODE.LOOP_OFF, out Sound sound);
        if (result != RESULT.OK)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} createStream failed ({result}) for '{filePath}'");
            return;
        }

        core.getMasterChannelGroup(out ChannelGroup master);
        result = core.playSound(sound, master, true, out Channel ch);
        if (result != RESULT.OK)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} playSound failed ({result})");
            sound.release();
            return;
        }

        float volume = module.GetVolume(channel) * module.CurrectionVolume;
        ch.setVolume(volume);
        uint lengthMs = 0;
        if (sound.getLength(out uint len, TIMEUNIT.MS) == RESULT.OK) lengthMs = len;
        if (position > 0f && lengthMs > 0)
            ch.setPosition((uint)(lengthMs * Math.Clamp(position, 0f, 1f)), TIMEUNIT.MS);
        ch.setPaused(false);

        Streams[channel] = new LocalStream { Sound = sound, Channel = ch, LengthSeconds = lengthMs / 1000f };
        Core.DebugLog($"{LOG_PREFIX} Streaming '{Path.GetFileName(filePath)}' on channel {channel} (volume {volume:F2})");
    }

    private static void ApplyVolumes(SoundModuleForFMOD module)
    {
        foreach (var pair in Streams)
            pair.Value.Channel.setVolume(module.GetVolume(pair.Key) * module.CurrectionVolume);
    }

    private static void StopLocal(ISoundModule.SoundChannel channel)
    {
        if (!Streams.Remove(channel, out var s)) return;
        s.Channel.stop();
        s.Sound.release();
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Stop))]
    public static class SoundModuleForFMOD_Stop_Patch
    {
        public static void Prefix(ISoundModule.SoundChannel channel) => StopLocal(channel);
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Pause))]
    public static class SoundModuleForFMOD_Pause_Patch
    {
        public static void Prefix(ISoundModule.SoundChannel channel)
        {
            if (Streams.TryGetValue(channel, out var s)) s.Channel.setPaused(true);
        }
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Resume), new Type[] { typeof(ISoundModule.SoundChannel) })]
    public static class SoundModuleForFMOD_Resume_Patch
    {
        public static void Prefix(ISoundModule.SoundChannel channel)
        {
            if (Streams.TryGetValue(channel, out var s)) s.Channel.setPaused(false);
        }
    }

    // Resume(time, channel): the original seeks the event timeline to time*1000 ms before unpausing.
    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.Resume), new Type[] { typeof(float), typeof(ISoundModule.SoundChannel) })]
    public static class SoundModuleForFMOD_ResumeAt_Patch
    {
        public static void Prefix(float time, ISoundModule.SoundChannel channel)
        {
            if (!Streams.TryGetValue(channel, out var s)) return;
            s.Channel.setPosition((uint)Math.Max(0.0, Math.Floor(time * 1000.0)), TIMEUNIT.MS);
            s.Channel.setPaused(false);
        }
    }

    // Scene transitions ramp CurrectionVolume (SceneLoader.FadeIn 1 -> 0, FadeOut 0 -> 1) and the volume options call
    // ChangeVolume; both re-apply volume only to the Studio events in _soundChannels. Without this, a preview started
    // during the music-select fade-out stayed at the partial volume it was started with.
    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.CurrectionVolume), MethodType.Setter)]
    public static class SoundModuleForFMOD_SetCurrectionVolume_Patch
    {
        public static void Postfix(SoundModuleForFMOD __instance) => ApplyVolumes(__instance);
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), "ChangeVolume")]
    public static class SoundModuleForFMOD_ChangeVolume_Patch
    {
        public static void Postfix(SoundModuleForFMOD __instance) => ApplyVolumes(__instance);
    }

    [HarmonyPatch(typeof(SoundModuleForFMOD), nameof(SoundModuleForFMOD.GetTimelinePosition))]
    public static class SoundModuleForFMOD_GetTimelinePosition_Patch
    {
        public static void Postfix(ISoundModule.SoundChannel channel, ref float __result)
        {
            if (Streams.TryGetValue(channel, out var s) && s.Channel.getPosition(out uint ms, TIMEUNIT.MS) == RESULT.OK)
                __result = ms / 1000f;
        }
    }
}
