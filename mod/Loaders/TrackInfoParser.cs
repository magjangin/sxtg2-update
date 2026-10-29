using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Album metadata from a "key: value" text file: {chart name}.txt, else trackinfo.txt / info.txt, else the first .txt.
/// Keys: 곡 제목/제목/title, 아티스트/작곡가/artist, 난이도/difficulty/level ("1, 4, 7, 10").
/// </summary>
public static class TrackInfoParser
{
    public class TrackInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public List<int> Difficulties { get; set; } = new();
    }

    public static TrackInfo ParseTrackInfo(string albumFolder, string chartName)
    {
        var info = new TrackInfo();
        try
        {
            string? file = FindTrackInfoFile(albumFolder, chartName);
            if (file == null) return info;

            foreach (var raw in File.ReadLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (value.Length == 0) continue;

                if (key.Contains("곡 제목") || key.Contains("title") || key == "제목")
                    info.Title = value;
                else if (key.Contains("아티스트") || key.Contains("artist") || key == "작곡가")
                    info.Artist = value;
                else if (key.Contains("난이도") || key.Contains("difficulty") || key.Contains("level"))
                    info.Difficulties = value.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => int.TryParse(p, out int d) ? (int?)d : null).OfType<int>().ToList();
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[TrackInfoParser] Error parsing track info in '{albumFolder}': {ex.Message}");
        }
        return info;
    }

    private static string? FindTrackInfoFile(string albumFolder, string chartName)
    {
        var txtFiles = Directory.GetFiles(albumFolder, "*.txt", SearchOption.TopDirectoryOnly);
        string? Named(string name) => txtFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        return (chartName.Length > 0 ? Named(chartName + ".txt") : null) ?? Named("trackinfo.txt") ?? Named("info.txt") ?? txtFiles.FirstOrDefault();
    }
}
