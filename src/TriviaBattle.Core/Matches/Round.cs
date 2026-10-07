using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// An answer that has been locked in for this question.
/// </summary>
/// <param name="OwnerId">
/// Who the answer counts for: a station id in individual modes, or a team id in 2v2 Group mode.
/// </param>
/// <param name="PressedByStationId">The station whose button press locked it in.</param>
public sealed record LockedAnswer(
    string OwnerId,
    string PressedByStationId,
    int AnswerIndex,
    DateTimeOffset PressedAt,
    long Sequence);

/// <summary>The outcome of one question for one answer owner (player or team).</summary>
/// <param name="Answer">Null if they didn't answer in time.</param>
/// <param name="ResponseTime">Time from question open to the press. Null if no answer.</param>
public sealed record RoundResult(
    string OwnerId,
    LockedAnswer? Answer,
    bool IsCorrect,
    int Points,
    TimeSpan? ResponseTime);

/// <summary>One question within a match, and everything that happened during it.</summary>
public sealed class Round(int number, Question question)
{
    private readonly Dictionary<string, LockedAnswer> _answers = new();

    /// <summary>1-based question number.</summary>
    public int Number { get; } = number;
    public Question Question { get; } = question;

    /// <summary>Set when the buttons go live (start of QuestionOpen).</summary>
    public DateTimeOffset? OpenedAt { get; private set; }

    /// <summary>When the answer window ends. Presses after this are ignored.</summary>
    public DateTimeOffset? ClosesAt { get; private set; }

    /// <summary>Locked answers, keyed by owner id (station id or team id).</summary>
    public IReadOnlyDictionary<string, LockedAnswer> Answers => _answers;

    /// <summary>Filled in at Reveal; empty before that.</summary>
    public IReadOnlyList<RoundResult> Results { get; private set; } = [];

    public bool IsRevealed => Results.Count > 0;

    public TimeSpan AnswerWindow => (ClosesAt - OpenedAt) ?? TimeSpan.Zero;

    public void Open(DateTimeOffset now, TimeSpan answerWindow)
    {
        OpenedAt = now;
        ClosesAt = now + answerWindow;
    }

    public bool AcceptsPressAt(DateTimeOffset pressedAt) =>
        OpenedAt != null && pressedAt >= OpenedAt && pressedAt <= ClosesAt;

    public void Lock(LockedAnswer answer) => _answers[answer.OwnerId] = answer;

    public void SetResults(IReadOnlyList<RoundResult> results) => Results = results;

    public TimeSpan ResponseTimeOf(LockedAnswer answer) => answer.PressedAt - (OpenedAt ?? answer.PressedAt);
}
