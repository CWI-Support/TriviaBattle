using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes.LockIn;

namespace TriviaBattle.Core.Modes;

/// <summary>
/// 2v2 Combined: every player answers for themselves and earns their own points.
/// A team's score is the sum of its players' scores.
/// </summary>
public sealed class TeamCombinedScoreMode : IGameMode
{
    public GameModeKind Kind => GameModeKind.TeamCombinedScore;
    public string DisplayName => "2v2 Combined";
    public bool UsesTeams => true;

    // Each player answers individually, so first press is final, same as free-for-all.
    public ILockInRule LockInRule { get; } = new FirstPressLocksRule();

    public string GetAnswerOwnerId(Player player) => player.StationId;

    public void AwardCorrectAnswer(RoundResult result, Player pressedBy, MatchRoster roster)
    {
        var responseTime = result.ResponseTime ?? TimeSpan.Zero;

        pressedBy.Tally.AddCorrectAnswer(result.Points, responseTime);
        roster.TeamOf(pressedBy)!.Tally.AddCorrectAnswer(result.Points, responseTime);
    }
}
