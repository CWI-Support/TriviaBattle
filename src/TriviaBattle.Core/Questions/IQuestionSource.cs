namespace TriviaBattle.Core.Questions;

/// <summary>
/// Supplies the questions for a match. The server implements this on top of SQLite;
/// tests use a simple in-memory list.
/// </summary>
public interface IQuestionSource
{
    /// <summary>
    /// Picks <paramref name="count"/> random active questions from one or more categories (mixed
    /// together), preferring the requested difficulty. If there aren't enough at that difficulty,
    /// the rest are filled from the other difficulties so a match never runs short.
    /// </summary>
    Task<IReadOnlyList<Question>> PickQuestionsAsync(IReadOnlyList<int> categoryIds, Difficulty difficulty, int count);
}
