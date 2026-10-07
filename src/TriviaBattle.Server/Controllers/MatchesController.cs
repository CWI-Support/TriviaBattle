using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Middleware;
using TriviaBattle.Server.Services;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// Starting, viewing and aborting matches. Screens get live updates over SignalR;
/// these endpoints are for the kiosk (start), the admin page, and debugging.
/// </summary>
[ApiController]
[Route("api/matches")]
public class MatchesController(MatchHost host, IWordFilterService wordFilter) : ControllerBase
{
    /// <summary>Starts a match. Returns 400 with a list of plain-English problems if the setup isn't valid.</summary>
    [HttpPost]
    public async Task<IActionResult> Start(MatchSetup setup)
    {
        foreach (var seat in setup.Seats)
        {
            if (await wordFilter.CheckAsync(seat.PlayerName) != null)
                return BadRequest(new { problems = new[] { $"Please choose a different name than '{seat.PlayerName}'." } });
        }

        var result = await host.StartMatchAsync(setup);
        if (!result.Started)
            return BadRequest(new { problems = result.Problems });

        return Ok(new { matchId = result.MatchId });
    }

    /// <summary>All running matches, with full (admin) visibility.</summary>
    [HttpGet]
    [AdminApiKey]
    public IActionResult GetAll() => Ok(host.GetAllSnapshots(Viewer.Admin));

    /// <summary>One match as the main screen sees it.</summary>
    [HttpGet("{matchId}")]
    public IActionResult Get(string matchId)
    {
        var snapshot = host.GetSnapshot(matchId, Viewer.Main);
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("{matchId}/abort")]
    [AdminApiKey]
    public IActionResult Abort(string matchId) => host.AbortMatch(matchId) ? NoContent() : NotFound();
}
