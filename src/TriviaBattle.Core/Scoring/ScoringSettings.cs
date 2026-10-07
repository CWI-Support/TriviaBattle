namespace TriviaBattle.Core.Scoring;

public enum ScoringRuleKind
{
    /// <summary>Correct answers earn more points the faster they come in.</summary>
    TimeRemaining,

    /// <summary>Only the first correct answer earns (full) points.</summary>
    FirstCorrectBuzz,
}

/// <summary>Bound from "Game:Scoring" in appsettings.json.</summary>
public sealed class ScoringSettings
{
    public ScoringRuleKind Rule { get; set; } = ScoringRuleKind.TimeRemaining;

    // TimeRemaining: an instant correct answer gets MaxPoints, a correct answer at the
    // last moment gets MinCorrectPoints, sliding linearly in between. Wrong = 0.
    public int MaxPoints { get; set; } = 1000;
    public int MinCorrectPoints { get; set; } = 500;

    // FirstCorrectBuzz: the first correct answer gets FirstCorrectPoints, any other
    // correct answers get OtherCorrectPoints (0 = winner takes all). Wrong = 0.
    public int FirstCorrectPoints { get; set; } = 1000;
    public int OtherCorrectPoints { get; set; } = 0;
}
