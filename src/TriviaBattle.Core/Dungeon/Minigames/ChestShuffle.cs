namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The chest shuffle (a shell game): five chests in a row, a coin drops into one, the lids close and the chests swap
/// places, pair by pair. Then the player picks the chest the coin's in, with the button under it: A, B, C, D, and the
/// action button for the fifth. Right, and the next round shuffles more and faster (<see cref="ChestShuffleSettings.Swaps"/>,
/// <see cref="ChestShuffleSettings.SwapSeconds"/>); right in every round wins it. The wrong chest loses it there and then.
///
/// Each round's swaps are made when it starts, and the screen moves the chests from the same list: from
/// <see cref="RoundStartsAt"/> the coin is shown for <see cref="ChestShuffleSettings.RevealSeconds"/>, then swap i
/// (the chests in places <see cref="Swaps"/>[i]) takes <see cref="SwapSeconds"/>, and picks count from <see cref="PickFrom"/>.
/// Chests are numbered by where they start (0-4, left to right); places likewise.
/// </summary>
public sealed class ChestShuffle : Minigame
{
    public const int Chests = 5;

    private readonly ChestShuffleSettings _settings;
    private readonly Random _random;

    public ChestShuffle(DateTimeOffset introAt, MinigameSettings settings, Random random)
        : base(MinigameKind.ChestShuffle, introAt, settings.IntroSeconds, settings.ChestShuffle.Seconds, settings.ResultSeconds)
    {
        _settings = settings.ChestShuffle;
        _random = random;
        StartRound(1, PlaysFrom);
    }

    /// <summary>The round being played (1-based), of <see cref="Rounds"/>.</summary>
    public int Round { get; private set; }

    public int Rounds => _settings.Swaps.Count;

    /// <summary>The chest (by where it started) with the coin in it.</summary>
    public int CoinChest { get; private set; }

    /// <summary>This round: the coin is shown, then the chests swap.</summary>
    public DateTimeOffset RoundStartsAt { get; private set; }

    /// <summary>This round's swaps, in order: the two places whose chests change places.</summary>
    public IReadOnlyList<(int A, int B)> Swaps { get; private set; } = [];

    public double RevealSeconds => _settings.RevealSeconds;
    public double SwapSeconds { get; private set; }

    /// <summary>The chests have stopped: a pick counts from now.</summary>
    public DateTimeOffset PickFrom => RoundStartsAt + TimeSpan.FromSeconds(RevealSeconds + SwapSeconds * Swaps.Count);

    /// <summary>Which chest is in each place once the chests have stopped (place 0 = A ... 4 = the action button).</summary>
    public IReadOnlyList<int> FinalPlaces
    {
        get
        {
            var places = Enumerable.Range(0, Chests).ToArray();
            foreach (var (a, b) in Swaps)
                (places[a], places[b]) = (places[b], places[a]);
            return places;
        }
    }

    /// <summary>The place they picked last (null before their first pick), and whether the coin was there.</summary>
    public int? LastPick { get; private set; }

    public bool LastWasRight { get; private set; }

    /// <summary>Picks that counted, so screens can show each once.</summary>
    public int Picks { get; private set; }

    /// <summary>A right pick: the coin's place, shown until the next round starts (null before the first, or after a wrong one).</summary>
    public int? FoundAt { get; private set; }

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or >= Chests || at < PickFrom)
            return MinigamePress.Ignored; // still shuffling (or between rounds)

        Picks++;
        LastPick = answerIndex;
        LastWasRight = FinalPlaces[answerIndex] == CoinChest;
        if (!LastWasRight)
            return MinigamePress.Lost;
        if (Round >= Rounds)
            return MinigamePress.Won;

        FoundAt = answerIndex;
        StartRound(Round + 1, at + TimeSpan.FromSeconds(_settings.BetweenSeconds));
        return MinigamePress.Hit;
    }

    /// <summary>A fresh round: the chests back in a row, the coin in one of them, and this round's swaps.</summary>
    private void StartRound(int round, DateTimeOffset startsAt)
    {
        Round = round;
        RoundStartsAt = startsAt;
        CoinChest = _random.Next(Chests);
        SwapSeconds = _settings.SwapSeconds[Math.Min(round, _settings.SwapSeconds.Count) - 1];
        var swaps = new List<(int, int)>();
        for (var i = 0; i < _settings.Swaps[round - 1]; i++)
        {
            var a = _random.Next(Chests);
            var b = (a + 1 + _random.Next(Chests - 1)) % Chests; // any other place
            swaps.Add((Math.Min(a, b), Math.Max(a, b)));
        }
        Swaps = swaps;
    }
}
