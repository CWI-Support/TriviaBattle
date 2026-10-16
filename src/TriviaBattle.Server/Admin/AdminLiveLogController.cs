using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Diagnostics;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>
///   GET /api/admin/live-log?after=N -> log lines newer than id N, and every screen the rooms should have
///                                     with whether it's connected. The admin "Live log" tab polls this.
/// </summary>
[ApiController]
[Route("api/admin/live-log")]
[AdminApiKey]
public class AdminLiveLogController(LiveLog liveLog, ScreenRegistry screens, StationLayout stations) : ControllerBase
{
    [HttpGet]
    public IActionResult Get(long after = 0)
    {
        var connected = screens.All.ToLookup(s => s.Key);

        // Every screen each room should have, so a missing one stands out.
        var expected = stations.RoomIds.SelectMany(roomId =>
            new[] { ($"main:{roomId}", "Main screen"), ($"kiosk:{roomId}", "Kiosk") }
                .Concat(stations.InRoom(roomId).Select(s => ($"player:{s.Id}", s.Label)))
                .Select(screen => new
                {
                    room = stations.RoomName(roomId),
                    name = screen.Item2,
                    connections = connected[screen.Item1].Select(c => new { c.Address, c.ConnectedAt }),
                }));

        return Ok(new { serverId = ServerInstance.Id, lastId = liveLog.LastId, entries = liveLog.After(after), screens = expected });
    }
}
