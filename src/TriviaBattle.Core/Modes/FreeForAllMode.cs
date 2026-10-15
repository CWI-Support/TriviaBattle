using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes;

/// <summary>Free-for-all: every player races through their own questions and keeps their own score.</summary>
public sealed class FreeForAllMode : IGameMode
{
    public GameModeKind Kind => GameModeKind.FreeForAll;
    public string DisplayName => "Free-for-All";
    public bool UsesTeams => false;

    public string GetAnswerOwnerId(Player player) => player.StationId;

    public void ScoreAnswer(GivenAnswer answer, Player pressedBy, MatchRoster roster) => pressedBy.Tally.Add(answer);
}
