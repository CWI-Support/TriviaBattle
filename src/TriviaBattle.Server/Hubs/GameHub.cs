using Microsoft.AspNetCore.SignalR;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Hardware.Input;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Hubs;

/// <summary>
/// The websocket endpoint every screen connects to (/gamehub).
///
/// Screens call:
///   Join(role, roomId, stationNumber) -> returns the current ScreenState, then receives "State" pushes.
///   Ping(clientTimeMs)                -> returns the server time, used to sync clocks.
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
                return screenStates.Create(roomId, null, MatchInRoom(roomId, Viewer.Main));

            case "kiosk":
                await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroups.Kiosk(roomId));
                return screenStates.Create(roomId, null, MatchInRoom(roomId, Viewer.Kiosk));

            case "player":
                var station = stations.Find(roomId, stationNumber ?? 0)
                    ?? throw new HubException($"Room {roomId} has no station {stationNumber}.");
                await Groups.AddToGroupAsync(Context.ConnectionId, ScreenGroups.PlayerScreen(station.Id));
                var match = host.GetSnapshotForStations([station.Id], Viewer.Player(station.Id));
                return screenStates.Create(roomId, station, match);

            default:
                throw new HubException($"Unknown screen role '{role}'.");
        }
    }

    public long Ping(long clientTimeMs) => clock.GetUtcNow().ToUnixTimeMilliseconds();

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

    private MatchSnapshot? MatchInRoom(string roomId, Viewer viewer) =>
        host.GetSnapshotForStations(stations.InRoom(roomId).Select(s => s.Id), viewer);
}

/// <summary>Whether developer helpers (keyboard buttons, /api/dev) are switched on.</summary>
public sealed record DevToolsSettings(bool Enabled);
