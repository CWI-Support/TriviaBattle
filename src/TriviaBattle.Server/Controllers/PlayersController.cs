using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Services;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// RFID player profiles, used by the kiosk:
///   1. A card is swiped -> GET  /api/players/rfid/{tag}  (known player? pre-fill their name)
///   2. "Start" pressed  -> POST /api/players             (create or rename the profile, get its id)
/// </summary>
[ApiController]
[Route("api/players")]
public class PlayersController(TriviaDbContext db, IWordFilterService wordFilter) : ControllerBase
{
    public sealed record PlayerDto(int Id, string RfidTag, string DisplayName);

    public sealed record RegisterPlayerRequest(string RfidTag, string DisplayName);

    [HttpGet("rfid/{rfidTag}")]
    public async Task<IActionResult> FindByRfid(string rfidTag)
    {
        var player = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.RfidTag == rfidTag.Trim());
        return player == null ? NotFound() : Ok(ToDto(player));
    }

    /// <summary>Creates the profile for a new card, or updates the name on a known one.</summary>
    [HttpPost]
    public async Task<IActionResult> Register(RegisterPlayerRequest request)
    {
        var tag = request.RfidTag.Trim();
        var name = request.DisplayName.Trim();

        if (tag.Length == 0)
            return BadRequest(new { problems = new[] { "Missing card id." } });
        if (name.Length == 0 || name.Length > MatchSetupValidator.MaxNameLength)
            return BadRequest(new { problems = new[] { $"Names must be 1-{MatchSetupValidator.MaxNameLength} characters." } });
        if (await wordFilter.CheckAsync(name) != null)
            return BadRequest(new { problems = new[] { $"Please choose a different name than '{name}'." } });

        var player = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.RfidTag == tag);
        if (player == null)
        {
            player = new PlayerProfileEntity { RfidTag = tag };
            db.PlayerProfiles.Add(player);
        }

        player.DisplayName = name;
        player.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToDto(player));
    }

    private static PlayerDto ToDto(PlayerProfileEntity p) => new(p.Id, p.RfidTag, p.DisplayName);
}
