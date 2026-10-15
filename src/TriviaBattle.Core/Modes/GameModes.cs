namespace TriviaBattle.Core.Modes;

/// <summary>Creates the right <see cref="IGameMode"/> for a <see cref="GameModeKind"/>.</summary>
public static class GameModes
{
    public static IGameMode Create(GameModeKind kind) => kind switch
    {
        GameModeKind.FreeForAll => new FreeForAllMode(),
        GameModeKind.TeamSharedAnswer => new TeamSharedAnswerMode(),
        GameModeKind.TeamCombinedScore => new TeamCombinedScoreMode(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown game mode."),
    };

    public static bool UsesTeams(GameModeKind kind) => kind != GameModeKind.FreeForAll;

    /// <summary>The on-screen name of a mode, e.g. "2v2 Group".</summary>
    public static string DisplayName(GameModeKind kind) => Create(kind).DisplayName;
}
