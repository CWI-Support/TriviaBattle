namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The numbers behind the mini games. Bound from "Game:Dungeon:Minigames" in appsettings.json. How often a floor has
/// one is content: <c>MinigameChance</c> in config/dungeon.json.
/// </summary>
public sealed class MinigameSettings
{
    /// <summary>Mini games on or off (off: floors with one planned simply don't stop for it).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The intro: what the game is and how to play, with a 3, 2, 1, before presses count.</summary>
    public double IntroSeconds { get; set; } = 3.5;

    /// <summary>The moment after it's won or lost that shows how it went.</summary>
    public double ResultSeconds { get; set; } = 2.5;

    /// <summary>Winning earns a pick of the floor rewards (see Reward), as after a boss.</summary>
    public bool WinEarnsReward { get; set; } = true;

    public RuneLockSettings RuneLock { get; set; } = new();
    public RuneEchoSettings RuneEcho { get; set; } = new();
    public ChestShuffleSettings ChestShuffle { get; set; } = new();

    public MinigameSettings Copy()
    {
        var copy = (MinigameSettings)MemberwiseClone();
        copy.RuneLock = RuneLock.Copy();
        copy.RuneEcho = RuneEcho.Copy();
        copy.ChestShuffle = ChestShuffle.Copy();
        return copy;
    }
}

/// <summary>The rune echo, a "Simon says" (<see cref="RuneEcho"/>).</summary>
public sealed class RuneEchoSettings
{
    /// <summary>How many runes the first round shows; each round after shows one more.</summary>
    public int StartLength { get; set; } = 3;

    /// <summary>Rounds to get right (with the defaults: 3, 4, 5, 6 and 7 runes).</summary>
    public int Rounds { get; set; } = 5;

    /// <summary>A rune lights up every this many seconds while a round is shown...</summary>
    public double StepSeconds { get; set; } = 0.65;

    /// <summary>...and stays lit this long (less than a step, so the same rune twice shows as two).</summary>
    public double LitSeconds { get; set; } = 0.45;

    /// <summary>The pause before a round's runes start (the first, and after one is got right).</summary>
    public double PauseSeconds { get; set; } = 0.8;

    /// <summary>After a wrong rune, the pause before that round's runes are shown again.</summary>
    public double MissPauseSeconds { get; set; } = 1.2;

    /// <summary>The time limit.</summary>
    public double Seconds { get; set; } = 55;

    public RuneEchoSettings Copy() => (RuneEchoSettings)MemberwiseClone();
}

/// <summary>The chest shuffle, a shell game (<see cref="ChestShuffle"/>).</summary>
public sealed class ChestShuffleSettings
{
    /// <summary>How many swaps each round has: one round each (so 3 rounds by default), more each time.</summary>
    public List<int> Swaps { get; set; } = [5, 7, 9];

    /// <summary>How long each swap takes, round by round (the last one for any rounds after): faster each time.</summary>
    public List<double> SwapSeconds { get; set; } = [0.4, 0.32, 0.26];

    /// <summary>
    /// The share of swaps (0-1) sure to move the coin's chest; the rest swap any two chests (which may include it too).
    /// Random swaps alone leave it standing still most of the time, which makes it easy to follow.
    /// </summary>
    public double CoinSwapShare { get; set; } = 0.5;

    /// <summary>How long the coin is shown going into its chest before the lids close.</summary>
    public double RevealSeconds { get; set; } = 1.6;

    /// <summary>After a right pick, the moment showing it before the next round.</summary>
    public double BetweenSeconds { get; set; } = 1.4;

    /// <summary>The time limit, for all the rounds.</summary>
    public double Seconds { get; set; } = 30;

    public ChestShuffleSettings Copy()
    {
        var copy = (ChestShuffleSettings)MemberwiseClone();
        copy.Swaps = [.. Swaps];
        copy.SwapSeconds = [.. SwapSeconds];
        return copy;
    }
}

/// <summary>The rune lock (<see cref="RuneLock"/>).</summary>
public sealed class RuneLockSettings
{
    /// <summary>Hits in a row that open it.</summary>
    public int Hits { get; set; } = 6;

    /// <summary>The time limit.</summary>
    public double Seconds { get; set; } = 30;

    /// <summary>After a miss, the buttons are dead this long (and the count starts again).</summary>
    public double LockoutSeconds { get; set; } = 2;

    /// <summary>How wide the glowing mark is at first, in degrees of the circle.</summary>
    public double MarkDegrees { get; set; } = 40;

    /// <summary>Each hit in a row makes the mark this share narrower (0.12: 40, 35, 31, 27, 24, 21 degrees); a miss puts it back.</summary>
    public double MarkShrinkPerHit { get; set; } = 0.12;

    /// <summary>A press this many degrees either side of the mark still counts (presses take a moment to arrive).</summary>
    public double LeewayDegrees { get; set; } = 6;

    /// <summary>How fast the needle starts, in degrees a second (360 = once round a second).</summary>
    public double DegreesPerSecond { get; set; } = 200;

    /// <summary>Each hit makes it this share faster (0.1 = 10%).</summary>
    public double SpeedUpPerHit { get; set; } = 0.1;

    public RuneLockSettings Copy() => (RuneLockSettings)MemberwiseClone();
}
