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
    PhotoStore photos,
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
        LobbyStep? from = null, to = null;
        var change = Change(roomId, lobby =>
        {
            from = lobby.Seat(press.StationId)?.Step;
            var pressed = lobby.Press(press.StationId, press.AnswerIndex, clock.GetUtcNow());
            to = lobby.Seat(press.StationId)?.Step;
            return pressed;
        });
        // Each step a station takes, so the log shows where a player got to (and where they stopped).
        if (from != to)
            logger.LogInformation("Lobby {Station}: {From} -> {To}", press.StationId, from, to);
        Apply(roomId, change);
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

        Apply(roomId, Change(roomId, lobby => lobby.Swipe(stationId, player.DisplayName, player.Id, clock.GetUtcNow())));
    }

    /// <summary>
    /// A station sent the hero photo it took in the lobby: kept (<see cref="PhotoStore"/>) if that station is
    /// taking one. False if it isn't, or already has one (a second screen open as the same station).
    /// </summary>
    public bool PhotoArrived(string stationId, HeroPicture picture)
    {
        if (stations.Find(stationId)?.RoomId is not { } roomId)
            return false;
        var accepted = lobbies.Use(roomId, lobby =>
        {
            if (!lobby.PhotoArrived(stationId))
                return false;
            photos.KeepLobby(stationId, picture);
            return true;
        });
        if (accepted)
        {
            logger.LogInformation("Lobby {Station}: hero photo arrived", stationId);
            broadcaster.LobbyChanged(roomId);
        }
        else
            logger.LogWarning("Lobby {Station}: a hero photo was refused (is another screen open as that station?)", stationId);
        return accepted;
    }

    /// <summary>A station without a camera skips the lobby's photo step.</summary>
    public void SkipPhoto(string stationId)
    {
        if (stations.Find(stationId)?.RoomId is not { } roomId)
            return;
        var change = Change(roomId, lobby => lobby.SkipPhoto(stationId));
        if (change.Result.Changed)
            logger.LogInformation("Lobby {Station}: no camera, so no hero photo", stationId);
        Apply(roomId, change);
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
            Apply(roomId, Change(roomId, lobby => lobby.Tick(now)));
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

    /// <summary>What a change to a lobby did, and the hero photos of any game it starts (taken out of the lobby).</summary>
    private sealed record LobbyChange(LobbyResult Result, IReadOnlyDictionary<string, HeroPicture> StartingPhotos);

    /// <summary>
    /// Changes the room's lobby and, under the same lock, keeps its photos in step with it (a tick or a press
    /// at another station can't come in between): a game starting takes its players' photos with it, and a
    /// photo nobody has any more (dropped, retaken, or its player left) is forgotten straight away.
    /// </summary>
    private LobbyChange Change(string roomId, Func<Lobby, LobbyResult> work) => lobbies.Use(roomId, lobby =>
    {
        var result = work(lobby);
        var startingPhotos = result.Starts.SelectMany(start => start.Players)
            .Where(p => p.Photo)
            .Select(p => (p.StationId, Picture: photos.TakeLobby(p.StationId)))
            .Where(p => p.Picture != null)
            .ToDictionary(p => p.StationId, p => p.Picture!);
        foreach (var seat in lobby.Seats.Where(s => !s.HasPhoto))
            photos.ForgetLobby(seat.StationId);
        return new LobbyChange(result, startingPhotos);
    });

    private void Apply(string roomId, LobbyChange change)
    {
        foreach (var start in change.Result.Starts)
        {
            logger.LogInformation("Lobby: starting a {Game} ({Category}) for {Players}", start.Game, start.Category?.Name ?? "everything",
                string.Join(", ", start.Players.Select(p => $"{p.StationId} {p.Name} ({p.Hero}{(start.Game == GameChoice.Battle ? $", side {p.Side + 1}" : "")})")));
            _ = StartAsync(start, change.StartingPhotos);
        }
        if (change.Result.Changed)
            broadcaster.LobbyChanged(roomId);
    }

    /// <summary>
    /// Starts a game from the lobby, with the hero photos its players brought (moved into the match). If it
    /// can't start, the players are back at "press any button" and the log says why.
    /// </summary>
    private async Task StartAsync(LobbyStart start, IReadOnlyDictionary<string, HeroPicture> lobbyPhotos)
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
            {
                logger.LogWarning("A {Game} game from the lobby didn't start: {Problems}", start.Game, string.Join(" ", result.Problems));
                return;
            }
            // Screens that ask before this lands get a 404 and ask again a moment later (js/hero-photo.js).
            foreach (var player in start.Players)
                if (lobbyPhotos.TryGetValue(player.StationId, out var picture))
                    photos.Keep(result.MatchId!, player.StationId, picture);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Starting a {Game} game from the lobby failed", start.Game);
        }
    }
}
