using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Scoring;

/// <summary>
/// Only the first correct answer earns full points. "First" uses the press Sequence from
/// the input hardware, so the PLC's latch order decides close calls, not network timing.
/// </summary>
public sealed class FirstCorrectBuzzScoring(ScoringSettings settings) : IScoringRule
{
    public IReadOnlyDictionary<string, int> ScoreCorrectAnswers(Round round, IReadOnlyList<LockedAnswer> correctAnswers)
    {
        var points = new Dictionary<string, int>();
        var fastestFirst = correctAnswers.OrderBy(a => a.Sequence).ThenBy(a => a.PressedAt).ToList();

        for (var i = 0; i < fastestFirst.Count; i++)
        {
            var isFirst = i == 0;
            points[fastestFirst[i].OwnerId] = isFirst ? settings.FirstCorrectPoints : settings.OtherCorrectPoints;
        }

        return points;
    }
}
