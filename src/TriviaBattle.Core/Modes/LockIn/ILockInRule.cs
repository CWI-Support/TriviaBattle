using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes.LockIn;

public enum LockInRuleKind
{
    /// <summary>The first button press locks the answer in. No changing it afterwards.</summary>
    FirstPress,

    // Future idea: Unanimous, where every teammate must press the same answer before it locks.
    // That rule would remember each station's latest press on the Round and lock once
    // all of `stationsSharingAnswer` agree. No engine changes are needed to add it.
}

/// <summary>
/// Decides whether a button press locks in an answer. Kept separate from the game modes
/// so 2v2 Group can switch rules (e.g. "both teammates must agree") via config.
/// </summary>
public interface ILockInRule
{
    /// <param name="round">The current question.</param>
    /// <param name="ownerId">Who the answer counts for (station id or team id).</param>
    /// <param name="stationsSharingAnswer">All stations that answer as this owner (1 in individual modes).</param>
    /// <param name="press">The press being considered.</param>
    /// <returns>The answer to lock in, or null if this press should be ignored.</returns>
    LockedAnswer? TryLock(Round round, string ownerId, IReadOnlyList<string> stationsSharingAnswer, ButtonPress press);
}
