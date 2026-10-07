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
///   <item>Question open, not answered yet: all four on ("press now").</item>
///   <item>Answer locked in: only the chosen button stays on (for the whole team in 2v2 Group).</item>
///   <item>Reveal: the correct button blinks, the rest go off.</item>
///   <item>Results: the winners' buttons blink.</item>
///   <item>Anything else, or match over: off.</item>
/// </list>
/// </summary>
public class RoomOutputCoordinator(
    IEnumerable<IRoomOutput> outputs,
    ButtonMap buttonMap,
    StationLayout stations) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events)
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
                    leds[buttonId] = finished ? LedState.Off : LedFor(match, player, answer);
            }
        }

        var cue = finished ? LightingCue.Idle : CueFor(match.Phase);
        var roomIds = match.Roster.Players.Select(p => stations.Find(p.StationId)!.RoomId).Distinct().ToList();

        foreach (var output in outputs)
        {
            output.SetButtonLeds(leds);
            foreach (var roomId in roomIds)
                output.SetLightingCue(roomId, cue);
        }
    }

    private static LedState LedFor(MatchEngine match, Player player, int answerIndex)
    {
        var round = match.CurrentRound;
        var lockedAnswer = round?.Answers.GetValueOrDefault(match.Mode.GetAnswerOwnerId(player));

        switch (match.Phase)
        {
            case MatchPhase.QuestionOpen:
                if (lockedAnswer == null) return LedState.On;
                return lockedAnswer.AnswerIndex == answerIndex ? LedState.On : LedState.Off;

            case MatchPhase.Reveal:
                return round!.Question.CorrectIndex == answerIndex ? LedState.Blink : LedState.Off;

            case MatchPhase.Results:
                var standingId = match.Mode.UsesTeams ? player.TeamId : player.StationId;
                var isWinner = match.GetStandings().Any(s => s.Rank == 1 && s.Id == standingId);
                return isWinner ? LedState.Blink : LedState.Off;

            default:
                return LedState.Off;
        }
    }

    private static LightingCue CueFor(MatchPhase phase) => phase switch
    {
        MatchPhase.Intro => LightingCue.Intro,
        MatchPhase.QuestionLeadIn or MatchPhase.QuestionOpen => LightingCue.Question,
        MatchPhase.Reveal => LightingCue.Reveal,
        MatchPhase.Standings => LightingCue.Standings,
        MatchPhase.Results => LightingCue.Results,
        _ => LightingCue.Idle,
    };
}
