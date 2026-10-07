using TriviaBattle.Core.Modes.LockIn;
using TriviaBattle.Core.Scoring;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Timing and rules for every match. Bound from the "Game" section of appsettings.json,
/// so operators can tune these without rebuilding. Defaults below are used for anything
/// missing from config.
/// </summary>
public sealed class MatchSettings
{
    public int QuestionsPerMatch { get; set; } = 10;

    // How long each phase lasts, in seconds. See MatchPhase for what each phase is.
    public double IntroSeconds { get; set; } = 8;
    public double LeadInSeconds { get; set; } = 3;
    public double AnswerSeconds { get; set; } = 15;
    public double RevealSeconds { get; set; } = 5;
    public double StandingsSeconds { get; set; } = 4;
    public double ResultsSeconds { get; set; } = 20;

    /// <summary>Skip the rest of the timer once every player/team has answered.</summary>
    public bool EndQuestionWhenAllAnswered { get; set; } = true;

    /// <summary>Show the four answers in a random order each time a question is used.</summary>
    public bool ShuffleAnswers { get; set; } = true;

    /// <summary>How a team locks in its one answer in 2v2 Group mode.</summary>
    public LockInRuleKind TeamLockIn { get; set; } = LockInRuleKind.FirstPress;

    public ScoringSettings Scoring { get; set; } = new();
}
