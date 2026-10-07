using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Modes.LockIn;

/// <summary>The first press for an owner locks the answer; every later press is ignored.</summary>
public sealed class FirstPressLocksRule : ILockInRule
{
    public LockedAnswer? TryLock(Round round, string ownerId, IReadOnlyList<string> stationsSharingAnswer, ButtonPress press)
    {
        if (round.Answers.ContainsKey(ownerId))
            return null;

        return new LockedAnswer(ownerId, press.StationId, press.AnswerIndex, press.PressedAt, press.Sequence);
    }
}
