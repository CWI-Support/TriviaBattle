using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>
/// The Insights tab: the design doc's indicators (time in use, dwell time, returning players) over the
/// last few days, from the match history. Aborted matches are left out. The working-out is in <see cref="Insights"/>.
/// </summary>
[ApiController]
[Route("api/admin/insights")]
[AdminApiKey]
public class AdminInsightsController(TriviaDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var toUtc = clock.GetUtcNow().UtcDateTime;
        // From the start of the first day, in the venue's own time.
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(toUtc, TimeZoneInfo.Local).Date;
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localToday.AddDays(1 - days), TimeZoneInfo.Local);

        var matches = await db.Matches
            .Where(m => !m.WasAborted && m.StartedAtUtc >= fromUtc)
            .Select(m => new { m.StartedAtUtc, m.EndedAtUtc, Players = m.Participants.Select(p => p.PlayerProfileId).ToList() })
            .ToListAsync();

        var playedBefore = await db.Matches
            .Where(m => !m.WasAborted && m.StartedAtUtc < fromUtc)
            .SelectMany(m => m.Participants)
            .Where(p => p.PlayerProfileId != null)
            .Select(p => p.PlayerProfileId!.Value)
            .Distinct()
            .ToListAsync();

        var report = Insights.Calculate(
            matches.Select(m => new PlayedMatch(m.StartedAtUtc, m.EndedAtUtc, m.Players)).ToList(),
            fromUtc, toUtc, playedBefore.ToHashSet(), TimeZoneInfo.Local);
        return Ok(report);
    }
}
