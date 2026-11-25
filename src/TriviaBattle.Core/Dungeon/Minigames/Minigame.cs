namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The mini games a floor can have between two of its rooms (see <see cref="MinigameCatalog"/>). Each is a class of its
/// own (one file each); add a value here and a case in <see cref="MinigameCatalog.Create"/> for a new one.
/// </summary>
public enum MinigameKind
{
    /// <summary>A needle spins round a lock: stop it on the glowing mark, several times in a row (<see cref="RuneLock"/>).</summary>
    RuneLock,

    /// <summary>The runes light up in a sequence; press them back in order, one longer each round (<see cref="RuneEcho"/>, a "Simon says").</summary>
    RuneEcho,

    /// <summary>A coin goes into one of five chests, they're shuffled; pick the right one (<see cref="ChestShuffle"/>, a shell game).</summary>
    ChestShuffle,

    /// <summary>A picture cut into rings, each turned out of line; turn them back to put it together (<see cref="RingPuzzle"/>).</summary>
    RingPuzzle,

    /// <summary>Four sinking bars, one per answer button: press them up into their zones, all four at once (<see cref="Tuner"/>).</summary>
    Tuner,

    /// <summary>A quiz about their own run (their floor, the last monster, their name...): 3 right wins (<see cref="PopQuiz"/>). Never the first.</summary>
    PopQuiz,

    /// <summary>A lit bomb goes round the party; whoever's holding it when it goes off takes the blast (<see cref="HotPotato"/>). Groups only.</summary>
    HotPotato,

    // The special rooms (MinigameCatalog.Rooms): only ever behind a door at a fork before a boss (see Fork), never
    // rolled between two rooms. They ask trivia questions, taken from the player's own question stream.

    /// <summary>A treasure room: a lightning round of easy questions; enough right in time opens the chest, a power-up (<see cref="TreasureRoom"/>).</summary>
    Treasure,

    /// <summary>A trap room: a true/false sprint; each miss hurts, and all of them right is a full heal (<see cref="TrapRoom"/>).</summary>
    Trap,

    /// <summary>High Stakes: bet HP on one brutal question (right, a reward pick; wrong, it hurts) or pass (<see cref="HighStakesRoom"/>).</summary>
    HighStakes,
}

/// <summary>What a press did in a mini game.</summary>
public enum MinigamePress
{
    /// <summary>Too early (the intro), too late (it's over), or the buttons are locked out: nothing happened.</summary>
    Ignored,
    Hit,
    Miss,

    /// <summary>The hit that won it.</summary>
    Won,

    /// <summary>A hit that won a round, with more to play (the ring puzzle: a picture put back together).</summary>
    Cleared,

    /// <summary>A press that lost it there and then (the wrong chest).</summary>
    Lost,

    /// <summary>A press that only nudges something along (a tuner bar), many a second: no sound of its own.</summary>
    Nudge,

    /// <summary>They chose to leave (passed on High Stakes' bet): it's over, nothing won and nothing lost.</summary>
    Left,
}

/// <summary>
/// One adventurer's go at a mini game. Every mini game has the same shape: an intro (the screen says what it is
/// and how to play) from <see cref="IntroAt"/>, then play from <see cref="PlaysFrom"/> until it's won or the time
/// limit (<see cref="EndsAt"/>), then a moment showing how it went (until <see cref="ResultEndsAt"/>). The answer
/// buttons play it. While it's on, the adventurer's question and hourglass wait, and the arcade clock stops: mini
/// games are outside the time loop. The rules of each game are in its own class; this keeps the timing.
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public abstract class Minigame
{
    protected Minigame(MinigameKind kind, DateTimeOffset introAt, double introSeconds, double playSeconds, double resultSeconds)
    {
        Kind = kind;
        IntroAt = introAt;
        PlaysFrom = introAt + TimeSpan.FromSeconds(introSeconds);
        EndsAt = PlaysFrom + TimeSpan.FromSeconds(playSeconds);
        ResultSeconds = resultSeconds;
    }

    public MinigameKind Kind { get; }

    /// <summary>The action button plays a part in this game (the chest shuffle's fifth chest, the ring puzzle's next ring), so its light is on.</summary>
    public virtual bool UsesActionButton => false;

    /// <summary>The intro starts (after the kill beat that emptied the room before it).</summary>
    public DateTimeOffset IntroAt { get; }

    /// <summary>Play starts: presses count from now.</summary>
    public DateTimeOffset PlaysFrom { get; }

    /// <summary>The time limit: not won by then, it's lost.</summary>
    public DateTimeOffset EndsAt { get; }

    private double ResultSeconds { get; }

    /// <summary>When it was won or lost (null while it's still on).</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    public bool Won { get; private set; }

    /// <summary>The moment showing how it went is over: back to the fight (or, won, to a reward). Null while it's on.</summary>
    public DateTimeOffset? ResultEndsAt => ResultFrom + TimeSpan.FromSeconds(ResultSeconds);

    /// <summary>
    /// The result starts showing: when it finished, or, won, after the game's <see cref="WinPauseSeconds"/> (the ring
    /// puzzle's last ring turning into place). Null while it's on.
    /// </summary>
    public DateTimeOffset? ResultFrom => FinishedAt + TimeSpan.FromSeconds(Won ? WinPauseSeconds : 0);

    /// <summary>
    /// How long after the press that wins it (or a round of it: <see cref="MinigamePress.Cleared"/>) the screen shows the
    /// win, and the room hears it. 0 for most: the ring puzzle holds the finished picture a moment first.
    /// </summary>
    public virtual double WinPauseSeconds => 0;

    /// <summary>
    /// A button pressed at <paramref name="at"/>: an answer button (0-3) or the action button (4, <see cref="Input.ButtonPress.Action"/>).
    /// Only counts while it's being played; each game decides what the buttons do.
    /// </summary>
    public MinigamePress Press(int answerIndex, DateTimeOffset at)
    {
        if (FinishedAt != null || at < PlaysFrom || at > EndsAt)
            return MinigamePress.Ignored;

        var result = Play(answerIndex, at);
        if (result is MinigamePress.Won or MinigamePress.Lost or MinigamePress.Left)
            Finish(at, won: result == MinigamePress.Won);
        return result;
    }

    /// <summary>
    /// A game with clocks of its own inside its time limit (the trap's statements, High Stakes' choice and question):
    /// whatever of them has run out by <paramref name="now"/>, as if it were a press (a miss, the last one losing it...).
    /// Checked every tick, again while it says something happened (a slow tick may be past more than one). Ignored
    /// when nothing ran out, as for most games.
    /// </summary>
    public MinigamePress Due(DateTimeOffset now)
    {
        if (FinishedAt != null || now < PlaysFrom || Overdue(now) is not { } overdue)
            return MinigamePress.Ignored;

        var (result, at) = overdue;
        if (result is MinigamePress.Won or MinigamePress.Lost or MinigamePress.Left)
            Finish(at, won: result == MinigamePress.Won);
        return result;
    }

    /// <summary>What one of the game's own clocks did if one has run out by <paramref name="now"/>, and when it ran out; null for most games.</summary>
    protected virtual (MinigamePress Result, DateTimeOffset At)? Overdue(DateTimeOffset now) => null;

    /// <summary>
    /// A win earns a pick of the floor rewards (when <see cref="MinigameSettings.WinEarnsReward"/> is on). Not the
    /// treasure room's (its chest is the prize) or the trap's (a full heal).
    /// </summary>
    public virtual bool WinEarnsReward => true;

    /// <summary>
    /// A game won by how things stand rather than by a press (the tuner: every bar held in its zone, which can happen
    /// as a bar sinks into place): checked every tick, before the time limit. Returns true if it was just won.
    /// </summary>
    public bool WinDue(DateTimeOffset now)
    {
        if (FinishedAt != null || now < PlaysFrom || WonAt(now) is not { } at || at > EndsAt)
            return false;
        Finish(at, won: true);
        return true;
    }

    /// <summary>When it was won by how things stand, if it has been by <paramref name="now"/> (see <see cref="WinDue"/>); null for most games.</summary>
    protected virtual DateTimeOffset? WonAt(DateTimeOffset now) => null;

    /// <summary>The time limit ran out: it's lost. Returns true if that just happened.</summary>
    public bool TimeUp(DateTimeOffset now)
    {
        if (FinishedAt != null || now < EndsAt)
            return false;
        Finish(EndsAt, won: false);
        return true;
    }

    /// <summary>The game's own rules for one press (it's being played, so the timing is already checked).</summary>
    protected abstract MinigamePress Play(int answerIndex, DateTimeOffset at);

    private void Finish(DateTimeOffset at, bool won)
    {
        FinishedAt = at;
        Won = won;
    }
}
