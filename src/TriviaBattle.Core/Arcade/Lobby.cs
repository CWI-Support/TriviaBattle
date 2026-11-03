using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Arcade;

/// <summary>Where a station is in the lobby, from waiting for a player to waiting for the game to start.</summary>
public enum LobbyStep
{
    /// <summary>Nobody here: "press any button to play".</summary>
    Idle,

    /// <summary>Choosing a hero: A and B flip through them, ACTION picks one.</summary>
    Hero,

    /// <summary>
    /// The hero photo: the station's camera counts down and takes it, then shows the hero wearing the player's
    /// face. ACTION keeps it, B takes another, D goes on without one. Where the venue asks first
    /// (<see cref="LobbyPhotoRule.AskFirst"/>) it starts with the question: ACTION yes, D no. Skipped where
    /// photos are off; a station without a camera skips it by itself.
    /// </summary>
    Photo,

    /// <summary>Choosing a game: A, B, C, D as listed (<see cref="Lobby.ChoicesFor"/>), ACTION back to the heroes.</summary>
    Game,

    /// <summary>Choosing the questions' category for a new game: A and B flip, ACTION picks, D goes back.</summary>
    Category,

    /// <summary>In a game that's forming: ACTION is ready, D leaves it.</summary>
    Waiting,
}

/// <summary>What a player can choose on the Game step, and what a game from the lobby is.</summary>
public enum GameChoice
{
    /// <summary>Just you: starts straight away, on the arcade clock.</summary>
    Solo,

    /// <summary>Starts a party the other stations can join: one dungeon, fought together (co-op).</summary>
    Party,

    /// <summary>Starts a race the other stations can join: everyone on their own delve, most points wins.</summary>
    Race,

    /// <summary>Starts a team battle the other stations join on a side: two teams, each sharing a delve.</summary>
    Battle,

    /// <summary>Joins the game that's forming (in a battle: the host's team).</summary>
    Join,

    /// <summary>Joins a forming battle on the other team.</summary>
    JoinOtherSide,
}

/// <summary>Hero photos in the lobby, as the venue's game rules have them (the same rule as a kiosk game's intro).</summary>
public enum LobbyPhotoRule
{
    /// <summary>No photos (Classic format, or hero photos switched off): straight from the hero to the games.</summary>
    Off,

    /// <summary>Photos asked for first: the Photo step asks, and ACTION says yes.</summary>
    AskFirst,

    /// <summary>Photos by default: the Photo step takes it, and D says no.</summary>
    Take,
}

/// <summary>A category of questions a player can pick (only ones with active questions are offered).</summary>
public sealed record LobbyCategory(int Id, string Name);

/// <summary>The lobby's numbers. Bound from "Game:Arcade".</summary>
public sealed class LobbySettings
{
    /// <summary>How long a party, race or battle waits for players to join before it starts anyway.</summary>
    public double JoinSeconds { get; set; } = 20;

    /// <summary>Someone who joins near the end gets at least this long to get ready.</summary>
    public double LateJoinSeconds { get; set; } = 6;

    /// <summary>A station left choosing (hero, photo, game or category) this long without a press goes back to "press any button".</summary>
    public double IdleSeconds { get; set; } = 30;

    /// <summary>The questions' difficulty for games started at the stations.</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Easy;

    /// <summary>
    /// The hero photo step after choosing a hero. Off: straight on to the games. On, it follows the venue's
    /// photo rule (<see cref="Lobby.SetPhotoRule"/>).
    /// </summary>
    public bool HeroPhotos { get; set; } = true;

    /// <summary>From the photo step starting (or a retake) to the photo being taken: the camera comes on, then "3, 2, 1".</summary>
    public double PhotoSeconds { get; set; } = 4;
}

/// <summary>One station in the lobby, and whoever is standing at it.</summary>
public sealed class LobbySeat(string stationId)
{
    public string StationId { get; } = stationId;
    public LobbyStep Step { get; private set; }
    public string Name { get; private set; } = "";

    /// <summary>Their card's profile, or null for a guest.</summary>
    public int? PlayerProfileId { get; private set; }
    public int HeroIndex { get; private set; }
    public string Hero => Heroes.All[HeroIndex];

    /// <summary>On the Photo step: when the station takes the photo (it counts down to this).</summary>
    public DateTimeOffset? PhotoTakeAt { get; private set; }

    /// <summary>On the Photo step: the station's camera is on, so its countdown is heard (a station without one stays quiet).</summary>
    public bool PhotoCameraOn { get; private set; }

    /// <summary>They have a hero photo (the server keeps the picture). Goes into their game with them.</summary>
    public bool HasPhoto { get; private set; }

    /// <summary>Goes up with each photo that arrives, so screens fetch the new one.</summary>
    public int PhotoVersion { get; private set; }

    /// <summary>On the Category step: the game they chose (Solo, Party, Race or Battle), still to start.</summary>
    public GameChoice PendingGame { get; private set; }

    /// <summary>On the Category step: 0 is everything mixed, then each of <see cref="Lobby.Categories"/>.</summary>
    public int CategoryIndex { get; private set; }

    /// <summary>The game they're in, while <see cref="LobbyStep.Waiting"/>.</summary>
    public LobbyGroup? Group { get; private set; }

    /// <summary>In a battle: 0 on the host's team, 1 on the other.</summary>
    public int Side { get; private set; }
    public bool Ready { get; private set; }
    public DateTimeOffset LastPressAt { get; private set; }

    public void Arrive(string name, int? playerProfileId, DateTimeOffset now)
    {
        Name = name;
        PlayerProfileId = playerProfileId;
        if (Step == LobbyStep.Idle)
        {
            Step = LobbyStep.Hero;
            HeroIndex = 0;
            HasPhoto = false;
        }
        LastPressAt = now;
    }

    public void Pressed(DateTimeOffset now) => LastPressAt = now;
    public void TurnHero(int by) => HeroIndex = (HeroIndex + by + Heroes.All.Count) % Heroes.All.Count;
    public void GoTo(LobbyStep step) => Step = step;

    /// <summary>To the Photo step, asking first (the venue's rule): nothing is taken until they say yes.</summary>
    public void OfferPhoto()
    {
        Step = LobbyStep.Photo;
        PhotoTakeAt = null;
        PhotoCameraOn = false;
        HasPhoto = false;
    }

    /// <summary>To the Photo step (or a retake): the station takes a new photo after the countdown.</summary>
    public void TakePhoto(DateTimeOffset takeAt)
    {
        Step = LobbyStep.Photo;
        PhotoTakeAt = takeAt;
        PhotoCameraOn = false;
        HasPhoto = false;
    }

    public void CameraOn() => PhotoCameraOn = true;

    public void PhotoArrived()
    {
        HasPhoto = true;
        PhotoVersion++;
    }

    /// <summary>On to the games, keeping the photo or not.</summary>
    public void LeavePhoto(bool keep)
    {
        HasPhoto = keep && HasPhoto;
        PhotoTakeAt = null;
        Step = LobbyStep.Game;
    }

    public void ChooseCategoryFor(GameChoice game)
    {
        PendingGame = game;
        CategoryIndex = 0;
        Step = LobbyStep.Category;
    }

    /// <param name="count">How many entries there are: everything mixed, plus each category.</param>
    public void TurnCategory(int by, int count) => CategoryIndex = (CategoryIndex + by + count) % count;

    public void JoinGroup(LobbyGroup group, int side = 0)
    {
        Group = group;
        Side = side;
        Ready = false;
        Step = LobbyStep.Waiting;
    }

    public void LeaveGroup()
    {
        Group = null;
        Side = 0;
        Ready = false;
        Step = LobbyStep.Game;
    }

    public void SetReady() => Ready = true;

    /// <summary>A battle whose host's team emptied: the other team becomes the host's team.</summary>
    public void SwitchSide() => Side = 1 - Side;

    /// <summary>Back to "press any button" (they left, or their game started). Their photo goes with them.</summary>
    public void Clear()
    {
        Step = LobbyStep.Idle;
        Name = "";
        PlayerProfileId = null;
        PhotoTakeAt = null;
        HasPhoto = false;
        Group = null;
        Side = 0;
        Ready = false;
    }
}

/// <summary>A game that's forming: who's in, its questions, and when it starts.</summary>
public sealed class LobbyGroup(string id, GameChoice kind, LobbyCategory? category, DateTimeOffset startsAt)
{
    public string Id { get; } = id;

    /// <summary>Party, Race or Battle.</summary>
    public GameChoice Kind { get; } = kind;

    /// <summary>The questions' category its host chose; null for everything mixed.</summary>
    public LobbyCategory? Category { get; } = category;

    /// <summary>In joining order: the first is the host (who started it).</summary>
    public List<LobbySeat> Members { get; } = [];
    public DateTimeOffset StartsAt { get; set; } = startsAt;

    /// <summary>Who it's named after: whoever started it, or the first still in on the host's team (side 0).</summary>
    public LobbySeat Host => Members.First(m => m.Side == 0);
}

/// <summary>A player in a game the lobby wants started.</summary>
/// <param name="Photo">They bring a hero photo from the lobby (true), or chose none (false).</param>
/// <param name="Side">In a battle: 0 for the host's team, 1 for the other.</param>
public sealed record LobbyPlayer(string StationId, string Name, int? PlayerProfileId, string Hero, bool Photo, int Side = 0);

/// <summary>A game the lobby wants started: the server starts it.</summary>
/// <param name="Game">Solo, Party, Race or Battle (a game nobody joined starts as Solo; a battle nobody took on, as a Party).</param>
/// <param name="Category">The questions' category the player chose; null for everything mixed.</param>
public sealed record LobbyStart(GameChoice Game, IReadOnlyList<LobbyPlayer> Players, LobbyCategory? Category)
{
    /// <summary>
    /// The match to start: a party is one team (2v2 Combined, where each player is their own adventurer in one
    /// shared delve), a battle one team a side; a race and a solo game are free-for-all. The chosen category's
    /// questions, or with everything mixed, <paramref name="allCategoryIds"/>.
    /// </summary>
    public MatchSetup ToSetup(IReadOnlyList<int> allCategoryIds, Difficulty difficulty)
    {
        IReadOnlyList<int> categoryIds = Category == null ? allCategoryIds : [Category.Id];
        var seats = Players.Select(p => new SeatSetup(p.StationId, p.Name, p.PlayerProfileId, p.Hero, p.Photo)).ToList();
        if (Game is not (GameChoice.Party or GameChoice.Battle))
            return new MatchSetup(GameModeKind.FreeForAll, categoryIds, difficulty, seats, []);

        // A party is one team; a battle, one team a side. Each team is named after its first player.
        var teams = Players.GroupBy(p => Game == GameChoice.Battle ? p.Side : 0)
            .OrderBy(side => side.Key)
            .Select(side => new TeamSetup($"side{side.Key + 1}", $"{side.First().Name}'s team", side.Select(p => p.StationId).ToList()))
            .ToList();
        return new MatchSetup(GameModeKind.TeamCombinedScore, categoryIds, difficulty, seats, teams);
    }
}

/// <summary>A second of a station's hero photo countdown in the lobby: 3, 2, 1, then 0 as it's taken (the shutter).</summary>
public sealed record LobbyPhotoCountdown(string StationId, int SecondsLeft);

/// <summary>What a lobby call did: whether anything changed (screens need redrawing), and any games to start.</summary>
public sealed record LobbyResult(bool Changed, IReadOnlyList<LobbyStart> Starts)
{
    public static readonly LobbyResult Nothing = new(false, []);
    public static LobbyResult Change() => new(true, []);
    public static LobbyResult Start(LobbyStart start) => new(true, [start]);
}

/// <summary>
/// The arcade lobby of one room: every station not in a game. A player walks up, presses any button (or
/// swipes their card), picks a hero, has their hero photo taken (or not), then picks a game and its
/// questions' category: solo straight away, or a party, race or team battle that the room's other stations
/// can join until its timer runs out or everyone is ready. Only one forms at a time, so joining is always
/// an obvious choice. When a game should start, the lobby hands back a <see cref="LobbyStart"/> and those
/// stations go back to idle: the match takes over their screens.
///
/// Like <see cref="MatchEngine"/>: the time is passed in, and the server calls it from one thread at a time.
/// </summary>
public sealed class Lobby
{
    private const int A = 0, B = 1, D = 3, Action = Input.ButtonPress.Action;

    /// <summary>The photo's countdown: the digits the station shows (js/hero-camera.js, COUNT_FROM).</summary>
    private const int PhotoCountFrom = 3;

    private readonly Dictionary<string, LobbySeat> _seats;
    private readonly LobbySettings _settings;
    private readonly Random _random;
    private int _groupsMade;
    private DateTimeOffset? _lastCountdownAt;

    public Lobby(IEnumerable<string> stationIds, LobbySettings settings, Random random)
    {
        _seats = stationIds.ToDictionary(id => id, id => new LobbySeat(id));
        _settings = settings;
        _random = random;
    }

    public IReadOnlyCollection<LobbySeat> Seats => _seats.Values;

    /// <summary>The categories on offer (kept up to date by the server: only ones with active questions).</summary>
    public IReadOnlyList<LobbyCategory> Categories { get; private set; } = [];

    public void SetCategories(IReadOnlyList<LobbyCategory> categories)
    {
        if (categories.SequenceEqual(Categories))
            return;
        Categories = categories;
        foreach (var seat in _seats.Values.Where(s => s.Step == LobbyStep.Category))
            seat.TurnCategory(0, categories.Count + 1); // stays in range if the list got shorter
        Version++;
    }

    /// <summary>A seat's category on the Category step: null for everything mixed.</summary>
    public LobbyCategory? CategoryOf(LobbySeat seat) =>
        seat.CategoryIndex > 0 && seat.CategoryIndex <= Categories.Count ? Categories[seat.CategoryIndex - 1] : null;

    /// <summary>The game that's forming, if any (only one at a time).</summary>
    public LobbyGroup? Forming { get; private set; }

    /// <summary>The venue's photo rule (kept up to date by the server from its game rules); only with <see cref="LobbySettings.HeroPhotos"/> on.</summary>
    public LobbyPhotoRule PhotoRule { get; private set; } = LobbyPhotoRule.Take;

    public void SetPhotoRule(LobbyPhotoRule rule)
    {
        if (rule == PhotoRule)
            return;
        PhotoRule = rule;
        Version++;
    }

    /// <summary>Goes up with every change, like <see cref="MatchEngine.Version"/>.</summary>
    public int Version { get; private set; }

    public LobbySeat? Seat(string stationId) => _seats.GetValueOrDefault(stationId);

    /// <summary>A guest name nobody in the lobby has.</summary>
    public string NewGuestName() => HeroNames.Make(_random, _seats.Values.Select(s => s.Name).ToList());

    /// <summary>
    /// The games on offer on the Game step, in button order (A, B, C, D). When a game is forming, joining comes
    /// first: a battle can be joined on either team.
    /// </summary>
    public IReadOnlyList<GameChoice> ChoicesFor(LobbySeat seat) => Forming?.Kind switch
    {
        GameChoice.Battle => [GameChoice.Join, GameChoice.JoinOtherSide, GameChoice.Solo],
        GameChoice.Party or GameChoice.Race => [GameChoice.Join, GameChoice.Solo],
        _ => [GameChoice.Solo, GameChoice.Party, GameChoice.Race, GameChoice.Battle],
    };

    /// <summary>A button at a station that isn't in a game: answerIndex 0-3 (A-D), or <see cref="Input.ButtonPress.Action"/>.</summary>
    public LobbyResult Press(string stationId, int button, DateTimeOffset now)
    {
        if (Seat(stationId) is not { } seat)
            return LobbyResult.Nothing;

        switch (seat.Step)
        {
            case LobbyStep.Idle:
                seat.Arrive(NewGuestName(), null, now);
                return Changed();

            case LobbyStep.Hero:
                seat.Pressed(now);
                if (button == A) seat.TurnHero(-1);
                else if (button == B) seat.TurnHero(+1);
                else if (button != Action) return LobbyResult.Nothing;
                else if (!_settings.HeroPhotos || PhotoRule == LobbyPhotoRule.Off) seat.GoTo(LobbyStep.Game);
                else if (PhotoRule == LobbyPhotoRule.AskFirst) seat.OfferPhoto();
                else seat.TakePhoto(now + TimeSpan.FromSeconds(_settings.PhotoSeconds));
                return Changed();

            case LobbyStep.Photo:
                seat.Pressed(now);
                var asking = seat.PhotoTakeAt == null;
                if (button == D) seat.LeavePhoto(keep: false);
                else if (button == Action && asking) seat.TakePhoto(now + TimeSpan.FromSeconds(_settings.PhotoSeconds)); // yes please
                else if (button == Action && seat.HasPhoto) seat.LeavePhoto(keep: true);
                else if (button == B && !asking) seat.TakePhoto(now + TimeSpan.FromSeconds(_settings.PhotoSeconds));
                else return LobbyResult.Nothing;
                return Changed();

            case LobbyStep.Game:
                seat.Pressed(now);
                if (button == Action)
                {
                    seat.GoTo(LobbyStep.Hero);
                    return Changed();
                }
                var choices = ChoicesFor(seat);
                return button < choices.Count ? Choose(seat, choices[button], now) : LobbyResult.Nothing;

            case LobbyStep.Category:
                seat.Pressed(now);
                if (button == A) seat.TurnCategory(-1, Categories.Count + 1);
                else if (button == B) seat.TurnCategory(+1, Categories.Count + 1);
                else if (button == D) seat.GoTo(LobbyStep.Game);
                else if (button == Action) return Begin(seat, now);
                else return LobbyResult.Nothing;
                return Changed();

            case LobbyStep.Waiting:
                seat.Pressed(now); // leaving a long-forming game mustn't find them idle
                if (button == D)
                {
                    Leave(seat);
                    return Changed();
                }
                if (button != Action || seat.Ready)
                    return LobbyResult.Nothing;
                seat.SetReady();
                Version++;
                return seat.Group!.Members.All(m => m.Ready) ? Begin(seat.Group) : LobbyResult.Change();
        }
        return LobbyResult.Nothing;
    }

    /// <summary>
    /// A card at a station that isn't in a game: the player is whoever the card says (the server looked it
    /// up; a new card gets a made-up name). At an idle station that's also how you start.
    /// </summary>
    public LobbyResult Swipe(string stationId, string name, int? playerProfileId, DateTimeOffset now)
    {
        if (Seat(stationId) is not { } seat)
            return LobbyResult.Nothing;
        // A different card at a station still choosing: whoever was there walked away (or it's someone
        // new), so start afresh, without their hero, their game or their face.
        if (seat.Step != LobbyStep.Idle && seat.PlayerProfileId != playerProfileId)
        {
            if (seat.Group != null)
                Leave(seat);
            seat.Clear();
        }
        seat.Arrive(name, playerProfileId, now);
        return Changed();
    }

    /// <summary>
    /// The station sent the hero photo it took for the take counting down to takeAtMs (the seat's
    /// PhotoTakeAtMs). Only while it's on the Photo step for that same take, without one yet: an upload from
    /// a take they've already replaced (B) doesn't count for the new one.
    /// </summary>
    public bool PhotoArrived(string stationId, long takeAtMs)
    {
        if (Seat(stationId) is not { Step: LobbyStep.Photo, HasPhoto: false, PhotoTakeAt: { } takeAt } seat
            || takeAt.ToUnixTimeMilliseconds() != takeAtMs)
            return false;
        seat.PhotoArrived();
        Version++;
        return true;
    }

    /// <summary>The station's camera came on for the photo it's taking, so its countdown can be heard. False if it isn't taking one.</summary>
    public bool PhotoCameraOn(string stationId)
    {
        if (Seat(stationId) is not { Step: LobbyStep.Photo, HasPhoto: false } seat)
            return false;
        seat.CameraOn();
        return true;
    }

    /// <summary>
    /// The countdown seconds due since the last call (the server calls it with every tick): each second of a
    /// station's "3, 2, 1" and the shutter (0), where its camera is on. For the station's speaker.
    /// </summary>
    public IReadOnlyList<LobbyPhotoCountdown> PhotoCountdowns(DateTimeOffset now)
    {
        var since = _lastCountdownAt ?? now;
        _lastCountdownAt = now;
        var due = new List<LobbyPhotoCountdown>();
        foreach (var seat in _seats.Values.Where(s => s is { Step: LobbyStep.Photo, PhotoCameraOn: true, HasPhoto: false, PhotoTakeAt: not null }))
        {
            for (var left = PhotoCountFrom; left >= 0; left--)
            {
                var moment = seat.PhotoTakeAt!.Value - TimeSpan.FromSeconds(left);
                if (moment > since && moment <= now)
                    due.Add(new LobbyPhotoCountdown(seat.StationId, left));
            }
        }
        return due;
    }

    /// <summary>A station without a camera skips the Photo step (it says so itself, as soon as it gets there).</summary>
    public LobbyResult SkipPhoto(string stationId)
    {
        if (Seat(stationId) is not { Step: LobbyStep.Photo } seat)
            return LobbyResult.Nothing;
        seat.LeavePhoto(keep: false);
        return Changed();
    }

    /// <summary>Starts a forming game whose timer has run out, and sends stations left choosing too long back to idle.</summary>
    public LobbyResult Tick(DateTimeOffset now)
    {
        if (Forming != null && now >= Forming.StartsAt)
            return Begin(Forming);

        var asleep = _seats.Values
            .Where(s => s.Step is LobbyStep.Hero or LobbyStep.Photo or LobbyStep.Game or LobbyStep.Category
                && now - s.LastPressAt > TimeSpan.FromSeconds(_settings.IdleSeconds))
            .ToList();
        foreach (var seat in asleep)
            seat.Clear();
        return asleep.Count > 0 ? Changed() : LobbyResult.Nothing;
    }

    /// <summary>A game chosen on the Game step: joining goes straight in; a new game asks for its category first.</summary>
    private LobbyResult Choose(LobbySeat seat, GameChoice choice, DateTimeOffset now)
    {
        switch (choice)
        {
            case GameChoice.Solo:
            case GameChoice.Party or GameChoice.Race or GameChoice.Battle when Forming == null:
                seat.ChooseCategoryFor(choice);
                return Changed();

            case GameChoice.Join or GameChoice.JoinOtherSide when Forming != null:
                seat.JoinGroup(Forming, choice == GameChoice.JoinOtherSide ? 1 : 0);
                Forming.Members.Add(seat);
                var lateJoin = now + TimeSpan.FromSeconds(_settings.LateJoinSeconds);
                if (Forming.StartsAt < lateJoin)
                    Forming.StartsAt = lateJoin;
                return Changed();
        }
        return LobbyResult.Nothing;
    }

    /// <summary>The category is chosen: a solo game starts; any other starts forming (unless another got there first).</summary>
    private LobbyResult Begin(LobbySeat seat, DateTimeOffset now)
    {
        var category = CategoryOf(seat);
        if (seat.PendingGame == GameChoice.Solo)
        {
            var start = new LobbyStart(GameChoice.Solo, [ToPlayer(seat)], category);
            seat.Clear();
            Version++;
            return LobbyResult.Start(start);
        }
        if (Forming != null)
        {
            seat.GoTo(LobbyStep.Game); // someone else's started forming meanwhile: join it, or play solo
            return Changed();
        }
        Forming = new LobbyGroup($"g{++_groupsMade}", seat.PendingGame, category, now + TimeSpan.FromSeconds(_settings.JoinSeconds));
        Forming.Members.Add(seat);
        seat.JoinGroup(Forming);
        return Changed();
    }

    private void Leave(LobbySeat seat)
    {
        var group = seat.Group!;
        group.Members.Remove(seat);
        seat.LeaveGroup();
        if (group.Members.Count == 0)
            Forming = null;
        else if (group.Members.All(m => m.Side == 1))
            group.Members.ForEach(m => m.SwitchSide()); // a battle needs someone on the host's team to be named after
    }

    /// <summary>
    /// The game starts with whoever is in it: alone, it's a solo game; a battle with nobody on one team is a
    /// party (everyone together).
    /// </summary>
    private LobbyResult Begin(LobbyGroup group)
    {
        var players = group.Members.Select(ToPlayer).ToList();
        var bothSides = players.Any(p => p.Side == 0) && players.Any(p => p.Side == 1);
        var game = players.Count == 1 ? GameChoice.Solo
            : group.Kind == GameChoice.Battle && !bothSides ? GameChoice.Party
            : group.Kind;
        foreach (var member in group.Members)
            member.Clear();
        Forming = null;
        Version++;
        return LobbyResult.Start(new LobbyStart(game, players, group.Category));
    }

    private static LobbyPlayer ToPlayer(LobbySeat seat) =>
        new(seat.StationId, seat.Name, seat.PlayerProfileId, seat.Hero, seat.HasPhoto, seat.Side);

    private LobbyResult Changed()
    {
        Version++;
        return LobbyResult.Change();
    }
}
