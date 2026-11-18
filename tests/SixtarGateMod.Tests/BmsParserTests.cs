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
}
