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

    /// <summary>A press that lost it there and then (the wrong chest).</summary>
    Lost,
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
    public DateTimeOffset? ResultEndsAt => FinishedAt + TimeSpan.FromSeconds(ResultSeconds);

    /// <summary>
    /// A button pressed at <paramref name="at"/>: an answer button (0-3) or the action button (4, <see cref="Input.ButtonPress.Action"/>).
    /// Only counts while it's being played; each game decides what the buttons do.
    /// </summary>
    public MinigamePress Press(int answerIndex, DateTimeOffset at)
    {
        if (FinishedAt != null || at < PlaysFrom || at > EndsAt)
            return MinigamePress.Ignored;

        var result = Play(answerIndex, at);
        if (result is MinigamePress.Won or MinigamePress.Lost)
            Finish(at, won: result == MinigamePress.Won);
        return result;
    }

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
