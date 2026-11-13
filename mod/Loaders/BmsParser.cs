using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Reads what the song list needs from an album chart (sxtg2 layout): the base BPM and when the last note ends,
/// long notes included. Time = measure position * 240 / base BPM (BPM changes and measure lengths are ignored).
/// sxtg2 lanes are channels 16/11/12/13/14/15/18 with the note type in the value (01 normal, 02/03 long start/end);
/// channels 04/05 carry the OPEN gate (04/05 start/end). 3-character charts (#WAV001) use values like "002".
/// Non-UTF-8 charts are decoded as Shift-JIS first (BMS convention); a #TITLE/#ARTIST that still has U+FFFD is dropped.
/// </summary>
public static class BmsParser
{
    public sealed record ParseResult(float BaseBpm, float MaxTime, string Title = "", string Artist = "");

    private const float DefaultBpm = 150f;
    private const char Replacement = (char)0xFFFD; // undecodable byte marker

    public static ParseResult? ParseBmsFile(string filePath)
    {
        try
        {
            return Parse(TextDecoder.ReadAllText(filePath, TextDecoder.BmsFallback).Split('\n'));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[BmsParser] BMS 파일 해석 실패 '{filePath}': {ex.Message}");
            return null;
        }
    }

    public static ParseResult Parse(string[] rawLines)
    {
        float bpm = 0f;
        int width = 2;
        string title = "";
        string artist = "";

        foreach (var raw in rawLines)
        {
            var line = raw.AsSpan().Trim();
            if (line.Length <= 1 || line[0] != '#') continue;

            if (bpm <= 0f && TryParseBpm(line, out float parsedBpm))
                bpm = parsedBpm;
            if (width == 2 && line.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase) && FirstTokenLength(line) == 7)
                width = 3;

            if (title.Length == 0 && line.StartsWith("#TITLE", StringComparison.OrdinalIgnoreCase))
            {
                var val = line.Slice(6).Trim();
                if (val.Length > 0 && val.IndexOf(Replacement) < 0)
                    title = TrackInfoParser.NormalizeMetadataText(val.ToString());
            }
            else if (artist.Length == 0 && line.StartsWith("#ARTIST", StringComparison.OrdinalIgnoreCase))
            {
                var val = line.Slice(7).Trim();
                if (val.Length > 0 && val.IndexOf(Replacement) < 0)
                    artist = TrackInfoParser.NormalizeMetadataText(val.ToString());
            }
        }
        if (bpm <= 0f) bpm = DefaultBpm;

        // 레인 0..6: 16/11/12/13/14/15/18, 레인 7: OPEN. LINQ GroupBy/OrderBy 대신 배열 버킷 + 제자리 정렬.
        // 각 항목은 (시각, 등장 순번, 타입): 타입 1 일반, 2/4 롱 시작, 3/5 롱 끝.
        var notesByLane = new List<(float Time, int Seq, int Type)>?[8];
        int seq = 0;
        foreach (var raw in rawLines)
        {
            var line = raw.AsSpan().Trim();
            if (line.Length <= 1 || line[0] != '#') continue;

            int colon = line.IndexOf(':');
            if (colon <= 2) continue;

            var ch = line.Slice(colon - 2, 2);
            int chLane = LaneIndexOf(ch);
            bool isOpenCh = ch.Equals("04", StringComparison.Ordinal) || ch.Equals("05", StringComparison.Ordinal);
            if (chLane < 0 && !isOpenCh) continue;

            int.TryParse(line.Slice(1, colon - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int measure);
            var data = line.Slice(colon + 1);
            int count = data.Length / width;
            for (int i = 0; i < count; i++)
            {
                int o = i * width;
                if (width == 3)
                {
                    if (data[o] != '0') continue;
                    o++;
                }
                int type = data[o] == '0' ? (data[o + 1] - '0') : 0;
                if (type < 1 || type > 5) continue;

                int laneIdx;
                if (type >= 4)
                    laneIdx = 7;
                else if (chLane >= 0)
                    laneIdx = chLane;
                else
                    continue;

                var list = notesByLane[laneIdx] ??= new List<(float, int, int)>();
                list.Add(((measure + (float)i / count) * 240f / bpm, seq++, type));
            }
        }

        // A hold ends at its end marker; an end marker with no open start in its lane is ignored.
        float maxTime = 0f;
        foreach (var lane in notesByLane)
        {
            if (lane == null || lane.Count == 0) continue;
            lane.Sort(static (a, b) =>
            {
                int c = a.Time.CompareTo(b.Time);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            });
            bool pending = false;
            foreach (var n in lane)
            {
                bool isEnd = n.Type == 3 || n.Type == 5;
                if (isEnd && !pending) continue;
                pending = n.Type == 2 || n.Type == 4 || (pending && !isEnd);
                if (n.Time > maxTime) maxTime = n.Time;
            }
        }
        return new ParseResult(bpm, maxTime, title, artist);
    }

    private static int LaneIndexOf(ReadOnlySpan<char> ch)
    {
        if (ch.Length != 2 || ch[0] != '1') return -1;
        return ch[1] switch
        {
            '6' => 0,
            '1' => 1,
            '2' => 2,
            '3' => 3,
            '4' => 4,
            '5' => 5,
            '8' => 6,
            _ => -1,
        };
    }

    private static int FirstTokenLength(ReadOnlySpan<char> line)
    {
        int sep = line.IndexOfAny(' ', '\t');
        return sep < 0 ? line.Length : sep;
    }

    private static bool TryParseBpm(ReadOnlySpan<char> line, out float bpm)
    {
        bpm = 0f;
        var rest = line.Slice(1);
        int sep = rest.IndexOfAny(' ', '\t');
        if (sep <= 0 || !rest.Slice(0, sep).Equals("BPM", StringComparison.OrdinalIgnoreCase))
            return false;
        return float.TryParse(rest.Slice(sep + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bpm) && bpm > 0f;
    }
}
