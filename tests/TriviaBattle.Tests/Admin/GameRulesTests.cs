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
    public async Task Saved_solo_clock_rules_reach_the_next_match_and_leave_the_configured_defaults_alone()
    {
        var rules = await _service.GetAsync() with { SoloStartSeconds = 75, SoloSecondsPerBoss = 45, SoloMaxSeconds = 300 };
        await _service.SaveAsync(rules);

        var settings = await _service.SettingsForNewMatchAsync();

        Assert.Equal(75, settings.Dungeon.SoloStartSeconds);
        Assert.Equal(45, settings.Dungeon.SoloSecondsPerBoss);
        Assert.Equal(300, settings.Dungeon.SoloMaxSeconds);
        Assert.Equal(90, _configured.Dungeon.SoloStartSeconds);
    }

    [Fact]
    public async Task The_defaults_are_valid()
    {
        Assert.Empty(GameRulesService.Validate(await _service.GetAsync()));
    }

    [Fact]
    public async Task The_longest_solo_game_cannot_be_shorter_than_its_starting_time()
    {
        var rules = await _service.GetAsync() with { SoloStartSeconds = 120, SoloMaxSeconds = 60 };

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
