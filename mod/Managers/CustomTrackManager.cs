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
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 'hwa' 폴더 생성: {HwaFolderPath}");
            }

            ScanHwaFolder();
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 초기화 오류: {ex}");
        }
    }

    public static void ScanHwaFolder()
    {
        LoadedAlbums.Clear();
        AlbumsByTrackId.Clear();

        if (!Directory.Exists(HwaFolderPath))
            return;

        Melon<Core>.Logger.Msg($"{LOG_PREFIX} 'hwa' 폴더에서 커스텀 곡 검색 중...");

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
                // "A B" and "A_B" folders map to the same ID; keep both by numbering the later one.
                if (AlbumsByTrackId.ContainsKey(album.TrackId))
                {
                    string baseId = album.TrackId;
                    int n = 2;
                    while (AlbumsByTrackId.ContainsKey($"{baseId}_{n}")) n++;
                    album.TrackId = $"{baseId}_{n}";
                    Melon<Core>.Logger.Warning($"{LOG_PREFIX} 곡 ID '{baseId}'가 겹쳐 '{Path.GetFileName(folder)}'는 '{album.TrackId}'로 등록");
                }
                LoadedAlbums.Add(album);
                AlbumsByTrackId[album.TrackId] = album;
                scanCount++;
                Core.DebugLog($"{LOG_PREFIX} 커스텀 곡 로드: '{album.Title}' - '{album.Artist}' (BPM: {album.Bpm}, 노트 길이: {album.Duration:F1}초, 자켓: {album.JacketPath}, BGA: {album.BgaPath}, 음원: {album.BgmPath}, 프리뷰: {album.PreviewPath})");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} 곡 폴더 로드 실패 '{Path.GetFileName(folder)}': {ex.Message}");
            }
        }

        Melon<Core>.Logger.Msg($"{LOG_PREFIX} 검색 완료: 커스텀 곡 {scanCount}곡 발견");
    }

    private static CustomAlbumInfo BuildAlbumInfo(string folder, string bmsFile)
    {
        string albumName = Path.GetFileName(folder);
        string trackId = "CUSTOM_" + albumName.ToUpperInvariant().Replace(" ", "_");

        var album = new CustomAlbumInfo
        {
            TrackId = trackId,
            AlbumFolder = folder,
            BmsPath = bmsFile,
            Title = TrackInfoParser.NormalizeMetadataText(albumName),
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

        // 2. Parse BMS headers for BPM & Duration (and fallback Title/Artist)
        var bmsResult = BmsParser.ParseBmsFile(bmsFile);
        if (bmsResult != null)
        {
            if (bmsResult.BaseBpm > 0)
                album.Bpm = bmsResult.BaseBpm;
            if (bmsResult.MaxTime > 0)
                album.Duration = bmsResult.MaxTime + 3.0f; // buffer 3s
            // info.txt wins even when its title equals the folder name; sxtg2 charts usually carry "#TITLE test".
            if (string.IsNullOrEmpty(info.Title) && !string.IsNullOrEmpty(bmsResult.Title))
                album.Title = bmsResult.Title;
            if (string.IsNullOrEmpty(info.Artist) && !string.IsNullOrEmpty(bmsResult.Artist))
                album.Artist = bmsResult.Artist;
        }

        // 3. Resolve Media files
        album.BgmPath = FindNamedFile(folder, "music", AUDIO_EXTENSIONS) ?? FindFirstFile(folder, AUDIO_EXTENSIONS.Select(e => "*" + e).ToArray());
        album.PreviewPath = FindNamedFile(folder, "demo", AUDIO_EXTENSIONS) ?? album.BgmPath;
        album.BgaPath = FindNamedFile(folder, "bg", VIDEO_EXTENSIONS) ?? FindNamedFile(folder, "video", VIDEO_EXTENSIONS) ?? FindFirstFile(folder, VIDEO_EXTENSIONS.Select(e => "*" + e).ToArray());
        album.JacketPath = FindNamedFile(folder, "thumbnail", IMAGE_EXTENSIONS) ?? FindNamedFile(folder, "thumb", IMAGE_EXTENSIONS) ?? FindNamedFile(folder, "jacket", IMAGE_EXTENSIONS) ?? FindFirstFile(folder, IMAGE_EXTENSIONS.Select(e => "*" + e).ToArray());

        return album;
    }

    private static string? _chartPath, _chart;
    private static DateTime _chartWrite;

    /// <summary>
    /// Reads the album chart and, when it uses the sxtg2 encoding, rewrites it for the game's BMSConverter.
    /// The play-scene bootstrap and MikazukiConverter.Parse both ask for it on one load, so the result is kept until
    /// another chart is requested or the file changes.
    /// </summary>
    public static string LoadChartText(CustomAlbumInfo album)
    {
        string path = album.BmsPath!;
        var write = File.GetLastWriteTimeUtc(path);
        if (_chart != null && _chartPath == path && _chartWrite == write) return _chart;

        string content = SxtgBmsConverter.NormalizeValueWidth(File.ReadAllText(path), out bool widened);
        if (widened)
            Core.DebugLog($"{LOG_PREFIX} '{Path.GetFileName(path)}'는 3자리 값(#WAV001) 차트라 2자리 값으로 변환해 읽음");
        if (SxtgBmsConverter.IsSxtgFormat(content))
        {
            content = SxtgBmsConverter.Convert(content, out int notes);
            Core.DebugLog($"{LOG_PREFIX} sxtg2 차트 '{Path.GetFileName(path)}'를 게임 BMS 형식으로 변환 (마커 {notes}개)");
        }
        (_chartPath, _chartWrite, _chart) = (path, write, content);
        return content;
    }

    public static CustomAlbumInfo? FindAlbum(string? trackId)
        => !string.IsNullOrEmpty(trackId) && AlbumsByTrackId.TryGetValue(trackId, out var album) ? album : null;

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
                // Windows matches a 3-letter extension pattern against longer extensions too ("*.bms" also
                // returns ".bmson", a JSON chart): keep exact extensions only.
                string ext = pat.StartsWith("*", StringComparison.Ordinal) ? pat.Substring(1) : "";
                var files = Array.FindAll(Directory.GetFiles(folder, pat, SearchOption.TopDirectoryOnly),
                    f => ext.Length == 0 || string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase));
                if (files.Length == 0) continue;
                // Directory order is only alphabetical on NTFS (exFAT/FAT32 return creation order): sort so the same
                // folder always picks the same chart / audio / image.
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
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

        // With no album in hwa/ yet, the tab still shows a 1-note Starting Point so the pipeline can be tried out.
        if (LoadedAlbums.Count == 0)
            resultList.Add(CloneTrackData(template, templateData, "CUSTOM_1NOTE_DEFAULT", "Starting Point (Custom 1-Note)", "Starting Point Custom", "LucaProject", 160f, null).Cast<ITrack>());

        foreach (var album in LoadedAlbums)
            resultList.Add(CloneTrackData(template, templateData, album.TrackId, album.Title, album.Title, album.Artist, album.Bpm, album.Difficulties).Cast<ITrack>());

        return resultList;
    }

    private static readonly Action<TrackLoader.TrackData, TrackLoader.TrackData> CopyTemplateTrackProperties =
        PatchResilience.CreatePropertyCloner<TrackLoader.TrackData>();

    private static TrackLoader.TrackData CloneTrackData(
        ITrack template, TrackLoader.TrackData? templateData, string trackId, string displayName, string displayNameEN,
        string composer, float bpm, List<int>? difficulties)
    {
        // Level array size follows the template (typically 4: Comet, Nova, SuperNova, Quasar).
        var templateLevels = templateData?.solarLevels;
        int levelCount = templateLevels != null && templateLevels.Length > 0 ? templateLevels.Length : 4;
        var solarArr = new Il2CppStringArray(levelCount);
        var lunarArr = new Il2CppStringArray(levelCount);
        for (int i = 0; i < levelCount; i++)
        {
            string level = difficulties != null && i < difficulties.Count ? difficulties[i].ToString("D2")
                : templateLevels != null && i < templateLevels.Length ? templateLevels[i]
                : "10";
            solarArr[i] = level;
            lunarArr[i] = level;
        }

        var cloned = new TrackLoader.TrackData();
        // Copy all properties from the donor TrackData first so any new fields added in future game updates are preserved.
        if (templateData != null)
            CopyTemplateTrackProperties(templateData, cloned);

        cloned._TrackID_k__BackingField = trackId;
        cloned._DisplayName_k__BackingField = displayName;
        cloned._DisplayNameEN_k__BackingField = displayNameEN;
        cloned._Composer_k__BackingField = composer;
        cloned._Category_k__BackingField = Core.CUSTOM_CATEGORY_NAME;
        cloned._LicenseCopy_k__BackingField = "";
        cloned._DisplayBPM_k__BackingField = $"{bpm:F0}";
        cloned._BGAOffset_k__BackingField = template.BGAOffset;
        cloned._VisibilityID_k__BackingField = template.VisibilityID;
        cloned._UnlockID_k__BackingField = template.UnlockID;
        cloned._UpdateData_k__BackingField = template.UpdateData;
        cloned._ArtistInfos_k__BackingField = template.ArtistInfos;
        cloned.solarLevels = solarArr;
        cloned.lunarLevels = lunarArr;
        return cloned;
    }
}
