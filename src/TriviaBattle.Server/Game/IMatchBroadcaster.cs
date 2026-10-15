using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Tells the outside world (screens, logs, later the room lights) that a match changed.
/// MatchHost calls this while it holds its lock, so implementations must be quick:
/// read what they need from the engine, then hand off any slow work (network sends) without waiting.
/// </summary>
public interface IMatchBroadcaster
{
    void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events);
}

/// <summary>Writes match activity to the server log. Handy when there are no screens attached.</summary>
public class LoggingMatchBroadcaster(ILogger<LoggingMatchBroadcaster> logger) : IMatchBroadcaster
{
    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events)
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
                case LeaderboardPlacement p:
                    logger.LogInformation("Match {Id}: {Names} placed #{Rank} on the {Board} board with {Score}",
                        match.MatchId, p.Names, p.Rank, p.Board, p.Score);
                    break;
            }
        }
    }
}
