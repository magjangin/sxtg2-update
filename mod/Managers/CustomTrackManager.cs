using System;
using System.Collections.Generic;
using System.IO;
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
    private static readonly string[] BMS_EXTENSIONS = { ".bms", ".bme", ".bml" };
    private static readonly string[] AUDIO_EXTENSIONS = { ".ogg", ".mp3", ".wav" };
    private static readonly string[] VIDEO_EXTENSIONS = { ".mp4" };
    private static readonly string[] IMAGE_EXTENSIONS = { ".png", ".jpg", ".jpeg" };

    public static string HwaFolderPath { get; private set; } = "";

    // 스캔은 작업 스레드가 이 두 필드에 채운다. 메인 스레드는 아래 속성으로만 읽고, 속성은 스캔이 끝나기를 기다린다.
    private static readonly List<CustomAlbumInfo> Albums = new();
    private static readonly Dictionary<string, CustomAlbumInfo> ById = new(StringComparer.OrdinalIgnoreCase);
    private static System.Threading.Tasks.Task<List<ModLog.Entry>>? _scan;
    private static long _lastHwaStamp;

    public static List<CustomAlbumInfo> LoadedAlbums { get { CompleteScan(); return Albums; } }
    public static Dictionary<string, CustomAlbumInfo> AlbumsByTrackId { get { CompleteScan(); return ById; } }

    public static CustomAlbumInfo? CurrentSelectedAlbum { get; set; } = null;

    /// <summary>
    /// hwa 폴더를 만들고 스캔을 작업 스레드에서 시작한다. 15곡 기준 0.1~0.4초(디스크 캐시에 따라)가 부팅 메인 스레드에서
    /// 빠지고 Harmony 적용(2~3초)과 겹친다. 결과는 <see cref="CompleteScan"/>(부팅 끝, 또는 첫 조회)에서 받는다.
    /// </summary>
    public static void Initialize(string gameRoot)
    {
        try
        {
            // Same root as every other folder of the mod (MelonEnvironment.GameRootDirectory). AppDomain.BaseDirectory
            // happened to be the game folder too, but it is the loader's choice, not a guarantee.
            HwaFolderPath = Path.GetFullPath(Path.Combine(gameRoot, "hwa"));

            if (!Directory.Exists(HwaFolderPath))
            {
                Directory.CreateDirectory(HwaFolderPath);
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 'hwa' 폴더 생성: {HwaFolderPath}");
            }

            Melon<Core>.Logger.Msg($"{LOG_PREFIX} 'hwa' 폴더에서 커스텀 곡 검색 시작 (작업 스레드)");
            _scan = System.Threading.Tasks.Task.Run(() => ModLog.RunCaptured(ScanHwaFolder));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 초기화 오류: {ex}");
        }
    }

    /// <summary>스캔이 끝나기를 기다리고(보통 이미 끝남) 그동안 모은 로그를 출력한다. 메인 스레드에서만 부른다.</summary>
    public static void CompleteScan()
    {
        var scan = _scan;
        if (scan == null) return;
        _scan = null;
        try
        {
            ModLog.Flush(scan.Result);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} 커스텀 곡 검색 실패: {ex.GetBaseException()}");
        }
    }

    /// <summary>
    /// hwa 폴더나 직계 하위 폴더가 변경되었으면 다시 스캔하고 true를 반환한다.
    /// CUSTOM 탭 진입 시 호출되어 게임 재시작 없이 새 커스텀 곡을 인식한다.
    /// </summary>
    public static bool RefreshIfChanged()
    {
        CompleteScan();
        if (string.IsNullOrEmpty(HwaFolderPath) || !Directory.Exists(HwaFolderPath))
            return false;

        // 곡 폴더 안 파일까지 훑으므로(아래) 탭을 연달아 바꿀 때 디스크를 반복해서 읽지 않게 1초에 한 번만 확인.
        long nowTicks = Environment.TickCount64;
        if (nowTicks - _lastStampCheck < 1000) return false;
        _lastStampCheck = nowTicks;

        long current = ComputeHwaStamp();
        if (current == _lastHwaStamp)
            return false;

        _chart = null;
        _chartPath = null;
        MediaOverrideManager.ClearFailed();
        var entries = ModLog.RunCaptured(ScanHwaFolder);
        ModLog.Flush(entries);
        Melon<Core>.Logger.Msg($"{LOG_PREFIX} 'hwa' 폴더 변경 감지 → 커스텀 곡 다시 스캔 ({Albums.Count}곡)");
        return true;
    }

    private static long _lastStampCheck = long.MinValue / 2;

    /// <summary>
    /// hwa 폴더(루트도 곡 폴더 하나로 쓰임)와 직계 하위 폴더의 파일 목록·크기·수정 시각을 접은 값.
    /// 폴더 수정 시각만 보던 때는 파일을 만들거나 지울 때만 바뀌어서, info.txt를 제자리에서 고쳐 저장하면
    /// (메모장 등) 제목·아티스트 변경이 게임을 다시 켤 때까지 반영되지 않았다. 열거 결과의 속성(FileSystemInfo)을
    /// 쓰므로 파일마다 따로 stat하지 않는다.
    /// </summary>
    private static long ComputeHwaStamp()
    {
        try
        {
            var root = new DirectoryInfo(HwaFolderPath);
            long hash = 17;
            hash = hash * 31 + FoldFiles(root);
            var dirs = root.GetDirectories();
            Array.Sort(dirs, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            hash = hash * 31 + dirs.Length;
            foreach (var dir in dirs)
            {
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(dir.Name);
                hash = hash * 31 + FoldFiles(dir);
            }
            return hash;
        }
        catch
        {
            return _lastHwaStamp;
        }
    }

    /// <summary>폴더 안 파일들의 이름·크기·수정 시각을 순서와 무관하게 접는다(파일 시스템마다 열거 순서가 달라서).</summary>
    private static long FoldFiles(DirectoryInfo dir)
    {
        long sum = dir.LastWriteTimeUtc.Ticks, xor = 0;
        foreach (var f in dir.EnumerateFiles())
        {
            long h = StringComparer.OrdinalIgnoreCase.GetHashCode(f.Name);
            h = h * 1_000_003 + f.Length;
            h = h * 1_000_003 + f.LastWriteTimeUtc.Ticks;
            sum += h;
            xor ^= h;
        }
        return sum * 31 + xor;
    }

    // 작업 스레드에서 돈다: Unity/IL2CPP를 부르지 않고, 로그는 ModLog로(메인 스레드에서 출력), 공개 속성 대신 필드에 쓴다.
    private static void ScanHwaFolder()
    {
        Albums.Clear();
        ById.Clear();
        _lastHwaStamp = ComputeHwaStamp();

        if (!Directory.Exists(HwaFolderPath))
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var subDirs = Directory.GetDirectories(HwaFolderPath);
        Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);
        var albumFolders = new List<string>(1 + subDirs.Length) { HwaFolderPath };
        albumFolders.AddRange(subDirs);

        int scanCount = 0;
        foreach (var folder in albumFolders)
        {
            // 폴더당 1회만 디렉터리를 읽고 정렬된 파일 목록을 메모리에서 재사용(곡 정보·차트·미디어 탐색 공용).
            string[] files = GetSortedFiles(folder);
            string? bmsFile = FindFirstByExtension(files, BMS_EXTENSIONS);
            if (string.IsNullOrEmpty(bmsFile))
                continue;

            try
            {
                var album = BuildAlbumInfo(folder, bmsFile, files);
                // "A B" and "A_B" folders map to the same ID; keep both by numbering the later one.
                if (ById.ContainsKey(album.TrackId))
                {
                    string baseId = album.TrackId;
                    int n = 2;
                    while (ById.ContainsKey($"{baseId}_{n}")) n++;
                    album.TrackId = $"{baseId}_{n}";
                    ModLog.Warning($"{LOG_PREFIX} 곡 ID '{baseId}'가 겹쳐 '{Path.GetFileName(folder)}'는 '{album.TrackId}'로 등록");
                }
                Albums.Add(album);
                ById[album.TrackId] = album;
                scanCount++;
                ModLog.Debug($"{LOG_PREFIX} 커스텀 곡 로드: '{album.Title}' - '{album.Artist}' (BPM: {album.Bpm}, 노트 길이: {album.Duration:F1}초, 자켓: {album.JacketPath}, BGA: {album.BgaPath}, 음원: {album.BgmPath}, 프리뷰: {album.PreviewPath})");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"{LOG_PREFIX} 곡 폴더 로드 실패 '{Path.GetFileName(folder)}': {ex.Message}");
            }
        }

        ModLog.Msg($"{LOG_PREFIX} 검색 완료: 커스텀 곡 {scanCount}곡 발견 ({sw.Elapsed.TotalMilliseconds:F0}ms, 작업 스레드)");
    }

    private static CustomAlbumInfo BuildAlbumInfo(string folder, string bmsFile, string[] files)
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
        var info = TrackInfoParser.ParseTrackInfo(folder, Path.GetFileNameWithoutExtension(bmsFile), files);
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

        // 3. Resolve Media files from preloaded folder file list
        album.BgmPath = FindNamedFile(files, "music", AUDIO_EXTENSIONS)
            ?? FindFirstByExtension(files, AUDIO_EXTENSIONS);
        album.PreviewPath = FindNamedFile(files, "demo", AUDIO_EXTENSIONS)
            ?? album.BgmPath;
        album.BgaPath = FindNamedFile(files, "bg", VIDEO_EXTENSIONS)
            ?? FindNamedFile(files, "video", VIDEO_EXTENSIONS)
            ?? FindFirstByExtension(files, VIDEO_EXTENSIONS);
        album.JacketPath = FindNamedFile(files, "thumbnail", IMAGE_EXTENSIONS)
            ?? FindNamedFile(files, "thumb", IMAGE_EXTENSIONS)
            ?? FindNamedFile(files, "jacket", IMAGE_EXTENSIONS)
            ?? FindFirstByExtension(files, IMAGE_EXTENSIONS);

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

        string rawText = TextDecoder.ReadAllText(path, TextDecoder.BmsFallback);
        string content = SxtgBmsConverter.NormalizeValueWidth(rawText, out bool widened);
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

    private static string[] GetSortedFiles(string folder)
    {
        try
        {
            string[] files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
            // Directory order is only alphabetical on NTFS (exFAT/FAT32 return creation order): sort once here.
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? FindNamedFile(string[] files, string baseName, string[] extensions)
    {
        foreach (var ext in extensions)
        {
            string targetName = baseName + ext;
            foreach (var file in files)
            {
                if (string.Equals(Path.GetFileName(file), targetName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        return null;
    }

    private static string? FindFirstByExtension(string[] files, string[] extensions)
    {
        foreach (var ext in extensions)
        {
            foreach (var file in files)
            {
                // Exact extension match only (Windows "*.bms" wildcards also match ".bmson").
                if (string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
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

        foreach (var album in Albums)
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
