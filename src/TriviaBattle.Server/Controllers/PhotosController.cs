using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// Hero photos (Dungeon format). In the intro a player's station counts down (unless they press the action
/// button to say no, or, with photos asked for first, until they press it to say yes), takes the photo,
/// cuts out just the face, renders the knight wearing it, and sends both here as PNGs. The station's 3D knight wears the face; every other screen shows the portrait. Pictures stay in
/// memory and go when the match ends (<see cref="PhotoStore"/>).
/// </summary>
[ApiController]
[Route("api/photos")]
public class PhotosController(PhotoStore photos, MatchHost host, ILogger<PhotosController> logger) : ControllerBase
{
    /// <summary>
    /// The station sends the photo it took: a form with two PNG files, "face" and "portrait". Only accepted
    /// for a player in that match whose photo is coming (not said no to), once.
    /// </summary>
    [HttpPut("{matchId}/{stationId}")]
    [RequestSizeLimit(PhotoStore.MaxBytes)]
    public async Task<IActionResult> Put(string matchId, string stationId, IFormFile face, IFormFile portrait)
    {
        var picture = new HeroPicture(await ReadAsync(face), await ReadAsync(portrait));
        if (!PhotoStore.IsPng(picture.Face) || !PhotoStore.IsPng(picture.Portrait))
            return BadRequest(new { problems = new[] { "A hero photo is two PNGs: the face and the portrait." } });

        // Kept first, so it's there by the time the screens hear about it and come to fetch it.
        photos.Keep(matchId, stationId, picture);
        if (host.PhotoTaken(matchId, stationId))
            return NoContent();

        photos.Forget(matchId, stationId);
        return Conflict(new { problems = new[] { "No hero photo is coming for that player, or they already have one." } });
    }

    /// <summary>The station's camera came on for a photo that's coming: the server can play its countdown on the station's speaker.</summary>
    [HttpPost("{matchId}/{stationId}/camera")]
    public IActionResult CameraOn(string matchId, string stationId)
    {
        if (!host.PhotoCameraOn(matchId, stationId))
            return Conflict(new { problems = new[] { "No hero photo is coming at that station." } });
        logger.LogInformation("Match {Id}: {Station}'s camera is on for a hero photo", matchId, stationId);
        return NoContent();
    }

    /// <param name="kind">"face" or "portrait".</param>
    [HttpGet("{matchId}/{stationId}/{kind}")]
    public IActionResult Get(string matchId, string stationId, string kind)
    {
        var picture = photos.Find(matchId, stationId);
        var png = kind switch
        {
            "face" => picture?.Face,
            "portrait" => picture?.Portrait,
            _ => null,
        };
        if (png == null)
            return NotFound();

        // A face is never stored on a screen's disk: screens keep it in memory for the match.
        Response.Headers.CacheControl = "no-store";
        return File(png, "image/png");
    }

    private static async Task<byte[]> ReadAsync(IFormFile file)
    {
        using var bytes = new MemoryStream();
        await file.CopyToAsync(bytes);
        return bytes.ToArray();
    }
}
