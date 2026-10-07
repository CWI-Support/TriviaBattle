using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Server.Data;

/// <summary>Picks random questions for a match from the database.</summary>
public class SqliteQuestionSource(IDbContextFactory<TriviaDbContext> dbFactory) : IQuestionSource
{
    public async Task<IReadOnlyList<Question>> PickQuestionsAsync(int categoryId, Difficulty difficulty, int count)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var candidates = await db.Questions
            .Include(q => q.Category)
            .Where(q => q.CategoryId == categoryId && q.IsActive)
            .ToListAsync();

        // Requested difficulty first (random order), then the other difficulties to fill any shortfall.
        // A question bank is a few hundred rows at most, so shuffling in memory is fine.
        return candidates
            .OrderBy(q => q.Difficulty == difficulty ? 0 : 1)
            .ThenBy(_ => Random.Shared.Next())
            .Take(count)
            .OrderBy(_ => Random.Shared.Next()) // so any fill-in questions aren't all bunched at the end
            .Select(q => q.ToQuestion())
            .ToList();
    }
}
