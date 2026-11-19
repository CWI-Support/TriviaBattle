using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Core.Arcade;
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
public partial class DevController(KeyboardButtonInput keyboard, ButtonMap buttonMap, MatchHost host, DevToolsSettings devTools,
    IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>
    /// /dev/heroes's "Save all": one of a hero's own pictures into wwwroot/media/heroes, so redrawing them after a hero's
    /// model or look changes is one press. Only those files ({hero}.png, {hero}-head.png, {hero}-sheet.webp), only for
    /// heroes there are, and only a PNG or WebP as named. The request body is the picture.
    /// </summary>
    [HttpPut("heroes/{file}")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> SaveHeroPicture(string file)
    {
        if (!devTools.Enabled)
            return NotFound();
        var name = HeroPictureName().Match(file);
        if (!name.Success || !Heroes.All.Contains(name.Groups["hero"].Value))
            return BadRequest(new { problems = new[] { "Only a hero's {hero}.png, {hero}-head.png or {hero}-sheet.webp." } });

        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body);
        var picture = body.ToArray();
        if (file.EndsWith(".webp") ? !PhotoStore.IsWebP(picture) : !PhotoStore.IsPng(picture))
            return BadRequest(new { problems = new[] { "That isn't the kind of picture its name says." } });

        await System.IO.File.WriteAllBytesAsync(Path.Combine(environment.WebRootPath, "media", "heroes", file), picture);
        return NoContent();
    }

    [GeneratedRegex(@"^(?<hero>[a-z]+)(\.png|-head\.png|-sheet\.webp)$")]
    private static partial Regex HeroPictureName();

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

    /// <summary>Hands the player at a station a floor reward (Vitality, Might, Armor, Time, Focus, Mastery, LastStand), to test it.</summary>
    [HttpPost("reward/{stationId}/{reward}")]
    public IActionResult GiveReward(string stationId, Reward reward)
    {
        if (!devTools.Enabled)
            return NotFound();

        return host.GiveReward(stationId, reward) ? NoContent() : Conflict(new { problems = new[] { "Nobody is playing a dungeon game at that station." } });
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
