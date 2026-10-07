using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes.LockIn;

namespace TriviaBattle.Core.Modes;

/// <summary>
/// 2v2 Group: each team submits ONE answer per question. How that answer gets locked in
/// (e.g. first teammate to press) is decided by the pluggable <see cref="ILockInRule"/>.
/// The team scores as a unit.
/// </summary>
public sealed class TeamSharedAnswerMode(ILockInRule lockInRule) : IGameMode
{
    public GameModeKind Kind => GameModeKind.TeamSharedAnswer;
    public string DisplayName => "2v2 Group";
    public bool UsesTeams => true;
    public ILockInRule LockInRule { get; } = lockInRule;

    public string GetAnswerOwnerId(Player player) =>
        player.TeamId ?? throw new InvalidOperationException($"Player at {player.StationId} has no team.");

    public void AwardCorrectAnswer(RoundResult result, Player pressedBy, MatchRoster roster)
    {
        var responseTime = result.ResponseTime ?? TimeSpan.Zero;

        // The team's score is what counts.
        roster.FindTeam(result.OwnerId)!.Tally.AddCorrectAnswer(result.Points, responseTime);

        // Also credit whoever pressed, so results can show each teammate's contribution.
        pressedBy.Tally.AddCorrectAnswer(result.Points, responseTime);
    }
}
