namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// Every mini game, and making one. A floor that gets a mini game (config/dungeon.json's <c>MinigameChance</c>, at most
/// one per floor, see <see cref="DungeonMap.MinigameOn"/>) picks one of <see cref="All"/>, each as likely as the others,
/// except that the match's first is never one of <see cref="NeverFirst"/>.
/// </summary>
public static class MinigameCatalog
{
    /// <summary>Every mini game there is.</summary>
    public static IReadOnlyList<MinigameKind> All { get; } = Enum.GetValues<MinigameKind>();

    /// <summary>
    /// The ones that can't be a match's first mini game: the pop quiz asks about the run so far, so it waits until
    /// there's been more of one.
    /// </summary>
    public static IReadOnlyList<MinigameKind> NeverFirst { get; } = [MinigameKind.PopQuiz];

    private static readonly IReadOnlyList<MinigameKind> CanBeFirst = All.Except(NeverFirst).ToList();

    /// <summary>One of them, each as likely as the others (<paramref name="first"/>: the match's first, so not one of <see cref="NeverFirst"/>).</summary>
    public static MinigameKind Pick(Random random, bool first = false)
    {
        var choices = first ? CanBeFirst : All;
        return choices[random.Next(choices.Count)];
    }

    /// <summary>
    /// An adventurer's go at one, its intro starting at <paramref name="introAt"/>. The pop quiz needs
    /// <paramref name="facts"/>: what it can ask them about.
    /// </summary>
    public static Minigame Create(MinigameKind kind, DateTimeOffset introAt, MinigameSettings settings, Random random, PopQuizFacts? facts = null) => kind switch
    {
        MinigameKind.RuneLock => new RuneLock(introAt, settings, random),
        MinigameKind.RuneEcho => new RuneEcho(introAt, settings, random),
        MinigameKind.ChestShuffle => new ChestShuffle(introAt, settings, random),
        MinigameKind.RingPuzzle => new RingPuzzle(introAt, settings, random),
        MinigameKind.Tuner => new Tuner(introAt, settings, random),
        MinigameKind.PopQuiz => new PopQuiz(introAt, settings, random, facts ?? throw new ArgumentNullException(nameof(facts), "The pop quiz needs something to ask about.")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A mini game needs a case here."),
    };
}
