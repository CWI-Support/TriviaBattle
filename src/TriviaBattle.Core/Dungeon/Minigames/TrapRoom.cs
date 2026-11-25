using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The trap room, one of the special rooms behind a fork's doors (<see cref="Fork"/>): a true/false sprint. Every
/// question has four answers, so each statement is one of the player's own questions with one answer shown ("Which
/// wizard school does Harry Potter attend? Durmstrang?"): true when that's its right answer, which is half the time.
/// A says true, B says false.
///
/// Each statement has <see cref="TrapSettings.StatementSeconds"/>. A wrong answer, or none in time, springs the trap
/// (<see cref="Misses"/> goes up, and <see cref="DungeonRun"/> takes <see cref="TrapSettings.MissShare"/> of their HP for
/// it, never the last of it). After each, whether it was true shows a moment (<see cref="TrapSettings.RevealSeconds"/>)
/// before the next. Once they've all been answered, every one right wins it: a full heal (<see cref="DungeonRun"/> gives
/// it: <see cref="Healed"/>), rather than a reward pick. A miss loses it (they got through, hurt). The snapshot never
/// carries whether the statement up is true.
/// </summary>
public sealed class TrapRoom : Minigame
{
    private readonly TrapSettings _settings;
    private readonly IReadOnlyList<TrapStatement> _statements;

    public TrapRoom(DateTimeOffset introAt, MinigameSettings settings, Random random, IReadOnlyList<Question> questions)
        : base(MinigameKind.Trap, introAt, settings.IntroSeconds, BackstopSeconds(settings.Trap), settings.ResultSeconds)
    {
        if (questions.Count == 0)
            throw new ArgumentException("The trap room needs questions to make its statements from.", nameof(questions));
        _settings = settings.Trap;
        _statements = Enumerable.Range(0, Math.Max(1, settings.Trap.Statements))
            .Select(i => Make(questions[i % questions.Count], random))
            .ToList();
        AskedAt = PlaysFrom;
    }

    /// <summary>The time limit: only a backstop, as each statement's own clock ends it first (<see cref="Overdue"/>).</summary>
    private static double BackstopSeconds(TrapSettings s) => Math.Max(1, s.Statements) * (s.StatementSeconds + s.RevealSeconds) + 1;

    /// <summary>How many questions it takes from their stream: one per statement.</summary>
    public static int QuestionsNeeded(TrapSettings settings) => Math.Max(1, settings.Statements);

    /// <summary>A statement from a question: its right answer shown (true) or one of the others (false), half the time each.</summary>
    private static TrapStatement Make(Question question, Random random)
    {
        var isTrue = random.Next(2) == 0;
        var wrong = Enumerable.Range(0, question.Answers.Count).Where(i => i != question.CorrectIndex).ToList();
        var shown = isTrue || wrong.Count == 0 ? question.CorrectIndex : wrong[random.Next(wrong.Count)];
        return new TrapStatement(question, shown, shown == question.CorrectIndex);
    }

    /// <summary>Every statement, in the order they're asked.</summary>
    public IReadOnlyList<TrapStatement> Statements => _statements;

    /// <summary>Which of <see cref="Statements"/> is up (0-based); once it's over, the last one answered.</summary>
    public int Asking { get; private set; }

    public TrapStatement Current => _statements[Asking];

    /// <summary>When the statement up was asked: presses count from then (after the last one's reveal).</summary>
    public DateTimeOffset AskedAt { get; private set; }

    /// <summary>Not answered by then, the trap springs.</summary>
    public DateTimeOffset AnswerBy => AskedAt + TimeSpan.FromSeconds(_settings.StatementSeconds);

    public int Right { get; private set; }

    /// <summary>Times the trap sprang (a wrong answer, or none in time).</summary>
    public int Misses { get; private set; }

    /// <summary>
    /// The last answer: which statement (index into <see cref="Statements"/>), what they said (true or false; null: too
    /// slow), whether that was right, and when. Null before the first.
    /// </summary>
    public (int Statement, bool? Said, bool WasRight, DateTimeOffset At)? Last { get; private set; }

    /// <summary>All the HP the trap has taken, and what its last spring took (<see cref="DungeonRun"/> works them out: <see cref="Hurt"/>).</summary>
    public int Damage { get; private set; }
    public int LastDamage { get; private set; }

    /// <summary>All of them right: the HP the full heal gave back (0 at full health already).</summary>
    public int Healed { get; private set; }

    /// <summary>The prize is a full heal, not a reward pick.</summary>
    public override bool WinEarnsReward => false;

    /// <summary>The last statement's truth shows before the win.</summary>
    public override double WinPauseSeconds => _settings.RevealSeconds;

    /// <summary>Every one right: the full heal gave back <paramref name="hp"/>.</summary>
    public void Heal(int hp) => Healed = hp;

    /// <summary>The trap sprang and took <paramref name="hp"/>.</summary>
    public void Hurt(int hp)
    {
        LastDamage = hp;
        Damage += hp;
    }

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        // Only A (true) and B (false); and not during the last one's reveal, nor once its time is up (the tick springs it).
        if (answerIndex is not (0 or 1) || at < AskedAt || at > AnswerBy)
            return MinigamePress.Ignored;
        return Answer(said: answerIndex == 0, at);
    }

    /// <summary>The statement up wasn't answered in time: the trap springs, as for a wrong answer.</summary>
    protected override (MinigamePress Result, DateTimeOffset At)? Overdue(DateTimeOffset now)
    {
        if (now <= AnswerBy)
            return null;
        var at = AnswerBy;
        return (Answer(said: null, at), at);
    }

    private MinigamePress Answer(bool? said, DateTimeOffset at)
    {
        var right = said == Current.IsTrue;
        Last = (Asking, said, right, at);
        if (right)
            Right++;
        else
            Misses++;
        if (Asking + 1 >= _statements.Count)
            return Misses == 0 ? MinigamePress.Won : MinigamePress.Lost;

        Asking++;
        AskedAt = at + TimeSpan.FromSeconds(_settings.RevealSeconds);
        return right ? MinigamePress.Hit : MinigamePress.Miss;
    }
}

/// <summary>A trap's statement: a question with one of its answers shown (<see cref="Shown"/>), true when that's its right one.</summary>
public sealed record TrapStatement(Question Question, int Shown, bool IsTrue)
{
    public string ShownAnswer => Question.Answers[Shown];
}
