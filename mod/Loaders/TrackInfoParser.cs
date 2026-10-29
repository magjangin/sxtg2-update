using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;

namespace SixtarGateMod.Loaders;

public static class TrackInfoParser
{
    public class TrackInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public List<int> Difficulties { get; set; } = new();
    }

    private static readonly string[] TRACK_INFO_FILE_NAMES = { "trackinfo.txt", "info.txt" };

    public static TrackInfo ParseTrackInfo(string albumFolder, string searchKey)
    {
        var trackInfo = new TrackInfo();

        try
        {
            if (string.IsNullOrEmpty(albumFolder) || !Directory.Exists(albumFolder))
            {
                return trackInfo;
            }

            string? targetFile = FindTrackInfoFile(albumFolder, searchKey);
            if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
            {
                return trackInfo;
            }

            ParseTrackInfoFile(targetFile, trackInfo);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[TrackInfoParser] Error parsing track info: {ex.Message}");
        }

        return trackInfo;
    }

    private static string? FindTrackInfoFile(string albumFolder, string searchKey)
    {
        try
        {
            var txtFiles = Directory.GetFiles(albumFolder, "*.txt", SearchOption.TopDirectoryOnly);
            if (txtFiles.Length == 0)
                return null;

            if (!string.IsNullOrEmpty(searchKey))
            {
                string trackIdFileName = $"{searchKey}.txt";
                var exactMatch = txtFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals(trackIdFileName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null)
                    return exactMatch;
            }

            foreach (var defaultFileName in TRACK_INFO_FILE_NAMES)
            {
                var defaultFile = txtFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals(defaultFileName, StringComparison.OrdinalIgnoreCase));
                if (defaultFile != null)
                    return defaultFile;
            }

            return txtFiles[0];
        }
        catch
        {
            return null;
        }
    }

    private static void ParseTrackInfoFile(string filePath, TrackInfo trackInfo)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                int colonIndex = line.IndexOf(':');
                if (colonIndex < 0)
                    continue;

                string key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                string value = line.Substring(colonIndex + 1).Trim();

                if (string.IsNullOrEmpty(value))
                    continue;

                if (key.Contains("곡 제목") || key.Contains("title") || key == "제목")
                {
                    trackInfo.Title = value;
                }
                else if (key.Contains("아티스트") || key.Contains("artist") || key == "작곡가")
                {
                    trackInfo.Artist = value;
                }
                else if (key.Contains("난이도") || key.Contains("difficulty") || key.Contains("level"))
                {
                    ParseDifficulties(value, trackInfo.Difficulties);
                }
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[TrackInfoParser] Error parsing file '{filePath}': {ex.Message}");
        }
    }

    private static void ParseDifficulties(string value, List<int> difficulties)
    {
        try
        {
            difficulties.Clear();
            var parts = value.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out int diff))
                {
                    difficulties.Add(diff);
                }
            }
        }
        catch
        {
        }
    }
}
