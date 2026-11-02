using Microsoft.AspNetCore.SignalR;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Hubs;

/// <summary>
/// Pushes a room's lobby changes to its screens: every station screen and the main screen and kiosk get a
/// fresh <see cref="ScreenState"/> (with the lobby, and any match they're showing, as they'd get on joining).
/// </summary>
public class LobbyBroadcaster(
    IHubContext<GameHub> hub,
    StationLayout stations,
    ScreenStateFactory screenStates,
    MatchHost host,
    ILogger<LobbyBroadcaster> logger)
{
    public void LobbyChanged(string roomId)
    {
        var roomStations = stations.InRoom(roomId);
        var ids = roomStations.Select(s => s.Id).ToList();

        Send(ScreenGroups.MainScreen(roomId), screenStates.ForRoom(roomId, host.GetSnapshotsForStations(ids, Viewer.Main)));
        Send(ScreenGroups.Kiosk(roomId), screenStates.ForRoom(roomId, host.GetSnapshotsForStations(ids, Viewer.Kiosk)));
        foreach (var station in roomStations)
        {
            var match = host.GetSnapshotForStations([station.Id], Viewer.Player(station.Id));
            Send(ScreenGroups.PlayerScreen(station.Id), screenStates.ForStation(roomId, station, match));
        }
    }

    /// <summary>Starts the send without waiting for it; a dropped screen is only logged.</summary>
    private void Send(string group, ScreenState state) =>
        hub.Clients.Group(group).SendAsync("State", state)
            .ContinueWith(t => logger.LogWarning(t.Exception, "Send to {Group} failed", group), TaskContinuationOptions.OnlyOnFaulted);
}
