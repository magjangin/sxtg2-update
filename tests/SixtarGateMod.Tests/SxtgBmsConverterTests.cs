using SixtarGateMod.Loaders;
using Xunit;

namespace SixtarGateMod.Tests;

public class SxtgBmsConverterTests
{
    [Fact]
    public void IsSxtgFormat_DetectsChannel16()
    {
        string bms = "#BPM 160\n#00116:0100\n";
        Assert.True(SxtgBmsConverter.IsSxtgFormat(bms));
    }

    [Fact]
    public void IsSxtgFormat_FalseForStandardGameBms()
    {
        string bms = "#BPM 160\n#00119:0100\n#00251:0101\n";
        Assert.False(SxtgBmsConverter.IsSxtgFormat(bms));
    }

    [Fact]
    public void IsSxtgFormat_DetectsSxtgLongNotesWithCrlf()
    {
        string bms = "#BPM 160\r\n#00111:02000300\r\n#00114:0005\r\n";
        Assert.True(SxtgBmsConverter.IsSxtgFormat(bms));
    }

    [Fact]
    public void Convert_MapsShortNotesToGameChannels()
    {
        // sxtg2: 16(LL)->11, 11(L)->12, 12(R)->18, 13(RR)->19, 14(LT)->13, 15(RT)->15, 18(GATE)->14
        string sxtg = "#BPM 150\n#00116:0100\n#00111:0100\n#00112:0100\n#00113:0100\n#00114:0100\n#00115:0100\n#00118:0100\n";

        string converted = SxtgBmsConverter.Convert(sxtg, out int notes);

        Assert.Equal(7, notes);
        Assert.Contains("#00111:0100", converted);
        Assert.Contains("#00112:0100", converted);
        Assert.Contains("#00118:0100", converted);
        Assert.Contains("#00119:0100", converted);
        Assert.Contains("#00113:0100", converted);
        Assert.Contains("#00115:0100", converted);
        Assert.Contains("#00114:0100", converted);
    }

    [Fact]
    public void Convert_MapsLongNoteStartAndEndTo5xChannel()
    {
        // sxtg2 channel 16 (LL -> 11, long -> 51), value 02 (start) & 03 (end)
        string sxtg = "#BPM 120\n#00116:02000300\n";

        string converted = SxtgBmsConverter.Convert(sxtg, out int notes);

        Assert.Equal(2, notes);
        Assert.Contains("#00151:01000100", converted);
    }

    [Fact]
    public void NormalizeValueWidth_RewritesThreeCharWavAndData()
    {
        string sxtg3 = "#WAV001 note.wav\n#WAV010 bgm.ogg\n#00116:001000002003\n";

        string normalized = SxtgBmsConverter.NormalizeValueWidth(sxtg3, out bool changed);

        Assert.True(changed);
        Assert.Contains("#WAV01 note.wav", normalized);
        Assert.Contains("#WAV10 bgm.ogg", normalized);
        Assert.Contains("#00116:01000203", normalized);
    }
}
