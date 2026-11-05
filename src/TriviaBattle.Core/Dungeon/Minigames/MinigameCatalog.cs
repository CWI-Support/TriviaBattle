namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// Every mini game, and making one. A floor that gets a mini game (config/dungeon.json's <c>MinigameChance</c>, at most
/// one per floor, see <see cref="DungeonMap.MinigameOn"/>) picks one of <see cref="All"/>, each as likely as the others.
/// </summary>
public static class MinigameCatalog
{
    /// <summary>Every mini game there is.</summary>
    public static IReadOnlyList<MinigameKind> All { get; } = Enum.GetValues<MinigameKind>();

    /// <summary>One of them, each as likely as the others.</summary>
    public static MinigameKind Pick(Random random) => All[random.Next(All.Count)];

    /// <summary>An adventurer's go at one, its intro starting at <paramref name="introAt"/>.</summary>
    public static Minigame Create(MinigameKind kind, DateTimeOffset introAt, MinigameSettings settings, Random random) => kind switch
    {
        MinigameKind.RuneLock => new RuneLock(introAt, settings, random),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A mini game needs a case here."),
    };
}
