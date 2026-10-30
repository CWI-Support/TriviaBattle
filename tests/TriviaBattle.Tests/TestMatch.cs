using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Tests;

/// <summary>
/// Helpers for building a match in tests with a controllable clock.
/// Every question's correct answer is A (index 0) so tests read clearly.
/// Teams in team modes: Red = A1 + A3, Blue = A2 + A4.
/// </summary>
public sealed class TestMatch
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private long _nextSequence = 1;

    /// <param name="stations">Free-for-all only: which stations play (default all four), e.g. a solo game on ["A1"].</param>
    public TestMatch(GameModeKind mode, MatchSettings? settings = null, int questionCount = 10, IReadOnlyList<Question>? questions = null,
        IReadOnlyList<string>? stations = null, string matchId = "test-match")
    {
        Settings = settings ?? new MatchSettings();
        Now = Start;

        var teams = mode == GameModeKind.FreeForAll
            ? new List<TeamSetup>()
            : [new TeamSetup("red", "Red", ["A1", "A3"]), new TeamSetup("blue", "Blue", ["A2", "A4"])];

        string[] names = ["Ann", "Bob", "Cat", "Dan"];
        var seats = (stations ?? ["A1", "A2", "A3", "A4"])
            .Select(id => new SeatSetup(id, names[int.Parse(id[1..]) - 1]))
            .ToList();

        var setup = new MatchSetup(
            mode,
            CategoryIds: [1],
            Difficulty.Easy,
            Seats: seats,
            Teams: teams);

        Engine = new MatchEngine(
            matchId,
            setup,
            questions ?? MakeQuestions(questionCount),
            GameModes.Create(mode),
            Settings,
            Now,
            new Random(1), // seeded, so question orders are the same every run
            Settings.Format == GameFormat.Dungeon ? Dungeon : null);
    }

    /// <summary>
    /// The dungeon for Dungeon-format tests: floor 1 has two rats (40 HP, hit for 10), floor 2 a boss
    /// (60 HP, hits for 50). Every hourglass is 10 seconds.
    /// </summary>
    public static readonly DungeonMap Dungeon = new(
        [new DepthDef("test", "Test Depth", [new FloorDef(["rat", "rat"]), new FloorDef(["boss"])])],
        [
            new MonsterDef("rat", "Rat", "rat", Hp: 40, Damage: 10, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "boss", Hp: 60, Damage: 50, AttackSeconds: 10, IsBoss: true),
        ]);

    public MatchSettings Settings { get; }
    public MatchEngine Engine { get; }
    public DateTimeOffset Now { get; private set; }

    public static IReadOnlyList<Question> MakeQuestions(int count, Difficulty difficulty = Difficulty.Easy, int firstId = 1) =>
        Enumerable.Range(firstId, count)
            .Select(i => new Question(i, "Test", difficulty, $"Question {i}?", ["Right", "Wrong 1", "Wrong 2", "Wrong 3"], CorrectIndex: 0))
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

    /// <summary>Skips the intro: the clock is running and everyone's first question is showing.</summary>
    public void StartPlaying()
    {
        while (Engine.Phase != MatchPhase.Playing)
            FinishPhase();
    }

    /// <summary>
    /// Waits <paramref name="afterSeconds"/> (time to read the question; presses in the first
    /// fraction of a second after a new question are ignored), then presses an answer (0-3).
    /// Pass 0 to press at the current time.
    /// </summary>
    public IReadOnlyList<MatchEvent> Press(string stationId, int answerIndex, double afterSeconds = 1)
    {
        Wait(afterSeconds);
        var press = new ButtonPress(stationId, answerIndex, _nextSequence++, Now);
        return Engine.HandlePress(press, Now);
    }

    public QuestionStream StreamOf(string stationId) => Engine.StreamFor(Engine.Roster.FindPlayer(stationId)!);

    /// <summary>Dungeon format: the delve a player is on (their own, or their team's in 2v2 Group).</summary>
    public Delve DelveOf(string stationId) => Engine.FindDelve(StreamOf(stationId).OwnerId)!;

    public int ScoreOf(string stationId) => Engine.Roster.FindPlayer(stationId)!.Tally.Score;

    public int TeamScore(string teamId) => Engine.Roster.FindTeam(teamId)!.Tally.Score;
}
