using System.Text.Json.Serialization;
using TriviaBattle.Server.Configuration;

namespace TriviaBattle.Server.Audio;

/// <summary>
/// Bound from "Audio" in appsettings.json: which sounds the server plays on the room's sound card,
/// and how loud. Which speaker each sound goes to is in config/stations.json (see <see cref="RoomSpeakers"/>).
/// All room audio comes from the server; the screen PCs play nothing (the main screen mutes the intro video).
/// </summary>
public class AudioSettings
{
    /// <summary>Off = no sound card is opened, and the main screen plays the intro video's own sound instead.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Outputs the card is opened with, as in RangerShack: 8 for the StarTech 7.1 (Windows must have it
    /// set to 7.1), 2 on a PC without it (every speaker in both ears; appsettings.Development.json).
    /// </summary>
    public int OutputCount { get; set; } = 8;

    public int SampleRate { get; set; } = 44100;

    // Sound files, by moment, under wwwroot. Empty = silence for that moment.
    public string LobbyMusic { get; set; } = "";
    public string AttractMusic { get; set; } = "";
    public string PlayingMusic { get; set; } = "";
    public string VictoryMusic { get; set; } = "";

    /// <summary>
    /// The intro video's soundtrack as its own file, played on the room speakers in sync with the
    /// screens (videos stay silent so the sound can go to the right speakers, as in the EscapeEngine).
    /// Make it with: ffmpeg -i intro.mp4 -vn intro_aud.mp3
    /// </summary>
    public string IntroSound { get; set; } = "";

    /// <summary>Played on a station's speaker for a right answer; one is picked at random each time.</summary>
    public List<string> CorrectStingers { get; set; } = new();
    public List<string> WrongStingers { get; set; } = new();

    // Volumes, 0-1.
    public double MusicVolume { get; set; } = 0.8;
    public double AttractVolume { get; set; } = 0.8;
    public double IntroVolume { get; set; } = 1;
    public double StingerVolume { get; set; } = 1;
    public double SubVolume { get; set; } = 1;

}

/// <summary>
/// The levels the admin page's sliders set, 0-2 (1 = as recorded). Saved in the database; until
/// they've been saved there, they come from the *Volume settings in "Audio".
/// </summary>
public sealed record AudioVolumes(double Music, double Attract, double Intro, double Stingers, double Sub)
{
    public const double Max = 2;

    public static AudioVolumes From(AudioSettings s) => new(s.MusicVolume, s.AttractVolume, s.IntroVolume, s.StingerVolume, s.SubVolume);

    [JsonIgnore]
    public bool IsValid => new[] { Music, Attract, Intro, Stingers, Sub }.All(v => v is >= 0 and <= Max);
}

/// <summary>One station's speaker.</summary>
/// <param name="Output">Output on the sound card, 0-based (null = no speaker).</param>
public sealed record StationSpeaker(string StationId, string Label, int? Output);

/// <summary>
/// Which output on the room's sound card each speaker is wired to: 0-based, the same numbering
/// as the EscapeEngine's AudioConfig (StarTech 7.1: 0/1 green jack, 2/3 orange, 4/5 black, 6/7 grey).
/// Null = no speaker. From config/stations.json.
/// </summary>
/// <param name="Device">Part of the sound card's Windows name (e.g. "StarTech"); empty = Windows default (skipping HDMI).</param>
public sealed record RoomSpeakers(string Device, IReadOnlyList<StationSpeaker> Stations, int? Room, int? Attract, int? Sub);

/// <summary>Every room's speakers, by room id.</summary>
public sealed class RoomAudioMap(IReadOnlyDictionary<string, RoomSpeakers> rooms)
{
    public RoomSpeakers? Find(string roomId) => rooms.GetValueOrDefault(roomId);

    public IReadOnlyDictionary<string, RoomSpeakers> Rooms => rooms;

    /// <summary>Reads every room's speakers from config/stations.json, and checks no two share an output.</summary>
    public static RoomAudioMap Load(IConfiguration config)
    {
        var rooms = config.GetSection("Rooms").Get<List<RoomConfig>>() ?? [];
        var result = new Dictionary<string, RoomSpeakers>();

        foreach (var room in rooms)
        {
            var speakers = new RoomSpeakers(
                room.Audio.Device,
                room.Stations.Select(s => new StationSpeaker($"{room.Id}{s.Number}", s.Label, s.AudioOutput)).ToList(),
                room.Audio.Room,
                room.Audio.Attract,
                room.Audio.Sub);

            var used = speakers.Stations.Select(s => s.Output)
                .Concat([speakers.Room, speakers.Attract, speakers.Sub])
                .OfType<int>()
                .ToList();
            var shared = used.GroupBy(o => o).FirstOrDefault(g => g.Count() > 1);
            if (shared != null)
                throw new InvalidOperationException($"stations.json: room {room.Id} has more than one speaker on audio output {shared.Key}.");
            if (used.Any(o => o is < 0 or > 31))
                throw new InvalidOperationException($"stations.json: room {room.Id} has an audio output outside 0-31.");

            result[room.Id] = speakers;
        }

        return new RoomAudioMap(result);
    }
}
