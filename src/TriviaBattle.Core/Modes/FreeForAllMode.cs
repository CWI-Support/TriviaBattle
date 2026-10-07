using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes.LockIn;

namespace TriviaBattle.Core.Modes;

/// <summary>Free-for-all: every player answers for themselves and keeps their own score.</summary>
public sealed class FreeForAllMode : IGameMode
{
    public GameModeKind Kind => GameModeKind.FreeForAll;
    public string DisplayName => "Free-for-All";
    public bool UsesTeams => false;

    // Individual answers are always final on the first press.
    public ILockInRule LockInRule { get; } = new FirstPressLocksRule();

    public string GetAnswerOwnerId(Player player) => player.StationId;

    public void AwardCorrectAnswer(RoundResult result, Player pressedBy, MatchRoster roster)
    {
        pressedBy.Tally.AddCorrectAnswer(result.Points, result.ResponseTime ?? TimeSpan.Zero);
    }
}
