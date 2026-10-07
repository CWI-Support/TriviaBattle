using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Scoring;

namespace TriviaBattle.Tests;

/// <summary>
/// Helpers for building a match in tests with a controllable clock.
/// Every question's correct answer is A (index 0) so tests read clearly.
/// </summary>
public sealed class TestMatch
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private long _nextSequence = 1;

    public TestMatch(GameModeKind mode, MatchSettings? settings = null, int questionCount = 3)
    {
        Settings = settings ?? new MatchSettings();
        Now = Start;

        var teams = mode == GameModeKind.FreeForAll
            ? new List<TeamSetup>()
            : [new TeamSetup("red", "Red", ["A1", "A3"]), new TeamSetup("blue", "Blue", ["A2", "A4"])];

        var setup = new MatchSetup(
            mode,
            CategoryId: 1,
            Difficulty.Easy,
            Seats: [new SeatSetup("A1", "Ann"), new SeatSetup("A2", "Bob"), new SeatSetup("A3", "Cat"), new SeatSetup("A4", "Dan")],
            Teams: teams);

        Engine = new MatchEngine(
            "test-match",
            setup,
            MakeQuestions(questionCount),
            GameModes.Create(mode, Settings.TeamLockIn),
            ScoringRules.Create(Settings.Scoring),
            Settings,
            Now);
    }

    public MatchSettings Settings { get; }
    public MatchEngine Engine { get; }
    public DateTimeOffset Now { get; private set; }

    public static IReadOnlyList<Question> MakeQuestions(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Question(i, "Test", Difficulty.Easy, $"Question {i}?", ["Right", "Wrong 1", "Wrong 2", "Wrong 3"], CorrectIndex: 0))
            .ToList();

    /// <summary>Moves the clock forward and ticks the engine.</summary>
    public IReadOnlyList<MatchEvent> Wait(double seconds)
    {
        Now += TimeSpan.FromSeconds(seconds);
        return Engine.Tick(Now);
    }

    /// <summary>Jumps the clock to the end of the current phase and ticks.</summary>
    public IReadOnlyList<MatchEvent> FinishPhase()
    {
        Now = Engine.PhaseEndsAt;
        return Engine.Tick(Now);
    }

    /// <summary>Skips ahead until the buttons are live for the next question.</summary>
    public void SkipToQuestionOpen()
    {
        while (Engine.Phase != MatchPhase.QuestionOpen)
            FinishPhase();
    }

    /// <summary>Presses an answer (0-3) at a station, at the current time.</summary>
    public IReadOnlyList<MatchEvent> Press(string stationId, int answerIndex)
    {
        var press = new ButtonPress(stationId, answerIndex, _nextSequence++, Now);
        return Engine.HandlePress(press, Now);
    }

    public int ScoreOf(string stationId) => Engine.Roster.FindPlayer(stationId)!.Tally.Score;

    public int TeamScore(string teamId) => Engine.Roster.FindTeam(teamId)!.Tally.Score;
}
