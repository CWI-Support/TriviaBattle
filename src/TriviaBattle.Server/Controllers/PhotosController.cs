using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// Hero photos (Dungeon format). A player says yes in the intro (the action button); their station counts
/// down, takes the photo, turns it into a small pixel portrait and sends it here as a PNG. Every screen
/// then fetches it to put their face on their hero. Pictures stay in memory and go when the match ends
/// (<see cref="PhotoStore"/>).
/// </summary>
[ApiController]
[Route("api/photos")]
public class PhotosController(PhotoStore photos, MatchHost host) : ControllerBase
{
    /// <summary>The station sends the photo it took. Only accepted from a player in that match who asked for one, once.</summary>
    [HttpPut("{matchId}/{stationId}")]
    [RequestSizeLimit(PhotoStore.MaxBytes)]
    public async Task<IActionResult> Put(string matchId, string stationId)
    {
        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body);
        var png = body.ToArray();
        if (!PhotoStore.IsPng(png))
            return BadRequest(new { problems = new[] { "A hero photo must be a PNG." } });

        // Kept first, so it's there by the time the screens hear about it and come to fetch it.
        photos.Keep(matchId, stationId, png);
        if (host.PhotoTaken(matchId, stationId))
            return NoContent();

        photos.Forget(matchId, stationId);
        return Conflict(new { problems = new[] { "That player didn't ask for a hero photo, or already has one." } });
    }

    [HttpGet("{matchId}/{stationId}")]
    public IActionResult Get(string matchId, string stationId)
    {
        var png = photos.Find(matchId, stationId);
        if (png == null)
            return NotFound();

        // A face is never stored on a screen's disk: screens keep it in memory for the match.
        Response.Headers.CacheControl = "no-store";
        return File(png, "image/png");
    }
}
