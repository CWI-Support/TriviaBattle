using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes.LockIn;

namespace TriviaBattle.Core.Modes;

public enum GameModeKind
{
    /// <summary>Everyone answers for themselves; highest score wins.</summary>
    FreeForAll,

    /// <summary>"2v2 Group": each team gives ONE answer per question.</summary>
    TeamSharedAnswer,

    /// <summary>"2v2 Combined": everyone answers for themselves; team score = sum of teammates.</summary>
    TeamCombinedScore,
}

/// <summary>
/// The rules that differ between game modes. The engine handles everything else
/// (timing, phases, deciding which answers are correct) the same way for every mode.
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

    /// <summary>Decides when a button press locks in an answer.</summary>
    ILockInRule LockInRule { get; }

    /// <summary>
    /// Who a player's answer counts for: their own station id (individual answers)
    /// or their team id (one shared answer per team).
    /// </summary>
    string GetAnswerOwnerId(Player player);

    /// <summary>
    /// Adds the points for one correct answer to the right players and/or teams.
    /// Called once per correct answer at Reveal (Points may be 0 under some scoring rules).
    /// </summary>
    void AwardCorrectAnswer(RoundResult result, Player pressedBy, MatchRoster roster);
}
