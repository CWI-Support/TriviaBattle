using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Audio;

/// <summary>
/// Plays the room audio from the server, through each room's sound card (see <see cref="RoomSound"/>).
/// It's an <see cref="IMatchBroadcaster"/>, so it hears every match change and picks the sound:
///
///   Lobby (no match)  lobby music          Intro    the intro's sound (music fades out)
///   Playing           in-game music        Results  victory music (once)
///   An answer         right/wrong stinger on the speaker of whoever answered (both teammates in 2v2 Group)
///
/// Sound files are decoded and the cards opened in the background once the server is fully up
/// (a server that fails to start never makes a sound); until that's done, match changes are not heard.
/// Only one server process per PC plays the room audio: a second copy (say `dotnet run` while
/// Visual Studio is running one) stays silent, so two copies of the music can never play out of step.
///
/// The admin page's volume sliders (<see cref="SetVolumesAsync"/>) apply live and are saved in the
/// AppSettings table, so they survive a restart.
/// </summary>
public sealed class RoomSoundService(
    IOptions<AudioSettings> audioSettings,
    IOptionsMonitor<MediaSettings> media,
    RoomAudioMap rooms,
    StationLayout stations,
    IWebHostEnvironment env,
    IDbContextFactory<TriviaDbContext> dbFactory,
    IHostApplicationLifetime lifetime,
    ILogger<RoomSoundService> logger) : IMatchBroadcaster, IHostedService
{
    private const string VolumesKey = "Audio:Volumes";

    // Held while this process owns the room audio. Named, so every process on the PC sees it.
    private Mutex? _audioOwner;

    private readonly AudioSettings _settings = audioSettings.Value;
    private volatile IReadOnlyDictionary<string, RoomSound> _rooms = new Dictionary<string, RoomSound>();
    private volatile AudioVolumes? _volumes;

    /// <summary>Each room's sound, once started (empty while loading, or when Audio:Enabled is false).</summary>
    public IReadOnlyDictionary<string, RoomSound> Rooms => _rooms;

    /// <summary>Why there's no sound, if there isn't (for the operator screen); null when playing or still starting.</summary>
    public string? NotPlayingReason { get; private set; }

    /// <summary>The slider levels: saved on the admin page, else the *Volume settings in "Audio".</summary>
    public AudioVolumes Volumes => _volumes ??= LoadVolumes();

    /// <summary>Saves new slider levels and applies them to every room straight away.</summary>
    public async Task SetVolumesAsync(AudioVolumes volumes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var setting = await db.AppSettings.FindAsync(VolumesKey);
        var json = JsonSerializer.Serialize(volumes);
        if (setting == null)
            db.AppSettings.Add(new AppSettingEntity { Key = VolumesKey, Value = json });
        else
            setting.Value = json;
        await db.SaveChangesAsync();

        _volumes = volumes;
        foreach (var room in _rooms.Values)
            room.ApplyVolumes(volumes);
    }

    private AudioVolumes LoadVolumes()
    {
        using var db = dbFactory.CreateDbContext();
        var saved = db.AppSettings.Find(VolumesKey)?.Value;
        return saved != null && JsonSerializer.Deserialize<AudioVolumes>(saved) is { IsValid: true } volumes
            ? volumes
            : AudioVolumes.From(_settings);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Room audio is off (Audio:Enabled is false)");
            return Task.CompletedTask;
        }

        // Wait until the server is really up (it may still fail, e.g. the port is taken), then decode
        // the music in the background: a few minutes of audio takes a moment.
        lifetime.ApplicationStarted.Register(() => Task.Run(() =>
        {
            try
            {
                Start();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Room audio could not start; carrying on without sound");
                NotPlayingReason = $"Could not start: {ex.Message}";
            }
        }));
        return Task.CompletedTask;
    }

    private void Start()
    {
        _audioOwner = new Mutex(initiallyOwned: true, @"Global\TriviaBattle.RoomAudio", out var ownsAudio);
        if (!ownsAudio)
        {
            _audioOwner.Dispose();
            _audioOwner = null;
            NotPlayingReason = "Another Trivia Battle server on this PC is already playing the room audio, so this one stays silent. Stop the other one (Task Manager: TriviaBattle.Server).";
            logger.LogWarning("Room audio: {Reason}", NotPlayingReason);
            return;
        }

        var files = new[] { _settings.LobbyMusic, _settings.AttractMusic, _settings.PlayingMusic, _settings.VictoryMusic, _settings.IntroSound }
            .Concat(_settings.CorrectStingers)
            .Concat(_settings.WrongStingers);
        foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            if (!File.Exists(Path.Combine(env.WebRootPath, file.TrimStart('/', '\\'))))
                logger.LogWarning("Sound file missing: {File}", file);
        }

        var started = new Dictionary<string, RoomSound>();
        foreach (var (roomId, speakers) in rooms.Rooms)
        {
            var room = new RoomSound(roomId, speakers, _settings, Volumes, env.WebRootPath, logger);
            room.StartAttract();
            room.SetRoomMusic(_settings.LobbyMusic);
            started[roomId] = room;
        }
        _rooms = started;
    }

    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events)
    {
        var roomsInMatch = match.Roster.Players
            .Select(p => stations.Find(p.StationId)?.RoomId)
            .Distinct()
            .Select(id => id == null ? null : _rooms.GetValueOrDefault(id))
            .OfType<RoomSound>()
            .ToList();

        foreach (var e in events)
        {
            foreach (var room in roomsInMatch)
                Handle(room, match, e);
        }
    }

    private void Handle(RoomSound room, MatchEngine match, MatchEvent e)
    {
        switch (e)
        {
            case PhaseChanged { Phase: MatchPhase.Intro }:
                room.SetRoomMusic(null);
                room.PlayIntro(media.CurrentValue.IntroVideoDelaySeconds); // the screens start the video after the same delay
                break;

            case PhaseChanged { Phase: MatchPhase.Playing }:
                room.StopIntro();
                room.SetRoomMusic(_settings.PlayingMusic);
                break;

            case PhaseChanged { Phase: MatchPhase.Results }:
                room.SetRoomMusic(_settings.VictoryMusic, loop: false);
                break;

            case MatchFinished:
                room.StopIntro();
                room.SetRoomMusic(_settings.LobbyMusic);
                break;

            case AnswerGiven { Answer: var answer }:
                // In 2v2 Group the team answers as one, so both teammates hear it.
                var team = match.Roster.FindTeam(answer.OwnerId);
                room.PlayStinger(team?.StationIds ?? [answer.PressedByStationId], answer.IsCorrect);
                break;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var room in _rooms.Values)
            room.Dispose();
        _audioOwner?.Dispose(); // the OS releases it anyway when the process ends
        return Task.CompletedTask;
    }
}
