using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Display;

/// <summary>
/// Which visual theme every screen uses. A theme is a folder in wwwroot/themes/ with a theme.css
/// (looks) and a copy.json (on-screen text); see docs/THEMES.md. The admin page can switch themes
/// live: the choice is saved in the database and pushed to every connected screen at once.
/// </summary>
public class ThemeService(
    IDbContextFactory<TriviaDbContext> dbFactory,
    IWebHostEnvironment env,
    IConfiguration config,
    IHubContext<GameHub> hub,
    ILogger<ThemeService> logger)
{
    private const string SettingKey = "Theme";
    private string? _current;

    /// <summary>Theme folder names that have a theme.css, e.g. ["dungeon", "neon", "showcase"].</summary>
    public IReadOnlyList<string> Available()
    {
        var folder = Path.Combine(env.WebRootPath, "themes");
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetDirectories(folder)
            .Where(dir => File.Exists(Path.Combine(dir, "theme.css")))
            .Select(Path.GetFileName)
            .Order()
            .ToList()!;
    }

    /// <summary>The active theme: the one chosen in admin, else Display:DefaultTheme from appsettings.</summary>
    public string Current
    {
        get
        {
            if (_current != null)
                return _current;

            using var db = dbFactory.CreateDbContext();
            var saved = db.AppSettings.Find(SettingKey)?.Value;
            _current = saved != null && Available().Contains(saved)
                ? saved
                : config.GetValue("Display:DefaultTheme", "neon")!;
            return _current;
        }
    }

    public async Task<bool> SetAsync(string theme)
    {
        if (!Available().Contains(theme))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync();
        var setting = await db.AppSettings.FindAsync(SettingKey);
        if (setting == null)
            db.AppSettings.Add(new AppSettingEntity { Key = SettingKey, Value = theme });
        else
            setting.Value = theme;
        await db.SaveChangesAsync();

        _current = theme;
        logger.LogInformation("Theme switched to {Theme}", theme);

        // Every connected screen reloads its look and text straight away.
        await hub.Clients.All.SendAsync("Theme", theme);
        return true;
    }
}
