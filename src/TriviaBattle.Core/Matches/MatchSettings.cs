using TriviaBattle.Core.Dungeon;

namespace TriviaBattle.Core.Matches;

/// <summary>Which game the room plays. The kiosk, scoring and screens all follow it.</summary>
public enum GameFormat
{
    /// <summary>The rapid-fire quiz: answer as many as you can, most points wins.</summary>
    Classic,

    /// <summary>Dungeon Quiz Quest: answers are attacks on monsters, floor by floor (see <see cref="Delve"/>).</summary>
    Dungeon,
}

/// <summary>What happens when someone answers wrong. Chosen by the operator on the admin page.</summary>
public enum WrongAnswerPenalty
{
    /// <summary>That player's (or team's) buttons stop working for <see cref="MatchSettings.LockoutSeconds"/>.</summary>
    Lockout,

    /// <summary><see cref="MatchSettings.WrongAnswerPoints"/> is taken off the score.</summary>
    LosePoints,
}

/// <summary>
/// Timing and rules for every match. Bound from the "Game" section of appsettings.json; the
/// admin page can override the game rules (see GameRulesService on the server). Defaults
/// below are used for anything missing from config.
/// </summary>
public sealed class MatchSettings
{
    public GameFormat Format { get; set; } = GameFormat.Classic;

    /// <summary>Combat numbers for <see cref="GameFormat.Dungeon"/>.</summary>
    public DungeonSettings Dungeon { get; set; } = new();

    /// <summary>How long the clock runs: answer as many questions as you can in this time.</summary>
    public double MatchSeconds { get; set; } = 60;

    // How long the other phases last, in seconds. See MatchPhase for what each phase is.
    public double IntroSeconds { get; set; } = 7;
    public double ResultsSeconds { get; set; } = 10;

    /// <summary>Points for each correct answer.</summary>
    public int CorrectPoints { get; set; } = 100;

    public WrongAnswerPenalty WrongAnswerPenalty { get; set; } = WrongAnswerPenalty.Lockout;

    /// <summary>With the Lockout penalty: how long the buttons stay dead after a wrong answer.</summary>
    public double LockoutSeconds { get; set; } = 2;

    /// <summary>With the LosePoints penalty: points taken off for each wrong answer.</summary>
    public int WrongAnswerPoints { get; set; } = 50;

    /// <summary>
    /// Presses this soon after a new question appears are ignored. Stops one press from also
    /// answering the next question (a bouncing button, or both 2v2 Group teammates pressing at once)
    /// before anyone has seen it.
    /// </summary>
    public double NewQuestionGraceSeconds { get; set; } = 0.3;

    /// <summary>Show the four answers in a random order each time a question is used.</summary>
    public bool ShuffleAnswers { get; set; } = true;

    /// <summary>A copy, so per-match overrides never change the shared configured instance.</summary>
    public MatchSettings Copy() => (MatchSettings)MemberwiseClone();
}
