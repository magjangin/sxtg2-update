using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppRhythmGame;
using Il2CppSixtarGate;
using Il2CppSixtarGate.MusicSelect;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SixtarGateMod.Loaders;
using SixtarGateMod.Models;

namespace SixtarGateMod.Managers;

public static class CustomTrackManager
{
    private const string LOG_PREFIX = "[CustomTrackManager]";
    private static readonly string[] BMS_PATTERNS = { "*.bms", "*.bme", "*.bml" };
    private static readonly string[] AUDIO_EXTENSIONS = { ".ogg", ".mp3", ".wav" };
    private static readonly string[] VIDEO_EXTENSIONS = { ".mp4" };
    private static readonly string[] IMAGE_EXTENSIONS = { ".png", ".jpg", ".jpeg" };

    public static string HwaFolderPath { get; private set; } = "";
    public static List<CustomAlbumInfo> LoadedAlbums { get; } = new();
    public static Dictionary<string, CustomAlbumInfo> AlbumsByTrackId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static CustomAlbumInfo? CurrentSelectedAlbum { get; set; } = null;

    public static void Initialize()
    {
        try
        {
            // Base directory is the game root folder (e.g., H:\steam\steamapps\common\Sixtar Gate STARTRAIL)
            string gameFolder = AppDomain.CurrentDomain.BaseDirectory;
            HwaFolderPath = Path.GetFullPath(Path.Combine(gameFolder, "hwa"));

            if (!Directory.Exists(HwaFolderPath))
            {
                Directory.CreateDirectory(HwaFolderPath);
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} Created 'hwa' folder at: {HwaFolderPath}");
            }

            ScanHwaFolder();
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Initialization error: {ex}");
        }
    }

    public static void ScanHwaFolder()
    {
        LoadedAlbums.Clear();
        AlbumsByTrackId.Clear();

        if (!Directory.Exists(HwaFolderPath))
            return;

        Melon<Core>.Logger.Msg($"{LOG_PREFIX} Scanning 'hwa' folder for custom albums...");

        var albumFolders = new List<string>();
        // Check if root hwa folder itself has bms
        if (FindFirstFile(HwaFolderPath, BMS_PATTERNS) != null)
            albumFolders.Add(HwaFolderPath);

        // Subdirectories as albums
        foreach (var dir in Directory.GetDirectories(HwaFolderPath))
        {
            albumFolders.Add(dir);
        }

        int scanCount = 0;
        foreach (var folder in albumFolders)
        {
            string? bmsFile = FindFirstFile(folder, BMS_PATTERNS);
            if (string.IsNullOrEmpty(bmsFile))
                continue;

            try
            {
                var album = BuildAlbumInfo(folder, bmsFile);
                LoadedAlbums.Add(album);
                AlbumsByTrackId[album.TrackId] = album;
                scanCount++;
                Core.DebugLog($"{LOG_PREFIX} Loaded Custom Track: '{album.Title}' by '{album.Artist}' (BPM: {album.Bpm}, Notes: {album.Duration:F1}s, Jacket: {album.JacketPath}, BGA: {album.BgaPath}, BGM: {album.BgmPath}, Preview: {album.PreviewPath})");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} Failed to load album '{Path.GetFileName(folder)}': {ex.Message}");
            }
        }

        Melon<Core>.Logger.Msg($"{LOG_PREFIX} Scan complete: Total {scanCount} custom album(s) discovered.");
    }

    private static CustomAlbumInfo BuildAlbumInfo(string folder, string bmsFile)
    {
        string albumName = Path.GetFileName(folder);
        string trackId = "CUSTOM_" + albumName.ToUpperInvariant().Replace(" ", "_");

        var album = new CustomAlbumInfo
        {
            TrackId = trackId,
            AlbumFolder = folder,
            AlbumName = albumName,
            BmsPath = bmsFile,
            Title = albumName,
            Artist = "Custom Artist"
        };

        // 1. Parse trackinfo.txt / info.txt
        var info = TrackInfoParser.ParseTrackInfo(folder, Path.GetFileNameWithoutExtension(bmsFile));
        if (!string.IsNullOrEmpty(info.Title))
            album.Title = info.Title;
        if (!string.IsNullOrEmpty(info.Artist))
            album.Artist = info.Artist;
        if (info.Difficulties != null && info.Difficulties.Count > 0)
            album.Difficulties = new List<int>(info.Difficulties);

        // 2. Parse BMS headers for BPM & Duration
        var bmsResult = BmsParser.ParseBmsFile(bmsFile);
        if (bmsResult != null)
        {
            if (bmsResult.BaseBpm > 0)
                album.Bpm = bmsResult.BaseBpm;
            if (bmsResult.MaxTime > 0)
                album.Duration = bmsResult.MaxTime + 3.0f; // buffer 3s
        }

        // 3. Resolve Media files
        album.BgmPath = FindNamedFile(folder, "music", AUDIO_EXTENSIONS) ?? FindFirstFile(folder, AUDIO_EXTENSIONS.Select(e => "*" + e).ToArray());
        album.PreviewPath = FindNamedFile(folder, "demo", AUDIO_EXTENSIONS) ?? album.BgmPath;
        album.BgaPath = FindNamedFile(folder, "bg", VIDEO_EXTENSIONS) ?? FindNamedFile(folder, "video", VIDEO_EXTENSIONS) ?? FindFirstFile(folder, VIDEO_EXTENSIONS.Select(e => "*" + e).ToArray());
        album.JacketPath = FindNamedFile(folder, "thumbnail", IMAGE_EXTENSIONS) ?? FindNamedFile(folder, "thumb", IMAGE_EXTENSIONS) ?? FindNamedFile(folder, "jacket", IMAGE_EXTENSIONS) ?? FindFirstFile(folder, IMAGE_EXTENSIONS.Select(e => "*" + e).ToArray());

        return album;
    }

    /// <summary>
    /// Reads the album chart and, when it uses the sxtg2 encoding, rewrites it for the game's BMSConverter.
    /// </summary>
    public static string LoadChartText(CustomAlbumInfo album)
    {
        string content = SxtgBmsConverter.NormalizeValueWidth(File.ReadAllText(album.BmsPath!), out bool widened);
        if (widened)
            Core.DebugLog($"{LOG_PREFIX} '{Path.GetFileName(album.BmsPath)}' uses 3-character values (#WAV001); read as 2-character values");
        if (!SxtgBmsConverter.IsSxtgFormat(content))
            return content;

        string converted = SxtgBmsConverter.Convert(content, out int notes);
        Core.DebugLog($"{LOG_PREFIX} Converted sxtg2 chart '{Path.GetFileName(album.BmsPath)}' to game BMS layout ({notes} markers)");
        return converted;
    }

    public static CustomAlbumInfo? FindAlbum(string? trackId, string? displayName = null)
    {
        if (!string.IsNullOrEmpty(trackId) && AlbumsByTrackId.TryGetValue(trackId, out var album))
            return album;
        if (!string.IsNullOrEmpty(displayName))
            return LoadedAlbums.Find(a => a.Title == displayName);
        return null;
    }

    private static string? FindNamedFile(string folder, string baseName, string[] extensions)
    {
        foreach (var ext in extensions)
        {
            string candidate = Path.Combine(folder, baseName + ext);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? FindFirstFile(string folder, string[] patterns)
    {
        try
        {
            foreach (var pat in patterns)
            {
                var files = Directory.GetFiles(folder, pat, SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Builds the list of ITrack objects to present in the "CUSTOM" category tab.
    /// Uses template TrackData (Starting Point) as donor for required Unity structures.
    /// </summary>
    public static Il2CppSystem.Collections.Generic.List<ITrack> CreateCustomTrackList(ITrack template)
    {
        var resultList = new Il2CppSystem.Collections.Generic.List<ITrack>();
        var templateData = template.TryCast<TrackLoader.TrackData>();

        if (LoadedAlbums.Count == 0)
        {
            // Fallback: If hwa/ has no custom albums yet, supply default 1-note Starting Point custom track
            var custom = CloneTrackData(template, templateData, "CUSTOM_1NOTE_DEFAULT", "Starting Point (Custom 1-Note)", "Starting Point Custom", "LucaProject", 160f, 125f, null);
            resultList.Add(custom.Cast<ITrack>());
            return resultList;
        }

        foreach (var album in LoadedAlbums)
        {
            var custom = CloneTrackData(
                template,
                templateData,
                album.TrackId,
                album.Title,
                album.Title,
                album.Artist,
                album.Bpm,
                album.Duration,
                album.Difficulties);

            resultList.Add(custom.Cast<ITrack>());
        }

        return resultList;
    }

    private static TrackLoader.TrackData CloneTrackData(
        ITrack template,
        TrackLoader.TrackData? templateData,
        string trackId,
        string displayName,
        string displayNameEN,
        string composer,
        float bpm,
        float duration,
        List<int>? difficulties)
    {
        var custom = new TrackLoader.TrackData();

        // Determine level array size (typically 4: Comet, Nova, SuperNova, Quasar)
        int levelCount = (templateData?.solarLevels != null && templateData.solarLevels.Length > 0)
            ? templateData.solarLevels.Length
            : 4;

        var solarArr = new Il2CppStringArray(levelCount);
        var lunarArr = new Il2CppStringArray(levelCount);

        for (int i = 0; i < levelCount; i++)
        {
            string diffStr;
            if (difficulties != null && i < difficulties.Count)
            {
                // Format with two digits (e.g. 1 -> "01", 10 -> "10")
                diffStr = difficulties[i].ToString("D2");
            }
            else if (templateData?.solarLevels != null && i < templateData.solarLevels.Length)
            {
                diffStr = templateData.solarLevels[i];
            }
            else
            {
                diffStr = "10";
            }

            solarArr[i] = diffStr;
            lunarArr[i] = diffStr;
        }

        if (templateData != null)
        {
            custom._TrackID_k__BackingField = trackId;
            custom._DisplayName_k__BackingField = displayName;
            custom._DisplayNameEN_k__BackingField = displayNameEN;
            custom._Composer_k__BackingField = composer;
            custom._Category_k__BackingField = Core.CUSTOM_CATEGORY_NAME;
            custom._LicenseCopy_k__BackingField = "";
            custom._DisplayBPM_k__BackingField = $"{bpm:F0}";
            custom._BGAOffset_k__BackingField = templateData._BGAOffset_k__BackingField;
            custom._VisibilityID_k__BackingField = templateData._VisibilityID_k__BackingField;
            custom._UnlockID_k__BackingField = templateData._UnlockID_k__BackingField;
            custom._UpdateData_k__BackingField = templateData._UpdateData_k__BackingField;
            custom._ArtistInfos_k__BackingField = templateData._ArtistInfos_k__BackingField;
            custom.solarLevels = solarArr;
            custom.lunarLevels = lunarArr;
        }
        else
        {
            custom._TrackID_k__BackingField = trackId;
            custom._DisplayName_k__BackingField = displayName;
            custom._DisplayNameEN_k__BackingField = displayNameEN;
            custom._Composer_k__BackingField = composer;
            custom._Category_k__BackingField = Core.CUSTOM_CATEGORY_NAME;
            custom._LicenseCopy_k__BackingField = "";
            custom._DisplayBPM_k__BackingField = $"{bpm:F0}";
            custom._BGAOffset_k__BackingField = template.BGAOffset;
            custom._VisibilityID_k__BackingField = template.VisibilityID;
            custom._UnlockID_k__BackingField = template.UnlockID;
            custom._UpdateData_k__BackingField = template.UpdateData;
            custom._ArtistInfos_k__BackingField = template.ArtistInfos;
            custom.solarLevels = solarArr;
            custom.lunarLevels = lunarArr;
        }

        return custom;
    }
}
