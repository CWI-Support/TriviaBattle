using Microsoft.AspNetCore.SignalR;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Hubs;

/// <summary>
/// Pushes match changes to screens. Every screen involved gets a fresh, personalised
/// <see cref="ScreenState"/> (main screen, kiosk, and one per player station), plus any
/// <see cref="ScreenEvent"/>s for animations.
/// </summary>
public class SignalRMatchBroadcaster(
    IHubContext<GameHub> hub,
    StationLayout stations,
    ScreenStateFactory screenStates,
    ILogger<SignalRMatchBroadcaster> logger) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events)
    {
        // When a match ends, screens go back to the lobby (Match = null).
        var finished = events.Any(e => e is MatchFinished);
        var screenEvents = events.Select(ToScreenEvent).ToList();
        MatchSnapshot? SnapshotFor(Viewer viewer) => finished ? null : SnapshotBuilder.Build(match, viewer);

        var stationIds = match.Roster.Players.Select(p => p.StationId).ToList();
        var roomIds = stationIds.Select(id => stations.Find(id)!.RoomId).Distinct();

        foreach (var roomId in roomIds)
        {
            Send(ScreenGroups.MainScreen(roomId), screenStates.Create(roomId, null, SnapshotFor(Viewer.Main)), screenEvents);
            Send(ScreenGroups.Kiosk(roomId), screenStates.Create(roomId, null, SnapshotFor(Viewer.Kiosk)), []);
        }

        foreach (var stationId in stationIds)
        {
            var station = stations.Find(stationId)!;
            Send(ScreenGroups.PlayerScreen(stationId), screenStates.Create(station.RoomId, station, SnapshotFor(Viewer.Player(stationId))), screenEvents);
        }
    }

    private static ScreenEvent ToScreenEvent(MatchEvent e) => e switch
    {
        PhaseChanged p => new ScreenEvent("PhaseChanged", Phase: p.Phase.ToString()),
        AnswerLocked a => new ScreenEvent("AnswerLocked", OwnerId: a.Answer.OwnerId, StationId: a.Answer.PressedByStationId),
        RoundRevealed => new ScreenEvent("RoundRevealed"),
        MatchFinished => new ScreenEvent("MatchFinished"),
        LeaderboardPlacement p => new ScreenEvent("LeaderboardPlacement", OwnerId: p.OwnerId, Placement: p),
        _ => new ScreenEvent(e.GetType().Name),
    };

    /// <summary>
    /// Starts the sends without waiting for them (we're inside MatchHost's lock).
    /// Failures are logged, never thrown: a dropped screen must not affect the game.
    /// </summary>
    private void Send(string group, ScreenState state, IReadOnlyList<ScreenEvent> screenEvents)
    {
        var client = hub.Clients.Group(group);
        LogIfFails(client.SendAsync("State", state), group);
        foreach (var e in screenEvents)
            LogIfFails(client.SendAsync("Event", e), group);
    }

    private void LogIfFails(Task send, string group) =>
        send.ContinueWith(t => logger.LogWarning(t.Exception, "Send to {Group} failed", group), TaskContinuationOptions.OnlyOnFaulted);
}
