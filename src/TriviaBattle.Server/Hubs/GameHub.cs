using Microsoft.AspNetCore.SignalR;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Hardware.Input;
using TriviaBattle.Server.Diagnostics;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Hubs;

/// <summary>
/// The websocket endpoint every screen connects to (/gamehub).
///
/// Screens call:
///   Join(role, roomId, stationNumber) -> returns the current ScreenState, then receives "State" pushes.
///   Ping(clientTimeMs)                -> returns the server time, used to sync clocks.
///   ServerId()                        -> changes on every server start; screens reload when it changes.
///   ReportError(message)              -> a screen's own error (script crash, failed request), into the log.
///   PressKey(key)                     -> dev only: simulates a button via the keyboard map.
///   PressStationButton(room, n, answer) -> dev only: a player screen pressing its own answer button.
///
/// Note there is no "answer" method: answers only come from the buttons.
/// Starting a match is a normal HTTP call (POST /api/matches) so the kiosk gets errors back directly.
/// </summary>
public class GameHub(
    StationLayout stations,
    MatchHost host,
    ScreenStateFactory screenStates,
    ButtonMap buttonMap,
    KeyboardButtonInput keyboard,
    DevToolsSettings devTools,
    ScreenRegistry screens,
    ILogger<GameHub> logger,
    TimeProvider clock) : Hub
{
    /// <param name="role">"main", "kiosk" or "player".</param>
    /// <param name="roomId">Room id from the URL (?room=A); null = the first room.</param>
    /// <param name="stationNumber">For players: the number in /display/player/{n}.</param>
    public async Task<ScreenState> Join(string role, string? roomId, int? stationNumber)
    {
        roomId ??= stations.RoomIds.First();
        if (stations.InRoom(roomId).Count == 0)
            throw new HubException($"Unknown room '{roomId}'.");

        switch (role)
        {
            case "main":
                await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroups.MainScreen(roomId));
                Register($"main:{roomId}", "Main screen");
                return screenStates.ForRoom(roomId, MatchesInRoom(roomId, Viewer.Main));

            case "kiosk":
                await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroups.Kiosk(roomId));
                Register($"kiosk:{roomId}", "Kiosk");
                return screenStates.ForRoom(roomId, MatchesInRoom(roomId, Viewer.Kiosk));

            case "player":
                var station = stations.Find(roomId, stationNumber ?? 0)
                    ?? throw new HubException($"Room {roomId} has no station {stationNumber}.");
                await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroups.PlayerScreen(station.Id));
                Register($"player:{station.Id}", station.Label);
                var match = host.GetSnapshotForStations([station.Id], Viewer.Player(station.Id));
                return screenStates.ForStation(roomId, station, match);

            default:
                throw new HubException($"Unknown screen role '{role}'.");
        }
    }

    public long Ping(long clientTimeMs) => clock.GetUtcNow().ToUnixTimeMilliseconds();

    public string ServerId() => ServerInstance.Id;

    /// <summary>A screen hit an error of its own (script crash, failed request). Goes to the log and the admin Live log.</summary>
    public void ReportError(string message)
    {
        var name = screens.Find(Context.ConnectionId)?.Name ?? "A screen";
        if (message.Length > 1000) message = message[..1000] + "...";
        logger.LogWarning("{Screen} error: {Message}", name, message);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var screen = screens.Left(Context.ConnectionId);
        if (screen != null)
            logger.LogInformation("{Screen} disconnected ({Address})", screen.Name, screen.Address);
        return base.OnDisconnectedAsync(exception);
    }

    public bool PressKey(string key)
    {
        if (!devTools.Enabled)
            throw new HubException("Dev tools are turned off (DevTools:Enabled).");

        return keyboard.PressKey(key);
    }

    /// <summary>
    /// Dev/demo: a player screen pressing one of its own four buttons (keys 1-4 / A-D, or tapping an answer).
    /// Goes through the same path as a real button: station + answer -> button id -> keyboard input -> ButtonRouter.
    /// </summary>
    public bool PressStationButton(string? roomId, int stationNumber, int answerIndex)
    {
        if (!devTools.Enabled)
            throw new HubException("Dev tools are turned off (DevTools:Enabled).");

        var station = stations.Find(roomId ?? stations.RoomIds.First(), stationNumber);
        var buttonId = station == null ? null : buttonMap.FindButtonId(station.Id, answerIndex);
        if (buttonId == null)
            return false;

        keyboard.PressButton(buttonId.Value);
        return true;
    }

    // A rejoin after a reconnect replaces the same entry, so only the first join is logged.
    private void Register(string key, string name)
    {
        var address = Context.GetHttpContext()?.Connection.RemoteIpAddress?.MapToIPv4().ToString();
        var isNew = screens.Find(Context.ConnectionId) == null;
        if (isNew)
            logger.LogInformation("{Screen} connected ({Address})", name, address);
        screens.Joined(Context.ConnectionId, new ConnectedScreen(key, name, address, clock.GetUtcNow()));

        // Two screens as one station both take the hero photo (and only one counts): worth saying.
        var same = screens.All.Where(s => s.Key == key).ToList();
        if (isNew && key.StartsWith("player:") && same.Count > 1)
            logger.LogWarning("{Screen} now has {Count} screens open ({Addresses}). Give every screen its own player number: each one with a camera takes the hero photo.",
                name, same.Count, string.Join(", ", same.Select(s => s.Address)));
    }

    private IReadOnlyList<MatchSnapshot> MatchesInRoom(string roomId, Viewer viewer) =>
        host.GetSnapshotsForStations(stations.InRoom(roomId).Select(s => s.Id), viewer);
}

/// <summary>Whether developer helpers (keyboard buttons, /api/dev) are switched on.</summary>
public sealed record DevToolsSettings(bool Enabled);
