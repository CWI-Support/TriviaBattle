using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>Question editor API for the admin page. Changes apply to the next match started.</summary>
[ApiController]
[Route("api/admin/questions")]
[AdminApiKey]
public class AdminQuestionsController(TriviaDbContext db) : ControllerBase
{
    public sealed record QuestionDto(
        int Id,
        int CategoryId,
        string CategoryName,
        Difficulty Difficulty,
        string Text,
        IReadOnlyList<string> Answers,
        int CorrectIndex,
        string? MediaUrl,
        bool IsActive);

    public sealed record SaveQuestionRequest(
        int CategoryId,
        Difficulty Difficulty,
        string Text,
        List<string> Answers,
        int CorrectIndex,
        string? MediaUrl,
        bool IsActive);

    /// <summary>Lists questions, newest first. All filters are optional.</summary>
    [HttpGet]
    public async Task<IActionResult> List(int? categoryId, Difficulty? difficulty, string? search, bool includeInactive = true)
    {
        var query = db.Questions.Include(q => q.Category).AsQueryable();

        if (categoryId != null) query = query.Where(q => q.CategoryId == categoryId);
        if (difficulty != null) query = query.Where(q => q.Difficulty == difficulty);
        if (!includeInactive) query = query.Where(q => q.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(q => EF.Functions.Like(q.Text, $"%{term}%")
                || EF.Functions.Like(q.AnswerA, $"%{term}%") || EF.Functions.Like(q.AnswerB, $"%{term}%")
                || EF.Functions.Like(q.AnswerC, $"%{term}%") || EF.Functions.Like(q.AnswerD, $"%{term}%"));
        }

        var questions = await query.OrderByDescending(q => q.Id).Take(1000).ToListAsync();
        return Ok(questions.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var question = await db.Questions.Include(q => q.Category).FirstOrDefaultAsync(q => q.Id == id);
        return question == null ? NotFound() : Ok(ToDto(question));
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveQuestionRequest request)
    {
        var problems = await ValidateAsync(request);
        if (problems.Count > 0)
            return BadRequest(new { problems });

        var question = new QuestionEntity();
        Apply(request, question);
        db.Questions.Add(question);
        await db.SaveChangesAsync();

        return await Get(question.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveQuestionRequest request)
    {
        var question = await db.Questions.FindAsync(id);
        if (question == null)
            return NotFound();

        var problems = await ValidateAsync(request);
        if (problems.Count > 0)
            return BadRequest(new { problems });

        Apply(request, question);
        await db.SaveChangesAsync();
        return await Get(id);
    }

    /// <summary>Deletes for good. To just stop using a question, set it inactive instead.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var question = await db.Questions.FindAsync(id);
        if (question == null)
            return NotFound();

        db.Questions.Remove(question);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<List<string>> ValidateAsync(SaveQuestionRequest request)
    {
        var problems = QuestionRules.Validate(request.Text, request.Answers ?? [], request.CorrectIndex, request.MediaUrl);
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId))
            problems.Add("Pick a category.");
        return problems;
    }

    private static void Apply(SaveQuestionRequest request, QuestionEntity question)
    {
        question.CategoryId = request.CategoryId;
        question.Difficulty = request.Difficulty;
        question.Text = request.Text.Trim();
        question.AnswerA = request.Answers[0].Trim();
        question.AnswerB = request.Answers[1].Trim();
        question.AnswerC = request.Answers[2].Trim();
        question.AnswerD = request.Answers[3].Trim();
        question.CorrectIndex = request.CorrectIndex;
        question.MediaUrl = string.IsNullOrWhiteSpace(request.MediaUrl) ? null : request.MediaUrl.Trim();
        question.IsActive = request.IsActive;
    }

    private static QuestionDto ToDto(QuestionEntity q) => new(
        q.Id, q.CategoryId, q.Category?.Name ?? "", q.Difficulty, q.Text,
        [q.AnswerA, q.AnswerB, q.AnswerC, q.AnswerD], q.CorrectIndex, q.MediaUrl, q.IsActive);
}
