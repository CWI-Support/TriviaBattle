using TriviaBattle.Core.Modes.LockIn;

namespace TriviaBattle.Core.Modes;

/// <summary>Creates the right <see cref="IGameMode"/> for a <see cref="GameModeKind"/>.</summary>
public static class GameModes
{
    public static IGameMode Create(GameModeKind kind, LockInRuleKind teamLockIn) => kind switch
    {
        GameModeKind.FreeForAll => new FreeForAllMode(),
        GameModeKind.TeamSharedAnswer => new TeamSharedAnswerMode(CreateLockInRule(teamLockIn)),
        GameModeKind.TeamCombinedScore => new TeamCombinedScoreMode(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown game mode."),
    };

    public static bool UsesTeams(GameModeKind kind) => kind != GameModeKind.FreeForAll;

    /// <summary>The on-screen name of a mode, e.g. "2v2 Group".</summary>
    public static string DisplayName(GameModeKind kind) => Create(kind, LockInRuleKind.FirstPress).DisplayName;

    private static ILockInRule CreateLockInRule(LockInRuleKind kind) => kind switch
    {
        LockInRuleKind.FirstPress => new FirstPressLocksRule(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown lock-in rule."),
    };
}
