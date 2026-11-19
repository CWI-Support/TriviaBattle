using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Diagnostics;
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
public class PhotosController(PhotoStore photos, MatchHost host, LobbyHost lobby, ScreenRegistry screens, ILogger<PhotosController> logger) : ControllerBase
{
    /// <summary>
    /// The photo taken in the lobby, right after choosing a hero: the same pictures, and takeAtMs, the take
    /// it's for (the seat's photoTakeAtMs). Only while that station is on the Photo step for that take, with
    /// no photo yet. It goes into the player's game when it starts.
    /// </summary>
    [HttpPut("lobby/{stationId}")]
    [RequestSizeLimit(PhotoStore.MaxBytes)]
    public async Task<IActionResult> PutLobby(string stationId, IFormFile face, IFormFile portrait, [FromForm] long takeAtMs, IFormFile? sheet = null)
    {
        var picture = await PictureFrom(face, portrait, sheet);
        if (Problem(picture) is { } problem)
            return BadRequest(new { problems = new[] { problem } });

        if (lobby.PhotoArrived(stationId, takeAtMs, picture))
            return NoContent();
        return Conflict(new { problems = new[] { "That station isn't taking a hero photo, or already has one." } });
    }

    /// <summary>The station's camera came on for the lobby's photo: its countdown can be heard on its speaker.</summary>
    [HttpPost("lobby/{stationId}/camera")]
    public IActionResult LobbyCameraOn(string stationId) =>
        lobby.PhotoCameraOn(stationId) ? NoContent() : Conflict(new { problems = new[] { "That station isn't taking a hero photo." } });

    /// <summary>
    /// A station without a camera skips the lobby's photo step. Not if another screen open as that station
    /// has one (a laptop on http beside the station's own PC, say): that one takes the photo.
    /// </summary>
    [HttpPost("lobby/{stationId}/skip")]
    public IActionResult SkipLobby(string stationId)
    {
        if (screens.AnyWithCamera($"player:{stationId}"))
            return Conflict(new { problems = new[] { "Another screen at that station has a camera: it takes the photo." } });
        lobby.SkipPhoto(stationId);
        return NoContent();
    }

    /// <param name="kind">"face", "portrait" or "sheet".</param>
    [HttpGet("lobby/{stationId}/{kind}")]
    public IActionResult GetLobby(string stationId, string kind) => Picture(photos.FindLobby(stationId), kind);

    /// <summary>
    /// The station sends the photo it took: a form with two PNG files, "face" and "portrait", and its animated
    /// "sheet" (a WebP or a PNG; left out by a station that can't draw in 3D). Only accepted for a player in that match
    /// whose photo is coming (not said no to), once.
    /// </summary>
    [HttpPut("{matchId}/{stationId}")]
    [RequestSizeLimit(PhotoStore.MaxBytes)]
    public async Task<IActionResult> Put(string matchId, string stationId, IFormFile face, IFormFile portrait, IFormFile? sheet = null)
    {
        var picture = await PictureFrom(face, portrait, sheet);
        if (Problem(picture) is { } problem)
            return BadRequest(new { problems = new[] { problem } });

        // Kept only once the match takes it (and before the screens hear about it and come to fetch it). A
        // refused one, say from a second screen open as the same station, never touches the one it has.
        if (host.PhotoTaken(matchId, stationId, () => photos.Keep(matchId, stationId, picture)))
            return NoContent();
        logger.LogWarning("Match {Id}: a hero photo for {Station} was refused (is another screen open as that station?)", matchId, stationId);
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

    /// <param name="kind">"face", "portrait" or "sheet".</param>
    [HttpGet("{matchId}/{stationId}/{kind}")]
    public IActionResult Get(string matchId, string stationId, string kind) => Picture(photos.Find(matchId, stationId), kind);

    private IActionResult Picture(HeroPicture? picture, string kind)
    {
        var bytes = kind switch
        {
            "face" => picture?.Face,
            "portrait" => picture?.Portrait,
            "sheet" => picture?.Sheet,
            _ => null,
        };
        if (bytes == null)
            return NotFound();

        // A face is never stored on a screen's disk: screens keep it in memory for the match.
        Response.Headers.CacheControl = "no-store";
        return File(bytes, PhotoStore.ContentTypeOf(bytes));
    }

    private static async Task<HeroPicture> PictureFrom(IFormFile face, IFormFile portrait, IFormFile? sheet) =>
        new(await ReadAsync(face), await ReadAsync(portrait), sheet == null ? null : await ReadAsync(sheet));

    /// <summary>What's wrong with the pictures a station sent, or null: the face and portrait are PNGs, the sheet a WebP or a PNG.</summary>
    private static string? Problem(HeroPicture picture)
    {
        if (!PhotoStore.IsPng(picture.Face) || !PhotoStore.IsPng(picture.Portrait))
            return "A hero photo's face and portrait are PNGs.";
        if (picture.Sheet is { } sheet && !PhotoStore.IsWebP(sheet) && !PhotoStore.IsPng(sheet))
            return "A hero photo's animated sheet is a WebP or a PNG.";
        return null;
    }

    private static async Task<byte[]> ReadAsync(IFormFile file)
    {
        using var bytes = new MemoryStream();
        await file.CopyToAsync(bytes);
        return bytes.ToArray();
    }
}
