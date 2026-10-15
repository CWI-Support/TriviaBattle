using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes;

/// <summary>
/// 2v2 Group: each team shares ONE stream of questions, shown on both teammates' screens.
/// Whichever teammate presses first answers for the team, and the next question appears for both.
/// The team scores as a unit.
/// </summary>
public sealed class TeamSharedAnswerMode : IGameMode
{
    public GameModeKind Kind => GameModeKind.TeamSharedAnswer;
    public string DisplayName => "2v2 Group";
    public bool UsesTeams => true;

    public string GetAnswerOwnerId(Player player) =>
        player.TeamId ?? throw new InvalidOperationException($"Player at {player.StationId} has no team.");

    public void ScoreAnswer(GivenAnswer answer, Player pressedBy, MatchRoster roster)
    {
        // The team's score is what counts.
        roster.FindTeam(answer.OwnerId)!.Tally.Add(answer);

        // Also credit whoever pressed, so results can show each teammate's contribution.
        pressedBy.Tally.Add(answer);
    }
}
