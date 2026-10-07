using TriviaBattle.Core.Input;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Scoring;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Runs one match: moves through the phases, accepts button presses, and scores answers.
/// This is the single source of truth for a match. Screens only display what it says.
///
/// How it's driven:
/// <list type="bullet">
///   <item><see cref="Tick"/> is called several times a second; it advances the phase when the current one's time is up.</item>
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

    private readonly IReadOnlyList<Question> _questions;
    private readonly IScoringRule _scoring;
    private readonly List<Round> _rounds = new();

    public MatchEngine(
        string matchId,
        MatchSetup setup,
        IReadOnlyList<Question> questions,
        IGameMode mode,
        IScoringRule scoring,
        MatchSettings settings,
        DateTimeOffset now)
    {
        if (questions.Count == 0)
            throw new ArgumentException("A match needs at least one question.", nameof(questions));

        MatchId = matchId;
        Setup = setup;
        Mode = mode;
        Settings = settings;
        Roster = new MatchRoster(setup);
        StartedAt = now;
        _questions = questions;
        _scoring = scoring;

        EnterPhase(MatchPhase.Intro, now, settings.IntroSeconds);
    }

    public string MatchId { get; }
    public MatchSetup Setup { get; }
    public IGameMode Mode { get; }
    public MatchSettings Settings { get; }
    public MatchRoster Roster { get; }
    public DateTimeOffset StartedAt { get; }

    public MatchPhase Phase { get; private set; }
    public DateTimeOffset PhaseStartedAt { get; private set; }

    /// <summary>When the current phase is scheduled to end. Screens use this for countdowns.</summary>
    public DateTimeOffset PhaseEndsAt { get; private set; }

    /// <summary>Goes up by one every time anything changes. Screens use it to ignore stale updates.</summary>
    public int Version { get; private set; }

    public bool WasAborted { get; private set; }

    public int TotalRounds => _questions.Count;

    /// <summary>The category name, taken from the questions (the setup only has the id).</summary>
    public string CategoryName => _questions[0].Category;

    /// <summary>Questions asked so far, including the current one.</summary>
    public IReadOnlyList<Round> Rounds => _rounds;

    public Round? CurrentRound => _rounds.Count > 0 ? _rounds[^1] : null;

    public bool IsFinished => Phase == MatchPhase.Finished;

    public bool HasStation(string stationId) => Roster.FindPlayer(stationId) != null;

    public IReadOnlyList<Standing> GetStandings() => Standings.Calculate(Roster, Mode.UsesTeams);

    // ------------------------------------------------------------------
    // Inputs
    // ------------------------------------------------------------------

    /// <summary>Advances to the next phase if the current phase's time is up.</summary>
    public IReadOnlyList<MatchEvent> Tick(DateTimeOffset now)
    {
        if (IsFinished || now < PhaseEndsAt)
            return NothingHappened;

        return AdvanceToNextPhase(now);
    }

    /// <summary>Handles a button press. Presses outside the answer window are ignored.</summary>
    public IReadOnlyList<MatchEvent> HandlePress(ButtonPress press, DateTimeOffset now)
    {
        var round = CurrentRound;
        var player = Roster.FindPlayer(press.StationId);

        if (Phase != MatchPhase.QuestionOpen || round == null || player == null)
            return NothingHappened;
        if (!round.AcceptsPressAt(press.PressedAt))
            return NothingHappened;

        var ownerId = Mode.GetAnswerOwnerId(player);
        var stationsSharingAnswer = Roster.Players
            .Where(p => Mode.GetAnswerOwnerId(p) == ownerId)
            .Select(p => p.StationId)
            .ToList();

        var locked = Mode.LockInRule.TryLock(round, ownerId, stationsSharingAnswer, press);
        if (locked == null)
            return NothingHappened;

        round.Lock(locked);
        Version++;

        var events = new List<MatchEvent> { new AnswerLocked(locked) };

        if (Settings.EndQuestionWhenAllAnswered && EveryoneHasAnswered(round))
            events.AddRange(AdvanceToNextPhase(now));

        return events;
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
            case MatchPhase.Standings:
                StartNextQuestion(now);
                return [new PhaseChanged(Phase)];

            case MatchPhase.QuestionLeadIn:
                CurrentRound!.Open(now, TimeSpan.FromSeconds(Settings.AnswerSeconds));
                EnterPhase(MatchPhase.QuestionOpen, now, Settings.AnswerSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.QuestionOpen:
                var results = ScoreCurrentQuestion();
                EnterPhase(MatchPhase.Reveal, now, Settings.RevealSeconds);
                return [new PhaseChanged(Phase), new RoundRevealed(CurrentRound!.Number, results)];

            case MatchPhase.Reveal:
                var wasLastQuestion = _rounds.Count >= TotalRounds;
                if (wasLastQuestion)
                    EnterPhase(MatchPhase.Results, now, Settings.ResultsSeconds);
                else
                    EnterPhase(MatchPhase.Standings, now, Settings.StandingsSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Results:
                EnterPhase(MatchPhase.Finished, now, seconds: null);
                return [new PhaseChanged(Phase), new MatchFinished(WasAborted: false)];

            default:
                return NothingHappened;
        }
    }

    private void StartNextQuestion(DateTimeOffset now)
    {
        var number = _rounds.Count + 1;
        _rounds.Add(new Round(number, _questions[number - 1]));
        EnterPhase(MatchPhase.QuestionLeadIn, now, Settings.LeadInSeconds);
    }

    /// <param name="seconds">How long the phase lasts; null = no time limit.</param>
    private void EnterPhase(MatchPhase phase, DateTimeOffset now, double? seconds)
    {
        Phase = phase;
        PhaseStartedAt = now;
        PhaseEndsAt = seconds == null ? DateTimeOffset.MaxValue : now + TimeSpan.FromSeconds(seconds.Value);
        Version++;
    }

    // ------------------------------------------------------------------
    // Scoring
    // ------------------------------------------------------------------

    private List<string> AllAnswerOwnerIds() =>
        Roster.Players.Select(Mode.GetAnswerOwnerId).Distinct().ToList();

    private bool EveryoneHasAnswered(Round round) =>
        AllAnswerOwnerIds().All(round.Answers.ContainsKey);

    private IReadOnlyList<RoundResult> ScoreCurrentQuestion()
    {
        var round = CurrentRound!;
        var correctIndex = round.Question.CorrectIndex;

        var correctAnswers = round.Answers.Values.Where(a => a.AnswerIndex == correctIndex).ToList();
        var pointsByOwner = _scoring.ScoreCorrectAnswers(round, correctAnswers);

        var results = new List<RoundResult>();
        foreach (var ownerId in AllAnswerOwnerIds())
        {
            var answer = round.Answers.GetValueOrDefault(ownerId);
            var isCorrect = answer != null && answer.AnswerIndex == correctIndex;
            var points = isCorrect ? pointsByOwner.GetValueOrDefault(ownerId) : 0;
            var responseTime = answer == null ? (TimeSpan?)null : round.ResponseTimeOf(answer);

            var result = new RoundResult(ownerId, answer, isCorrect, points, responseTime);
            results.Add(result);

            if (isCorrect)
                Mode.AwardCorrectAnswer(result, Roster.FindPlayer(answer!.PressedByStationId)!, Roster);
        }

        round.SetResults(results);
        return results;
    }
}
