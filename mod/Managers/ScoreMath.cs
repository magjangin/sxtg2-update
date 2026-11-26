using System;

namespace SixtarGateMod.Managers;

/// <summary>
/// MaxScore 점수 환산(순수 계산, 게임·Unity 의존 없음 → 단위 테스트 대상).
/// 게임 점수는 int이고 원래 만점은 1,000,000. 설정의 MaxScore를 그대로 곱하면 큰 값(예: 1e10)에서
/// int 변환이 넘쳐 음수 점수가 화면과 역카운트다운에 나왔다(2026-11-26 이전).
/// </summary>
public static class ScoreMath
{
    public const int GameMaxScore = 1_000_000;

    /// <summary>MaxScore 상한. 게임 UI 자릿수와 int 범위(약 21억)를 함께 고려해 10억.</summary>
    public const float MaxScoreLimit = 1_000_000_000f;

    /// <summary>설정값을 쓸 수 있는 범위로: 0 이하·NaN·무한대는 게임 기본(1,000,000), 상한 초과는 상한.</summary>
    public static float ClampMaxScore(float value, out bool clamped)
    {
        clamped = false;
        if (!float.IsFinite(value) || value <= 0f) { clamped = !float.IsFinite(value) || value < 0f; return GameMaxScore; }
        if (value > MaxScoreLimit) { clamped = true; return MaxScoreLimit; }
        return value;
    }

    /// <summary>만점 기준(게임 1,000,000) 점수를 MaxScore 기준으로. 결과는 0..MaxScore(int 범위 안).</summary>
    public static int Scale(int gameScore, float maxScore)
    {
        double v = Math.Floor(gameScore * (double)maxScore / GameMaxScore);
        return (int)Math.Clamp(v, int.MinValue, int.MaxValue);
    }

    /// <summary>만점(BLUESTAR 풀콤보) 점수.</summary>
    public static int Total(float maxScore) => (int)Math.Clamp(Math.Floor((double)maxScore), 0, int.MaxValue);
}
