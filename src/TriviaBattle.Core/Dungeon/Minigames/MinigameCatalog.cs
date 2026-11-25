using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// Every mini game, and making one. A floor that gets a mini game (config/dungeon.json's <c>MinigameChance</c>, at most
/// one per floor, see <see cref="DungeonMap.MinigameOn"/>) picks one of <see cref="Rolled"/>, each as likely as the others,
/// except that the match's first is never one of <see cref="NeverFirst"/>. A game for a group (<see cref="GroupOnly"/>)
/// that comes up for a party with fewer than two to play it gets a stand-in (<see cref="DungeonMap.MinigameFor"/>).
/// The special rooms (<see cref="Rooms"/>) are never rolled: they're behind the doors at a fork (<see cref="Fork"/>).
/// </summary>
public static class MinigameCatalog
{
    /// <summary>Every mini game there is, the special rooms included.</summary>
    public static IReadOnlyList<MinigameKind> All { get; } = Enum.GetValues<MinigameKind>();

    /// <summary>
    /// The special rooms: only ever behind a door at a fork before a boss (<see cref="Fork"/>), never rolled between two
    /// rooms. They ask trivia questions from the player's own stream (<see cref="CreateRoom"/>).
    /// </summary>
    public static IReadOnlyList<MinigameKind> Rooms { get; } = [MinigameKind.Treasure, MinigameKind.Trap, MinigameKind.HighStakes];

    /// <summary>The ones a floor can roll to play between two of its rooms: all but the special rooms.</summary>
    public static IReadOnlyList<MinigameKind> Rolled { get; } = All.Except(Rooms).ToList();

    /// <summary>
    /// The ones that can't be a match's first mini game: the pop quiz asks about the run so far, so it waits until
    /// there's been more of one.
    /// </summary>
    public static IReadOnlyList<MinigameKind> NeverFirst { get; } = [MinigameKind.PopQuiz];

    /// <summary>The ones the whole party plays together, so they need at least two: the hot potato.</summary>
    public static IReadOnlyList<MinigameKind> GroupOnly { get; } = [MinigameKind.HotPotato];

    private static readonly IReadOnlyList<MinigameKind> CanBeFirst = Rolled.Except(NeverFirst).ToList();

    /// <summary>The ones anyone can play at any point: what stands in for a group's game.</summary>
    private static readonly IReadOnlyList<MinigameKind> StandIns = Rolled.Except(NeverFirst).Except(GroupOnly).ToList();

    /// <summary>One to roll between two rooms, each as likely as the others (<paramref name="first"/>: the match's first, so not one of <see cref="NeverFirst"/>).</summary>
    public static MinigameKind Pick(Random random, bool first = false)
    {
        var choices = first ? CanBeFirst : Rolled;
        return choices[random.Next(choices.Count)];
    }

    /// <summary>One anyone can play, at any point in a match, in place of a group's game for a party that can't play it.</summary>
    public static MinigameKind StandIn(Random random) => StandIns[random.Next(StandIns.Count)];

    /// <summary>
    /// An adventurer's go at one, its intro starting at <paramref name="introAt"/>. The pop quiz needs
    /// <paramref name="facts"/>: what it can ask them about. Not a group's game: <see cref="HotPotato.Deal"/> sets the
    /// whole party's places at the hot potato. Not a special room: <see cref="CreateRoom"/> gives it its questions.
    /// </summary>
    public static Minigame Create(MinigameKind kind, DateTimeOffset introAt, MinigameSettings settings, Random random, PopQuizFacts? facts = null) => kind switch
    {
        MinigameKind.RuneLock => new RuneLock(introAt, settings, random),
        MinigameKind.RuneEcho => new RuneEcho(introAt, settings, random),
        MinigameKind.ChestShuffle => new ChestShuffle(introAt, settings, random),
        MinigameKind.RingPuzzle => new RingPuzzle(introAt, settings, random),
        MinigameKind.Tuner => new Tuner(introAt, settings, random),
        MinigameKind.PopQuiz => new PopQuiz(introAt, settings, random, facts ?? throw new ArgumentNullException(nameof(facts), "The pop quiz needs something to ask about.")),
        MinigameKind.HotPotato => throw new ArgumentException("The hot potato is the whole party's: HotPotato.Deal sets it up.", nameof(kind)),
        MinigameKind.Treasure or MinigameKind.Trap or MinigameKind.HighStakes => throw new ArgumentException("A special room asks questions: CreateRoom gives it them.", nameof(kind)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A mini game needs a case here."),
    };

    /// <summary>
    /// How hard a special room's questions are, and how many it takes from the player's stream. High Stakes asks its own
    /// bank's (<see cref="HighStakesQuestions"/>): this is what it asks with none, a hard one.
    /// </summary>
    public static (Difficulty Difficulty, int Count) RoomQuestions(MinigameKind room, MinigameSettings settings) => room switch
    {
        MinigameKind.Treasure => (settings.Treasure.Difficulty, TreasureRoom.QuestionsNeeded(settings.Treasure)),
        MinigameKind.Trap => (settings.Trap.Difficulty, TrapRoom.QuestionsNeeded(settings.Trap)),
        MinigameKind.HighStakes => (Difficulty.Hard, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(room), room, "Not a special room."),
    };

    /// <summary>
    /// An adventurer's go at a special room (one of <see cref="Rooms"/>), its intro starting at <paramref name="introAt"/>,
    /// with the questions it asks (<see cref="RoomQuestions"/> says how many, and how hard).
    /// </summary>
    public static Minigame CreateRoom(MinigameKind room, DateTimeOffset introAt, MinigameSettings settings, Random random, IReadOnlyList<Question> questions) => room switch
    {
        MinigameKind.Treasure => new TreasureRoom(introAt, settings, questions),
        MinigameKind.Trap => new TrapRoom(introAt, settings, random, questions),
        MinigameKind.HighStakes => new HighStakesRoom(introAt, settings, questions.Count > 0 ? questions[0] : throw new ArgumentException("High Stakes needs a question.", nameof(questions))),
        _ => throw new ArgumentOutOfRangeException(nameof(room), room, "Not a special room."),
    };
}
