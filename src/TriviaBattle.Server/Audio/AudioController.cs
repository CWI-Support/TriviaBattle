using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TriviaBattle.Hardware.Audio;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Audio;

/// <summary>
///   GET  /api/audio              -> { enabled }: whether the server plays the room audio (the main screen then mutes the intro video)
///   GET  /api/admin/audio        -> each room's sound card status and speakers, plus the playback devices Windows has
///   POST /api/admin/audio/test   -> play a test sound on one speaker: { roomId, speaker } (station id, "room", "attract" or "sub")
///   PUT  /api/admin/audio/volumes -> the volume sliders: { music, attract, intro, stingers, sub }, 0-2, applied live
/// </summary>
[ApiController]
public class AudioController(IOptions<AudioSettings> settings, RoomSoundService sound) : ControllerBase
{
    public sealed record TestRequest(string RoomId, string Speaker);

    [HttpGet("api/audio")]
    public IActionResult Get() => Ok(new { enabled = settings.Value.Enabled });

    [HttpGet("api/admin/audio")]
    [AdminApiKey]
    public IActionResult Status() => Ok(new
    {
        enabled = settings.Value.Enabled,
        notPlayingReason = sound.NotPlayingReason,
        volumes = sound.Volumes,
        rooms = sound.Rooms.Values.Select(r => new { r.RoomId, r.Status, r.Speakers }),
        devices = AudioService.GetAvailableDeviceNames(),
    });

    [HttpPut("api/admin/audio/volumes")]
    [AdminApiKey]
    public async Task<IActionResult> SetVolumes(AudioVolumes volumes)
    {
        if (!volumes.IsValid)
            return BadRequest(new { problems = new[] { $"Volumes must be between 0 and {AudioVolumes.Max * 100}%." } });

        await sound.SetVolumesAsync(volumes);
        return NoContent();
    }

    [HttpPost("api/admin/audio/test")]
    [AdminApiKey]
    public IActionResult Test(TestRequest request)
    {
        if (!sound.Rooms.TryGetValue(request.RoomId, out var room))
            return BadRequest(new { problems = new[] { settings.Value.Enabled ? "Room audio is still starting, or that room doesn't exist." : "Room audio is off (Audio:Enabled)." } });

        return room.TestSpeaker(request.Speaker)
            ? NoContent()
            : BadRequest(new { problems = new[] { $"Room {request.RoomId} has no '{request.Speaker}' speaker." } });
    }
}
