namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The rune lock: a needle goes round a circle, and a mark glows somewhere on its rim. Press any answer button
/// while the needle is on the mark: <see cref="RuneLockSettings.Hits"/> hits in a row open the lock. Each hit moves
/// the mark and sends the needle back the other way, a little faster, and the mark gets narrower. A miss starts the
/// count again from nothing (the mark back to full width, the needle back to its first speed) and locks the buttons for <see cref="RuneLockSettings.LockoutSeconds"/>.
///
/// The needle is worked out from the time alone: it turns at <see cref="DegreesPerSecond"/> (negative: the other way)
/// from <see cref="TurnAngle"/> at <see cref="TurnStartedAt"/>, and the screen draws it from the same numbers. So a
/// press's time decides a hit here, on the server, and the screen only shows it. Angles are degrees clockwise from
/// the top (0 to 360).
/// </summary>
public sealed class RuneLock : Minigame
{
    private readonly RuneLockSettings _settings;
    private readonly Random _random;

    public RuneLock(DateTimeOffset introAt, MinigameSettings settings, Random random)
        : base(MinigameKind.RuneLock, introAt, settings.IntroSeconds, settings.RuneLock.Seconds, settings.ResultSeconds)
    {
        _settings = settings.RuneLock;
        _random = random;
        TurnStartedAt = PlaysFrom;
        TurnAngle = 0;
        DegreesPerSecond = _settings.DegreesPerSecond;
        MarkAngle = 90 + _random.NextDouble() * 180; // somewhere round the far side from the top, where the needle starts
    }

    /// <summary>Hits in a row so far, of <see cref="HitsNeeded"/>.</summary>
    public int Hits { get; private set; }

    public int HitsNeeded => _settings.Hits;

    /// <summary>After a miss: the buttons do nothing until then.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    // The needle's current turn: from TurnAngle at TurnStartedAt, at DegreesPerSecond.

    public DateTimeOffset TurnStartedAt { get; private set; }
    public double TurnAngle { get; private set; }
    public double DegreesPerSecond { get; private set; }

    /// <summary>The middle of the mark, and its width (the needle must be within half of it either side).</summary>
    public double MarkAngle { get; private set; }

    /// <summary>The mark gets narrower with each hit in a row (<see cref="RuneLockSettings.MarkShrinkPerHit"/>), back to full after a miss.</summary>
    public double MarkDegrees => _settings.MarkDegrees * Math.Pow(1 - _settings.MarkShrinkPerHit, Hits);

    /// <summary>Presses that counted (hits and misses), so screens can flash each one once.</summary>
    public int Presses { get; private set; }

    /// <summary>The last press that counted was a hit.</summary>
    public bool LastWasHit { get; private set; }

    /// <summary>Where the needle points at a moment (0-360).</summary>
    public double NeedleAt(DateTimeOffset at)
    {
        var turned = DegreesPerSecond * Math.Max(0, (at - TurnStartedAt).TotalSeconds);
        return Normalize(TurnAngle + turned);
    }

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (LockedUntil > at)
            return MinigamePress.Ignored;

        var needle = NeedleAt(at);
        Presses++;
        // A little leeway past the drawn mark, for the time a press takes to arrive.
        LastWasHit = Distance(needle, MarkAngle) <= MarkDegrees / 2 + _settings.LeewayDegrees;
        if (!LastWasHit)
        {
            // Back to the start: no hits, the mark full width, the needle at its first speed (still turning the
            // way it was, from where it is), and the buttons locked for a moment.
            Hits = 0;
            TurnAngle = needle;
            TurnStartedAt = at;
            DegreesPerSecond = Math.Sign(DegreesPerSecond) * _settings.DegreesPerSecond;
            LockedUntil = at + TimeSpan.FromSeconds(_settings.LockoutSeconds);
            return MinigamePress.Miss;
        }

        Hits++;
        if (Hits >= HitsNeeded)
            return MinigamePress.Won;

        // The needle turns back the other way, faster, from where it is; the mark moves well away from it.
        TurnAngle = needle;
        TurnStartedAt = at;
        DegreesPerSecond = -DegreesPerSecond * (1 + _settings.SpeedUpPerHit);
        MarkAngle = Normalize(needle + (DegreesPerSecond > 0 ? 1 : -1) * (100 + _random.NextDouble() * 160));
        return MinigamePress.Hit;
    }

    private static double Normalize(double angle) => ((angle % 360) + 360) % 360;

    /// <summary>How far apart two angles are, the short way round (0-180).</summary>
    private static double Distance(double a, double b)
    {
        var d = Math.Abs(Normalize(a) - Normalize(b));
        return d > 180 ? 360 - d : d;
    }
}
