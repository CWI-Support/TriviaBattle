using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Admin;

public class GameRulesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MatchSettings _configured = new() { Format = GameFormat.Dungeon }; // what appsettings.json would give
    private readonly GameRulesService _service;

    public GameRulesTests()
    {
        _connection.Open();
        var factory = new Factory(_connection);
        using (var db = factory.CreateDbContext())
            db.Database.EnsureCreated();
        _service = new GameRulesService(factory, new FixedOptions(_configured));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Saved_clock_rules_reach_the_next_match_and_leave_the_configured_defaults_alone()
    {
        var rules = await _service.GetAsync() with { ClockStartSeconds = 75, SecondsPerBoss = 45 };
        await _service.SaveAsync(rules);

        var settings = await _service.SettingsForNewMatchAsync();

        Assert.Equal(75, settings.Dungeon.ClockStartSeconds);
        Assert.Equal(45, settings.Dungeon.SecondsPerBoss);
        Assert.Equal(new MatchSettings().Dungeon.ClockStartSeconds, _configured.Dungeon.ClockStartSeconds);
    }

    [Fact]
    public async Task The_defaults_are_valid()
    {
        Assert.Empty(GameRulesService.Validate(await _service.GetAsync()));
    }

    [Fact]
    public async Task A_clock_that_starts_too_short_is_refused()
    {
        var rules = await _service.GetAsync() with { ClockStartSeconds = 5 };

        var problem = Assert.Single(GameRulesService.Validate(rules));

        Assert.Contains("starting time", problem);
    }

    private sealed class Factory(SqliteConnection connection) : IDbContextFactory<TriviaDbContext>
    {
        public TriviaDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<TriviaDbContext>().UseSqlite(connection).Options);
    }

    private sealed class FixedOptions(MatchSettings value) : IOptionsMonitor<MatchSettings>
    {
        public MatchSettings CurrentValue => value;
        public MatchSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<MatchSettings, string?> listener) => null;
    }
}
