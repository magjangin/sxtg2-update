using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Reads what the song list needs from an album chart (sxtg2 layout): the base BPM and when the last note ends,
/// long notes included. Time = measure position * 240 / base BPM (BPM changes and measure lengths are ignored).
/// sxtg2 lanes are channels 16/11/12/13/14/15/18 with the note type in the value (01 normal, 02/03 long start/end);
/// channels 04/05 carry the OPEN gate (04/05 start/end). 3-character charts (#WAV001) use values like "002".
/// </summary>
public static class BmsParser
{
    public sealed record ParseResult(float BaseBpm, float MaxTime);

    private const float DefaultBpm = 150f;
    private static readonly HashSet<string> LaneChannels = new() { "16", "11", "12", "13", "14", "15", "18" };

    public static ParseResult? ParseBmsFile(string filePath)
    {
        try
        {
            return Parse(File.ReadAllLines(filePath));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[BmsParser] Failed to parse BMS file '{filePath}': {ex.Message}");
            return null;
        }
    }

    public static ParseResult Parse(string[] rawLines)
    {
        var lines = rawLines.Select(l => l.Trim()).Where(l => l.Length > 1 && l[0] == '#').ToArray();
        float bpm = lines.Select(BpmOf).FirstOrDefault(b => b > 0f, DefaultBpm);
        int width = lines.Any(l => l.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase) && l.Split(' ', '\t')[0].Length == 7) ? 3 : 2;

        // (lane key, time, type): type 1 normal, 2/4 hold start, 3/5 hold end.
        var notes = new List<(string Lane, float Time, int Type)>();
        foreach (var line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon <= 2) continue;
            string channel = line.Substring(1, colon - 1), data = line.Substring(colon + 1);
            string ch = channel.Substring(channel.Length - 2);
            int.TryParse(channel.Substring(0, channel.Length - 2), out int measure);

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
                string lane = type >= 4 ? "OPEN" : ch;
                if (type < 4 && !LaneChannels.Contains(lane)) continue;
                if (type >= 4 && ch != "04" && ch != "05" && !LaneChannels.Contains(ch)) continue;
                notes.Add((lane, (measure + (float)i / count) * 240f / bpm, type));
            }
        }

        // A hold ends at its end marker; an end marker with no open start in its lane is ignored.
        float maxTime = 0f;
        foreach (var lane in notes.GroupBy(n => n.Lane))
        {
            bool pending = false;
            foreach (var n in lane.OrderBy(n => n.Time))
            {
                bool isEnd = n.Type == 3 || n.Type == 5;
                if (isEnd && !pending) continue;
                pending = n.Type == 2 || n.Type == 4 || (pending && !isEnd);
                maxTime = Math.Max(maxTime, n.Time);
            }
        }
        return new ParseResult(bpm, maxTime);
    }

    private static float BpmOf(string line)
    {
        var parts = line.Substring(1).Split(new[] { ' ', '\t' }, 2);
        return parts.Length == 2 && parts[0].Equals("BPM", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float bpm) ? bpm : 0f;
    }
}
