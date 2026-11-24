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
    // Jackets that could not be decoded. Without this every list-row refresh retried a synchronous main-thread decode
    // (and logged an error) for a broken or unsupported image file.
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static bool _preloading;

    /// <summary>
    /// 스프라이트 캐시·대기·실패 기록의 키: 곡 ID + 자켓 파일 지문(크기·수정 시각). 자켓 그림을 바꾸면 hwa 재스캔 뒤 키가
    /// 달라져 새로 읽는다. 같은 곡의 옛 키 항목은 새 스프라이트를 넣을 때 캐시에서 뺀다(화면에 아직 쓰일 수 있어 파괴는 안 함).
    /// </summary>
    private static string KeyOf(CustomAlbumInfo album) => album.TrackId + "|" + album.JacketStamp;
    private static readonly Dictionary<string, string> CurrentKeyByTrack = new(StringComparer.OrdinalIgnoreCase);

    private static void Store(CustomAlbumInfo album, string key, Sprite sprite)
    {
        if (CurrentKeyByTrack.TryGetValue(album.TrackId, out var old) && !string.Equals(old, key, StringComparison.OrdinalIgnoreCase))
            SpriteCache.Remove(old);
        CurrentKeyByTrack[album.TrackId] = key;
        SpriteCache[key] = sprite;
    }

    // 관리 null 비교: 캐시 스프라이트는 DontUnloadUnusedAsset이고 이 모드가 파괴하지 않는다. Unity의 !=는 IL2CPP
    // op_Equality라 목록 스크롤 중 행마다 들었다.
    private static bool TryCached(string key, out Sprite sprite)
        => SpriteCache.TryGetValue(key, out sprite!) && sprite is not null;

    /// <summary>
    /// Synchronous jacket (decodes on the main thread on a cache miss). Used by the play-flow screens, which normally
    /// hit the cache because the song was shown in the music select first.
    /// </summary>
    public static Sprite? GetOrCreateAlbumSprite(CustomAlbumInfo album)
    {
        if (album == null || !album.HasCustomJacket) return null;
        string key = KeyOf(album);
        if (TryCached(key, out var cached))
            return cached;
        if (Failed.Contains(key)) return null;

        var sprite = LoadSpriteFromFile(album.JacketPath!);
        if (sprite != null) Store(album, key, sprite);
        else
        {
            Failed.Add(key);
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 스프라이트 생성 실패: '{album.JacketPath}' (이 곡은 기본 자켓 유지)");
        }
        return sprite;
    }

    /// <summary>
    /// Jacket without blocking the main thread: a cache hit calls back immediately, otherwise the PNG is decoded by
    /// PngDecoder on a worker thread and every waiting callback runs when it is done.
    /// </summary>
    public static void RequestAlbumSprite(CustomAlbumInfo album, Action<Sprite> onReady)
    {
        if (album == null || !album.HasCustomJacket) return;
        string key = KeyOf(album);
        if (TryCached(key, out var cached))
        {
            onReady(cached);
            return;
        }
        if (Failed.Contains(key)) return;
        if (Pending.TryGetValue(key, out var waiting))
        {
            waiting.Add(onReady);
            return;
        }
        Pending[key] = new List<Action<Sprite>> { onReady };
        MelonCoroutines.Start(LoadAsync(album, key));
    }

    /// <summary>Clears failed jacket records so newly fixed image files can be retried after a folder refresh.</summary>
    public static void ClearFailed() => Failed.Clear();

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
        try
        {
            foreach (var album in albums)
            {
                string key = KeyOf(album);
                if (!album.HasCustomJacket || SpriteCache.ContainsKey(key) || Failed.Contains(key)) continue;
                bool done = false;
                RequestAlbumSprite(album, _ => done = true);
                float waitUntil = Time.realtimeSinceStartup + 10f;
                while (!done && Pending.ContainsKey(key) && Time.realtimeSinceStartup < waitUntil) yield return null;
                if (SpriteCache.ContainsKey(key)) loaded++;
            }
            Core.DebugLog($"{LOG_PREFIX} 자켓 {loaded}장 미리 로드 ({(DateTime.Now - started).TotalSeconds:F1}초)");
        }
        finally
        {
            _preloading = false;
        }
    }

    private static bool _rawUploadBroken;

    /// <summary>
    /// Uploads a decoded RGBA32 buffer (bottom row first) in one call. Returns null when raw upload is unavailable,
    /// so the caller can fall back to ImageConversion.LoadImage.
    /// </summary>
    public static Texture2D? TextureFromRgba(int w, int h, byte[] rgba)
    {
        if (_rawUploadBroken) return null;
        Texture2D? tex = null;
        try
        {
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(rgba, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { tex.LoadRawTextureData(pin.AddrOfPinnedObject(), rgba.Length); }
            finally { pin.Free(); }
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
        catch (Exception ex)
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            _rawUploadBroken = true;
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 원시 텍스처 업로드를 쓸 수 없어 이후 LoadImage 사용: {ex.Message}");
            return null;
        }
    }

    // PNG bytes are read and decoded on a worker thread (Loaders/PngDecoder); the main thread only uploads the
    // finished RGBA buffer. Unity's own async path is unusable here: DownloadHandlerTexture's constructor is
    // stripped from the game build ("Method not found").
    private static System.Collections.IEnumerator LoadAsync(CustomAlbumInfo album, string key)
    {
        Sprite? sprite = null;
        try
        {
            string? path = null;
            try { path = Path.GetFullPath(album.JacketPath!); }
            catch (Exception ex) { Melon<Core>.Logger.Warning($"{LOG_PREFIX} 자켓 경로 확인 실패 ({album.Title}): {ex.Message}"); }

            if (path != null)
            {
                var decode = System.Threading.Tasks.Task.Run(() =>
                {
                    byte[] file = File.ReadAllBytes(path);
                    bool ok = PngDecoder.TryDecode(file, 1024, out int w, out int h, out byte[] rgba);
                    // PNG 해독에 실패한 JPG 등은 이미 읽은 바이트를 메인 스레드로 넘겨 디스크 이중 읽기를 막는다.
                    return (ok, w, h, rgba, raw: ok ? null : file);
                });
                while (!decode.IsCompleted) yield return null;

                // 비동기 해독 도중 동기 경로(플레이 씬 등)가 먼저 캐시를 채웠으면 중복 GPU 텍스처를 만들지 않는다.
                if (TryCached(key, out var already))
                {
                    sprite = already;
                }
                else
                {
                    byte[]? rawBytes = null;
                    if (decode.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    {
                        var res = decode.Result;
                        rawBytes = res.raw;
                        if (res.ok)
                        {
                            Texture2D? tex = null;
                            try
                            {
                                tex = TextureFromRgba(res.w, res.h, res.rgba);
                                if (tex != null)
                                    sprite = Features.Overlay.MakeSprite(tex);
                            }
                            catch (Exception ex)
                            {
                                if (tex != null) UnityEngine.Object.Destroy(tex);
                                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 자켓 텍스처 업로드 실패 ({album.Title}): {ex.Message}");
                            }
                        }
                    }

                    // JPG, 16-bit / interlaced PNG or a failed upload: synchronous decoder (reusing bytes read on worker when available).
                    if (sprite == null)
                        sprite = rawBytes != null
                            ? LoadSpriteFromBytes(rawBytes, album.JacketPath!)
                            : LoadSpriteFromFile(album.JacketPath!);
                }
            }

            if (sprite != null) Store(album, key, sprite);
            else Failed.Add(key);
        }
        finally
        {
            if (Pending.Remove(key, out var callbacks) && sprite != null)
            {
                foreach (var cb in callbacks)
                {
                    try { cb(sprite); }
                    catch (Exception ex) { Melon<Core>.Logger.Warning($"{LOG_PREFIX} 자켓 콜백 실패: {ex.Message}"); }
                }
            }
        }
    }

    public static Sprite? LoadSpriteFromFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 파일 없음: {filePath}");
            return null;
        }

        try
        {
            return LoadSpriteFromBytes(File.ReadAllBytes(filePath), filePath);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 스프라이트 로드 실패 '{filePath}': {ex}");
            return null;
        }
    }

    private static Sprite? LoadSpriteFromBytes(byte[] fileData, string label)
    {
        Texture2D? texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // markNonReadable frees the CPU-side copy once the texture is uploaded.
            if (ImageConversion.LoadImage(texture, fileData, true))
            {
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return Features.Overlay.MakeSprite(texture);
            }
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
        catch (Exception ex)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 스프라이트 로드 실패 '{label}': {ex}");
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
            catch (Exception ex) { Core.DebugLog($"{LOG_PREFIX} BGA Prepare() 예외 (무시): {ex.Message}"); }

            Core.DebugLog($"{LOG_PREFIX} 인게임 BGA를 '{Path.GetFileName(album.BgaPath)}'로 교체");
            return true;
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} BGA 교체 실패: {ex.Message}");
            return false;
        }
    }
}
