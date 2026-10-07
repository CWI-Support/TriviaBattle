using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>Category management for the admin page. Inactive categories are hidden from the kiosk.</summary>
[ApiController]
[Route("api/admin/categories")]
[AdminApiKey]
public class AdminCategoriesController(TriviaDbContext db) : ControllerBase
{
    public sealed record SaveCategoryRequest(string Name, bool IsActive = true);

    /// <summary>Every category, with how many active questions it has at each difficulty.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var categories = await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.IsActive,
                TotalQuestions = c.Questions.Count,
                Easy = c.Questions.Count(q => q.IsActive && q.Difficulty == Difficulty.Easy),
                Medium = c.Questions.Count(q => q.IsActive && q.Difficulty == Difficulty.Medium),
                Hard = c.Questions.Count(q => q.IsActive && q.Difficulty == Difficulty.Hard),
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveCategoryRequest request)
    {
        var problem = await ValidateNameAsync(request.Name, excludeId: null);
        if (problem != null)
            return BadRequest(new { problems = new[] { problem } });

        var category = new CategoryEntity { Name = request.Name.Trim(), IsActive = request.IsActive };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Ok(new { category.Id });
    }

    /// <summary>Rename and/or switch a category on or off.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveCategoryRequest request)
    {
        var category = await db.Categories.FindAsync(id);
        if (category == null)
            return NotFound();

        var problem = await ValidateNameAsync(request.Name, excludeId: id);
        if (problem != null)
            return BadRequest(new { problems = new[] { problem } });

        category.Name = request.Name.Trim();
        category.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Deletes the category AND all its questions. Match history keeps the category's name.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category == null)
            return NotFound();

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> ValidateNameAsync(string? name, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "The category needs a name.";
        if (name.Trim().Length > 40)
            return "Category names can be at most 40 characters.";

        var trimmed = name.Trim().ToLower();
        var taken = await db.Categories.AnyAsync(c => c.Id != excludeId && c.Name.ToLower() == trimmed);
        return taken ? $"There's already a category called \"{name.Trim()}\"." : null;
    }
}
