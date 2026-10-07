using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Scoring;

/// <summary>
/// Decides how many points each correct answer is worth. Wrong and missing answers
/// always get 0; the engine handles that, so rules only see correct answers.
/// </summary>
public interface IScoringRule
{
    /// <param name="round">The question that just closed.</param>
    /// <param name="correctAnswers">The locked answers that were correct.</param>
    /// <returns>Points for each correct answer, keyed by owner id.</returns>
    IReadOnlyDictionary<string, int> ScoreCorrectAnswers(Round round, IReadOnlyList<LockedAnswer> correctAnswers);
}

public static class ScoringRules
{
    public static IScoringRule Create(ScoringSettings settings) => settings.Rule switch
    {
        ScoringRuleKind.TimeRemaining => new TimeRemainingScoring(settings),
        ScoringRuleKind.FirstCorrectBuzz => new FirstCorrectBuzzScoring(settings),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Rule, "Unknown scoring rule."),
    };
}
