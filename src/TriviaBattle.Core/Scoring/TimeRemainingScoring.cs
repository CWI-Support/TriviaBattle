using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Scoring;

/// <summary>
/// Faster correct answers are worth more. Points slide linearly from MaxPoints
/// (answered instantly) down to MinCorrectPoints (answered as the timer hits zero).
/// </summary>
public sealed class TimeRemainingScoring(ScoringSettings settings) : IScoringRule
{
    public IReadOnlyDictionary<string, int> ScoreCorrectAnswers(Round round, IReadOnlyList<LockedAnswer> correctAnswers)
    {
        var points = new Dictionary<string, int>();

        foreach (var answer in correctAnswers)
        {
            var timeLeft = round.ClosesAt!.Value - answer.PressedAt;
            var fractionLeft = Math.Clamp(timeLeft / round.AnswerWindow, 0, 1);
            var bonusRange = settings.MaxPoints - settings.MinCorrectPoints;

            points[answer.OwnerId] = settings.MinCorrectPoints + (int)Math.Round(bonusRange * fractionLeft);
        }

        return points;
    }
}
