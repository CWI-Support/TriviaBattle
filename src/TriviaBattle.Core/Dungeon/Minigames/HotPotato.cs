namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The hot potato: a game for the whole party at once (the only one: <see cref="MinigameCatalog.GroupOnly"/>). A lit bomb
/// goes round the party. Whoever holds it sees a few runes (<see cref="HotPotatoSettings.PassPresses"/>, A-D) and presses
/// them in order to throw it on to the next player; a wrong one fumbles it (the buttons are dead a moment, and it's back
/// to the first rune). The fuse is a secret: somewhere between <see cref="HotPotatoSettings.FuseShortest"/> and
/// <see cref="HotPotatoSettings.FuseLongest"/>. When it runs out the bomb goes off: whoever's holding it loses (the blast
/// takes <see cref="HotPotatoSettings.BlastShare"/> of their HP, never the last of it) and everyone else wins.
///
/// Each player has their own seat (a <see cref="HotPotato"/>: the timing every mini game has), and the seats share one
/// <see cref="HotPotatoBomb"/>. <see cref="Deal"/> sets the table. The fuse is every seat's time limit, so the bomb goes
/// off as their time runs out: the holder's go is lost by the time limit, the others' are won then
/// (<see cref="Minigame.WinDue"/>). Screens never learn the fuse before it goes (see the snapshot).
/// </summary>
public sealed class HotPotato : Minigame
{
    private HotPotato(HotPotatoBomb bomb, int seat, DateTimeOffset introAt, MinigameSettings settings, double fuseSeconds)
        : base(MinigameKind.HotPotato, introAt, settings.IntroSeconds, fuseSeconds, settings.ResultSeconds)
    {
        Bomb = bomb;
        Seat = seat;
    }

    /// <summary>The party's bomb, shared by every seat.</summary>
    public HotPotatoBomb Bomb { get; }

    /// <summary>Their place round the table (0-based, in <see cref="HotPotatoBomb.Seats"/>).</summary>
    public int Seat { get; }

    /// <summary>The bomb's in their hands.</summary>
    public bool Holding => Bomb.Holder == Seat;

    /// <summary>
    /// Sets the table for a party (at least two: <paramref name="ownerIds"/>, in the order the bomb goes round): picks
    /// the fuse and who holds the bomb first, and gives each player their seat, in the same order.
    /// </summary>
    public static IReadOnlyList<HotPotato> Deal(DateTimeOffset introAt, MinigameSettings settings, Random random, IReadOnlyList<string> ownerIds)
    {
        if (ownerIds.Count < 2)
            throw new ArgumentException("The hot potato needs at least two players.", nameof(ownerIds));
        var rules = settings.HotPotato;
        var fuse = rules.FuseShortest + random.NextDouble() * Math.Max(0, rules.FuseLongest - rules.FuseShortest);
        var playsFrom = introAt + TimeSpan.FromSeconds(settings.IntroSeconds);
        var bomb = new HotPotatoBomb(ownerIds, random.Next(ownerIds.Count), playsFrom, rules, new Random(random.Next()));
        return ownerIds.Select((_, seat) => new HotPotato(bomb, seat, introAt, settings, fuse)).ToList();
    }

    /// <summary>Only the holder's buttons do anything: they throw it, rune by rune.</summary>
    protected override MinigamePress Play(int answerIndex, DateTimeOffset at) =>
        Holding ? Bomb.Press(answerIndex, at) : MinigamePress.Ignored;

    /// <summary>The bomb goes off as the time runs out: everyone not holding it has won then.</summary>
    protected override DateTimeOffset? WonAt(DateTimeOffset now) => !Holding && now >= EndsAt ? EndsAt : null;
}

/// <summary>
/// One party's bomb in the hot potato (see <see cref="HotPotato"/>): who's holding it, the runes they have to press to
/// throw it, and how far they've got. Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class HotPotatoBomb
{
    private readonly HotPotatoSettings _settings;
    private readonly Random _random;

    internal HotPotatoBomb(IReadOnlyList<string> seats, int holder, DateTimeOffset playsFrom, HotPotatoSettings settings, Random random)
    {
        Seats = seats;
        Holder = holder;
        _settings = settings;
        _random = random;
        CaughtAt = playsFrom - TimeSpan.FromSeconds(settings.CatchSeconds); // the first holder can throw it as play starts
        Sequence = NewSequence();
    }

    /// <summary>Everyone at the table (answer owners), in the order it goes round: each throw goes to the next.</summary>
    public IReadOnlyList<string> Seats { get; }

    /// <summary>Who's holding it (0-based, in <see cref="Seats"/>).</summary>
    public int Holder { get; private set; }

    public string HolderOwnerId => Seats[Holder];

    /// <summary>When the holder caught it (the first holder: a moment before play starts).</summary>
    public DateTimeOffset CaughtAt { get; private set; }

    /// <summary>Presses count from here: a moment after the catch.</summary>
    public DateTimeOffset ThrowFrom => CaughtAt + TimeSpan.FromSeconds(_settings.CatchSeconds);

    /// <summary>The runes (0-3, A-D) the holder presses, in order, to throw it. New for each catch.</summary>
    public IReadOnlyList<int> Sequence { get; private set; }

    /// <summary>How many of <see cref="Sequence"/> the holder has pressed so far.</summary>
    public int Entered { get; private set; }

    /// <summary>Throws so far (it changes with each, so screens can fly it once).</summary>
    public int Passes { get; private set; }

    /// <summary>After a fumble, the holder's buttons are dead until then.</summary>
    public DateTimeOffset? FumbledUntil { get; private set; }

    /// <summary>Fumbles so far (it changes with each, so screens can flash each once).</summary>
    public int Fumbles { get; private set; }

    /// <summary>When the bomb went off: who it went off on and the HP it took (null before then).</summary>
    public (string OwnerId, int Damage)? Blast { get; private set; }

    /// <summary>The holder presses a rune at <paramref name="at"/>: the right next one (the last throws it), or a fumble.</summary>
    internal MinigamePress Press(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or > 3 || at < ThrowFrom || at < FumbledUntil)
            return MinigamePress.Ignored; // the action button, still catching it, or fumbled a moment ago
        if (answerIndex != Sequence[Entered])
        {
            Entered = 0;
            Fumbles++;
            FumbledUntil = at + TimeSpan.FromSeconds(_settings.FumbleSeconds);
            return MinigamePress.Miss;
        }
        if (++Entered < Sequence.Count)
            return MinigamePress.Nudge;

        // Thrown: the next one round the table catches it, with runes of their own.
        Holder = (Holder + 1) % Seats.Count;
        CaughtAt = at;
        Entered = 0;
        Passes++;
        Sequence = NewSequence();
        return MinigamePress.Hit;
    }

    /// <summary>It went off in <paramref name="ownerId"/>'s hands, taking <paramref name="damage"/> HP (DungeonRun works it out).</summary>
    public void WentOff(string ownerId, int damage) => Blast = (ownerId, damage);

    /// <summary>A few runes to press, no rune twice in a row (so every press reads as a new one).</summary>
    private List<int> NewSequence()
    {
        var runes = new List<int>();
        for (var i = 0; i < Math.Max(1, _settings.PassPresses); i++)
        {
            int rune;
            do rune = _random.Next(4);
            while (runes.Count > 0 && rune == runes[^1]);
            runes.Add(rune);
        }
        return runes;
    }
}
