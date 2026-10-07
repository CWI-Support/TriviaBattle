using TriviaBattle.Core.Questions;

namespace TriviaBattle.Server.Data.Entities;

/// <summary>
/// A stored multiple-choice question. Table: Questions.
/// The four answers are separate columns so they're easy to read and edit in any SQLite browser.
/// </summary>
public class QuestionEntity
{
    public int Id { get; set; }

    public int CategoryId { get; set; }
    public CategoryEntity? Category { get; set; }

    /// <summary>Stored as text ("Easy", "Medium", "Hard").</summary>
    public Difficulty Difficulty { get; set; }

    public string Text { get; set; } = "";
    public string AnswerA { get; set; } = "";
    public string AnswerB { get; set; } = "";
    public string AnswerC { get; set; } = "";
    public string AnswerD { get; set; } = "";

    /// <summary>0 = A, 1 = B, 2 = C, 3 = D.</summary>
    public int CorrectIndex { get; set; }

    /// <summary>Optional image/video shown with the question, e.g. "/media/questions/eiffel.jpg".</summary>
    public string? MediaUrl { get; set; }

    /// <summary>Inactive questions are never picked for a match.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Question ToQuestion() => new(
        Id,
        Category?.Name ?? "",
        Difficulty,
        Text,
        [AnswerA, AnswerB, AnswerC, AnswerD],
        CorrectIndex,
        MediaUrl);
}
