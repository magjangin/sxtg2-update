using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Il2Cpp;
using Il2CppSixtarGate.Play;
using SixtarGateMod.Loaders;
using SixtarGateMod.Models;

namespace SixtarGateMod.Managers;

public static class MediaOverrideManager
{
    private const string LOG_PREFIX = "[MediaOverride]";
    private static readonly Dictionary<string, Sprite> SpriteCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action<Sprite>>> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static bool _preloading;

    /// <summary>
    /// Synchronous jacket (decodes on the main thread on a cache miss). Used by the play-flow screens, which normally
    /// hit the cache because the song was shown in the music select first.
    /// </summary>
    public static Sprite? GetOrCreateAlbumSprite(CustomAlbumInfo album)
    {
        if (album == null || !album.HasCustomJacket) return null;
        if (SpriteCache.TryGetValue(album.TrackId, out var cached) && cached != null)
            return cached;

        var sprite = LoadSpriteFromFile(album.JacketPath!);
        if (sprite != null) SpriteCache[album.TrackId] = sprite;
        else Melon<Core>.Logger.Error($"{LOG_PREFIX} Failed creating Sprite from '{album.JacketPath}'");
        return sprite;
    }

    /// <summary>
    /// Jacket without blocking the main thread: a cache hit calls back immediately, otherwise the PNG is decoded by
    /// PngDecoder on a worker thread and every waiting callback runs when it is done.
    /// </summary>
    public static void RequestAlbumSprite(CustomAlbumInfo album, Action<Sprite> onReady)
    {
        if (album == null || !album.HasCustomJacket) return;
        if (SpriteCache.TryGetValue(album.TrackId, out var cached) && cached != null)
        {
            onReady(cached);
            return;
        }
        if (Pending.TryGetValue(album.TrackId, out var waiting))
        {
            waiting.Add(onReady);
            return;
        }
        Pending[album.TrackId] = new List<Action<Sprite>> { onReady };
        MelonCoroutines.Start(LoadAsync(album));
    }

    /// <summary>Decodes every custom jacket in the background, one request at a time, so browsing never waits.</summary>
    public static void PreloadAll(IEnumerable<CustomAlbumInfo> albums)
    {
        if (_preloading) return;
        _preloading = true;
        MelonCoroutines.Start(PreloadRoutine(new List<CustomAlbumInfo>(albums)));
    }

    private static System.Collections.IEnumerator PreloadRoutine(List<CustomAlbumInfo> albums)
    {
        var started = DateTime.Now;
        int loaded = 0;
        foreach (var album in albums)
        {
            if (!album.HasCustomJacket || SpriteCache.ContainsKey(album.TrackId)) continue;
            bool done = false;
            RequestAlbumSprite(album, _ => done = true);
            float waitUntil = Time.realtimeSinceStartup + 10f;
            while (!done && Pending.ContainsKey(album.TrackId) && Time.realtimeSinceStartup < waitUntil) yield return null;
            loaded++;
        }
        Core.DebugLog($"{LOG_PREFIX} Preloaded {loaded} jacket(s) in {(DateTime.Now - started).TotalSeconds:F1}s");
    }

    private static bool _rawUploadBroken;

    // PNG bytes are read and decoded on a worker thread (Loaders/PngDecoder); the main thread only uploads the
    // finished RGBA buffer. Unity's own async path is unusable here: DownloadHandlerTexture's constructor is
    // stripped from the game build ("Method not found").
    private static System.Collections.IEnumerator LoadAsync(CustomAlbumInfo album)
    {
        string path = Path.GetFullPath(album.JacketPath!);
        var decode = System.Threading.Tasks.Task.Run(() =>
        {
            byte[] file = File.ReadAllBytes(path);
            bool ok = PngDecoder.TryDecode(file, 2048, out int w, out int h, out byte[] rgba);
            return (ok, w, h, rgba);
        });
        while (!decode.IsCompleted) yield return null;

        Sprite? sprite = null;
        if (decode.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && decode.Result.ok && !_rawUploadBroken)
        {
            var (_, w, h, rgba) = decode.Result;
            try
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(rgba, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { tex.LoadRawTextureData(pin.AddrOfPinnedObject(), rgba.Length); }
                finally { pin.Free(); }
                tex.Apply(false, true);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            catch (Exception ex)
            {
                _rawUploadBroken = true;
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} Raw texture upload unavailable, using LoadImage from now on: {ex.Message}");
            }
        }

        // JPG, 16-bit / interlaced PNG or a failed upload: synchronous decoder.
        sprite ??= LoadSpriteFromFile(album.JacketPath!);
        if (sprite != null) SpriteCache[album.TrackId] = sprite;
        if (Pending.Remove(album.TrackId, out var callbacks) && sprite != null)
        {
            foreach (var cb in callbacks)
            {
                try { cb(sprite); }
                catch (Exception ex) { Melon<Core>.Logger.Warning($"{LOG_PREFIX} Jacket callback failed: {ex.Message}"); }
            }
        }
    }

    public static Sprite? LoadSpriteFromFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} File does not exist: {filePath}");
            return null;
        }

        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // markNonReadable frees the CPU-side copy once the texture is uploaded.
            if (ImageConversion.LoadImage(texture, fileData, true))
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Failed loading sprite '{filePath}': {ex}");
        }

        return null;
    }

    public static bool OverrideBGA(VideoPlayer videoPlayer, CustomAlbumInfo album)
    {
        if (videoPlayer == null || album == null || !album.HasCustomBga)
            return false;

        try
        {
            // Absolute path: a relative "file://hwa/..." resolves against *_Data and VideoPlayer reports an empty file.
            string url = Path.GetFullPath(album.BgaPath!);
            // Called from Initialize and again after the game's async VideoClip callback; keep an already-loaded URL.
            if (videoPlayer.source == VideoSource.Url && videoPlayer.url == url)
                return true;

            if (videoPlayer.isPlaying)
                videoPlayer.Stop();

            // Clear the clip before switching to Url so the clip assignment cannot flip the source back.
            videoPlayer.clip = null;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = url;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.skipOnDrop = true;

            try
            {
                videoPlayer.Prepare();
            }
            catch { }

            Core.DebugLog($"{LOG_PREFIX} Overrode In-Game BGA with '{Path.GetFileName(album.BgaPath)}'");
            return true;
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Failed overriding BGA: {ex.Message}");
            return false;
        }
    }

    public static bool OverrideJacket(Image jacketImage, CustomAlbumInfo album)
    {
        if (jacketImage == null || album == null || !album.HasCustomJacket)
            return false;

        try
        {
            var sprite = GetOrCreateAlbumSprite(album);
            if (sprite != null)
            {
                jacketImage.sprite = sprite;
                jacketImage.color = Color.white;
                jacketImage.enabled = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} Failed overriding Jacket: {ex.Message}");
        }

        return false;
    }
}
