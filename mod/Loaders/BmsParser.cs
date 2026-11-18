using System;
using System.Collections.Generic;
using System.Globalization;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Reads what the song list needs from an album chart (sxtg2 layout): the base BPM and when the last note ends,
/// long notes included. Supports measure length changes (channel 02) and BPM changes (channel 03 / #BPMxx + channel 08).
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
            Managers.ModLog.Error($"[BmsParser] BMS 파일 해석 실패 '{filePath}': {ex.Message}");
            return null;
        }
    }

    public static ParseResult Parse(string[] rawLines)
    {
        float baseBpm = 0f;
        int width = 2;
        string title = "";
        string artist = "";
        Dictionary<string, float>? extBpms = null;

        foreach (var raw in rawLines)
        {
            var line = raw.AsSpan().Trim();
            if (line.Length <= 1 || line[0] != '#') continue;

            if (baseBpm <= 0f && TryParseBpm(line, out float parsedBpm))
                baseBpm = parsedBpm;
            else if (TryParseExtendedBpm(line, out string extKey, out float extVal))
                (extBpms ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase))[extKey] = extVal;

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
        if (baseBpm <= 0f) baseBpm = DefaultBpm;

        // 레인 0..6: 16/11/12/13/14/15/18, 레인 7: OPEN.
        // 각 항목은 (마디, 마디 내 비율, 등장 순번, 타입): 타입 1 일반, 2/4 롱 시작, 3/5 롱 끝.
        var notesByLane = new List<(int Measure, double Frac, int Seq, int Type)>?[8];
        Dictionary<int, double>? measureScales = null;
        List<(int Measure, double Frac, int Seq, float Bpm)>? bpmEvents = null;
        int maxMeasure = 0;
        int seq = 0;

        foreach (var raw in rawLines)
        {
            var line = raw.AsSpan().Trim();
            if (!IsDataLine(line)) continue;

            int measure = (line[1] - '0') * 100 + (line[2] - '0') * 10 + (line[3] - '0');
            if (measure > maxMeasure) maxMeasure = measure;
            var ch = line.Slice(4, 2);
            var data = line.Slice(7);

            // 채널 02: 마디 길이 배율
            if (ch.Equals("02", StringComparison.Ordinal))
            {
                if (double.TryParse(data.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0.0)
                    (measureScales ??= new Dictionary<int, double>())[measure] = scale;
                continue;
            }

            // 채널 03 (16진수 BPM) / 채널 08 (#BPMxx 참조)
            bool isHexBpm = ch.Equals("03", StringComparison.Ordinal);
            bool isExtBpm = ch.Equals("08", StringComparison.Ordinal);
            if (isHexBpm || isExtBpm)
            {
                int count = data.Length / width;
                for (int i = 0; i < count; i++)
                {
                    int o = i * width;
                    if (width == 3)
                    {
                        if (data[o] != '0') continue;
                        o++;
                    }
                    var token = data.Slice(o, 2);
                    if (token.Equals("00", StringComparison.Ordinal)) continue;

                    float nextBpm = 0f;
                    if (isHexBpm)
                    {
                        if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexVal) && hexVal > 0)
                            nextBpm = hexVal;
                    }
                    else if (extBpms != null && extBpms.TryGetValue(token.ToString(), out float found))
                    {
                        nextBpm = found;
                    }
                    if (nextBpm > 0f)
                        (bpmEvents ??= new List<(int, double, int, float)>()).Add((measure, (double)i / count, seq++, nextBpm));
                }
                continue;
            }

            int chLane = LaneIndexOf(ch);
            bool isOpenCh = ch.Equals("04", StringComparison.Ordinal) || ch.Equals("05", StringComparison.Ordinal);
            if (chLane < 0 && !isOpenCh) continue;

            int noteCount = data.Length / width;
            for (int i = 0; i < noteCount; i++)
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

                var list = notesByLane[laneIdx] ??= new List<(int, double, int, int)>();
                list.Add((measure, (double)i / noteCount, seq++, type));
            }
        }

        Func<int, double, float> toSeconds = BuildTimeMapper(baseBpm, maxMeasure, measureScales, bpmEvents);

        // A hold ends at its end marker; an end marker with no open start in its lane is ignored.
        float maxTime = 0f;
        foreach (var lane in notesByLane)
        {
            if (lane == null || lane.Count == 0) continue;
            lane.Sort(static (a, b) =>
            {
                int c = a.Measure.CompareTo(b.Measure);
                if (c != 0) return c;
                c = a.Frac.CompareTo(b.Frac);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            });
            bool pending = false;
            foreach (var n in lane)
            {
                bool isEnd = n.Type == 3 || n.Type == 5;
                if (isEnd && !pending) continue;
                pending = n.Type == 2 || n.Type == 4 || (pending && !isEnd);
                float t = toSeconds(n.Measure, n.Frac);
                if (t > maxTime) maxTime = t;
            }
        }
        return new ParseResult(baseBpm, maxTime, title, artist);
    }

    private static Func<int, double, float> BuildTimeMapper(
        float baseBpm,
        int maxMeasure,
        Dictionary<int, double>? measureScales,
        List<(int Measure, double Frac, int Seq, float Bpm)>? bpmEvents)
    {
        if (measureScales == null && (bpmEvents == null || bpmEvents.Count == 0))
            return (m, f) => (float)((m + f) * 240.0 / baseBpm);

        bpmEvents?.Sort(static (a, b) =>
        {
            int c = a.Measure.CompareTo(b.Measure);
            if (c != 0) return c;
            c = a.Frac.CompareTo(b.Frac);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });

        var segments = new List<(double Pos, double Time, float Bpm)>();
        double curPos = 0.0;
        double curTime = 0.0;
        float curBpm = baseBpm;
        int evIdx = 0;

        for (int m = 0; m <= maxMeasure; m++)
        {
            double scale = measureScales != null && measureScales.TryGetValue(m, out double s) ? s : 1.0;
            double mStartPos = curPos;
            double mFracPos = 0.0;

            while (bpmEvents != null && evIdx < bpmEvents.Count && bpmEvents[evIdx].Measure == m)
            {
                var ev = bpmEvents[evIdx++];
                double deltaFrac = ev.Frac - mFracPos;
                if (deltaFrac > 0.0)
                    curTime += deltaFrac * scale * 240.0 / curBpm;
                mFracPos = ev.Frac;
                curBpm = ev.Bpm;
                double evPos = mStartPos + ev.Frac * scale;
                if (segments.Count > 0 && segments[^1].Pos == evPos)
                    segments[^1] = (evPos, curTime, curBpm);
                else
                    segments.Add((evPos, curTime, curBpm));
            }

            curTime += (1.0 - mFracPos) * scale * 240.0 / curBpm;
            curPos = mStartPos + scale;
        }

        // 마디 시작 위치(measure -> 누적 정규화 마디 길이) 캐시
        var measureStartPos = new double[maxMeasure + 2];
        double p = 0.0;
        for (int m = 0; m <= maxMeasure; m++)
        {
            measureStartPos[m] = p;
            double scale = measureScales != null && measureScales.TryGetValue(m, out double s) ? s : 1.0;
            p += scale;
        }
        measureStartPos[maxMeasure + 1] = p;

        // 각 마디 시작 시점의 (Pos, Time, Bpm)을 빠르게 찾기 위해 세그먼트 테이블 구성
        var segPos = new double[segments.Count + 1];
        var segTime = new double[segments.Count + 1];
        var segBpm = new float[segments.Count + 1];
        segPos[0] = 0.0;
        segTime[0] = 0.0;
        segBpm[0] = baseBpm;
        for (int i = 0; i < segments.Count; i++)
        {
            segPos[i + 1] = segments[i].Pos;
            segTime[i + 1] = segments[i].Time;
            segBpm[i + 1] = segments[i].Bpm;
        }

        return (m, f) =>
        {
            double scale = measureScales != null && measureScales.TryGetValue(m, out double s) ? s : 1.0;
            double targetPos = measureStartPos[m] + f * scale;
            int idx = Array.BinarySearch(segPos, targetPos);
            if (idx < 0) idx = (~idx) - 1;
            if (idx < 0) idx = 0;
            return (float)(segTime[idx] + (targetPos - segPos[idx]) * 240.0 / segBpm[idx]);
        };
    }

    private static bool IsDataLine(ReadOnlySpan<char> line)
    {
        if (line.Length < 7 || line[0] != '#' || line[6] != ':') return false;
        return char.IsDigit(line[1]) && char.IsDigit(line[2]) && char.IsDigit(line[3])
            && IsAlphaNum(line[4]) && IsAlphaNum(line[5]);
    }

    private static bool IsAlphaNum(char c)
        => (uint)(c - '0') <= 9 || (uint)(c - 'A') <= 25 || (uint)(c - 'a') <= 25;

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

    private static bool TryParseExtendedBpm(ReadOnlySpan<char> line, out string key, out float bpm)
    {
        key = "";
        bpm = 0f;
        // #BPM01 180.5 형태 (길이가 #BPMxx + 공백 + 값 이상)
        if (line.Length < 8 || !line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsAlphaNum(line[4]) || !IsAlphaNum(line[5]) || (line[6] != ' ' && line[6] != '\t'))
            return false;
        if (!float.TryParse(line.Slice(7).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bpm) || bpm <= 0f)
            return false;
        key = line.Slice(4, 2).ToString();
        return true;
    }
}
