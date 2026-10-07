using Microsoft.Extensions.Options;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Scoring;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Leaderboards;

namespace TriviaBattle.Server.Game;

/// <summary>The outcome of asking to start a match.</summary>
public sealed record StartMatchResult(string? MatchId, IReadOnlyList<string> Problems)
{
    public bool Started => MatchId != null;
}

/// <summary>
/// Owns every running match (there can be several, e.g. one per room). This is the only
/// place that touches a <see cref="MatchEngine"/>, and it does so under a single lock, so
/// button presses, timer ticks and admin actions never run at the same time.
/// One lock for everything is plenty: each call does a few microseconds of work.
/// </summary>
public sealed class MatchHost(
    IQuestionSource questionSource,
    StationLayout stations,
    IOptionsMonitor<MatchSettings> settings,
    TimeProvider clock,
    IEnumerable<IMatchBroadcaster> broadcasters,
    MatchHistory history,
    LeaderboardService leaderboards,
    ILogger<MatchHost> logger)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, MatchEngine> _matches = new();
    private readonly HashSet<string> _savedMatchIds = new();

    public async Task<StartMatchResult> StartMatchAsync(MatchSetup setup)
    {
        var problems = MatchSetupValidator.Validate(setup, stations);
        if (problems.Count > 0)
            return new StartMatchResult(null, problems);

        // Read settings once, so a config edit mid-match can't change the rules halfway through.
        var matchSettings = settings.CurrentValue;

        // Load questions before taking the lock; it's the only slow (database) step.
        var questions = await questionSource.PickQuestionsAsync(setup.CategoryId, setup.Difficulty, matchSettings.QuestionsPerMatch);
        if (questions.Count == 0)
            return new StartMatchResult(null, ["That category has no active questions."]);

        if (matchSettings.ShuffleAnswers)
            questions = questions.Select(q => q.WithShuffledAnswers(Random.Shared)).ToList();

        lock (_lock)
        {
            var busy = setup.Seats.Select(s => s.StationId).Where(IsStationInMatch).ToList();
            if (busy.Count > 0)
                return new StartMatchResult(null, [$"Station(s) {string.Join(", ", busy)} are already in a match."]);

            var match = new MatchEngine(
                matchId: Guid.NewGuid().ToString("N")[..8],
                setup,
                questions,
                GameModes.Create(setup.Mode, matchSettings.TeamLockIn),
                ScoringRules.Create(matchSettings.Scoring),
                matchSettings,
                clock.GetUtcNow());

            _matches.Add(match.MatchId, match);
            logger.LogInformation("Match {Id} started: {Mode}, {Players} players, {Questions} questions",
                match.MatchId, match.Mode.DisplayName, setup.Seats.Count, questions.Count);

            Publish(match, [new PhaseChanged(match.Phase)]);
            return new StartMatchResult(match.MatchId, []);
        }
    }

    /// <summary>Called by the button router for every translated press.</summary>
    public void HandlePress(ButtonPress press)
    {
        lock (_lock)
        {
            var match = _matches.Values.FirstOrDefault(m => m.HasStation(press.StationId));
            if (match == null)
                return; // nobody is playing at that station right now

            var events = match.HandlePress(press, clock.GetUtcNow());
            Publish(match, events);
        }
    }

    /// <summary>Called several times a second by <see cref="MatchTicker"/>.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();

            foreach (var match in _matches.Values.ToList())
            {
                var events = match.Tick(now);
                Publish(match, events);

                if (match.IsFinished)
                    RemoveFinishedMatch(match);
            }
        }
    }

    public bool AbortMatch(string matchId)
    {
        lock (_lock)
        {
            if (!_matches.TryGetValue(matchId, out var match))
                return false;

            Publish(match, match.Abort(clock.GetUtcNow()));
            RemoveFinishedMatch(match);
            return true;
        }
    }

    public MatchSnapshot? GetSnapshot(string matchId, Viewer viewer)
    {
        lock (_lock)
        {
            return _matches.TryGetValue(matchId, out var match) ? SnapshotBuilder.Build(match, viewer) : null;
        }
    }

    /// <summary>
    /// The match being played at any of these stations (e.g. all stations in a room),
    /// as that viewer sees it. Null if none of them are in a match.
    /// </summary>
    public MatchSnapshot? GetSnapshotForStations(IEnumerable<string> stationIds, Viewer viewer)
    {
        lock (_lock)
        {
            var ids = stationIds.ToList();
            var match = _matches.Values.FirstOrDefault(m => ids.Any(m.HasStation));
            return match == null ? null : SnapshotBuilder.Build(match, viewer);
        }
    }

    public IReadOnlyList<MatchSnapshot> GetAllSnapshots(Viewer viewer)
    {
        lock (_lock)
        {
            return _matches.Values.Select(m => SnapshotBuilder.Build(m, viewer)).ToList();
        }
    }

    private bool IsStationInMatch(string stationId) => _matches.Values.Any(m => m.HasStation(stationId));

    private void RemoveFinishedMatch(MatchEngine match)
    {
        _matches.Remove(match.MatchId);
        _savedMatchIds.Remove(match.MatchId);
        logger.LogInformation("Match {Id} {How}", match.MatchId, match.WasAborted ? "aborted" : "finished");
    }

    /// <summary>Everything that happens after the engine reports events: tell the screens, and save results.</summary>
    private void Publish(MatchEngine match, IReadOnlyList<MatchEvent> events)
    {
        Broadcast(match, events);

        // Scores are final once Results starts, so save then. Results is still on screen
        // when the save finishes, so any new high score can be shown right away.
        // An aborted match is saved (flagged as aborted) unless it was already saved.
        var resultsStarted = events.Any(e => e is PhaseChanged { Phase: MatchPhase.Results });
        var aborted = events.Any(e => e is MatchFinished { WasAborted: true });

        if ((resultsStarted || aborted) && _savedMatchIds.Add(match.MatchId))
        {
            var result = MatchResult.From(match, clock.GetUtcNow());
            _ = SaveResultAsync(result); // runs after the lock is released; never blocks the game
        }
    }

    private async Task SaveResultAsync(MatchResult result)
    {
        try
        {
            await history.SaveAsync(result);
            if (result.WasAborted)
                return;

            var placements = await leaderboards.FindPlacementsAsync(result);
            if (placements.Count == 0)
                return;

            lock (_lock)
            {
                // Only announce if the match is still on screen.
                if (_matches.TryGetValue(result.MatchId, out var match))
                    Broadcast(match, placements);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Saving match {Id} failed", result.MatchId);
        }
    }

    private void Broadcast(MatchEngine match, IReadOnlyList<MatchEvent> events)
    {
        if (events.Count == 0)
            return;

        foreach (var broadcaster in broadcasters)
        {
            try
            {
                broadcaster.MatchChanged(match, events);
            }
            catch (Exception ex)
            {
                // A broken screen connection must never stop the game.
                logger.LogError(ex, "{Broadcaster} failed", broadcaster.GetType().Name);
            }
        }
    }
}
