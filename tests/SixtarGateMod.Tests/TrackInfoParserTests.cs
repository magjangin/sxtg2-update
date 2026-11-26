using SixtarGateMod.Loaders;
using Xunit;

namespace SixtarGateMod.Tests;

public class TrackInfoParserTests
{
    [Fact]
    public void ParseLines_SupportsCjkKeysAndFullwidthColon()
    {
        var info = TrackInfoParser.ParseLines(new[]
        {
            "曲名：測試曲",
            "副标题：ignore me",
            "曲师：作曲家",
            "难度：3，7，12"
        });

        Assert.Equal("測試曲", info.Title);
        Assert.Equal("作曲家", info.Artist);
        Assert.Equal(new[] { 3, 7, 12 }, info.Difficulties);
    }

    [Fact]
    public void NormalizeMetadataText_FixesBrokenContractionsWithoutTouchingValidApostrophes()
    {
        Assert.Equal("you're", TrackInfoParser.NormalizeMetadataText("you'  re"));
        Assert.Equal("don't", TrackInfoParser.NormalizeMetadataText("don ' t"));
        Assert.Equal("rock 'n' roll", TrackInfoParser.NormalizeMetadataText("rock '  n '  roll"));
        Assert.Equal("Guns N' Roses", TrackInfoParser.NormalizeMetadataText("Guns N' Roses"));
        Assert.Equal("Starting Point", TrackInfoParser.NormalizeMetadataText("  Starting Point  "));
        Assert.Equal("3 o'clock", TrackInfoParser.NormalizeMetadataText("3 o ' clock"));
        Assert.Equal("D'Arc", TrackInfoParser.NormalizeMetadataText("D ' Arc"));
        Assert.Equal("Hello World", TrackInfoParser.NormalizeMetadataText("Hello　World"));
        Assert.Equal("A B C", TrackInfoParser.NormalizeMetadataText("A   B\tC"));
    }

    [Fact]
    public void ParseLines_IgnoresVisualCreditsAndNonPositiveLevels()
    {
        var info = TrackInfoParser.ParseLines(new[]
        {
            "BGA作者: VideoMaker",
            "Jacket Artist: Illustrator",
            "作曲: RealComposer",
            "난이도: 0, -2, 4, 11",
        });

        Assert.Equal("RealComposer", info.Artist);
        Assert.Equal(new[] { 4, 11 }, info.Difficulties);
    }
}
