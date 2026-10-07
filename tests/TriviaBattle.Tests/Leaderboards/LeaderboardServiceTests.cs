using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Leaderboards;

namespace TriviaBattle.Tests.Leaderboards;

public class LeaderboardServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbFactory _dbFactory;
    private readonly LeaderboardSettings _settings = new() { Size = 10 };
    private readonly MatchHistory _history;
    private readonly LeaderboardService _leaderboards;

    public LeaderboardServiceTests()
    {
        _connection.Open();
        _dbFactory = new TestDbFactory(new DbContextOptionsBuilder<TriviaDbContext>().UseSqlite(_connection).Options);
        using (var db = _dbFactory.CreateDbContext())
            db.Database.EnsureCreated();

        _history = new MatchHistory(_dbFactory);
        _leaderboards = new LeaderboardService(_dbFactory, new FixedOptions<LeaderboardSettings>(_settings), new FixedClock(Now));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task FreeForAll_board_ranks_scores_and_keeps_each_registered_players_best()
    {
        await SaveFreeForAll("m1", Now.AddHours(-1), ("Ann", 7, 3000), ("Bob", null, 2000));
        await SaveFreeForAll("m2", Now.AddMinutes(-30), ("Ann", 7, 5000), ("Guest", null, 4000));

        var board = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.AllTime);

        // Ann (registered) appears once, with her best (5000). Guests each count separately.
        Assert.Equal(["Ann", "Guest", "Bob"], board.Entries.Select(e => e.Names));
        Assert.Equal([5000, 4000, 2000], board.Entries.Select(e => e.Score));
        Assert.Equal([1, 2, 3], board.Entries.Select(e => e.Rank));
    }

    [Fact]
    public async Task Aborted_matches_never_count()
    {
        await SaveFreeForAll("m1", Now.AddHours(-1), ("Ann", null, 9000));
        await using (var db = _dbFactory.CreateDbContext())
        {
            (await db.Matches.SingleAsync()).WasAborted = true;
            await db.SaveChangesAsync();
        }

        var board = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.AllTime);

        Assert.Empty(board.Entries);
    }

    [Fact]
    public async Task Zero_scores_never_make_a_board()
    {
        await SaveFreeForAll("m1", Now.AddHours(-1), ("Ann", null, 1200), ("Bob", null, 0));

        var board = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.AllTime);

        Assert.Equal(["Ann"], board.Entries.Select(e => e.Names));
    }

    [Fact]
    public async Task Today_board_only_has_todays_matches()
    {
        await SaveFreeForAll("old", Now.AddDays(-2), ("Old Timer", null, 9000));
        await SaveFreeForAll("new", Now.AddHours(-1), ("Fresh", null, 100));

        var today = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.Today);
        var allTime = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.AllTime);

        Assert.Equal(["Fresh"], today.Entries.Select(e => e.Names));
        Assert.Equal(2, allTime.Entries.Count);
    }

    [Fact]
    public async Task Team_board_shows_teammates_names_and_team_score()
    {
        await SaveTeamMatch("t1", GameModeKind.TeamSharedAnswer, red: 3000, blue: 1000);

        var board = await _leaderboards.GetAsync(GameModeKind.TeamSharedAnswer, LeaderboardPeriod.AllTime);

        Assert.Equal(["Ann & Cat", "Bob & Dan"], board.Entries.Select(e => e.Names));
        Assert.Equal([3000, 1000], board.Entries.Select(e => e.Score));
    }

    [Fact]
    public async Task Each_mode_has_its_own_board()
    {
        await SaveTeamMatch("t1", GameModeKind.TeamSharedAnswer, red: 3000, blue: 1000);

        var combined = await _leaderboards.GetAsync(GameModeKind.TeamCombinedScore, LeaderboardPeriod.AllTime);

        Assert.Empty(combined.Entries);
    }

    [Fact]
    public async Task Registered_only_setting_hides_guests()
    {
        _settings.RegisteredPlayersOnly = true;
        await SaveFreeForAll("m1", Now.AddHours(-1), ("Ann", 7, 3000), ("Guest", null, 9000));

        var board = await _leaderboards.GetAsync(GameModeKind.FreeForAll, LeaderboardPeriod.AllTime);

        Assert.Equal(["Ann"], board.Entries.Select(e => e.Names));
    }

    [Fact]
    public async Task Placements_report_where_a_new_match_landed()
    {
        await SaveFreeForAll("m1", Now.AddHours(-1), ("Ann", null, 3000));
        var result = await SaveFreeForAll("m2", Now, ("Bob", null, 5000), ("Cat", null, 1000));

        var placements = await _leaderboards.FindPlacementsAsync(result);

        var bob = placements.Single(p => p.Names == "Bob");
        Assert.Equal(1, bob.Rank);
        Assert.Equal("all-time", bob.Board);
        Assert.Contains(placements, p => p.Names == "Cat" && p.Rank == 3);
    }

    // ---------------- helpers ----------------

    private async Task<MatchResult> SaveFreeForAll(string matchId, DateTimeOffset endedAt, params (string Name, int? ProfileId, int Score)[] players)
    {
        var participants = players
            .Select((p, i) => new ParticipantResult($"A{i + 1}", p.Name, p.ProfileId, null, p.Score, 1, null))
            .ToList();

        var result = new MatchResult(matchId, "FreeForAll", 1, "Test", "Easy", endedAt.AddMinutes(-5), endedAt, false, 10, participants, [], []);
        await _history.SaveAsync(result);
        return result;
    }

    private async Task SaveTeamMatch(string matchId, GameModeKind mode, int red, int blue)
    {
        var result = new MatchResult(matchId, mode.ToString(), 1, "Test", "Easy", Now.AddMinutes(-10), Now.AddMinutes(-5), false, 10,
            Participants:
            [
                new("A1", "Ann", null, "red", red, 1, null),
                new("A3", "Cat", null, "red", 0, 0, null),
                new("A2", "Bob", null, "blue", blue, 1, null),
                new("A4", "Dan", null, "blue", 0, 0, null),
            ],
            Teams: [new("red", "Red Team", red, 1, 1), new("blue", "Blue Team", blue, 1, 2)],
            Answers: []);
        await _history.SaveAsync(result);
    }

    private sealed class TestDbFactory(DbContextOptions<TriviaDbContext> options) : IDbContextFactory<TriviaDbContext>
    {
        public TriviaDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>A clock stuck at one moment, in UTC (so "today" is predictable).</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
