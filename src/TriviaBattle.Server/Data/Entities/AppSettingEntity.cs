namespace TriviaBattle.Server.Data.Entities;

/// <summary>
/// Settings changed at runtime from the admin page (as opposed to appsettings.json, which
/// is edited by hand). Simple key/value pairs. Table: AppSettings.
/// Keys in use: "Theme".
/// </summary>
public class AppSettingEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
