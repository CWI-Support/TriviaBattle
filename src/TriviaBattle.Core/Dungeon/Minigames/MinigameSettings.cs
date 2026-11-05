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

    public MinigameSettings Copy()
    {
        var copy = (MinigameSettings)MemberwiseClone();
        copy.RuneLock = RuneLock.Copy();
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
