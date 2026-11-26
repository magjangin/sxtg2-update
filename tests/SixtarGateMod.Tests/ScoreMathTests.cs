using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class ScoreMathTests
{
    [Theory]
    [InlineData(2_000_000f, 2_000_000f, false)]
    [InlineData(0f, 1_000_000f, false)]          // 0 = 기본값(경고 없음)
    [InlineData(-5f, 1_000_000f, true)]
    [InlineData(1e10f, 1_000_000_000f, true)]     // 전에는 int 변환이 넘쳐 음수 점수
    [InlineData(float.PositiveInfinity, 1_000_000f, true)]
    [InlineData(float.NaN, 1_000_000f, true)]
    public void ClampMaxScore_KeepsUsableRange(float input, float expected, bool clamped)
    {
        Assert.Equal(expected, ScoreMath.ClampMaxScore(input, out bool c));
        Assert.Equal(clamped, c);
    }

    [Fact]
    public void Scale_AtLimit_DoesNotOverflow()
    {
        int full = ScoreMath.Scale(ScoreMath.GameMaxScore, ScoreMath.MaxScoreLimit);
        Assert.Equal(1_000_000_000, full);
        Assert.True(ScoreMath.Scale(999_999, ScoreMath.MaxScoreLimit) > 0);
        Assert.Equal(1_000_000_000, ScoreMath.Total(ScoreMath.MaxScoreLimit));
    }

    [Fact]
    public void Scale_FloorsLikeGame()
    {
        Assert.Equal(1, ScoreMath.Scale(999_999, 2f));     // 1.999998 → 1
        Assert.Equal(500_000, ScoreMath.Scale(500_000, 1_000_000f));
        Assert.Equal(0, ScoreMath.Scale(0, 123_456f));
    }

    [Fact]
    public void Total_HugeValueSaturatesInsteadOfWrapping()
    {
        Assert.Equal(int.MaxValue, ScoreMath.Total(1e12f));
        Assert.Equal(0, ScoreMath.Total(-1f));
    }
}
