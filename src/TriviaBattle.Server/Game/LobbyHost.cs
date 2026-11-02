using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Arcade;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Runs the arcade lobby: the button presses and card swipes of stations that aren't in a game, and its
/// clock. When the lobby says a game should start, this starts it with <see cref="MatchHost"/> (the chosen
/// category, or every active one; the lobby's difficulty), and the match takes over those stations' screens.
/// It also keeps the lobbies' list of categories up to date (only ones with active questions). The rules
/// are in <see cref="Lobby"/> (Core).
/// </summary>
public sealed class LobbyHost(
    Lobbies lobbies,
    StationLayout stations,
    MatchHost host,
    LobbyBroadcaster broadcaster,
    IServiceScopeFactory scopes,
    IOptions<LobbySettings> settings,
    TimeProvider clock,
    ILogger<LobbyHost> logger)
{
    /// <summary>How often the categories on offer are read again (admin edits show up within this long).</summary>
    private static readonly TimeSpan CategoryRefresh = TimeSpan.FromMinutes(1);

    private DateTimeOffset _nextCategoryRefresh;
    private int _refreshing; // 1 while a refresh is running
    /// <summary>A press at a station that isn't in a game (<see cref="ButtonRouter"/> decides).</summary>
    public void HandlePress(ButtonPress press)
    {
        if (stations.Find(press.StationId)?.RoomId is not { } roomId)
            return;
        Apply(roomId, lobbies.Use(roomId, lobby => lobby.Press(press.StationId, press.AnswerIndex, clock.GetUtcNow())));
    }

    /// <summary>
    /// A card at a station that isn't in a game: the player is whoever the card belongs to. A card seen for
    /// the first time is registered with a made-up hero name, so it's the same hero next time.
    /// </summary>
    public async Task SwipeAsync(string stationId, string cardId)
    {
        if (stations.Find(stationId)?.RoomId is not { } roomId)
            return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var tag = cardId.Trim();
        var player = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.RfidTag == tag);
        if (player == null)
        {
            player = new PlayerProfileEntity { RfidTag = tag, DisplayName = lobbies.Use(roomId, lobby => lobby.NewGuestName()) };
            db.PlayerProfiles.Add(player);
            logger.LogInformation("New card {Card} at {Station}: {Name}", tag, stationId, player.DisplayName);
        }
        player.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Apply(roomId, lobbies.Use(roomId, lobby => lobby.Swipe(stationId, player.DisplayName, player.Id, clock.GetUtcNow())));
    }

    /// <summary>Called several times a second by <see cref="MatchTicker"/>: forming games start on time, idle stations go back to the attract screen.</summary>
    public void Tick()
    {
        var now = clock.GetUtcNow();
        if (now >= _nextCategoryRefresh && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            _nextCategoryRefresh = now + CategoryRefresh;
            _ = RefreshCategoriesAsync();
        }
        foreach (var roomId in lobbies.RoomIds)
            Apply(roomId, lobbies.Use(roomId, lobby => lobby.Tick(now)));
    }

    /// <summary>Reads the categories with active questions and gives them to every room's lobby.</summary>
    private async Task RefreshCategoriesAsync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var categories = await db.Categories
                .Where(c => c.IsActive && c.Questions.Any(q => q.IsActive))
                .OrderBy(c => c.Name)
                .Select(c => new LobbyCategory(c.Id, c.Name))
                .ToListAsync();
            foreach (var roomId in lobbies.RoomIds)
            {
                var changed = lobbies.Use(roomId, lobby =>
                {
                    var before = lobby.Version;
                    lobby.SetCategories(categories);
                    return lobby.Version != before;
                });
                if (changed)
                    broadcaster.LobbyChanged(roomId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading the lobby's categories failed");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void Apply(string roomId, LobbyResult result)
    {
        foreach (var start in result.Starts)
            _ = StartAsync(start);
        if (result.Changed)
            broadcaster.LobbyChanged(roomId);
    }

    /// <summary>Starts a game from the lobby. If it can't start, the players are back at "press any button" and the log says why.</summary>
    private async Task StartAsync(LobbyStart start)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var categoryIds = await db.Categories
                .Where(c => c.IsActive && c.Questions.Any(q => q.IsActive))
                .Select(c => c.Id)
                .ToListAsync();

            var result = await host.StartMatchAsync(start.ToSetup(categoryIds, settings.Value.Difficulty));
            if (!result.Started)
                logger.LogWarning("A {Game} game from the lobby didn't start: {Problems}", start.Game, string.Join(" ", result.Problems));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Starting a {Game} game from the lobby failed", start.Game);
        }
    }
}
