namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The ring puzzle: a picture cut into rings round a middle disc, each ring turned out of line. The player turns the
/// ring they're on with A (anticlockwise) and D (clockwise), and the action button moves them to the next ring out
/// (from the outside one, back to the middle). Every ring back in line puts the picture back together and solves the
/// round; each round has one ring more (<see cref="RingPuzzleSettings.StartRings"/> to start, <see cref="RingPuzzleSettings.Rounds"/>
/// rounds), and solving the last one in time wins it. B and C do nothing; only the time limit loses it.
///
/// Rings are numbered from the middle (0, the disc) outwards, and turn in steps (<see cref="Steps"/> to a full turn):
/// a ring's <see cref="Offsets"/> is how many steps clockwise it's turned out of line, 0 when it's in line. Which
/// picture it is is up to the screen.
/// </summary>
public sealed class RingPuzzle : Minigame
{
    /// <summary>The buttons that turn the ring: A anticlockwise, D clockwise (the action button changes ring).</summary>
    public const int TurnLeft = 0, TurnRight = 3;

    private readonly RingPuzzleSettings _settings;
    private readonly Random _random;
    private int[] _offsets = [];

    public RingPuzzle(DateTimeOffset introAt, MinigameSettings settings, Random random)
        : base(MinigameKind.RingPuzzle, introAt, settings.IntroSeconds, settings.RingPuzzle.Seconds, settings.ResultSeconds)
    {
        _settings = settings.RingPuzzle;
        _random = random;
        StartRound(1, PlaysFrom);
    }

    /// <summary>The round being played (1-based), of <see cref="Rounds"/>.</summary>
    public int Round { get; private set; }

    public int Rounds => _settings.Rounds;

    /// <summary>How many rings this round's picture is cut into (the middle disc counts as one).</summary>
    public int Rings => _offsets.Length;

    /// <summary>Steps to a full turn (12: each press turns a ring 30 degrees).</summary>
    public int Steps => _settings.Steps;

    /// <summary>Each ring's turn out of line, in steps clockwise (0 = in line), from the middle out.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    /// <summary>The ring A and D turn (0 = the middle).</summary>
    public int Selected { get; private set; }

    /// <summary>This round starts: presses count from now (after a round is solved, a moment showing the picture first).</summary>
    public DateTimeOffset RoundStartsAt { get; private set; }

    /// <summary>The round before was solved: how many rings it had, so the screen can show its picture until this one starts (null in round 1).</summary>
    public int? SolvedRings { get; private set; }

    /// <summary>Presses that counted, so screens can show each once.</summary>
    public int Presses { get; private set; }

    /// <summary>
    /// The last picture put back together shows whole ("Solved!", or the win): a moment after the press that did it,
    /// so the last ring is seen turning into place first (<see cref="RingPuzzleSettings.SolvedPauseSeconds"/>). Null
    /// before the first.
    /// </summary>
    public DateTimeOffset? WholeAt { get; private set; }

    public override double WinPauseSeconds => _settings.SolvedPauseSeconds;

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (at < RoundStartsAt)
            return MinigamePress.Ignored; // the solved picture is still showing
        switch (answerIndex)
        {
            case Input.ButtonPress.Action:
                Selected = (Selected + 1) % Rings;
                Presses++;
                return MinigamePress.Hit;
            case TurnLeft or TurnRight:
                var step = answerIndex == TurnRight ? 1 : -1;
                _offsets[Selected] = (_offsets[Selected] + step + Steps) % Steps;
                Presses++;
                break;
            default:
                return MinigamePress.Ignored; // B and C
        }

        if (_offsets.Any(offset => offset != 0))
            return MinigamePress.Hit;
        WholeAt = at + TimeSpan.FromSeconds(_settings.SolvedPauseSeconds);
        if (Round >= Rounds)
            return MinigamePress.Won; // the result shows from WholeAt (WinPauseSeconds)
        StartRound(Round + 1, WholeAt.Value + TimeSpan.FromSeconds(_settings.BetweenSeconds));
        return MinigamePress.Cleared;
    }

    /// <summary>A fresh round: one ring more, every one turned out of line, starting on the middle.</summary>
    private void StartRound(int round, DateTimeOffset startsAt)
    {
        SolvedRings = round > 1 ? Rings : null;
        Round = round;
        RoundStartsAt = startsAt;
        Selected = 0;
        _offsets = Enumerable.Range(0, _settings.StartRings + round - 1)
            .Select(_ => 1 + _random.Next(Steps - 1)) // never already in line
            .ToArray();
    }
}
