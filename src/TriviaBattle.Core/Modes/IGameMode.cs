using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes;

public enum GameModeKind
{
    /// <summary>Everyone answers for themselves; highest score wins.</summary>
    FreeForAll,

    /// <summary>"2v2 Group": each team works through ONE shared stream of questions.</summary>
    TeamSharedAnswer,

    /// <summary>"2v2 Combined": everyone answers for themselves; team score = sum of teammates.</summary>
    TeamCombinedScore,
}

/// <summary>
/// The rules that differ between game modes. The engine handles everything else
/// (the clock, question streams, deciding which answers are correct) the same way for every mode.
///
/// To add a new mode: add a value to <see cref="GameModeKind"/>, write a class implementing
/// this interface, and add it to <see cref="GameModes.Create"/>. See docs/ARCHITECTURE.md.
/// </summary>
public interface IGameMode
{
    GameModeKind Kind { get; }

    /// <summary>Name shown on screens, e.g. "2v2 Group".</summary>
    string DisplayName { get; }

    bool UsesTeams { get; }

    /// <summary>
    /// Who a player's answers count for: their own station id (each player has their own
    /// question stream) or their team id (teammates share one stream; either can answer).
    /// </summary>
    string GetAnswerOwnerId(Player player);

    /// <summary>Adds one answer's points (negative for a penalty) to the right players and/or teams.</summary>
    void ScoreAnswer(GivenAnswer answer, Player pressedBy, MatchRoster roster);
}
