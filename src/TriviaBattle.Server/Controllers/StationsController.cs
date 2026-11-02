using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Controllers;

/// <summary>Things that happen at a player station besides its buttons: for now, its card reader.</summary>
[ApiController]
[Route("api/stations")]
public class StationsController(StationLayout stations, MatchHost host, LobbyHost lobby) : ControllerBase
{
    public sealed record SwipeRequest(string CardId);

    /// <summary>
    /// A card swiped at a station (its reader types into the station screen, js/card-reader.js). In a solo
    /// game waiting on "Continue?" it's a continue; at a station not in a game it's the player arriving
    /// (the arcade lobby: whoever the card belongs to, or a new hero for a new card).
    /// </summary>
    [HttpPost("{stationId}/swipe")]
    public async Task<IActionResult> Swipe(string stationId, SwipeRequest request)
    {
        if (stations.Find(stationId) == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.CardId))
            return BadRequest(new { problems = new[] { "Missing card id." } });

        if (host.ContinueAtStation(stationId))
            return Ok(new { took = "continue" });
        if (host.IsPlaying(stationId))
            return Conflict(new { problems = new[] { "That station is in a game." } });

        await lobby.SwipeAsync(stationId, request.CardId);
        return Ok(new { took = "lobby" });
    }
}
