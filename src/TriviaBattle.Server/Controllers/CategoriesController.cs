using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Data;

namespace TriviaBattle.Server.Controllers;

/// <summary>Read-only category list for the kiosk. (Editing lives in the admin API, Phase 5.)</summary>
[ApiController]
[Route("api/categories")]
public class CategoriesController(TriviaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetActive()
    {
        var categories = await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                QuestionCount = c.Questions.Count(q => q.IsActive),
            })
            .ToListAsync();

        return Ok(categories);
    }
}
