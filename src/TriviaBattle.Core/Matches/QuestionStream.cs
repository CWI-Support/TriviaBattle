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
public sealed record GivenAnswer(
    string OwnerId,
    int Number,
    Question Question,
    string PressedByStationId,
    int AnswerIndex,
    bool IsCorrect,
    int Points,
    DateTimeOffset PressedAt,
    TimeSpan ResponseTime);

/// <summary>
/// One answer owner's run through the questions during the Playing phase. Every player has
/// their own stream, except in 2v2 Group where each team shares one. Answering (right or wrong)
/// moves straight on to the next question, so the faster you go, the more you answer.
///
/// Each stream has its own question order (so neighbours can't read ahead on each other's
/// screens). If someone gets through every question, the order starts again from the top.
/// </summary>
public sealed class QuestionStream(string ownerId, IReadOnlyList<Question> questionOrder)
{
    private readonly List<GivenAnswer> _answers = new();

    public string OwnerId { get; } = ownerId;

    /// <summary>The question on screen now (also the one shown first, before Playing starts).</summary>
    public Question CurrentQuestion => questionOrder[_answers.Count % questionOrder.Count];

    /// <summary>1-based number of the current question for this owner.</summary>
    public int CurrentNumber => _answers.Count + 1;

    /// <summary>When the current question appeared. Null until Playing starts.</summary>
    public DateTimeOffset? CurrentShownAt { get; private set; }

    /// <summary>With the Lockout penalty: buttons are dead until this time. Null when not locked out.</summary>
    public DateTimeOffset? LockedOutUntil { get; private set; }

    public IReadOnlyList<GivenAnswer> Answers => _answers;

    public GivenAnswer? LastAnswer => _answers.Count > 0 ? _answers[^1] : null;

    public bool IsLockedOutAt(DateTimeOffset time) => LockedOutUntil != null && time < LockedOutUntil;

    /// <summary>Shows the first question. Called when Playing starts.</summary>
    public void Start(DateTimeOffset now) => CurrentShownAt = now;

    /// <summary>Records an answer to the current question and moves on to the next one.</summary>
    public void Record(GivenAnswer answer, DateTimeOffset now)
    {
        _answers.Add(answer);
        CurrentShownAt = now;
    }

    public void LockOut(DateTimeOffset until) => LockedOutUntil = until;

    public void EndLockout() => LockedOutUntil = null;
}
