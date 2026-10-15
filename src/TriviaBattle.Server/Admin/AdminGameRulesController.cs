using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>
///   GET /api/admin/game-rules  -> round length and wrong-answer penalty
///   PUT /api/admin/game-rules  -> change them (applies from the next match)
/// </summary>
[ApiController]
[Route("api/admin/game-rules")]
[AdminApiKey]
public class AdminGameRulesController(GameRulesService gameRules) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await gameRules.GetAsync());

    [HttpPut]
    public async Task<IActionResult> Save(GameRules rules)
    {
        var problems = GameRulesService.Validate(rules);
        if (problems.Count > 0)
            return BadRequest(new { problems });

        await gameRules.SaveAsync(rules);
        return NoContent();
    }
}
