using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;

namespace SixtarGateMod.Loaders;

public static class BmsParser
{
    public enum NoteType
    {
        Normal,
        Long,
        HoldEnd,
        Open,
        Close
    }

    public class ParsedNote
    {
        public float Time { get; set; }
        public int Lane { get; set; }
        public NoteType NoteType { get; set; }
        public float Length { get; set; }
        public string OriginalNoteValue { get; set; } = "";
    }

    public class ParseResult
    {
        public float BaseBpm { get; set; }
        public List<ParsedNote> Notes { get; set; } = new();
        public float MaxTime { get; set; }
        public int TotalNotes { get; set; }
    }

    private static readonly Dictionary<string, int> LaneMapping = new()
    {
        { "16", 0 },
        { "11", 1 },
        { "12", 2 },
        { "13", 3 },
        { "14", 4 },
        { "15", 5 },
        { "18", 6 }
    };

    private static readonly Dictionary<string, NoteType> NoteTypeMapping = new()
    {
        { "01", NoteType.Normal },
        { "02", NoteType.Long },
        { "03", NoteType.HoldEnd },
        { "04", NoteType.Open },
        { "05", NoteType.Close }
    };

    private const int DefaultNoteValueWidth = 2;
    private const int ExtendedNoteValueWidth = 3;
    private const float DefaultBpm = 150f;

    public static ParseResult? ParseBmsFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var lines = File.ReadAllLines(filePath);
            return ParseBmsFromLines(lines);
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[BmsParser] Failed to parse BMS file '{filePath}': {ex.Message}");
            return null;
        }
    }

    public static ParseResult ParseBmsFromText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new ParseResult { BaseBpm = DefaultBpm };

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return ParseBmsFromLines(lines);
    }

    public static ParseResult ParseBmsFromLines(string[] lines)
    {
        var result = new ParseResult { BaseBpm = DefaultBpm };
        if (lines == null || lines.Length == 0)
            return result;

        try
        {
            float bpm = FindBaseBpm(lines);
            int valueWidth = DetectNoteValueWidth(lines);
            var notes = new List<ParsedNote>();

            foreach (string rawLine in lines)
            {
                string line = rawLine?.Trim() ?? "";
                if (string.IsNullOrEmpty(line) || line[0] != '#')
                    continue;

                int colon = line.IndexOf(':');
                if (colon <= 1)
                    continue;

                ParseNoteData(
                    line.Substring(1, colon - 1),
                    line.Substring(colon + 1),
                    valueWidth,
                    bpm,
                    notes);
            }

            PairHoldNotes(notes);
            notes.RemoveAll(note =>
                note.NoteType == NoteType.HoldEnd ||
                note.NoteType == NoteType.Close);

            float maxTime = 0f;
            foreach (var n in notes)
            {
                float endTime = n.Time + n.Length;
                if (endTime > maxTime)
                    maxTime = endTime;
            }

            result.BaseBpm = bpm;
            result.Notes = notes;
            result.TotalNotes = notes.Count;
            result.MaxTime = maxTime;

            return result;
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"[BmsParser] BMS parsing error: {ex.Message}");
            return result;
        }
    }

    private static float FindBaseBpm(IEnumerable<string> lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine?.Trim() ?? "";
            if (string.IsNullOrEmpty(line) ||
                !line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] split = line.Substring(1).Split(new[] { ' ', '\t' }, 2);
            if (split.Length == 2 &&
                split[0].Equals("BPM", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(
                    split[1].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float bpm) &&
                bpm > 0f)
            {
                return bpm;
            }
        }

        return DefaultBpm;
    }

    private static int DetectNoteValueWidth(IEnumerable<string> lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine?.Trim() ?? "";
            if (string.IsNullOrEmpty(line) ||
                !line.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int keyEnd = line.IndexOfAny(new[] { ' ', '\t' });
            string key = keyEnd >= 0
                ? line.Substring(1, keyEnd - 1)
                : line.Substring(1);
            if (key.Length == 6)
                return ExtendedNoteValueWidth;
        }

        return DefaultNoteValueWidth;
    }

    private static void ParseNoteData(
        string channel,
        string data,
        int valueWidth,
        float bpm,
        List<ParsedNote> notes)
    {
        if (channel.Length < 2 || data.Length < valueWidth)
            return;

        string channelNumber = channel.Substring(channel.Length - 2);
        if (!LaneMapping.ContainsKey(channelNumber) &&
            channelNumber != "04" &&
            channelNumber != "05")
        {
            return;
        }

        int measure = 0;
        if (channel.Length > 2)
        {
            int.TryParse(
                channel.Substring(0, channel.Length - 2),
                out measure);
        }

        int objectCount = data.Length / valueWidth;
        for (int index = 0; index < objectCount; index++)
        {
            string value = data.Substring(index * valueWidth, valueWidth);
            if (value.All(character => character == '0'))
                continue;

            string typeKey = valueWidth == ExtendedNoteValueWidth && value[0] == '0'
                ? value.Substring(1)
                : value;
            if (!NoteTypeMapping.TryGetValue(typeKey, out var noteType) ||
                !TryResolveLane(noteType, channelNumber, out int lane))
            {
                continue;
            }

            // BMS Formula: time = (measure + index / objectCount) * 240 / bpm
            float measurePosition = measure + (float)index / objectCount;
            notes.Add(new ParsedNote
            {
                Time = measurePosition * 240f / bpm,
                Lane = lane,
                NoteType = noteType,
                OriginalNoteValue = value
            });
        }
    }

    private static bool TryResolveLane(
        NoteType noteType,
        string channel,
        out int lane)
    {
        if (noteType == NoteType.Open || noteType == NoteType.Close)
        {
            lane = 9;
            return true;
        }

        return LaneMapping.TryGetValue(channel, out lane);
    }

    private static void PairHoldNotes(List<ParsedNote> notes)
    {
        foreach (var laneGroup in notes
            .GroupBy(note => note.Lane)
            .Select(group => new
            {
                Lane = group.Key,
                Notes = group.OrderBy(note => note.Time).ToList()
            }))
        {
            NoteType startType = laneGroup.Lane == 9
                ? NoteType.Open
                : NoteType.Long;
            NoteType endType = laneGroup.Lane == 9
                ? NoteType.Close
                : NoteType.HoldEnd;
            ParsedNote? pending = null;

            foreach (var note in laneGroup.Notes)
            {
                if (note.NoteType == startType)
                {
                    pending = note;
                }
                else if (note.NoteType == endType)
                {
                    if (pending != null)
                    {
                        pending.Length = note.Time - pending.Time;
                        pending = null;
                    }
                }
            }
        }
    }
}
