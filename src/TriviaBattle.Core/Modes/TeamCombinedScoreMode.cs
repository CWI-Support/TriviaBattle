using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes;

/// <summary>
/// 2v2 Combined: every player races through their own questions and earns their own points.
/// A team's score is the sum of its players' scores.
/// </summary>
public sealed class TeamCombinedScoreMode : IGameMode
{
    public GameModeKind Kind => GameModeKind.TeamCombinedScore;
    public string DisplayName => "2v2 Combined";
    public bool UsesTeams => true;

    public string GetAnswerOwnerId(Player player) => player.StationId;

    public void ScoreAnswer(GivenAnswer answer, Player pressedBy, MatchRoster roster)
    {
        pressedBy.Tally.Add(answer);
        roster.TeamOf(pressedBy)!.Tally.Add(answer);
    }
}
