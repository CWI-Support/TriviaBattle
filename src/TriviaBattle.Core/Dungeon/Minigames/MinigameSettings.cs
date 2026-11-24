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
    public double ResultSeconds { get; set; } = 3.2;

    /// <summary>Winning earns a pick of the floor rewards (see Reward), as after a boss.</summary>
    public bool WinEarnsReward { get; set; } = true;

    public RuneLockSettings RuneLock { get; set; } = new();
    public RuneEchoSettings RuneEcho { get; set; } = new();
    public ChestShuffleSettings ChestShuffle { get; set; } = new();
    public RingPuzzleSettings RingPuzzle { get; set; } = new();
    public TunerSettings Tuner { get; set; } = new();
    public PopQuizSettings PopQuiz { get; set; } = new();
    public HotPotatoSettings HotPotato { get; set; } = new();

    public MinigameSettings Copy()
    {
        var copy = (MinigameSettings)MemberwiseClone();
        copy.RuneLock = RuneLock.Copy();
        copy.RuneEcho = RuneEcho.Copy();
        copy.ChestShuffle = ChestShuffle.Copy();
        copy.RingPuzzle = RingPuzzle.Copy();
        copy.Tuner = Tuner.Copy();
        copy.PopQuiz = PopQuiz.Copy();
        copy.HotPotato = HotPotato.Copy();
        return copy;
    }
}

/// <summary>The hot potato: a lit bomb passed round the party (<see cref="HotPotato"/>).</summary>
public sealed class HotPotatoSettings
{
    /// <summary>The fuse: somewhere between these (a secret until it goes off). It's the game's time limit.</summary>
    public double FuseShortest { get; set; } = 14;
    public double FuseLongest { get; set; } = 24;

    /// <summary>Runes the holder presses, in order, to throw it on.</summary>
    public int PassPresses { get; set; } = 3;

    /// <summary>After a catch, how long before the new holder's presses count (it flies over, and they see their runes).</summary>
    public double CatchSeconds { get; set; } = 0.6;

    /// <summary>A wrong rune fumbles it: the holder's buttons are dead this long, and it's back to the first rune.</summary>
    public double FumbleSeconds { get; set; } = 0.8;

    /// <summary>The blast takes this share of the holder's max HP (it never takes the last of it).</summary>
    public double BlastShare { get; set; } = 0.25;

    public HotPotatoSettings Copy() => (HotPotatoSettings)MemberwiseClone();
}

/// <summary>The pop quiz: questions about their own run (<see cref="PopQuiz"/>).</summary>
public sealed class PopQuizSettings
{
    /// <summary>Right answers that win it.</summary>
    public int RightNeeded { get; set; } = 3;

    /// <summary>Wrong answers that lose it.</summary>
    public int Strikes { get; set; } = 3;

    /// <summary>How long the right answer shows after each answer, before the next question (presses wait for it).</summary>
    public double RevealSeconds { get; set; } = 1.4;

    /// <summary>The time limit.</summary>
    public double Seconds { get; set; } = 40;

    public PopQuizSettings Copy() => (PopQuizSettings)MemberwiseClone();
}

/// <summary>The tuner: four sinking bars, pressed up into their zones (<see cref="Tuner"/>). Levels are shares of the bar (0-1).</summary>
public sealed class TunerSettings
{
    /// <summary>How far one press raises a bar.</summary>
    public double Step { get; set; } = 0.1;

    /// <summary>
    /// How fast the bars sink, a second: each picks its own speed between these. With these, about four presses a second
    /// (aimed at the lowest bar) wins it 9 times in 10, in about 20 s; three and a half a second, about half the time.
    /// </summary>
    public double SinkSlowest { get; set; } = 0.04;
    public double SinkFastest { get; set; } = 0.11;

    /// <summary>How tall each target zone is (keep it taller than a Step, so a press can always land a bar in it).</summary>
    public double ZoneSize { get; set; } = 0.15;

    /// <summary>Where a zone's middle can be: each picks its own height between these.</summary>
    public double ZoneLowest { get; set; } = 0.3;
    public double ZoneHighest { get; set; } = 0.8;

    /// <summary>All four in their zones together for this long wins it (0: the moment they all are).</summary>
    public double HoldSeconds { get; set; } = 0.5;

    /// <summary>The time limit.</summary>
    public double Seconds { get; set; } = 40;

    public TunerSettings Copy() => (TunerSettings)MemberwiseClone();
}

/// <summary>The ring puzzle: a picture cut into rings, each turned out of line (<see cref="RingPuzzle"/>).</summary>
public sealed class RingPuzzleSettings
{
    /// <summary>How many rings the first round's picture has (the middle disc counts as one); each round after has one more.</summary>
    public int StartRings { get; set; } = 3;

    /// <summary>Rounds to solve (with the defaults: 3, 4 and 5 rings).</summary>
    public int Rounds { get; set; } = 3;

    /// <summary>Steps to a full turn: each press turns a ring 360 / Steps degrees (12: 30 degrees, at most 6 presses a ring).</summary>
    public int Steps { get; set; } = 12;

    /// <summary>
    /// After the press that puts a picture back together, a moment with the last ring turning into place before it
    /// shows as solved (with the win sound), so it doesn't flip the instant the last ring lines up.
    /// </summary>
    public double SolvedPauseSeconds { get; set; } = 1;

    /// <summary>Then the picture shows whole ("Solved!") this long before the next round.</summary>
    public double BetweenSeconds { get; set; } = 1.5;

    /// <summary>The time limit, for all the rounds.</summary>
    public double Seconds { get; set; } = 60;

    public RingPuzzleSettings Copy() => (RingPuzzleSettings)MemberwiseClone();
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
