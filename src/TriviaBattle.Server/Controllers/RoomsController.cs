using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Diagnostics;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// Rooms and stations from config/stations.json, and which screens are open where (used by the launcher
/// page, so a second PC picks a station nobody has yet). screens: "player:A1" -> the addresses it's open on.
/// </summary>
[ApiController]
[Route("api/rooms")]
public class RoomsController(StationLayout stations, ScreenStateFactory screenStates, DevToolsSettings devTools, ScreenRegistry screenRegistry) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        var rooms = stations.RoomIds.Select(screenStates.Room);
        var screens = screenRegistry.All
            .GroupBy(s => s.Key)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Address).ToList());

        return Ok(new { devToolsEnabled = devTools.Enabled, rooms, screens });
    }
}
