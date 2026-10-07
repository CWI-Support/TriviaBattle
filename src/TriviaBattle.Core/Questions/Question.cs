namespace TriviaBattle.Core.Questions;

public enum Difficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// A multiple-choice question as the game engine sees it. Always exactly four answers
/// (one per physical button), with <see cref="CorrectIndex"/> 0-3 meaning A-D.
/// </summary>
public sealed record Question(
    int Id,
    string Category,
    Difficulty Difficulty,
    string Text,
    IReadOnlyList<string> Answers,
    int CorrectIndex,
    string? MediaUrl = null)
{
    public const int AnswerCount = 4;

    /// <summary>Returns a copy with the answers in a random order (and CorrectIndex updated to match).</summary>
    public Question WithShuffledAnswers(Random random)
    {
        var order = Enumerable.Range(0, AnswerCount).OrderBy(_ => random.Next()).ToArray();
        var shuffled = order.Select(i => Answers[i]).ToList();
        return this with { Answers = shuffled, CorrectIndex = Array.IndexOf(order, CorrectIndex) };
    }
}
