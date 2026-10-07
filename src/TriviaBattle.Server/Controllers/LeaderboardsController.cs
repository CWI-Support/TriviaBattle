using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Modes;
using TriviaBattle.Server.Leaderboards;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// High-score boards, one per game mode.
///   GET /api/leaderboards?period=Today              -> every mode's board
///   GET /api/leaderboards/FreeForAll?period=AllTime -> one board
/// period: Today, Week or AllTime (default).
/// </summary>
[ApiController]
[Route("api/leaderboards")]
public class LeaderboardsController(LeaderboardService leaderboards) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(LeaderboardPeriod period = LeaderboardPeriod.AllTime)
    {
        var boards = new List<Leaderboard>();
        foreach (var mode in Enum.GetValues<GameModeKind>())
            boards.Add(await leaderboards.GetAsync(mode, period));

        return Ok(boards);
    }

    [HttpGet("{mode}")]
    public async Task<IActionResult> Get(GameModeKind mode, LeaderboardPeriod period = LeaderboardPeriod.AllTime) =>
        Ok(await leaderboards.GetAsync(mode, period));
}
