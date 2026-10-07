using TriviaBattle.Core.Input;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Server.Configuration;

// Plain classes that config/stations.json and config/buttons.json are read into,
// plus conversion to the Core types the game uses.

public class RoomConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<StationConfig> Stations { get; set; } = new();
}

public class StationConfig
{
    public int Number { get; set; }
    public string Label { get; set; } = "";
    public string Color { get; set; } = "#ffffff";
}

public class ButtonConfig
{
    public int Id { get; set; }
    public string Station { get; set; } = "";

    /// <summary>"A", "B", "C" or "D".</summary>
    public string Answer { get; set; } = "";
}

public static class HardwareConfig
{
    public static StationLayout LoadStationLayout(IConfiguration config)
    {
        var rooms = config.GetSection("Rooms").Get<List<RoomConfig>>() ?? [];
        if (rooms.Count == 0)
            throw new InvalidOperationException("No rooms found. Check config/stations.json.");

        var stations =
            from room in rooms
            from s in room.Stations
            select new Station($"{room.Id}{s.Number}", room.Id, s.Number, s.Label, s.Color);

        return new StationLayout(stations, rooms.ToDictionary(r => r.Id, r => r.Name));
    }

    public static ButtonMap LoadButtonMap(IConfiguration config, StationLayout layout)
    {
        var buttons = config.GetSection("Buttons").Get<List<ButtonConfig>>() ?? [];

        var unknown = buttons.FirstOrDefault(b => layout.Find(b.Station) == null);
        if (unknown != null)
            throw new InvalidOperationException($"buttons.json: button {unknown.Id} refers to unknown station '{unknown.Station}'.");

        return new ButtonMap(buttons.Select(b =>
            new ButtonAssignment(b.Id, b.Station, ButtonMap.AnswerLetterToIndex(b.Answer))));
    }

    /// <summary>Reads config/keyboard.json: key name -> button id.</summary>
    public static Dictionary<string, int> LoadKeyboardMap(IConfiguration config) =>
        config.GetSection("KeyboardInput:Keys").Get<Dictionary<string, int>>() ?? new();
}
