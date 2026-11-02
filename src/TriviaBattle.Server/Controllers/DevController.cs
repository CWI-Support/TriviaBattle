using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Hardware.Input;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Controllers;

/// <summary>
/// Developer helpers for testing without hardware. Only available when "DevTools:Enabled" is
/// true (on by default in the Development environment).
/// Example: curl -X POST http://localhost:5000/api/dev/buttons/12   (Player 1 presses C)
/// </summary>
[ApiController]
[Route("api/dev")]
public class DevController(KeyboardButtonInput keyboard, ButtonMap buttonMap, MatchHost host, DevToolsSettings devTools) : ControllerBase
{
    [HttpPost("buttons/{buttonId:int}")]
    public IActionResult PressButton(int buttonId)
    {
        if (!devTools.Enabled)
            return NotFound();

        keyboard.PressButton(buttonId);
        return NoContent();
    }

    /// <summary>A card swipe at a station, standing in for the station card readers: gives a continue if one is on offer.</summary>
    [HttpPost("continue/{stationId}")]
    public IActionResult SwipeToContinue(string stationId)
    {
        if (!devTools.Enabled)
            return NotFound();

        return host.ContinueAtStation(stationId) ? NoContent() : Conflict(new { problems = new[] { "No continue on offer at that station." } });
    }

    /// <summary>Hands the player at a station a power-up (Potion, Fireball, Freeze, FiftyFifty, Shield, TimeCrystal), to test it.</summary>
    [HttpPost("powerup/{stationId}/{powerUp}")]
    public IActionResult GivePowerUp(string stationId, PowerUp powerUp)
    {
        if (!devTools.Enabled)
            return NotFound();

        return host.GivePowerUp(stationId, powerUp) ? NoContent() : Conflict(new { problems = new[] { "Nobody is playing a dungeon game at that station." } });
    }

    /// <summary>The keyboard map plus what each button is, for the /dev/buttons page.</summary>
    [HttpGet("keyboard")]
    public IActionResult GetKeyboardMap()
    {
        if (!devTools.Enabled)
            return NotFound();

        var keys = keyboard.KeyMap.Select(kv =>
        {
            var button = buttonMap.Assignments.FirstOrDefault(a => a.ButtonId == kv.Value);
            return new
            {
                key = kv.Key,
                buttonId = kv.Value,
                stationId = button?.StationId,
                answer = button == null ? null : ButtonMap.AnswerIndexToLetter(button.AnswerIndex),
            };
        });

        return Ok(keys.OrderBy(k => k.buttonId));
    }
}
