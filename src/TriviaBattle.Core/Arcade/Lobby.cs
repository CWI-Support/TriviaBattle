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

    /// <summary>Choosing a game: A, B, C as listed (<see cref="Lobby.ChoicesFor"/>), D to go back to the heroes.</summary>
    Game,

    /// <summary>In a party or race that's forming: ACTION is ready, D leaves it.</summary>
    Waiting,
}

/// <summary>What a player can choose on the Game step.</summary>
public enum GameChoice
{
    /// <summary>Just you: starts straight away, on the arcade clock.</summary>
    Solo,

    /// <summary>Starts a party the other stations can join: one dungeon, fought together.</summary>
    Party,

    /// <summary>Starts a race the other stations can join: everyone on their own delve, most points wins.</summary>
    Race,

    /// <summary>Joins the party or race that's forming.</summary>
    Join,
}

/// <summary>The lobby's numbers. Bound from "Game:Arcade".</summary>
public sealed class LobbySettings
{
    /// <summary>How long a party or race waits for players to join before it starts anyway.</summary>
    public double JoinSeconds { get; set; } = 20;

    /// <summary>Someone who joins near the end gets at least this long to get ready.</summary>
    public double LateJoinSeconds { get; set; } = 6;

    /// <summary>A station left choosing (hero or game) this long without a press goes back to "press any button".</summary>
    public double IdleSeconds { get; set; } = 30;

    /// <summary>The questions' difficulty for games started at the stations.</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Easy;
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

    /// <summary>The party or race they're in, while <see cref="LobbyStep.Waiting"/>.</summary>
    public LobbyGroup? Group { get; private set; }
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
        }
        LastPressAt = now;
    }

    public void Pressed(DateTimeOffset now) => LastPressAt = now;
    public void TurnHero(int by) => HeroIndex = (HeroIndex + by + Heroes.All.Count) % Heroes.All.Count;
    public void GoTo(LobbyStep step) => Step = step;

    public void JoinGroup(LobbyGroup group)
    {
        Group = group;
        Ready = false;
        Step = LobbyStep.Waiting;
    }

    public void LeaveGroup()
    {
        Group = null;
        Ready = false;
        Step = LobbyStep.Game;
    }

    public void SetReady() => Ready = true;

    /// <summary>Back to "press any button" (they left, or their game started).</summary>
    public void Clear()
    {
        Step = LobbyStep.Idle;
        Name = "";
        PlayerProfileId = null;
        Group = null;
        Ready = false;
    }
}

/// <summary>A party or race that's forming: who's in, and when it starts.</summary>
public sealed class LobbyGroup(string id, GameChoice kind, DateTimeOffset startsAt)
{
    public string Id { get; } = id;
    public GameChoice Kind { get; } = kind;

    /// <summary>In joining order: the first is the host (who started it).</summary>
    public List<LobbySeat> Members { get; } = [];
    public DateTimeOffset StartsAt { get; set; } = startsAt;
    public LobbySeat Host => Members[0];
}

/// <summary>A player in a game the lobby wants started.</summary>
public sealed record LobbyPlayer(string StationId, string Name, int? PlayerProfileId, string Hero);

/// <summary>A game the lobby wants started: the server adds the questions' categories and starts it.</summary>
/// <param name="Game">Solo, Party or Race. A party or race nobody joined starts as a solo game.</param>
public sealed record LobbyStart(GameChoice Game, IReadOnlyList<LobbyPlayer> Players)
{
    /// <summary>
    /// The match to start: a party is one team (2v2 Combined, where each player is their own adventurer
    /// in one shared delve); a race and a solo game are free-for-all.
    /// </summary>
    public MatchSetup ToSetup(IReadOnlyList<int> categoryIds, Difficulty difficulty)
    {
        var seats = Players.Select(p => new SeatSetup(p.StationId, p.Name, p.PlayerProfileId, p.Hero)).ToList();
        if (Game != GameChoice.Party)
            return new MatchSetup(GameModeKind.FreeForAll, categoryIds, difficulty, seats, []);

        var team = new TeamSetup("party", PartyName(Players[0].Name), seats.Select(s => s.StationId).ToList());
        return new MatchSetup(GameModeKind.TeamCombinedScore, categoryIds, difficulty, seats, [team]);
    }

    /// <summary>"Sir Bonkington's party", kept short enough to be a team name.</summary>
    private static string PartyName(string host) => $"{host}'s party";
}

/// <summary>What a lobby call did: whether anything changed (screens need redrawing), and any games to start.</summary>
public sealed record LobbyResult(bool Changed, IReadOnlyList<LobbyStart> Starts)
{
    public static readonly LobbyResult Nothing = new(false, []);
    public static LobbyResult Change() => new(true, []);
    public static LobbyResult Start(LobbyStart start) => new(true, [start]);
}

/// <summary>
/// The arcade lobby of one room: every station not in a game. A player walks up, presses any button (or
/// swipes their card), picks a hero, then a game: solo straight away, or a party or race that the room's
/// other stations can join until its timer runs out or everyone is ready. Only one party or race forms at
/// a time, so joining is always one obvious choice. When a game should start, the lobby hands back a
/// <see cref="LobbyStart"/> and those stations go back to idle: the match takes over their screens.
///
/// Like <see cref="MatchEngine"/>: the time is passed in, and the server calls it from one thread at a time.
/// </summary>
public sealed class Lobby
{
    private readonly Dictionary<string, LobbySeat> _seats;
    private readonly LobbySettings _settings;
    private readonly Random _random;
    private int _groupsMade;

    public Lobby(IEnumerable<string> stationIds, LobbySettings settings, Random random)
    {
        _seats = stationIds.ToDictionary(id => id, id => new LobbySeat(id));
        _settings = settings;
        _random = random;
    }

    public IReadOnlyCollection<LobbySeat> Seats => _seats.Values;

    /// <summary>The party or race that's forming, if any (only one at a time).</summary>
    public LobbyGroup? Forming { get; private set; }

    /// <summary>Goes up with every change, like <see cref="MatchEngine.Version"/>.</summary>
    public int Version { get; private set; }

    public LobbySeat? Seat(string stationId) => _seats.GetValueOrDefault(stationId);

    /// <summary>A guest name nobody in the lobby has.</summary>
    public string NewGuestName() => HeroNames.Make(_random, _seats.Values.Select(s => s.Name).ToList());

    /// <summary>The games on offer on the Game step, in button order (A, B, C): joining comes first when a game is forming.</summary>
    public IReadOnlyList<GameChoice> ChoicesFor(LobbySeat seat) =>
        Forming != null ? [GameChoice.Join, GameChoice.Solo] : [GameChoice.Solo, GameChoice.Party, GameChoice.Race];

    /// <summary>A button at a station that isn't in a game: answerIndex 0-3 (A-D), or <see cref="Input.ButtonPress.Action"/>.</summary>
    public LobbyResult Press(string stationId, int button, DateTimeOffset now)
    {
        if (Seat(stationId) is not { } seat)
            return LobbyResult.Nothing;
        const int A = 0, B = 1, D = 3, Action = Input.ButtonPress.Action;

        switch (seat.Step)
        {
            case LobbyStep.Idle:
                seat.Arrive(NewGuestName(), null, now);
                return Changed();

            case LobbyStep.Hero:
                seat.Pressed(now);
                if (button == A) seat.TurnHero(-1);
                else if (button == B) seat.TurnHero(+1);
                else if (button == Action) seat.GoTo(LobbyStep.Game);
                else return LobbyResult.Nothing;
                return Changed();

            case LobbyStep.Game:
                seat.Pressed(now);
                if (button == D)
                {
                    seat.GoTo(LobbyStep.Hero);
                    return Changed();
                }
                var choices = ChoicesFor(seat);
                return button < choices.Count ? Choose(seat, choices[button], now) : LobbyResult.Nothing;

            case LobbyStep.Waiting:
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
        seat.Arrive(name, playerProfileId, now);
        return Changed();
    }

    /// <summary>Starts a forming game whose timer has run out, and sends stations left choosing too long back to idle.</summary>
    public LobbyResult Tick(DateTimeOffset now)
    {
        if (Forming != null && now >= Forming.StartsAt)
            return Begin(Forming);

        var asleep = _seats.Values.Where(s => s.Step is LobbyStep.Hero or LobbyStep.Game && now - s.LastPressAt > TimeSpan.FromSeconds(_settings.IdleSeconds)).ToList();
        foreach (var seat in asleep)
            seat.Clear();
        return asleep.Count > 0 ? Changed() : LobbyResult.Nothing;
    }

    private LobbyResult Choose(LobbySeat seat, GameChoice choice, DateTimeOffset now)
    {
        switch (choice)
        {
            case GameChoice.Solo:
                var start = new LobbyStart(GameChoice.Solo, [ToPlayer(seat)]);
                seat.Clear();
                Version++;
                return LobbyResult.Start(start);

            case GameChoice.Join when Forming != null:
                seat.JoinGroup(Forming);
                Forming.Members.Add(seat);
                var lateJoin = now + TimeSpan.FromSeconds(_settings.LateJoinSeconds);
                if (Forming.StartsAt < lateJoin)
                    Forming.StartsAt = lateJoin;
                return Changed();

            case GameChoice.Party or GameChoice.Race when Forming == null:
                Forming = new LobbyGroup($"g{++_groupsMade}", choice, now + TimeSpan.FromSeconds(_settings.JoinSeconds));
                Forming.Members.Add(seat);
                seat.JoinGroup(Forming);
                return Changed();
        }
        return LobbyResult.Nothing;
    }

    private void Leave(LobbySeat seat)
    {
        var group = seat.Group!;
        group.Members.Remove(seat);
        seat.LeaveGroup();
        if (group.Members.Count == 0)
            Forming = null;
    }

    /// <summary>The party or race starts with whoever is in it (alone, it's a solo game).</summary>
    private LobbyResult Begin(LobbyGroup group)
    {
        var players = group.Members.Select(ToPlayer).ToList();
        var game = players.Count > 1 ? group.Kind : GameChoice.Solo;
        foreach (var member in group.Members)
            member.Clear();
        Forming = null;
        Version++;
        return LobbyResult.Start(new LobbyStart(game, players));
    }

    private static LobbyPlayer ToPlayer(LobbySeat seat) => new(seat.StationId, seat.Name, seat.PlayerProfileId, seat.Hero);

    private LobbyResult Changed()
    {
        Version++;
        return LobbyResult.Change();
    }
}
