namespace TriviaBattle.Core.Dungeon;

/// <summary>Why an adventurer took a hit.</summary>
public enum HitReason
{
    /// <summary>They answered wrong.</summary>
    WrongAnswer,

    /// <summary>The monster's hourglass ran out before they answered.</summary>
    TooSlow,
}

/// <summary>A hit on the monster. <see cref="Number"/> counts up so screens can tell a new hit from an old one.</summary>
/// <param name="Killed">The monster died; the next one is already up.</param>
/// <param name="FloorCleared">That was the floor's last monster; the delve is now on the next floor.</param>
/// <param name="MonsterName">The monster hit (the battle scene draws it even after it died).</param>
/// <param name="IsBoss">The monster hit was a boss.</param>
/// <param name="MonsterHpLeft">Its HP after the hit (0 if it died), of <paramref name="MonsterMaxHp"/>.</param>
/// <param name="PartyCombo">Everyone standing in the party answered right within a few seconds: a bonus hit.</param>
/// <param name="RevivedOwnerId">This answer was the last one needed to revive a downed teammate (their answer owner id).</param>
public sealed record DealtHit(
    int Number,
    int Damage,
    bool Crit,
    bool Combo,
    bool Killed,
    bool FloorCleared,
    string MonsterName,
    string MonsterSprite,
    bool IsBoss,
    int MonsterHpLeft,
    int MonsterMaxHp,
    bool PartyCombo,
    string? RevivedOwnerId);

/// <summary>A hit on the adventurer. <see cref="Number"/> counts up like <see cref="DealtHit.Number"/>.</summary>
/// <param name="Downed">It took them to 0 HP.</param>
/// <param name="HpLeft">Their HP after the hit.</param>
public sealed record TakenHit(int Number, int Damage, HitReason Reason, bool Downed, string MonsterName, string MonsterSprite, int HpLeft);

/// <summary>
/// One party's trip down the dungeon during a Dungeon-format match: the floor they're on and the
/// monster they're fighting. A party is a team, or a single player (a solo game is a party of one).
/// Every member's correct answers hit the same monster; each member has their own hit points
/// (see <see cref="Adventurer"/>). Parties race each other down.
///
/// This class only keeps the delve's state; the combat rules are in <see cref="DungeonRun"/>.
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class Delve
{
    private readonly DungeonMap _map;

    public Delve(string partyId, DungeonMap map)
    {
        PartyId = partyId;
        _map = map;
        Meet(_map.EncounterAt(1, 1));
    }

    /// <summary>A team id, or a station id for a player on their own.</summary>
    public string PartyId { get; }

    public Encounter Encounter { get; private set; } = null!;
    public int MonsterHp { get; private set; }

    public int Kills { get; private set; }
    public int DamageDealt { get; private set; }

    /// <summary>Deepest floor reached (1-based). Ranking in Dungeon format is by this, then score.</summary>
    public int Floor => Encounter.Floor;

    /// <summary>The last hit on the monster, by anyone in the party.</summary>
    public DealtHit? LastHit { get; private set; }

    /// <summary>The last hit the monster landed on anyone in the party, numbered for the party (the main screen's party lane).</summary>
    public TakenHit? LastTaken { get; private set; }

    /// <summary>When the party last pulled off a party combo. The next one needs a fresh right answer from everyone.</summary>
    public DateTimeOffset LastPartyComboAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Every monster of the Depth they're in, to its boss (see <see cref="DungeonMap.DepthPath"/>).</summary>
    public IReadOnlyList<Encounter> DepthPath() => _map.DepthPath(Floor);

    /// <summary>Hits the monster, and moves on to the next one if it dies.</summary>
    /// <param name="partyCombo">Recorded on the hit, and the party's combo starts over.</param>
    /// <param name="revivedOwnerId">Recorded on the hit, for the screens.</param>
    public DealtHit Hit(int damage, bool crit, bool combo, bool partyCombo, string? revivedOwnerId, DateTimeOffset now)
    {
        if (partyCombo)
            LastPartyComboAt = now;

        var monster = Encounter.Monster;
        var maxHp = Encounter.MaxHp;
        MonsterHp = Math.Max(0, MonsterHp - damage);
        var hpLeft = MonsterHp;
        DamageDealt += damage;

        var killed = MonsterHp == 0;
        var floorCleared = false;
        if (killed)
        {
            Kills++;
            floorCleared = Encounter.Room == Encounter.RoomsOnFloor;
            Meet(floorCleared
                ? _map.EncounterAt(Encounter.Floor + 1, 1)
                : _map.EncounterAt(Encounter.Floor, Encounter.Room + 1));
        }

        LastHit = new DealtHit((LastHit?.Number ?? 0) + 1, damage, crit, combo, killed, floorCleared, monster.Name, monster.Sprite,
            monster.IsBoss, hpLeft, maxHp, partyCombo, revivedOwnerId);
        return LastHit;
    }

    /// <summary>Keeps a party-numbered copy of a hit a member took, so the party lane can animate each one once.</summary>
    public void RecordTaken(TakenHit hit) => LastTaken = hit with { Number = (LastTaken?.Number ?? 0) + 1 };

    /// <summary>The whole party is down, so the monster gets its breath back.</summary>
    public void HealMonster() => MonsterHp = Encounter.MaxHp;

    private void Meet(Encounter encounter)
    {
        Encounter = encounter;
        MonsterHp = encounter.MaxHp;
    }
}
