using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Hubs;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Display;

/// <summary>
///   GET /api/display              -> { theme, themes, devTools, httpsPort }   (screens read this when they start;
///                                    httpsPort: the server's https address for station cameras, 0 = none)
///   PUT /api/admin/display/theme  -> switch every screen to another theme (admin)
/// </summary>
[ApiController]
public class DisplayController(ThemeService themes, DevToolsSettings devTools, IConfiguration config) : ControllerBase
{
    public sealed record SetThemeRequest(string Theme);

    [HttpGet("api/display")]
    public IActionResult Get() => Ok(new
    {
        theme = themes.Current,
        themes = themes.Available(),
        devTools = devTools.Enabled,
        httpsPort = config.GetValue("HttpsPort", 5001),
    });

    [HttpPut("api/admin/display/theme")]
    [AdminApiKey]
    public async Task<IActionResult> SetTheme(SetThemeRequest request) =>
        await themes.SetAsync(request.Theme)
            ? NoContent()
            : BadRequest(new { problems = new[] { $"There's no theme called \"{request.Theme}\" in wwwroot/themes." } });
}
