using TriviaBattle.Hardware.Audio;

namespace TriviaBattle.Server.Audio;

/// <summary>
/// One room's sound, on RangerShack's audio engine (<see cref="AudioService"/>): one zone per speaker.
///
///   Station zones  a stinger (an "effect") when that station answers
///   Room + Sub     the music bed (a "loop"); victory and the intro's sound play as effects
///   Attract        the attract music, looping all the time
///
/// Like RangerShack's game flow, this only tells zones what to play; the engine does the rest.
/// </summary>
public sealed class RoomSound : IDisposable
{
    private const string RoomZone = "room", AttractZone = "attract", SubZone = "sub";

    private readonly RoomSpeakers _speakers;
    private readonly AudioSettings _settings;
    private readonly string _webRoot;
    private readonly AudioService _audio;
    private readonly object _lock = new();

    private AudioVolumes _volumes;
    private string? _roomMusicFile;
    private CancellationTokenSource? _introDelay;

    public RoomSound(string roomId, RoomSpeakers speakers, AudioSettings settings, AudioVolumes volumes, string webRoot, ILogger logger)
    {
        RoomId = roomId;
        _speakers = speakers;
        _settings = settings;
        _volumes = volumes;
        _webRoot = webRoot;

        // A zone for every speaker that's wired up. Attract and sub only repeat other zones' sound,
        // so they're left out of the stereo test mix (see AudioZone.InStereoTest).
        var zones = new List<AudioZone>();
        foreach (var station in speakers.Stations.Where(s => s.Output != null))
            zones.Add(new AudioZone(station.StationId, station.Output!.Value, 0));
        if (speakers.Room is int room) zones.Add(new AudioZone(RoomZone, room, (float)volumes.Music));
        if (speakers.Attract is int attract) zones.Add(new AudioZone(AttractZone, attract, (float)volumes.Attract, InStereoTest: false));
        if (speakers.Sub is int sub) zones.Add(new AudioZone(SubZone, sub, (float)volumes.Sub, InStereoTest: false));

        _audio = new AudioService(zones, speakers.Device, settings.OutputCount, settings.SampleRate,
            effectsVolume: (float)volumes.Stingers, duckLevel: 1, duckFadeMs: 200, logger);
    }

    public string RoomId { get; }

    /// <summary>For the operator screen, e.g. "Speakers (StarTech 7.1) — WASAPI exclusive 8ch".</summary>
    public string Status => _audio.DeviceStatus;

    public RoomSpeakers Speakers => _speakers;

    /// <summary>New slider levels, applied straight away.</summary>
    public void ApplyVolumes(AudioVolumes volumes)
    {
        lock (_lock)
        {
            _volumes = volumes;
            Zone(RoomZone)?.SetVolume((float)volumes.Music);
            Zone(AttractZone)?.SetVolume((float)volumes.Attract);
            Zone(SubZone)?.SetVolume((float)volumes.Sub);
            foreach (var station in _speakers.Stations)
                Zone(station.StationId)?.SetEffectsVolume((float)volumes.Stingers);
        }
    }

    /// <summary>Starts the attract loop. It plays for as long as the server runs.</summary>
    public void StartAttract()
    {
        if (Path(_settings.AttractMusic) is { } path)
            Zone(AttractZone)?.StartLoop(path);
    }

    /// <summary>
    /// The room's music: a loop on the room and sub zones, or (loop: false, e.g. victory) played once.
    /// Null = silence. Asking for what's already playing does nothing.
    /// </summary>
    public void SetRoomMusic(string? file, bool loop = true)
    {
        lock (_lock)
        {
            file = string.IsNullOrWhiteSpace(file) ? null : file;
            if (file == _roomMusicFile)
                return;
            _roomMusicFile = file;

            Zone(RoomZone)?.StopLoop();
            Zone(SubZone)?.StopLoop();

            if (Path(file) is not { } path)
                return;

            if (loop)
            {
                Zone(RoomZone)?.StartLoop(path);
                Zone(SubZone)?.StartLoop(path);
            }
            else
            {
                Zone(RoomZone)?.PlayEffect(path, (float)_volumes.Music);
                Zone(SubZone)?.PlayEffect(path, (float)_volumes.Sub);
            }
        }
    }

    /// <summary>The intro video's soundtrack on the room (and sub), starting when the screens start the video.</summary>
    public void PlayIntro(double delaySeconds)
    {
        StopIntro();
        if (Path(_settings.IntroSound) is not { } path)
            return;

        var cancel = new CancellationTokenSource();
        lock (_lock) _introDelay = cancel;
        _ = Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancel.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Zone(RoomZone)?.PlayEffect(path, (float)_volumes.Intro);
            Zone(SubZone)?.PlayEffect(path, (float)_volumes.Sub);
        });
    }

    public void StopIntro()
    {
        lock (_lock)
        {
            _introDelay?.Cancel();
            _introDelay = null;
        }
        Zone(RoomZone)?.StopEffect();
        Zone(SubZone)?.StopEffect();
    }

    /// <summary>A right/wrong stinger on these stations' speakers.</summary>
    public void PlayStinger(IEnumerable<string> stationIds, bool correct)
    {
        var files = correct ? _settings.CorrectStingers : _settings.WrongStingers;
        if (files.Count == 0 || Path(files[Random.Shared.Next(files.Count)]) is not { } path)
            return;

        var stations = stationIds.ToList();
        // In stereo test mode every station is in both ears: one copy, not two out of step.
        if (_audio.IsStereoTest)
            stations = stations.Take(1).ToList();

        foreach (var stationId in stations)
            Zone(stationId)?.PlayEffect(path);
    }

    /// <summary>Speaker test from the operator screen: a stinger on that speaker.</summary>
    /// <param name="speaker">A station id, or "room", "attract" or "sub".</param>
    /// <returns>False if there's no such speaker in this room.</returns>
    public bool TestSpeaker(string speaker)
    {
        var zone = Zone(speaker);
        var file = _settings.CorrectStingers.Select(Path).FirstOrDefault(p => p != null);
        if (zone == null || file == null)
            return false;

        zone.PlayEffect(file, (float)Math.Max(_volumes.Stingers, 0.5));
        return true;
    }

    private AudioChannel? Zone(string name) => _audio.Zones.GetValueOrDefault(name);

    /// <summary>The file on disk for a settings path under wwwroot, or null if it isn't set.</summary>
    private string? Path(string? file) =>
        string.IsNullOrWhiteSpace(file) ? null : System.IO.Path.Combine(_webRoot, file.TrimStart('/', '\\'));

    public void Dispose() => _audio.Dispose();
}
