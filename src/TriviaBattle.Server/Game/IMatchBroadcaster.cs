using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Tells the outside world (screens, logs, room lights and sound) that a match changed.
/// MatchHost calls this while it holds its lock, so implementations must be quick:
/// read what they need from the engines, then hand off any slow work (network sends) without waiting.
/// </summary>
public interface IMatchBroadcaster
{
    /// <param name="match">The match that changed.</param>
    /// <param name="events">What happened to it.</param>
    /// <param name="runningMatches">
    /// Every match still running, on any stations (it no longer includes <paramref name="match"/>
    /// once that has finished). Several groups can play in one room at once, so anything the room
    /// shares (main screen, room speaker, room lights) looks at all of them.
    /// </param>
    void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches);
}

/// <summary>Writes match activity to the server log. Handy when there are no screens attached.</summary>
public class LoggingMatchBroadcaster(ILogger<LoggingMatchBroadcaster> logger) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case PhaseChanged p:
                    logger.LogInformation("Match {Id}: {Phase}", match.MatchId, p.Phase);
                    break;
                case AnswerGiven a:
                    logger.LogInformation("Match {Id}: {Owner} answered #{N} {Answer} ({Result}, {Points:+0;-0;0}) at {Station}",
                        match.MatchId, a.Answer.OwnerId, a.Answer.Number, ButtonMap.AnswerIndexToLetter(a.Answer.AnswerIndex),
                        a.Answer.IsCorrect ? "right" : "wrong", a.Answer.Points, a.Answer.PressedByStationId);
                    break;
                case PowerUpFound f:
                    logger.LogInformation("Match {Id}: {Owner} found a {PowerUp}", match.MatchId, f.OwnerId, f.PowerUp);
                    break;
                case PhotoAsked p:
                    logger.LogInformation("Match {Id}: {Station} said yes to a hero photo", match.MatchId, p.StationId);
                    break;
                case PhotoDeclined p:
                    logger.LogInformation("Match {Id}: {Station} said no to a hero photo", match.MatchId, p.StationId);
                    break;
                case PhotoTaken p:
                    logger.LogInformation("Match {Id}: {Station}'s hero photo arrived", match.MatchId, p.StationId);
                    break;
                case PowerUpUsed u:
                    logger.LogInformation("Match {Id}: {Owner} used a {PowerUp} on {Target} ({Amount})",
                        match.MatchId, u.OwnerId, u.Use.PowerUp, u.Use.TargetOwnerId, u.Use.Amount);
                    break;
                case LeaderboardPlacement p:
                    logger.LogInformation("Match {Id}: {Names} placed #{Rank} on the {Board} board with {Score}",
                        match.MatchId, p.Names, p.Rank, p.Board, p.Score);
                    break;
            }
        }
    }
}
