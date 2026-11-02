using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Output;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Decides what the button LEDs and room lights should do for the current match state, and
/// tells every <see cref="IRoomOutput"/> (console in dev, the PLC in the venue).
///
/// LED rules for each seated station's four buttons:
/// <list type="bullet">
///   <item>Playing: all four on ("press now"), except off during a wrong-answer lockout
///         (for the whole team in 2v2 Group).</item>
///   <item>Results: the winners' buttons blink.</item>
///   <item>Anything else, or match over: off.</item>
/// </list>
/// Several groups can play in one room at once. LEDs belong to each station's own match; the
/// room's lighting cue follows the busiest match in the room: Playing, then Results, then Intro, else Idle.
/// </summary>
public class RoomOutputCoordinator(
    IEnumerable<IRoomOutput> outputs,
    ButtonMap buttonMap,
    StationLayout stations) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        if (events.All(e => e is LeaderboardPlacement))
            return; // a new high score doesn't change the lights

        var finished = events.Any(e => e is MatchFinished);
        var leds = new Dictionary<int, LedState>();

        foreach (var player in match.Roster.Players)
        {
            for (var answer = 0; answer < 4; answer++)
            {
                if (buttonMap.FindButtonId(player.StationId, answer) is int buttonId)
                    leds[buttonId] = finished ? LedState.Off : LedFor(match, player);
            }
        }

        var roomIds = match.Roster.Players.Select(p => stations.Find(p.StationId)!.RoomId).Distinct().ToList();

        foreach (var output in outputs)
        {
            output.SetButtonLeds(leds);
            foreach (var roomId in roomIds)
                output.SetLightingCue(roomId, RoomCue(roomId, runningMatches));
        }
    }

    /// <summary>The cue for the busiest match still running in the room (Idle if there are none).</summary>
    private LightingCue RoomCue(string roomId, IReadOnlyList<MatchEngine> runningMatches)
    {
        var phases = runningMatches
            .Where(m => !m.IsFinished && m.Roster.Players.Any(p => stations.Find(p.StationId)?.RoomId == roomId))
            .Select(m => m.Phase)
            .ToList();

        if (phases.Contains(MatchPhase.Playing) || phases.Contains(MatchPhase.Continue)) return LightingCue.Question; // "Continue?" is still the game
        if (phases.Contains(MatchPhase.Results)) return LightingCue.Results;
        if (phases.Contains(MatchPhase.Intro)) return LightingCue.Intro;
        return LightingCue.Idle;
    }

    private static LedState LedFor(MatchEngine match, Player player)
    {
        switch (match.Phase)
        {
            case MatchPhase.Playing:
                // LockoutEnded (from the engine's tick) triggers a fresh update when the lockout runs out.
                return match.StreamFor(player).LockedOutUntil == null ? LedState.On : LedState.Off;

            case MatchPhase.Results:
                var standingId = match.Mode.UsesTeams ? player.TeamId : player.StationId;
                var isWinner = match.GetStandings().Any(s => s.Rank == 1 && s.Id == standingId);
                return isWinner ? LedState.Blink : LedState.Off;

            default:
                return LedState.Off;
        }
    }
}
