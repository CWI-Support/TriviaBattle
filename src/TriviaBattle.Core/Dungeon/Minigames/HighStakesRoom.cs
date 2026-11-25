using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// High Stakes, one of the special rooms behind a fork's doors (<see cref="Fork"/>): a bet they choose to make or not.
/// First the choice: A bets, D passes, and not choosing within <see cref="HighStakesSettings.DecideSeconds"/> is passing.
/// Passing ends it at once with nothing won and nothing lost (<see cref="MinigamePress.Left"/>).
///
/// Betting asks one brutal question: from the high-stakes bank (config/high-stakes.json: <see cref="HighStakesQuestions"/>),
/// harder than any in the question database, or with none, a hard one from their own stream. They have
/// <see cref="HighStakesSettings.AnswerSeconds"/> to answer it (presses count a moment after the bet:
/// <see cref="HighStakesSettings.ReadSeconds"/>). Right wins it: a pick of the floor rewards. Wrong, or too slow, loses it:
/// it takes <see cref="HighStakesSettings.PenaltyShare"/> of their HP, never the last of it (<see cref="DungeonRun"/> does
/// that: <see cref="Penalty"/>). The snapshot carries the question only once they've bet, and its right answer only once
/// it's been answered.
/// </summary>
public sealed class HighStakesRoom : Minigame
{
    private readonly HighStakesSettings _settings;

    public HighStakesRoom(DateTimeOffset introAt, MinigameSettings settings, Question question)
        : base(MinigameKind.HighStakes, introAt, settings.IntroSeconds, BackstopSeconds(settings.HighStakes), settings.ResultSeconds)
    {
        _settings = settings.HighStakes;
        Question = question;
    }

    /// <summary>The time limit: only a backstop, as the choice's and the question's own clocks end it first (<see cref="Overdue"/>).</summary>
    private static double BackstopSeconds(HighStakesSettings s) => s.DecideSeconds + s.ReadSeconds + s.AnswerSeconds + 1;

    /// <summary>The question it asks if they bet.</summary>
    public Question Question { get; }

    /// <summary>Not chosen by then, they pass.</summary>
    public DateTimeOffset DecideBy => PlaysFrom + TimeSpan.FromSeconds(_settings.DecideSeconds);

    /// <summary>When they bet; null until they do.</summary>
    public DateTimeOffset? BetAt { get; private set; }

    /// <summary>Once they've bet: presses answer the question from then.</summary>
    public DateTimeOffset? AskedAt => BetAt + TimeSpan.FromSeconds(_settings.ReadSeconds);

    /// <summary>Once they've bet: not answered by then, it's lost.</summary>
    public DateTimeOffset? AnswerBy => AskedAt + TimeSpan.FromSeconds(_settings.AnswerSeconds);

    /// <summary>They passed (pressed D, or didn't choose in time).</summary>
    public bool Passed { get; private set; }

    /// <summary>Their answer: the button (null: too slow), whether it was right, and when. Null until they've answered.</summary>
    public (int? Chosen, bool WasRight, DateTimeOffset At)? Answer { get; private set; }

    /// <summary>The HP a wrong answer (or none) cost them, once it has (<see cref="DungeonRun"/> works it out).</summary>
    public int Penalty { get; private set; }

    public double PenaltyShare => _settings.PenaltyShare;

    /// <summary>The right answer shows a moment before the win.</summary>
    public override double WinPauseSeconds => _settings.RevealSeconds;

    /// <summary>A wrong answer cost them <paramref name="hp"/>.</summary>
    public void Punish(int hp) => Penalty = hp;

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (BetAt == null)
        {
            if (at > DecideBy)
                return MinigamePress.Ignored; // too late to choose: the tick passes for them
            switch (answerIndex)
            {
                case 0:
                    BetAt = at;
                    return MinigamePress.Hit;
                case 3:
                    Passed = true;
                    return MinigamePress.Left;
                default:
                    return MinigamePress.Ignored; // only A (bet) and D (pass) mean anything yet
            }
        }

        if (answerIndex is < 0 or > 3 || at < AskedAt || at > AnswerBy)
            return MinigamePress.Ignored; // the action button, still reading it, or too late (the tick ends it)
        var right = answerIndex == Question.CorrectIndex;
        Answer = (answerIndex, right, at);
        return right ? MinigamePress.Won : MinigamePress.Lost;
    }

    /// <summary>Not chosen in time: they pass. Bet and not answered in time: it's lost.</summary>
    protected override (MinigamePress Result, DateTimeOffset At)? Overdue(DateTimeOffset now)
    {
        if (BetAt == null)
        {
            if (now <= DecideBy)
                return null;
            Passed = true;
            return (MinigamePress.Left, DecideBy);
        }
        if (AnswerBy is not { } by || now <= by)
            return null;
        Answer = (null, false, by);
        return (MinigamePress.Lost, by);
    }
}
