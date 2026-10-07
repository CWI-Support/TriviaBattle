using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Middleware;
using TriviaBattle.Server.Services;

namespace TriviaBattle.Server.Admin;

/// <summary>RFID player profiles for the admin page: find, rename (e.g. an offensive name), delete.</summary>
[ApiController]
[Route("api/admin/players")]
[AdminApiKey]
public class AdminPlayersController(TriviaDbContext db, IWordFilterService wordFilter) : ControllerBase
{
    public sealed record RenameRequest(string DisplayName);

    [HttpGet]
    public async Task<IActionResult> List(string? search)
    {
        var query = db.PlayerProfiles.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.Like(p.DisplayName, $"%{search.Trim()}%") || p.RfidTag == search.Trim());

        var players = await query.OrderByDescending(p => p.LastSeenAt).Take(500).ToListAsync();
        return Ok(players.Select(p => new { p.Id, p.RfidTag, p.DisplayName, p.CreatedAt, p.LastSeenAt }));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, RenameRequest request)
    {
        var player = await db.PlayerProfiles.FindAsync(id);
        if (player == null)
            return NotFound();

        var name = request.DisplayName?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MatchSetupValidator.MaxNameLength)
            return BadRequest(new { problems = new[] { $"Names must be 1-{MatchSetupValidator.MaxNameLength} characters." } });
        if (await wordFilter.CheckAsync(name) != null)
            return BadRequest(new { problems = new[] { "That name is blocked by the word filter." } });

        player.DisplayName = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Forgets the card. Their past scores stay in match history (under the name they played with);
    /// the card will be treated as new next time it's swiped.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var player = await db.PlayerProfiles.FindAsync(id);
        if (player == null)
            return NotFound();

        db.PlayerProfiles.Remove(player);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
