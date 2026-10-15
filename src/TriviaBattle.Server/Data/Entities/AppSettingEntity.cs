namespace TriviaBattle.Server.Data.Entities;

/// <summary>
/// Settings changed at runtime from the admin page (as opposed to appsettings.json, which
/// is edited by hand). Simple key/value pairs. Table: AppSettings.
/// Keys in use: "Theme", and "Game:..." for the game rules (see Game/GameRulesService).
/// </summary>
public class AppSettingEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
