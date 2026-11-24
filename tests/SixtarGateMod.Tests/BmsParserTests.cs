using SixtarGateMod.Loaders;
using Xunit;

namespace SixtarGateMod.Tests;

public class BmsParserTests
{
    [Fact]
    public void Parse_BasicChart_ComputesBaseBpmAndMaxTime()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 120",
            "#TITLE Test Song",
            "#ARTIST Composer",
            "#00116:0100",
        });

        Assert.Equal(120f, result.BaseBpm);
        Assert.Equal("Test Song", result.Title);
        Assert.Equal("Composer", result.Artist);
        // 120 BPM => 240 / 120 = 2.0s per measure
        Assert.Equal(2.0f, result.MaxTime, 3);
    }

    [Fact]
    public void Parse_IgnoresColonInsideTitleHeader()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 150",
            "#TITLE Mix 11:30",
        });

        Assert.Equal("Mix 11:30", result.Title);
        Assert.Equal(0f, result.MaxTime);
    }

    [Fact]
    public void Parse_AppliesMeasureLengthAndBpmChanges()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 120",       // 2.0s per standard measure
            "#BPM01 240",     // 1.0s per standard measure
            "#00002:0.5",     // measure 0 is 0.5x => 1.0s
            "#00108:0100",    // at measure 1.0, BPM becomes 240
            "#00216:0100",    // measure 2 start = 1.0s (m0) + 1.0s (m1 @ 240) = 2.0s
        });

        Assert.Equal(120f, result.BaseBpm);
        Assert.Equal(2.0f, result.MaxTime, 3);
    }

    [Fact]
    public void Parse_HandlesLongNotesAndIgnoresUnmatchedEnd()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 240", // 1.0s per measure
            "#00116:0300", // unmatched end at 1.0s -> ignored
            "#00216:0200", // start at 2.0s
            "#00416:0300", // end at 4.0s
        });

        Assert.Equal(4.0f, result.MaxTime, 3);
    }

    [Fact]
    public void Parse_DuplicateBpmAtSamePositionUsesLastEffectiveBpm()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 120",
            "#BPM01 240",
            "#00103:78",   // 120 BPM at measure 1.0
            "#00108:01",   // overwritten to 240 BPM at exact same measure 1.0
            "#00216:0100", // measure 0 @ 120 (2.0s) + measure 1 @ 240 (1.0s) = 3.0s
        });

        Assert.Equal(3.0f, result.MaxTime, 3);
    }

    [Fact]
    public void Parse_TitleAndArtistRequireWhitespaceSeparator()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM 120",
            "#TITLE2 Wrong",
            "#TITLE Real Title",
            "#ARTIST_EN Wrong",
            "#ARTIST Real Artist",
        });

        Assert.Equal("Real Title", result.Title);
        Assert.Equal("Real Artist", result.Artist);
    }

    [Fact]
    public void Parse_RejectsInfinityBpmAndMeasureScale()
    {
        var result = BmsParser.Parse(new[]
        {
            "#BPM Infinity",
            "#00002:Infinity",
            "#00116:0100",
        });

        Assert.Equal(150f, result.BaseBpm);
        Assert.True(float.IsFinite(result.MaxTime) && result.MaxTime > 0f);
    }

    [Fact]
    public void Parse_NativeBmsChannels_ComputeMaxTime()
    {
        // 게임 네이티브 채널(19, 51, 비-01..05 WAV 키)도 MaxTime에 반영돼야 함
        var result = BmsParser.Parse(new[]
        {
            "#BPM 240", // 1.0s per measure
            "#00119:0100",
            "#00251:0001", // 2.5s
            "#00311:0A00", // 3.0s
        });

        Assert.Equal(3.0f, result.MaxTime, 3);
    }

    [Fact]
    public void Parse_ThreeCharChartAndMidMeasureBpm()
    {
        var result = BmsParser.Parse(new[]
        {
            "#WAV001 hit.wav",
            "#BPM 120",        // 2.0s per measure
            "#00003:0000F0",   // measure 0.5에서 0xF0 = 240 BPM (1.0s per measure)
            "#00116:001000",   // m0 전반 1.0s + m0 후반 0.5s = 1.5s
        });

        Assert.Equal(1.5f, result.MaxTime, 3);
    }
}
