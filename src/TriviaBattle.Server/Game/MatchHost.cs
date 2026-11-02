using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
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
    GameRulesService gameRules,
    TimeProvider clock,
    IEnumerable<IMatchBroadcaster> broadcasters,
    MatchHistory history,
    LeaderboardService leaderboards,
    DungeonMap dungeon,
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

        // Read settings once, so a config or admin edit mid-match can't change the rules halfway through.
        var matchSettings = await gameRules.SettingsForNewMatchAsync();

        // Load questions before taking the lock; it's the only slow (database) step.
        // The whole category is the pool: nobody knows in advance how many questions a fast player gets through.
        var questions = await questionSource.PickQuestionsAsync(setup.CategoryIds, setup.Difficulty, int.MaxValue);
        if (questions.Count == 0)
            return new StartMatchResult(null, ["Those categories have no active questions."]);

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
                GameModes.Create(setup.Mode),
                matchSettings,
                clock.GetUtcNow(),
                Random.Shared,
                dungeon);

            _matches.Add(match.MatchId, match);
            logger.LogInformation("Match {Id} started: {Format} {Mode}, {Players} players, {Seconds}s{Clock}, {Questions} questions in the pool, wrong answers: {Penalty}",
                match.MatchId, matchSettings.Format, match.Mode.DisplayName, setup.Seats.Count, match.RoundSeconds,
                match.HasSoloClock ? $" (solo clock, up to {matchSettings.Dungeon.SoloMaxSeconds}s)" : "", questions.Count, matchSettings.WrongAnswerPenalty);

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

    /// <summary>
    /// A swipe at a station: if that station's match is waiting on "Continue?" (solo Dungeon), the
    /// player goes back in with more time. False if there's no continue on offer there.
    /// The station card readers will call this; until they exist, the dev buttons page does.
    /// </summary>
    public bool ContinueAtStation(string stationId)
    {
        lock (_lock)
        {
            var match = _matches.Values.FirstOrDefault(m => !m.IsFinished && m.HasStation(stationId));
            return match != null && Continue(match);
        }
    }

    /// <summary>Dev tools: hands the player at a station a power-up. False if they're not in a Dungeon game that's playing.</summary>
    public bool GivePowerUp(string stationId, PowerUp powerUp)
    {
        lock (_lock)
        {
            var match = _matches.Values.FirstOrDefault(m => !m.IsFinished && m.HasStation(stationId));
            var events = match?.GivePowerUp(stationId, powerUp) ?? [];
            if (events.Count == 0)
                return false;

            Publish(match!, events);
            return true;
        }
    }

    /// <summary>The operator gives a continue from the admin page. False if there's no continue on offer.</summary>
    public bool ContinueMatch(string matchId)
    {
        lock (_lock)
        {
            return _matches.TryGetValue(matchId, out var match) && Continue(match);
        }
    }

    private bool Continue(MatchEngine match)
    {
        var events = match.Continue(clock.GetUtcNow());
        if (events.Count == 0)
            return false;

        logger.LogInformation("Match {Id}: continue #{Count}", match.MatchId, match.ContinuesUsed);
        Publish(match, events);
        return true;
    }

    public MatchSnapshot? GetSnapshot(string matchId, Viewer viewer)
    {
        lock (_lock)
        {
            return _matches.TryGetValue(matchId, out var match) ? SnapshotBuilder.Build(match, viewer) : null;
        }
    }

    /// <summary>
    /// The match being played at any of these stations (e.g. a player's own station),
    /// as that viewer sees it. Null if none of them are in a match.
    /// </summary>
    public MatchSnapshot? GetSnapshotForStations(IEnumerable<string> stationIds, Viewer viewer) =>
        GetSnapshotsForStations(stationIds, viewer).FirstOrDefault();

    /// <summary>
    /// Every match being played at any of these stations (e.g. all stations in a room: several
    /// groups can play at once), as that viewer sees them, in station order.
    /// </summary>
    public IReadOnlyList<MatchSnapshot> GetSnapshotsForStations(IEnumerable<string> stationIds, Viewer viewer)
    {
        lock (_lock)
        {
            var ids = stationIds.ToList();
            return RunningMatchesAt(ids).Select(m => SnapshotBuilder.Build(m, viewer)).ToList();
        }
    }

    /// <summary>Matches with a player at any of these stations, ordered by their first station.</summary>
    private IEnumerable<MatchEngine> RunningMatchesAt(List<string> stationIds) =>
        _matches.Values
            .Where(m => !m.IsFinished && stationIds.Any(m.HasStation))
            .OrderBy(m => m.Roster.Players.Select(p => stationIds.IndexOf(p.StationId)).Where(i => i >= 0).Min());

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

        // Every running match, so outputs shared by a room (main screen, room speaker, room lights)
        // can take all the groups playing there into account.
        var running = _matches.Values.Where(m => !m.IsFinished).ToList();

        foreach (var broadcaster in broadcasters)
        {
            try
            {
                broadcaster.MatchChanged(match, events, running);
            }
            catch (Exception ex)
            {
                // A broken screen connection must never stop the game.
                logger.LogError(ex, "{Broadcaster} failed", broadcaster.GetType().Name);
            }
        }
    }
}
