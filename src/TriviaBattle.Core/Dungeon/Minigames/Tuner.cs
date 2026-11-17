namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The tuner: four bars, one per answer button (A-D), each sinking at its own speed. A press of its button raises a bar
/// by <see cref="TunerSettings.Step"/>; each bar has a target zone. Get all four into their zones and hold them there
/// together for <see cref="TunerSettings.HoldSeconds"/> to win it; only the time limit loses it. The action button does
/// nothing.
///
/// Levels run from 0 (empty) to 1 (full). A bar's level is worked out from when it was last pressed: it was
/// <see cref="TunerBar.Level"/> then, and sinks <see cref="TunerBar.Rate"/> a second until it's empty. So the screen
/// draws it from the same numbers, and when all four were in their zones together is known exactly, not just at a tick.
/// </summary>
public sealed class Tuner : Minigame
{
    public const int Bars = 4;

    private readonly TunerSettings _settings;
    private readonly TunerBar[] _bars;

    public Tuner(DateTimeOffset introAt, MinigameSettings settings, Random random)
        : base(MinigameKind.Tuner, introAt, settings.IntroSeconds, settings.Tuner.Seconds, settings.ResultSeconds)
    {
        _settings = settings.Tuner;
        _bars = Enumerable.Range(0, Bars).Select(_ =>
        {
            var rate = _settings.SinkSlowest + random.NextDouble() * (_settings.SinkFastest - _settings.SinkSlowest);
            var middle = _settings.ZoneLowest + random.NextDouble() * (_settings.ZoneHighest - _settings.ZoneLowest);
            return new TunerBar(0, PlaysFrom, rate, middle - _settings.ZoneSize / 2, middle + _settings.ZoneSize / 2);
        }).ToArray();
    }

    /// <summary>The four bars, A to D.</summary>
    public IReadOnlyList<TunerBar> BarList => _bars;

    public double HoldSeconds => _settings.HoldSeconds;

    /// <summary>Presses that counted, so screens can show each once.</summary>
    public int Presses { get; private set; }

    /// <summary>A bar's level at a moment (0-1): what it was when last pressed, less what it's sunk since.</summary>
    public double LevelOf(int bar, DateTimeOffset at) => _bars[bar].LevelAt(at);

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or >= Bars)
            return MinigamePress.Ignored; // the action button: nothing to tune
        if (WonAt(at) is { } won && won <= at)
            return MinigamePress.Won; // it was all held in tune already (between two ticks): this press doesn't spoil it

        var bar = _bars[answerIndex];
        _bars[answerIndex] = bar with { Level = Math.Min(1, bar.LevelAt(at) + _settings.Step), At = at };
        Presses++;
        return WonAt(at) is { } now && now <= at ? MinigamePress.Won : MinigamePress.Nudge;
    }

    /// <summary>
    /// When all four bars had been in their zones together for <see cref="HoldSeconds"/>, if that's happened by
    /// <paramref name="now"/>. Each bar sinks steadily from its last press, so it's in its zone over one stretch of
    /// time (from when it sinks into the top of it, to when it sinks out of the bottom); all four are in together where
    /// those overlap.
    /// </summary>
    protected override DateTimeOffset? WonAt(DateTimeOffset now)
    {
        var from = DateTimeOffset.MinValue;
        var until = DateTimeOffset.MaxValue;
        foreach (var bar in _bars)
        {
            if (bar.InZone() is not { } stretch)
                return null; // below its zone: it won't get there without a press
            if (stretch.From > from) from = stretch.From;
            if (stretch.Until < until) until = stretch.Until;
        }
        var held = from + TimeSpan.FromSeconds(HoldSeconds);
        return held <= until && held <= now ? held : null;
    }
}

/// <summary>
/// One tuner bar: its <see cref="Level"/> (0-1) when last pressed (<see cref="At"/>), how fast it sinks
/// (<see cref="Rate"/>, of the bar a second), and its target zone (<see cref="ZoneLow"/> to <see cref="ZoneHigh"/>).
/// </summary>
public sealed record TunerBar(double Level, DateTimeOffset At, double Rate, double ZoneLow, double ZoneHigh)
{
    public double LevelAt(DateTimeOffset at) => Math.Max(0, Level - Rate * Math.Max(0, (at - At).TotalSeconds));

    /// <summary>
    /// The stretch of time it's in its zone, sinking from its last press: from when it sinks to the top of the zone (or
    /// from the press, if it's in already) until it sinks out of the bottom. Null if it's below the zone.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset Until)? InZone()
    {
        if (Level < ZoneLow)
            return null;
        if (Rate <= 0)
            return Level <= ZoneHigh ? (At, DateTimeOffset.MaxValue) : null;
        var from = Level > ZoneHigh ? At + TimeSpan.FromSeconds((Level - ZoneHigh) / Rate) : At;
        return (from, At + TimeSpan.FromSeconds((Level - ZoneLow) / Rate));
    }
}
