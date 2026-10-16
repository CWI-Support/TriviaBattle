using TriviaBattle.Core.Input;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Runs one match: moves through the phases, accepts button presses, and scores answers.
/// This is the single source of truth for a match. Screens only display what it says.
///
/// Every match is a timed rapid-fire round. While the clock runs (Playing), each answer owner
/// (a player, or a team in 2v2 Group) has their own <see cref="QuestionStream"/>: a press answers
/// their current question and the next one appears straight away. Wrong answers cost a lockout
/// or points, depending on <see cref="MatchSettings.WrongAnswerPenalty"/>.
///
/// How it's driven:
/// <list type="bullet">
///   <item><see cref="Tick"/> is called several times a second; it ends lockouts and phases when their time is up.</item>
///   <item><see cref="HandlePress"/> is called for each button press at one of this match's stations.</item>
/// </list>
/// Both return the <see cref="MatchEvent"/>s that happened (empty if nothing changed).
///
/// The current time is always passed in rather than read from a clock, which keeps the
/// engine predictable and easy to test.
///
/// NOT thread-safe on purpose: the server's MatchHost makes sure only one call runs at a time.
/// </summary>
public sealed class MatchEngine
{
    private static readonly IReadOnlyList<MatchEvent> NothingHappened = [];

    private readonly Dictionary<string, QuestionStream> _streams;

    /// <param name="questions">The question pool. Every answer owner gets their own order of it.</param>
    /// <param name="random">Shuffles each owner's question order. Tests pass a seeded one.</param>
    public MatchEngine(
        string matchId,
        MatchSetup setup,
        IReadOnlyList<Question> questions,
        IGameMode mode,
        MatchSettings settings,
        DateTimeOffset now,
        Random random)
    {
        if (questions.Count == 0)
            throw new ArgumentException("A match needs at least one question.", nameof(questions));

        MatchId = matchId;
        Setup = setup;
        Mode = mode;
        Settings = settings;
        Roster = new MatchRoster(setup);
        StartedAt = now;
        CategoryName = string.Join(", ", questions.Select(q => q.Category).Distinct().Order());

        // The chosen difficulty comes first; other difficulties only fill in for the very fast.
        _streams = Roster.Players
            .Select(Mode.GetAnswerOwnerId)
            .Distinct()
            .ToDictionary(ownerId => ownerId, ownerId => new QuestionStream(ownerId, questions
                .OrderBy(q => q.Difficulty == setup.Difficulty ? 0 : 1)
                .ThenBy(_ => random.Next())
                .ToList()));

        EnterPhase(MatchPhase.Intro, now, settings.IntroSeconds);
    }

    public string MatchId { get; }
    public MatchSetup Setup { get; }
    public IGameMode Mode { get; }
    public MatchSettings Settings { get; }
    public MatchRoster Roster { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>The category name(s), taken from the questions (the setup only has ids): "Movies, Music".</summary>
    public string CategoryName { get; }

    public MatchPhase Phase { get; private set; }
    public DateTimeOffset PhaseStartedAt { get; private set; }

    /// <summary>When the current phase is scheduled to end. Screens use this for countdowns (the match clock while Playing).</summary>
    public DateTimeOffset PhaseEndsAt { get; private set; }

    /// <summary>Goes up by one every time anything changes. Screens use it to ignore stale updates.</summary>
    public int Version { get; private set; }

    public bool WasAborted { get; private set; }

    public bool IsFinished => Phase == MatchPhase.Finished;

    /// <summary>Every answer owner's question stream, in roster order.</summary>
    public IReadOnlyCollection<QuestionStream> Streams => _streams.Values;

    /// <summary>The stream a player answers in: their own, or their team's in 2v2 Group.</summary>
    public QuestionStream StreamFor(Player player) => _streams[Mode.GetAnswerOwnerId(player)];

    public QuestionStream? FindStream(string ownerId) => _streams.GetValueOrDefault(ownerId);

    public bool HasStation(string stationId) => Roster.FindPlayer(stationId) != null;

    public IReadOnlyList<Standing> GetStandings() => Standings.Calculate(Roster, Mode.UsesTeams);

    // ------------------------------------------------------------------
    // Inputs
    // ------------------------------------------------------------------

    /// <summary>Ends any lockouts that have run out, and moves to the next phase when the current one's time is up.</summary>
    public IReadOnlyList<MatchEvent> Tick(DateTimeOffset now)
    {
        if (IsFinished)
            return NothingHappened;

        var events = new List<MatchEvent>();

        foreach (var stream in _streams.Values.Where(s => s.LockedOutUntil != null && now >= s.LockedOutUntil))
        {
            stream.EndLockout();
            Version++;
            events.Add(new LockoutEnded(stream.OwnerId));
        }

        if (now >= PhaseEndsAt)
            events.AddRange(AdvanceToNextPhase(now));

        return events;
    }

    /// <summary>Handles a button press: answers that owner's current question. Presses outside Playing are ignored.</summary>
    public IReadOnlyList<MatchEvent> HandlePress(ButtonPress press, DateTimeOffset now)
    {
        var player = Roster.FindPlayer(press.StationId);
        if (Phase != MatchPhase.Playing || player == null || press.PressedAt > PhaseEndsAt)
            return NothingHappened;

        var stream = StreamFor(player);
        var shownAt = stream.CurrentShownAt!.Value;

        if (stream.IsLockedOutAt(press.PressedAt))
            return NothingHappened;
        if (press.PressedAt < shownAt + TimeSpan.FromSeconds(Settings.NewQuestionGraceSeconds))
            return NothingHappened; // pressed before the new question could have been read

        var question = stream.CurrentQuestion;
        var isCorrect = press.AnswerIndex == question.CorrectIndex;
        var losesPoints = !isCorrect && Settings.WrongAnswerPenalty == WrongAnswerPenalty.LosePoints;

        var answer = new GivenAnswer(
            stream.OwnerId,
            stream.CurrentNumber,
            question,
            press.StationId,
            press.AnswerIndex,
            isCorrect,
            Points: isCorrect ? Settings.CorrectPoints : losesPoints ? -Settings.WrongAnswerPoints : 0,
            press.PressedAt,
            ResponseTime: press.PressedAt - shownAt);

        stream.Record(answer, now);
        Mode.ScoreAnswer(answer, player, Roster);

        if (!isCorrect && Settings.WrongAnswerPenalty == WrongAnswerPenalty.Lockout)
            stream.LockOut(now + TimeSpan.FromSeconds(Settings.LockoutSeconds));

        Version++;
        return [new AnswerGiven(answer)];
    }

    /// <summary>Ends the match immediately (operator abort from the admin page).</summary>
    public IReadOnlyList<MatchEvent> Abort(DateTimeOffset now)
    {
        if (IsFinished)
            return NothingHappened;

        WasAborted = true;
        EnterPhase(MatchPhase.Finished, now, seconds: null);
        return [new PhaseChanged(MatchPhase.Finished), new MatchFinished(WasAborted: true)];
    }

    // ------------------------------------------------------------------
    // Phase flow (see MatchPhase for the diagram)
    // ------------------------------------------------------------------

    private IReadOnlyList<MatchEvent> AdvanceToNextPhase(DateTimeOffset now)
    {
        switch (Phase)
        {
            case MatchPhase.Intro:
                foreach (var stream in _streams.Values)
                    stream.Start(now);
                EnterPhase(MatchPhase.Playing, now, Settings.MatchSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Playing:
                EnterPhase(MatchPhase.Results, now, Settings.ResultsSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Results:
                EnterPhase(MatchPhase.Finished, now, seconds: null);
                return [new PhaseChanged(Phase), new MatchFinished(WasAborted: false)];

            default:
                return NothingHappened;
        }
    }

    /// <param name="seconds">How long the phase lasts; null = no time limit.</param>
    private void EnterPhase(MatchPhase phase, DateTimeOffset now, double? seconds)
    {
        Phase = phase;
        PhaseStartedAt = now;
        PhaseEndsAt = seconds == null ? DateTimeOffset.MaxValue : now + TimeSpan.FromSeconds(seconds.Value);
        Version++;
    }
}
