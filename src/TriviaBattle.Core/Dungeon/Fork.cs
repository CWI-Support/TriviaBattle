using TriviaBattle.Core.Dungeon.Minigames;

namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// A floor's fork as the map has it (<see cref="DungeonMap.ForkOn"/>): the special room behind each of its two doors
/// (<see cref="MinigameCatalog.Rooms"/>, two different ones), door A's first. Nobody's told which is which until one is
/// opened. The same for every party in the match, so a race is fair.
/// </summary>
public sealed record ForkPlan(IReadOnlyList<MinigameKind> Rooms);

/// <summary>
/// A party at a fork, right before a floor's boss (the boss's entrance has played: they've seen what's waiting). Two
/// doors that look the same, each to a special room (<see cref="MinigameCatalog.Rooms"/>), and everyone in the party
/// who's there (<see cref="Voters"/>) votes with an answer button: A for the first door, B for the second. It's a gamble:
/// what's behind a door only shows once it's opened. Presses count from <see cref="VotesFrom"/>. It's decided once
/// everyone has voted, or when time's up (<see cref="EndsAt"/>): the door with the most votes. A tie goes to the door voted
/// for first, and with no votes at all one is picked at random. The door taken opens until <see cref="RoomFrom"/>, then
/// everyone who voted plays the room behind it, each their own go, and after it the boss. The arcade clock stops
/// meanwhile, as in a mini game. Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class Fork
{
    private readonly ForkSettings _settings;
    private readonly List<(string OwnerId, int Door, DateTimeOffset At)> _votes = [];

    public Fork(string partyId, int floor, ForkPlan plan, IReadOnlyList<string> voters, DateTimeOffset opensAt, ForkSettings settings)
    {
        PartyId = partyId;
        Floor = floor;
        Plan = plan;
        Voters = voters;
        OpensAt = opensAt;
        _settings = settings;
    }

    public string PartyId { get; }

    /// <summary>The floor whose boss is behind it.</summary>
    public int Floor { get; }

    /// <summary>What's behind each door (a secret until one's opened: the snapshot only sends <see cref="Room"/>).</summary>
    public ForkPlan Plan { get; }

    /// <summary>How many doors there are, one per button that votes for them (A, B).</summary>
    public int Doors => Plan.Rooms.Count;

    /// <summary>Everyone at the fork (answer owner ids): the party, but not anyone out of lives or away at the time.</summary>
    public IReadOnlyList<string> Voters { get; }

    /// <summary>The doors come up (after the boss's entrance).</summary>
    public DateTimeOffset OpensAt { get; }

    /// <summary>Votes count from now, once the doors have come up.</summary>
    public DateTimeOffset VotesFrom => OpensAt + TimeSpan.FromSeconds(_settings.ReadSeconds);

    /// <summary>Time's up: it's decided by the votes so far.</summary>
    public DateTimeOffset EndsAt => OpensAt + TimeSpan.FromSeconds(_settings.VoteSeconds);

    /// <summary>The votes so far, in the order they came: who, which door (0-based), and when.</summary>
    public IReadOnlyList<(string OwnerId, int Door, DateTimeOffset At)> Votes => _votes;

    /// <summary>The door taken (0-based), once it's decided.</summary>
    public int? Taken { get; private set; }

    /// <summary>When it was decided; the door taken opens then.</summary>
    public DateTimeOffset? TakenAt { get; private set; }

    /// <summary>The room behind the door taken starts then (after it's opened: <see cref="ForkSettings.RevealSeconds"/>).</summary>
    public DateTimeOffset? RoomFrom => TakenAt + TimeSpan.FromSeconds(_settings.RevealSeconds);

    /// <summary>The special room behind the door taken. Null until it's decided.</summary>
    public MinigameKind? Room => Taken is { } door ? Plan.Rooms[door] : null;

    public bool EveryoneVoted => Voters.All(v => VoteOf(v) != null);

    public bool IsDecided => Taken != null;

    /// <summary>The door someone voted for (0-based), or null if they haven't.</summary>
    public int? VoteOf(string ownerId)
    {
        foreach (var vote in _votes)
        {
            if (vote.OwnerId == ownerId)
                return vote.Door;
        }
        return null;
    }

    /// <summary>
    /// Someone votes for a door (0-based: the answer button), once each. Returns false if it didn't count: not theirs to
    /// vote, voted already, no such door, too early, or already decided.
    /// </summary>
    public bool Vote(string ownerId, int door, DateTimeOffset at)
    {
        if (Taken != null || at < VotesFrom || door < 0 || door >= Doors || !Voters.Contains(ownerId) || VoteOf(ownerId) != null)
            return false;
        _votes.Add((ownerId, door, at));
        return true;
    }

    /// <summary>
    /// It's decided (everyone voted, or time's up): the door with the most votes, a tie to the one voted for first, with
    /// none a random one. Nothing happens if it's decided already.
    /// </summary>
    public void Decide(DateTimeOffset at, Random random)
    {
        if (Taken != null)
            return;
        Taken = _votes.Count == 0
            ? random.Next(Doors)
            : _votes.GroupBy(v => v.Door).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(v => v.At)).First().Key;
        TakenAt = at;
    }
}
