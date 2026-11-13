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

    /// <summary>Dungeon format: the room music while anyone in the room is fighting a boss. Empty = keep the playing music.</summary>
    public string BossMusic { get; set; } = "";

    /// <summary>
    /// The intro video's soundtrack as its own file, played on the room speakers in sync with the
    /// screens (videos stay silent so the sound can go to the right speakers, as in the EscapeEngine).
    /// Make it with: ffmpeg -i intro.mp4 -vn intro_aud.mp3
    /// </summary>
    public string IntroSound { get; set; } = "";

    /// <summary>Played on a station's speaker for a right answer; one is picked at random each time.</summary>
    public List<string> CorrectStingers { get; set; } = new();
    public List<string> WrongStingers { get; set; } = new();

    /// <summary>Dungeon Quiz Quest's fight sounds, played instead of the right/wrong stingers in dungeon games.</summary>
    public DungeonSounds Dungeon { get; set; } = new();

    // Volumes, 0-1.
    public double MusicVolume { get; set; } = 0.8;
    public double AttractVolume { get; set; } = 0.8;
    public double IntroVolume { get; set; } = 1;
    public double StingerVolume { get; set; } = 1;
    public double SubVolume { get; set; } = 1;

}

/// <summary>
/// "Audio:Dungeon": the fight's sound effects, on the station speakers at the moment each thing happens
/// (the screens show the same moment as a battle beat). Mostly on the speaker of whoever it happened to;
/// a floor cleared, a boss, a party combo or a revive is heard by the whole party. Empty = silence.
/// While none is set, dungeon games use the right/wrong stingers instead.
/// </summary>
public class DungeonSounds
{
    public string Hit { get; set; } = "";
    public string Crit { get; set; } = "";
    public string Kill { get; set; } = "";

    /// <summary>A floor cleared: on the way down.</summary>
    public string Floor { get; set; } = "";

    /// <summary>A floor cleared, and the next floor's boss steps up.</summary>
    public string Boss { get; set; } = "";

    /// <summary>The boss's entrance: it lands in the room with a slam (after the Boss warning).</summary>
    public string BossLands { get; set; } = "";

    public string PartyCombo { get; set; } = "";
    public string Revive { get; set; } = "";

    /// <summary>The monster hits you (a wrong answer, or too slow).</summary>
    public string Hurt { get; set; } = "";
    public string Downed { get; set; } = "";

    /// <summary>A Shield takes the monster's hit.</summary>
    public string Block { get; set; } = "";

    /// <summary>A power-up found (the LOOT! card), and one used with the action button.</summary>
    public string Loot { get; set; } = "";
    public string PowerUp { get; set; } = "";

    /// <summary>A hero photo: a blip each second of the "3, 2, 1", then the shutter as the station takes it.</summary>
    public string PhotoCountdown { get; set; } = "";
    public string Photo { get; set; } = "";

    public IEnumerable<string> All => [Hit, Crit, Kill, Floor, Boss, BossLands, PartyCombo, Revive, Hurt, Downed, Block, Loot, PowerUp, PhotoCountdown, Photo];

    /// <summary>At least one is set, so dungeon games play these instead of the stingers.</summary>
    public bool AnySet => All.Any(file => !string.IsNullOrWhiteSpace(file));
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
