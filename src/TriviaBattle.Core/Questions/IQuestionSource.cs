namespace TriviaBattle.Core.Questions;

/// <summary>
/// Supplies the questions for a match. The server implements this on top of SQLite;
/// tests use a simple in-memory list.
/// </summary>
public interface IQuestionSource
{
    /// <summary>
    /// Picks <paramref name="count"/> random active questions from a category, preferring the
    /// requested difficulty. If there aren't enough at that difficulty, the rest are filled from
    /// the category's other difficulties so a match never runs short.
    /// </summary>
    Task<IReadOnlyList<Question>> PickQuestionsAsync(int categoryId, Difficulty difficulty, int count);
}
