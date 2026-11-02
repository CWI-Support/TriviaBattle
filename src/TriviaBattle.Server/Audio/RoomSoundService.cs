using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Dungeon;
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
///   Lobby (no match)  lobby music          Intro    the intro's sound (music stops)
///   Playing           in-game music        Results  victory music (once)
///   An answer         right/wrong stinger on the speaker of whoever answered (both teammates in 2v2 Group)
///   Dungeon fights    instead of stingers: hit, crit, kill, floor, boss, party combo, revive, hurt, downed,
///                     block, loot, power-up ("Audio:Dungeon"), each timed to land with the battle beat's
///                     picture; boss music while anyone in the room fights a boss ("Audio:BossMusic")
///
/// Several groups can play in one room at once. The room speaker then follows the whole room
/// (in-game music while anyone plays; victory only when the last game is on its results), and a
/// group's intro and victory play on that group's own station speakers instead.
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

    // When the fight's sounds play, in seconds after the battle beat starts, so they land with the picture.
    // These are the moments in the table at the top of css/dungeon-battle.css (and js/battle3d/beats.js):
    // change them together.
    private const double BlowLands = 0.19;
    private const double ClawsLand = 0.24;
    private const double KnightFalls = 0.64;
    private const double MonsterBursts = 1.0;
    private const double CurtainFalls = 1.3;

    /// <summary>When the LOOT! card comes up (DungeonBattle.loot): after a kill, over the curtain on a floor cleared, soon after a plain hit.</summary>
    private static double LootShows(DealtHit hit) => hit.FloorCleared ? 1.45 : hit.Killed ? 1.25 : 0.6;

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
            .Concat(_settings.WrongStingers)
            .Concat(_settings.Dungeon.All);
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

    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        var roomIds = match.Roster.Players.Select(p => stations.Find(p.StationId)?.RoomId).OfType<string>().Distinct();

        foreach (var roomId in roomIds)
        {
            if (_rooms.GetValueOrDefault(roomId) is not { } room)
                continue;

            var inRoom = runningMatches.Where(m => !m.IsFinished && IsInRoom(m, roomId)).ToList();
            var othersPlaying = inRoom.Any(m => m != match);
            var groupStations = match.Roster.Players.Select(p => p.StationId).Where(id => stations.Find(id)?.RoomId == roomId).ToList();

            foreach (var e in events)
                Handle(room, match, e, othersPlaying, groupStations);

            UpdateRoomMusic(room, inRoom);
        }
    }

    /// <summary>
    /// One match's moments. When it's the only game in the room, its intro and victory use the room
    /// speaker; when other groups are playing too, they go to this group's own station speakers instead.
    /// </summary>
    private void Handle(RoomSound room, MatchEngine match, MatchEvent e, bool othersPlaying, List<string> groupStations)
    {
        var introDelay = media.CurrentValue.IntroVideoDelaySeconds; // the screens start the video after the same delay

        switch (e)
        {
            case PhaseChanged { Phase: MatchPhase.Intro }:
                if (othersPlaying)
                    room.PlayOnStations(groupStations, _settings.IntroSound, room.IntroVolume, introDelay);
                else
                    room.PlayIntro(introDelay);
                break;

            case PhaseChanged { Phase: MatchPhase.Playing }:
                room.StopOnStations(groupStations);
                if (!othersPlaying)
                    room.StopIntro();
                break;

            case PhaseChanged { Phase: MatchPhase.Results } when othersPlaying:
                // A short celebration on their own speakers; the room keeps the music for the others.
                room.PlayOnStations(groupStations, _settings.VictoryMusic, room.MusicVolume);
                break;

            case MatchFinished:
                room.StopOnStations(groupStations);
                break;

            case AnswerGiven { Answer: var answer } when !(match.IsDungeon && _settings.Dungeon.AnySet):
                // In 2v2 Group the team answers as one, so both teammates hear it. Otherwise the owner is
                // the player's own station (also when a dungeon monster's hourglass ran out and nobody pressed).
                room.PlayStinger(StationsOf(match, answer.OwnerId), answer.IsCorrect);
                break;

            case MonsterHit { OwnerId: var ownerId, Hit: var hit }:
                PlayDungeonHit(room, match, ownerId, hit);
                break;

            case AdventurerHit { OwnerId: var ownerId, Hit: var taken }:
                var hurt = StationsOf(match, ownerId);
                room.PlayEffect(hurt, taken.Blocked ? _settings.Dungeon.Block : _settings.Dungeon.Hurt, ClawsLand);
                if (taken.Downed)
                    room.PlayEffect(hurt, _settings.Dungeon.Downed, KnightFalls);
                break;

            case PowerUpUsed { OwnerId: var ownerId, Use: var use }:
                room.PlayEffect([.. StationsOf(match, ownerId), .. StationsOf(match, use.TargetOwnerId)], _settings.Dungeon.PowerUp);
                break;

            case PhotoAsked { StationId: var stationId }:
                // In step with the station's "3, 2, 1" (js/hero-camera.js), which takes the photo as the shutter sounds.
                var countdown = match.Settings.Dungeon.PhotoCountdownSeconds;
                for (var second = 0; second < countdown; second++)
                    room.PlayEffect([stationId], _settings.Dungeon.PhotoCountdown, second);
                room.PlayEffect([stationId], _settings.Dungeon.Photo, countdown);
                break;

            case PowerUpFound { OwnerId: var ownerId } when match.Dungeon?.FindAdventurer(ownerId)?.LastDealt?.Found == null:
                // Handed over without a hit (the dev tools): no LOOT! card to wait for. Found with a hit,
                // PlayDungeonHit times it to the card.
                room.PlayEffect(StationsOf(match, ownerId), _settings.Dungeon.Loot);
                break;
        }
    }

    /// <summary>
    /// A hit on a dungeon monster, as a little sequence timed with the battle beat: the blow (a revive, a
    /// party combo, a crit or fireball, or a plain hit), then the monster bursting if it died, then the way
    /// down (or the boss stepping up) if that cleared the floor, then the loot if it dropped some. Big
    /// moments are heard by the whole party. Each one cuts off the end of the last, like the pictures do.
    /// </summary>
    private void PlayDungeonHit(RoomSound room, MatchEngine match, string ownerId, DealtHit hit)
    {
        var sounds = _settings.Dungeon;
        var striker = StationsOf(match, ownerId);
        var party = PartyStationsOf(match, ownerId);
        var bossNext = match.Dungeon?.FindAdventurer(ownerId) is { } adventurer && match.Dungeon.DelveOf(adventurer).Encounter.Monster.IsBoss;

        if (hit.RevivedOwnerId != null)
            room.PlayEffect(party, sounds.Revive, BlowLands);
        else if (hit.PartyCombo)
            room.PlayEffect(party, sounds.PartyCombo, BlowLands);
        else
            room.PlayEffect(striker, hit.Crit || hit.Fireball ? sounds.Crit : sounds.Hit, BlowLands);

        if (hit.Killed)
            room.PlayEffect(hit.FloorCleared ? party : striker, sounds.Kill, MonsterBursts);
        if (hit.FloorCleared)
            room.PlayEffect(party, bossNext ? sounds.Boss : sounds.Floor, CurtainFalls);
        if (hit.Found != null)
            room.PlayEffect(striker, sounds.Loot, LootShows(hit));
    }

    /// <summary>An answer owner's stations: a player's own, or both teammates' in 2v2 Group (the team answers as one).</summary>
    private static IReadOnlyList<string> StationsOf(MatchEngine match, string ownerId) =>
        match.Roster.FindTeam(ownerId)?.StationIds ?? [ownerId];

    /// <summary>Everyone in an answer owner's dungeon party: their whole team, or just them.</summary>
    private static IReadOnlyList<string> PartyStationsOf(MatchEngine match, string ownerId)
    {
        var team = match.Roster.FindTeam(ownerId)
            ?? (match.Roster.FindPlayer(ownerId) is { } player ? match.Roster.TeamOf(player) : null);
        return team?.StationIds ?? [ownerId];
    }

    /// <summary>
    /// The room speaker follows the whole room: lobby music with nobody playing, in-game music while
    /// any group plays (a solo player's "Continue?" countdown counts as playing), boss music instead
    /// while anyone is fighting a boss (if there is any), victory music once the last game is on its
    /// results, silence during an intro.
    /// </summary>
    private void UpdateRoomMusic(RoomSound room, List<MatchEngine> inRoom)
    {
        var playing = inRoom.Where(m => m.Phase is MatchPhase.Playing or MatchPhase.Continue).ToList();
        if (inRoom.Count == 0)
            room.SetRoomMusic(_settings.LobbyMusic);
        else if (playing.Any(FightingABoss) && !string.IsNullOrWhiteSpace(_settings.BossMusic))
            room.SetRoomMusic(_settings.BossMusic);
        else if (playing.Count > 0)
            room.SetRoomMusic(_settings.PlayingMusic);
        else if (inRoom.All(m => m.Phase == MatchPhase.Results))
            room.SetRoomMusic(_settings.VictoryMusic, loop: false);
        else
            room.SetRoomMusic(null); // an intro is playing
    }

    /// <summary>A Dungeon game where some party is facing a boss.</summary>
    private static bool FightingABoss(MatchEngine match) =>
        match.Dungeon?.Delves.Any(d => d.Encounter.Monster.IsBoss) == true;

    private bool IsInRoom(MatchEngine match, string roomId) =>
        match.Roster.Players.Any(p => stations.Find(p.StationId)?.RoomId == roomId);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var room in _rooms.Values)
            room.Dispose();
        _audioOwner?.Dispose(); // the OS releases it anyway when the process ends
        return Task.CompletedTask;
    }
}
