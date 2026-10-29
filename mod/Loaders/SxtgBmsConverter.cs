using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Rewrites sxtg2-style BMS (lane by channel 16/11/12/13/14/15/18, note type by value 01-05)
/// into the channel layout the game's SixtarGate.BMSConverter reads.
///
/// sxtg2 lanes are RhythmGame.LaneIndexOld (LL, L, R, RR, LT, RT, GATE, OPEN=9).
/// BMSConverter.GetLaneIndex maps channels to SixtarGate.Play.LaneIndex:
///   11 LL, 12 L, 18 R, 19 RR, 14 Gate, 13 LT, 15 RT; long layer 5x with the same digit; 56 = GateAlarm (99).
/// Every non-skipped value is a marker; long notes are consecutive marker pairs on the same 5x channel.
/// </summary>
public static class SxtgBmsConverter
{
    private static readonly Regex NoteLine = new(@"^#(\d{3})(\d{2}):(\S+)\s*$", RegexOptions.Compiled);

    // sxtg2 channel -> game short-note channel (the long channel is +40).
    private static readonly Dictionary<int, int> ChannelMap = new()
    {
        { 16, 11 }, // LL
        { 11, 12 }, // L
        { 12, 18 }, // R
        { 13, 19 }, // RR
        { 14, 13 }, // LT
        { 15, 15 }, // RT
        { 18, 14 }, // GATE
    };

    private const int OPEN_CHANNEL = 56;

    private static readonly Regex WavLine = new(@"^#WAV([0-9A-Za-z]{3})(\s.*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// sxtg2 "3자리" charts: when a #WAV key has three characters (#WAV001), every object value in the data lines is
    /// three characters wide ("001" normal, "002"/"003" long, "010" BGM ...). sxtg2's BmsParser reads such a value
    /// by dropping its leading '0'. Rewrite the chart to the usual two-character values (and #WAVxx keys) so the
    /// converter and the game's BMSConverter, which both read pairs, see the same notes.
    /// </summary>
    public static string NormalizeValueWidth(string content, out bool changed)
    {
        changed = false;
        var lines = content.Replace("\r", "").Split('\n');
        if (!lines.Any(l => WavLine.IsMatch(l.Trim()))) return content;

        changed = true;
        var output = new StringBuilder();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            var wav = WavLine.Match(line);
            if (wav.Success)
            {
                string key = wav.Groups[1].Value;
                output.Append("#WAV").Append(key[0] == '0' ? key.Substring(1) : key).Append(wav.Groups[2].Value).Append('\n');
                continue;
            }

            var note = NoteLine.Match(line);
            if (!note.Success)
            {
                output.Append(raw).Append('\n');
                continue;
            }

            string data = note.Groups[3].Value;
            var pairs = new StringBuilder();
            for (int i = 0; i + 3 <= data.Length; i += 3)
            {
                string v = data.Substring(i, 3);
                // sxtg2 drops the leading '0'; a value without one matches no note type there, so skip it here too.
                pairs.Append(v[0] == '0' ? v.Substring(1) : "00");
            }
            output.Append('#').Append(note.Groups[1].Value).Append(note.Groups[2].Value).Append(':').Append(pairs).Append('\n');
        }
        return output.ToString();
    }

    /// <summary>True when the chart uses the sxtg2 encoding rather than the game's own channel layout.</summary>
    public static bool IsSxtgFormat(string content)
    {
        bool usesSxtgOnly = false;
        foreach (var raw in content.Split('\n'))
        {
            var m = NoteLine.Match(raw.Trim());
            if (!m.Success) continue;
            int ch = int.Parse(m.Groups[2].Value);
            if (ch == 19 || (ch >= 50 && ch <= 59))
                return false; // game-native channels present
            if (ch == 16) usesSxtgOnly = true;
            if (ch >= 11 && ch <= 18)
            {
                string data = m.Groups[3].Value;
                for (int i = 0; i + 1 < data.Length; i += 2)
                {
                    string v = data.Substring(i, 2);
                    if (v == "02" || v == "03" || v == "05") usesSxtgOnly = true;
                }
            }
        }
        return usesSxtgOnly;
    }

    public static string Convert(string content, out int convertedNotes)
    {
        convertedNotes = 0;
        var output = new StringBuilder();
        // (measure, channel) -> list of (index, length) marker positions
        var groups = new SortedDictionary<(int measure, int channel), List<(int index, int length)>>();

        foreach (var raw in content.Replace("\r", "").Split('\n'))
        {
            var m = NoteLine.Match(raw.Trim());
            if (!m.Success || !ChannelMap.TryGetValue(int.Parse(m.Groups[2].Value), out int shortCh))
            {
                output.Append(raw).Append('\n');
                continue;
            }

            int measure = int.Parse(m.Groups[1].Value);
            string data = m.Groups[3].Value;
            int length = data.Length / 2;
            for (int i = 0; i < length; i++)
            {
                int target = data.Substring(i * 2, 2) switch
                {
                    "01" => shortCh,
                    "02" or "03" => shortCh + 40,
                    "04" or "05" => OPEN_CHANNEL,
                    _ => -1,
                };
                if (target < 0) continue;

                if (!groups.TryGetValue((measure, target), out var list))
                    groups[(measure, target)] = list = new List<(int, int)>();
                list.Add((i, length));
                convertedNotes++;
            }
        }

        foreach (var ((measure, channel), markers) in groups)
        {
            int resolution = markers.Select(x => x.length).Aggregate(1, Lcm);
            var slots = Enumerable.Repeat("00", resolution).ToArray();
            foreach (var (index, length) in markers)
                slots[index * (resolution / length)] = "01";
            output.Append($"#{measure:D3}{channel:D2}:{string.Concat(slots)}\n");
        }

        return output.ToString();
    }

    private static int Lcm(int a, int b) => a / Gcd(a, b) * b;

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
