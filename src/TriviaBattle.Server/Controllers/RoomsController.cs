using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Controllers;

/// <summary>Rooms and stations from config/stations.json (used by the launcher page).</summary>
[ApiController]
[Route("api/rooms")]
public class RoomsController(StationLayout stations, ScreenStateFactory screenStates, DevToolsSettings devTools) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        var rooms = stations.RoomIds.Select(screenStates.Room);

        return Ok(new { devToolsEnabled = devTools.Enabled, rooms });
    }
}
