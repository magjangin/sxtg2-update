using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Album metadata from a "key: value" text file: {chart name}.txt, else trackinfo.txt / info.txt, else the first .txt.
/// Separator: half-width ':' or full-width '：' (whichever comes first). The first value read for each field wins.
/// Keys (lower-cased, substring match unless noted):
///   title    곡 제목/title/曲名/歌名/标题/標題/タイトル, exact 제목/曲目
///   artist   아티스트/artist/composer/艺术家/藝術家/曲师/曲師/作曲/作者/アーティスト, exact 작곡가
///   level    난이도/difficulty/level/难度/難度/等级/等級/難易度/レベル ("1, 4, 7, 10"; ，、/; and full-width digits accepted)
/// Keys for other things (subtitle 副标题/부제, charter 谱面作者/채보/chart, lyricist 作词/작사/lyric) are ignored.
/// Files that are not UTF-8 are decoded with <see cref="TextDecoder.InfoFallback"/>.
/// </summary>
public static class TrackInfoParser
{
    // Apostrophe-like characters a title may carry: ' ’ ‘ ` ＇ ′
    private const string Q = "['’‘`＇′]";
    private static readonly Regex RegexOClock = new($@"\b([Oo])\s*{Q}\s*(clock)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // French/Italian elision with a space before the quote ("D ' Arc", "L ' amour"). The space before the quote is
    // required so "Guns N' Roses" stays, and the word must not end in a quote so "M 'Hello'" stays.
    private static readonly Regex RegexPrefix = new($@"\b([CcDdJjLlMmNnTtSs])\s+{Q}\s*([A-Za-z]{{2,}})\b(?!{Q})", RegexOptions.Compiled);
    private static readonly Regex RegexRockNRoll = new($@"\b([A-Za-z]+)\s*{Q}\s*n\s*{Q}\s*([A-Za-z]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Contraction tail not followed by another quote, so quoted letters ("Mister 'T'", "the 'S' Club") stay.
    private static readonly Regex RegexContractions = new($@"\b([A-Za-z]+)\s*{Q}\s*(re|ve|ll|t|s|d|m)\b(?!\s*{Q})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RegexMultipleSpaces = new(@"[ \t]+", RegexOptions.Compiled);

    // Subtitle / chart author keys are never title or artist.
    private static readonly string[] IgnoredKeyParts = { "sub", "副", "부제", "谱", "譜", "chart", "채보" };
    // Lyricist / arranger keys are skipped unless the key also names the composer ("作词/作曲", "작사·작곡").
    private static readonly string[] OtherCreditParts = { "作词", "作詞", "작사", "lyric", "編曲", "编曲", "편곡", "arrange" };
    private static readonly string[] ComposerParts = { "作曲", "composer", "작곡" };
    private static readonly string[] TitleKeys = { "곡 제목", "곡제목", "title", "曲名", "歌名", "标题", "標題", "タイトル" };
    private static readonly string[] TitleExactKeys = { "제목", "曲目" };
    private static readonly string[] ArtistKeys = { "아티스트", "artist", "composer", "艺术家", "藝術家", "曲师", "曲師", "作曲", "作者", "アーティスト" };
    private static readonly string[] ArtistExactKeys = { "작곡가" };
    private static readonly string[] LevelKeys = { "난이도", "difficulty", "level", "难度", "難度", "等级", "等級", "難易度", "レベル" };
    private static readonly char[] LevelSeparators = { ',', ' ', '\t', '，', '、', '/', '／', ';', '；', '　' };

    /// <summary>
    /// Normalizes metadata text from external BMS charts or user info.txt:
    /// - Replaces full-width CJK ideographic space (U+3000) with ASCII space.
    /// - Removes stray spaces inside English contractions: "you'  re" -> "you're", "don ' t" -> "don't",
    ///   "rock '  n '  roll" -> "rock 'n' roll", "3 o ' clock" -> "3 o'clock", "D ' Arc" -> "D'Arc".
    ///   A matched quote becomes '. Quoted letters ("Mister 'T'") and "Guns N' Roses" are left alone.
    /// - Collapses runs of spaces and trims. Hangul, kana and Han characters are not changed.
    /// </summary>
    public static string NormalizeMetadataText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = text.Replace('　', ' ');
        s = RegexOClock.Replace(s, "$1'$2");
        s = RegexPrefix.Replace(s, "$1'$2");
        s = RegexRockNRoll.Replace(s, "$1 'n' $2");
        s = RegexContractions.Replace(s, "$1'$2");
        s = RegexMultipleSpaces.Replace(s, " ");
        return s.Trim();
    }

    public class TrackInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public List<int> Difficulties { get; set; } = new();
    }

    public static TrackInfo ParseTrackInfo(string albumFolder, string chartName)
        => ParseTrackInfo(albumFolder, chartName, null);

    public static TrackInfo ParseTrackInfo(string albumFolder, string chartName, string[]? preloadedFiles)
    {
        try
        {
            string? file = FindTrackInfoFile(albumFolder, chartName, preloadedFiles);
            if (file == null) return new TrackInfo();
            return ParseLines(TextDecoder.ReadAllText(file, TextDecoder.InfoFallback).Split('\n'));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[TrackInfoParser] '{albumFolder}'의 곡 정보 해석 오류: {ex.Message}");
            return new TrackInfo();
        }
    }

    public static TrackInfo ParseLines(IEnumerable<string> lines)
    {
        var info = new TrackInfo();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
            int colon = SeparatorIndex(line);
            if (colon <= 0) continue;
            string key = line.Substring(0, colon).Trim().ToLowerInvariant();
            string value = line.Substring(colon + 1).Trim();
            if (value.Length == 0 || IgnoredKeyParts.Any(key.Contains)) continue;
            if (OtherCreditParts.Any(key.Contains) && !ComposerParts.Any(key.Contains)) continue;

            if (Matches(key, TitleKeys, TitleExactKeys))
            {
                if (info.Title.Length == 0) info.Title = NormalizeMetadataText(value);
            }
            else if (Matches(key, ArtistKeys, ArtistExactKeys))
            {
                if (info.Artist.Length == 0) info.Artist = NormalizeMetadataText(value);
            }
            else if (Matches(key, LevelKeys, Array.Empty<string>()))
            {
                if (info.Difficulties.Count == 0) info.Difficulties = ParseLevels(value);
            }
        }
        return info;
    }

    private static int SeparatorIndex(string line)
    {
        int half = line.IndexOf(':'), full = line.IndexOf('：');
        return half < 0 ? full : full < 0 ? half : Math.Min(half, full);
    }

    private static bool Matches(string key, string[] parts, string[] exact)
        => exact.Contains(key) || parts.Any(key.Contains);

    private static List<int> ParseLevels(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(c >= '０' && c <= '９' ? (char)('0' + (c - '０')) : c);
        return sb.ToString().Split(LevelSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out int d) ? (int?)d : null).OfType<int>().ToList();
    }

    private static string? FindTrackInfoFile(string albumFolder, string chartName, string[]? preloadedFiles)
    {
        string[] source = preloadedFiles ?? Directory.GetFiles(albumFolder, "*", SearchOption.TopDirectoryOnly);
        if (preloadedFiles == null)
            Array.Sort(source, StringComparer.OrdinalIgnoreCase);
        string? chartTxt = null, trackInfoTxt = null, infoTxt = null, firstTxt = null;
        string? chartTarget = chartName.Length > 0 ? chartName + ".txt" : null;

        foreach (string f in source)
        {
            if (!Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)) continue;
            firstTxt ??= f;
            string name = Path.GetFileName(f);
            if (chartTarget != null && chartTxt == null && name.Equals(chartTarget, StringComparison.OrdinalIgnoreCase))
                chartTxt = f;
            else if (trackInfoTxt == null && name.Equals("trackinfo.txt", StringComparison.OrdinalIgnoreCase))
                trackInfoTxt = f;
            else if (infoTxt == null && name.Equals("info.txt", StringComparison.OrdinalIgnoreCase))
                infoTxt = f;
        }
        return chartTxt ?? trackInfoTxt ?? infoTxt ?? firstTxt;
    }
}
