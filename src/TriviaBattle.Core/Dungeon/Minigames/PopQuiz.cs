namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The pop quiz: a quiz inside the quiz, about the player's own run so far. What floor they're on, what they just
/// fought, their own name, their hero, the answer to their last question... (<see cref="PopQuizTopic"/>), four answers
/// each, one per answer button. <see cref="PopQuizSettings.RightNeeded"/> right wins it; <see cref="PopQuizSettings.Strikes"/>
/// wrong loses it, as does the time limit. After each answer the right one shows for a moment
/// (<see cref="PopQuizSettings.RevealSeconds"/>) before the next question, and presses wait for it.
///
/// The questions are written when the game starts, from <see cref="PopQuizFacts"/> (<see cref="DungeonRun"/> gathers them),
/// so it's never the first mini game of a match: there'd be too little to ask about (<see cref="MinigameCatalog.Pick"/>).
/// Each topic is asked once, in a shuffled order, and the answers are shuffled too.
/// </summary>
public sealed class PopQuiz : Minigame
{
    private readonly PopQuizSettings _settings;
    private readonly IReadOnlyList<PopQuizQuestion> _questions;

    public PopQuiz(DateTimeOffset introAt, MinigameSettings settings, Random random, PopQuizFacts facts)
        : base(MinigameKind.PopQuiz, introAt, settings.IntroSeconds, settings.PopQuiz.Seconds, settings.ResultSeconds)
    {
        _settings = settings.PopQuiz;
        _questions = Write(facts, random);
        AskedAt = PlaysFrom;
    }

    public int RightNeeded => _settings.RightNeeded;
    public int Strikes => _settings.Strikes;
    public double RevealSeconds => _settings.RevealSeconds;

    /// <summary>Every question written for this go, in the order they're asked (only as many as it takes are).</summary>
    public IReadOnlyList<PopQuizQuestion> Questions => _questions;

    /// <summary>Which of <see cref="Questions"/> is up (0-based); once it's over, the last one answered.</summary>
    public int Asking { get; private set; }

    public PopQuizQuestion Current => _questions[Asking];

    /// <summary>When the question up was asked: presses count from then (a moment after the last answer, its reveal).</summary>
    public DateTimeOffset AskedAt { get; private set; }

    public int Right { get; private set; }
    public int Wrong { get; private set; }

    /// <summary>The last answer: which question (index into <see cref="Questions"/>), the button pressed, and whether it was right. Null before the first.</summary>
    public (int Question, int Chosen, bool WasRight, DateTimeOffset At)? Last { get; private set; }

    /// <summary>The winning answer's right one shows a moment before the win.</summary>
    public override double WinPauseSeconds => _settings.RevealSeconds * 0.6;

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or > 3 || at < AskedAt)
            return MinigamePress.Ignored; // the action button, or the last answer's still showing

        var question = Current;
        var right = answerIndex == question.CorrectIndex;
        Last = (Asking, answerIndex, right, at);
        if (right) Right++; else Wrong++;
        if (Right >= RightNeeded)
            return MinigamePress.Won;
        if (Wrong >= Strikes)
            return MinigamePress.Lost;

        // The next question after the reveal (the topics come round again if somehow they've all been asked).
        Asking = (Asking + 1) % _questions.Count;
        AskedAt = at + TimeSpan.FromSeconds(RevealSeconds);
        return right ? MinigamePress.Hit : MinigamePress.Miss;
    }

    /// <summary>One question per topic the facts can answer, in a shuffled order.</summary>
    private static List<PopQuizQuestion> Write(PopQuizFacts facts, Random random)
    {
        var questions = new List<PopQuizQuestion>();
        void Ask(PopQuizTopic topic, string right, IEnumerable<string> wrong)
        {
            var others = wrong.Where(w => !string.IsNullOrWhiteSpace(w) && w != right).Distinct().OrderBy(_ => random.Next()).Take(3).ToList();
            if (others.Count < 3)
                return; // not enough to choose between: skip it
            var correct = random.Next(4);
            others.Insert(correct, right);
            questions.Add(new PopQuizQuestion(topic, others, correct));
        }

        Ask(PopQuizTopic.Floor, facts.Floor.ToString(), NumbersNear(facts.Floor, from: 1));
        if (facts.LastMonster is { } monster)
            Ask(PopQuizTopic.LastMonster, monster, facts.MonsterNames);
        Ask(PopQuizTopic.Name, facts.Name, facts.OtherNames.Concat(facts.MadeUpNames));
        Ask(PopQuizTopic.Hero, facts.Hero, facts.Heroes);
        Ask(PopQuizTopic.Kills, facts.Kills.ToString(), NumbersNear(facts.Kills, from: 0));
        if (facts.Lives is { } lives)
            Ask(PopQuizTopic.Lives, lives.ToString(), Enumerable.Range(0, facts.MaxLives + 2).Select(n => n.ToString()));
        if (facts.LastQuestion is { } last)
            Ask(PopQuizTopic.LastAnswer, last.Answers[last.CorrectIndex], last.Answers);
        if (facts.LastBoss is { } boss)
            Ask(PopQuizTopic.LastBoss, boss, facts.BossNames.Concat(facts.MonsterNames));
        return questions.OrderBy(_ => random.Next()).ToList();
    }

    /// <summary>Numbers a few either side of <paramref name="n"/> (not under <paramref name="from"/>), nearest first.</summary>
    private static IEnumerable<string> NumbersNear(int n, int from) =>
        new[] { n - 1, n + 1, n + 2, n - 2, n + 3, n + 4 }.Where(x => x >= from).Take(3).Select(x => x.ToString());
}

/// <summary>What a pop quiz question asks about; the screen words each one (dungeon.minigamePopQuizAsk... in the copy).</summary>
public enum PopQuizTopic
{
    /// <summary>"What floor are you on?"</summary>
    Floor,

    /// <summary>"What did you just fight?" (the monster whose death emptied the room).</summary>
    LastMonster,

    /// <summary>"What's your name?"</summary>
    Name,

    /// <summary>"Which hero are you?"</summary>
    Hero,

    /// <summary>"How many monsters have fallen on your run?" (the party's kills).</summary>
    Kills,

    /// <summary>"How many lives do you have left?" (only with lives on).</summary>
    Lives,

    /// <summary>"What was the answer to your last question?"</summary>
    LastAnswer,

    /// <summary>"Which boss guarded the last floor?" (from floor 2 on).</summary>
    LastBoss,
}

/// <summary>One pop quiz question: its topic and four answers, <see cref="CorrectIndex"/> the right one (never sent before it's answered).</summary>
public sealed record PopQuizQuestion(PopQuizTopic Topic, IReadOnlyList<string> Answers, int CorrectIndex);

/// <summary>
/// What the pop quiz can ask a player about, gathered when it starts (<see cref="DungeonRun"/>). Each topic comes with
/// the right answer and things to mix it up with: <paramref name="OtherNames"/> the other players' names and
/// <paramref name="MadeUpNames"/> some invented ones, <paramref name="MonsterNames"/> and <paramref name="BossNames"/> from
/// the dungeon's content, <paramref name="Heroes"/> every hero. <paramref name="Lives"/>: null with no limit on lives;
/// <paramref name="LastQuestion"/>: the last trivia question they answered (null if none yet); <paramref name="LastBoss"/>:
/// the boss of the floor above (null on floor 1).
/// </summary>
public sealed record PopQuizFacts(
    string Name,
    IReadOnlyList<string> OtherNames,
    IReadOnlyList<string> MadeUpNames,
    int Floor,
    string? LastMonster,
    IReadOnlyList<string> MonsterNames,
    string Hero,
    IReadOnlyList<string> Heroes,
    int Kills,
    int? Lives,
    int MaxLives,
    Questions.Question? LastQuestion,
    string? LastBoss,
    IReadOnlyList<string> BossNames);
