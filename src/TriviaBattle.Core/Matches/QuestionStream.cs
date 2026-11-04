using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>One answer given during a match.</summary>
/// <param name="OwnerId">
/// Who the answer counts for: a station id in individual modes, or a team id in 2v2 Group.
/// </param>
/// <param name="Number">1-based: this owner's 1st, 2nd, 3rd... answer.</param>
/// <param name="PressedByStationId">The station whose button press gave the answer.</param>
/// <param name="Points">Points added (or, as a negative number, taken off) for this answer.</param>
/// <param name="ResponseTime">Time from the question appearing to the press.</param>
/// <param name="TimedOut">
/// Dungeon format: nobody answered before the monster's hourglass ran out. There was no press
/// (<see cref="AnswerIndex"/> is -1) and the question is gone.
/// </param>
public sealed record GivenAnswer(
    string OwnerId,
    int Number,
    Question Question,
    string PressedByStationId,
    int AnswerIndex,
    bool IsCorrect,
    int Points,
    DateTimeOffset PressedAt,
    TimeSpan ResponseTime,
    bool TimedOut = false);

/// <summary>
/// One answer owner's run through the questions during the Playing phase. Every player has
/// their own stream, except in 2v2 Group where each team shares one. Answering (right or wrong)
/// moves straight on to the next question, so the faster you go, the more you answer.
///
/// Each stream has its own question order (so neighbours can't read ahead on each other's
/// screens). If someone gets through every question, the order starts again from the top.
/// A Dungeon floor can ask for a difficulty: the next question is then the first in the order of
/// that difficulty not yet asked (or the first not yet asked, if none of that difficulty is left).
/// </summary>
public sealed class QuestionStream(string ownerId, IReadOnlyList<Question> questionOrder)
{
    private readonly List<GivenAnswer> _answers = new();
    private readonly bool[] _asked = new bool[questionOrder.Count];
    private int _current;

    public string OwnerId { get; } = ownerId;

    /// <summary>The question on screen now (also the one shown first, before Playing starts).</summary>
    public Question CurrentQuestion => questionOrder[_current];

    /// <summary>1-based number of the current question for this owner.</summary>
    public int CurrentNumber => _answers.Count + 1;

    /// <summary>When the current question appeared (or will: after a battle beat it's a moment ahead). Null until Playing starts.</summary>
    public DateTimeOffset? CurrentShownAt { get; private set; }

    /// <summary>With the Lockout penalty: buttons are dead until this time. Null when not locked out.</summary>
    public DateTimeOffset? LockedOutUntil { get; private set; }

    public IReadOnlyList<GivenAnswer> Answers => _answers;

    public GivenAnswer? LastAnswer => _answers.Count > 0 ? _answers[^1] : null;

    public bool IsLockedOutAt(DateTimeOffset time) => LockedOutUntil != null && time < LockedOutUntil;

    /// <summary>Shows the current question from now: the first one when Playing starts, the same one again after a continue.</summary>
    public void Start(DateTimeOffset now) => CurrentShownAt = now;

    /// <summary>
    /// Records an answer to the current question and moves on to the next one, which appears at
    /// <paramref name="nextShownAt"/>: straight away, or after a Dungeon battle beat.
    /// </summary>
    /// <param name="wanted">The difficulty the next question should be (a Dungeon floor's mix); null = the next in order.</param>
    public void Record(GivenAnswer answer, DateTimeOffset nextShownAt, Difficulty? wanted = null)
    {
        _answers.Add(answer);
        _asked[_current] = true;
        _current = NextIndex(wanted);
        CurrentShownAt = nextShownAt;
    }

    /// <summary>Before any answer: makes the first question one of this difficulty (if there is one).</summary>
    public void Prefer(Difficulty wanted)
    {
        if (_answers.Count == 0)
            _current = NextIndex(wanted);
    }

    /// <summary>The first question in the order not asked yet, of the wanted difficulty if one is left. All asked: start over.</summary>
    private int NextIndex(Difficulty? wanted)
    {
        if (_asked.All(asked => asked))
            Array.Clear(_asked);

        var fallback = -1;
        for (var i = 0; i < questionOrder.Count; i++)
        {
            if (_asked[i])
                continue;
            if (wanted == null || questionOrder[i].Difficulty == wanted)
                return i;
            if (fallback < 0)
                fallback = i;
        }
        return fallback;
    }

    public void LockOut(DateTimeOffset until) => LockedOutUntil = until;

    public void EndLockout() => LockedOutUntil = null;
}
