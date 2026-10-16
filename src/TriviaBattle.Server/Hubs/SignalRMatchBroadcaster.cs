using Microsoft.AspNetCore.SignalR;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Hubs;

/// <summary>
/// Pushes match changes to screens. Every screen involved gets a fresh, personalised
/// <see cref="ScreenState"/>, plus any <see cref="ScreenEvent"/>s for animations:
/// <list type="bullet">
///   <item>The main screen and kiosk of each room the match is in get every match running in
///         that room (several groups can play at once).</item>
///   <item>Each of the match's player screens gets that match (or none, once it has finished).</item>
/// </list>
/// </summary>
public class SignalRMatchBroadcaster(
    IHubContext<GameHub> hub,
    StationLayout stations,
    ScreenStateFactory screenStates,
    ILogger<SignalRMatchBroadcaster> logger) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        // When a match ends, its screens go back to the lobby (no match).
        var finished = events.Any(e => e is MatchFinished);
        var screenEvents = events.Select(e => ToScreenEvent(e, match.MatchId)).ToList();

        var stationIds = match.Roster.Players.Select(p => p.StationId).ToList();
        var roomIds = stationIds.Select(id => stations.Find(id)!.RoomId).Distinct();

        foreach (var roomId in roomIds)
        {
            var roomStations = stations.InRoom(roomId).Select(s => s.Id).ToList();
            var inRoom = RoomMatches(runningMatches, roomStations, except: finished ? match : null);

            Send(ScreenGroups.MainScreen(roomId), screenStates.ForRoom(roomId, Snapshots(inRoom, Viewer.Main)), screenEvents);
            Send(ScreenGroups.Kiosk(roomId), screenStates.ForRoom(roomId, Snapshots(inRoom, Viewer.Kiosk)), []);
        }

        foreach (var stationId in stationIds)
        {
            var station = stations.Find(stationId)!;
            var snapshot = finished ? null : SnapshotBuilder.Build(match, Viewer.Player(stationId));
            Send(ScreenGroups.PlayerScreen(stationId), screenStates.ForStation(station.RoomId, station, snapshot), screenEvents);
        }
    }

    /// <summary>The running matches with a player in this room, ordered by their first station.</summary>
    private static List<MatchEngine> RoomMatches(IReadOnlyList<MatchEngine> running, List<string> roomStations, MatchEngine? except) =>
        running
            .Where(m => m != except && !m.IsFinished && m.Roster.Players.Any(p => roomStations.Contains(p.StationId)))
            .OrderBy(m => m.Roster.Players.Select(p => roomStations.IndexOf(p.StationId)).Where(i => i >= 0).Min())
            .ToList();

    private static IReadOnlyList<MatchSnapshot> Snapshots(List<MatchEngine> matches, Viewer viewer) =>
        matches.Select(m => SnapshotBuilder.Build(m, viewer)).ToList();

    private static ScreenEvent ToScreenEvent(MatchEvent e, string matchId) => e switch
    {
        PhaseChanged p => new ScreenEvent("PhaseChanged", matchId, Phase: p.Phase.ToString()),
        AnswerGiven a => new ScreenEvent("AnswerGiven", matchId, OwnerId: a.Answer.OwnerId, StationId: a.Answer.PressedByStationId),
        LockoutEnded l => new ScreenEvent("LockoutEnded", matchId, OwnerId: l.OwnerId),
        MatchFinished => new ScreenEvent("MatchFinished", matchId),
        LeaderboardPlacement p => new ScreenEvent("LeaderboardPlacement", matchId, OwnerId: p.OwnerId, Placement: p),
        _ => new ScreenEvent(e.GetType().Name, matchId),
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
