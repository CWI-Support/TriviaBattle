namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The rune echo (a "Simon says"): the four runes, one for each answer button, light up one after another, and the
/// player presses them back in the same order. Each round is one rune longer (<see cref="RuneEchoSettings.StartLength"/>
/// to start, <see cref="RuneEchoSettings.Rounds"/> rounds); getting the last one right wins it. A wrong rune shows that
/// round's runes again from the start (and costs the time it takes): only the time limit loses it.
///
/// The whole sequence is made at the start (every round shows the first few of it), and the screen lights the runes
/// from the same numbers the presses are checked against: a round's runes light one every
/// <see cref="RuneEchoSettings.StepSeconds"/> from <see cref="ShowFrom"/>, and presses count from <see cref="InputFrom"/>.
/// </summary>
public sealed class RuneEcho : Minigame
{
    private readonly RuneEchoSettings _settings;
    private readonly List<int> _sequence;

    public RuneEcho(DateTimeOffset introAt, MinigameSettings settings, Random random)
        : base(MinigameKind.RuneEcho, introAt, settings.IntroSeconds, settings.RuneEcho.Seconds, settings.ResultSeconds)
    {
        _settings = settings.RuneEcho;
        // Never the same rune twice running: a repeat is hard to see as two.
        _sequence = [];
        while (_sequence.Count < _settings.StartLength + _settings.Rounds - 1)
        {
            var rune = random.Next(4);
            if (_sequence.Count == 0 || rune != _sequence[^1])
                _sequence.Add(rune);
        }
        Round = 1;
        ShowFrom = PlaysFrom + TimeSpan.FromSeconds(_settings.PauseSeconds);
    }

    /// <summary>The round being played (1-based), of <see cref="Rounds"/>.</summary>
    public int Round { get; private set; }

    public int Rounds => _settings.Rounds;

    /// <summary>How many runes this round shows (and the player has to press back).</summary>
    public int Length => _settings.StartLength + Round - 1;

    /// <summary>This round's runes, in order (0-3: the answer buttons A-D).</summary>
    public IReadOnlyList<int> Sequence => _sequence.Take(Length).ToList();

    /// <summary>This round's runes start lighting up: one every <see cref="StepSeconds"/>.</summary>
    public DateTimeOffset ShowFrom { get; private set; }

    public double StepSeconds => _settings.StepSeconds;

    /// <summary>How long each rune stays lit (less than a step, so two in a row show as two).</summary>
    public double LitSeconds => _settings.LitSeconds;

    /// <summary>The runes have all been shown: presses count from now.</summary>
    public DateTimeOffset InputFrom => ShowFrom + TimeSpan.FromSeconds(StepSeconds * Length);

    /// <summary>How many of this round's runes they've pressed back right so far.</summary>
    public int Entered { get; private set; }

    /// <summary>The rune to press next (for the simulated players in the tests; never sent to a screen as such).</summary>
    public int Expected => _sequence[Entered];

    /// <summary>Presses that counted, so screens can flash each once: the last one's rune and whether it was right.</summary>
    public int Presses { get; private set; }

    public int LastRune { get; private set; } = -1;
    public bool LastWasRight { get; private set; }

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or > 3 || at < InputFrom)
            return MinigamePress.Ignored; // the runes are still showing (or the action button: no rune)

        Presses++;
        LastRune = answerIndex;
        LastWasRight = answerIndex == _sequence[Entered];
        if (!LastWasRight)
        {
            Entered = 0;
            ShowFrom = at + TimeSpan.FromSeconds(_settings.MissPauseSeconds); // this round's runes again
            return MinigamePress.Miss;
        }

        Entered++;
        if (Entered < Length)
            return MinigamePress.Hit;
        if (Round >= Rounds)
            return MinigamePress.Won;

        Round++;
        Entered = 0;
        ShowFrom = at + TimeSpan.FromSeconds(_settings.PauseSeconds); // the next, one rune longer
        return MinigamePress.Hit;
    }
}
