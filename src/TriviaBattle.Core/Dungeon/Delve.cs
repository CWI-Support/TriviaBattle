using TriviaBattle.Core.Dungeon.Minigames;
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
/// <param name="Damage">What the monster in front took. <see cref="TotalDamage"/> adds any splash.</param>
/// <param name="Combo">A Pugilist's combo (<see cref="ComboCount"/> right answers in a row).</param>
/// <param name="Killed">The monster in front died; the next one (in the room, or the next room) is already up.</param>
/// <param name="FloorCleared">That was the floor's last monster; the delve is now on the next floor.</param>
/// <param name="MonsterName">The monster hit (the battle scene draws it even after it died).</param>
/// <param name="IsBoss">The monster hit was a boss.</param>
/// <param name="MonsterHpLeft">Its HP after the hit (0 if it died), of <paramref name="MonsterMaxHp"/>.</param>
/// <param name="PartyCombo">Everyone standing in the party answered right within a few seconds: a bonus hit.</param>
/// <param name="RevivedOwnerId">This answer was the last one needed to revive a downed teammate (their answer owner id).</param>
/// <param name="Fireball">A Fireball power-up was behind it.</param>
/// <param name="Found">A power-up they found with this hit (dropped by the monster, or earned by the streak).</param>
/// <param name="Splashes">A Wizard's fast answer: what it did to each of the other monsters in the room.</param>
/// <param name="HealedOwnerId">A Healer's fast answer: who it healed (by <see cref="Healed"/> HP).</param>
/// <param name="PoisonStacks">A Healer's fast answer with nobody to heal: the monster's poison stacks now.</param>
/// <param name="Poison">Not an answer: the poison on the monster killed it (nobody scores for that).</param>
/// <param name="PatronPrice">A Warlock's slow answer: the HP their patron took for it.</param>
/// <param name="BossAhead">This kill emptied the room and the next is a boss's: the boss steps up (the screens play its entrance).</param>
/// <param name="Staggered">A right answer when the boss was at full charge: its blow is broken, its charge starts over, and the hit does extra damage.</param>
/// <param name="MinigameAhead">This kill emptied a room that leads to the floor's mini game: it's played before the next room.</param>
/// <param name="RoomCleared">This kill emptied the room and the party walks on to the floor's next room (not a floor cleared). With no
/// boss or mini game ahead, the screens walk them there (<see cref="DungeonSettings.RoomBeatSeconds"/>).</param>
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
    string? RevivedOwnerId,
    bool Fireball = false,
    PowerUp? Found = null,
    IReadOnlyList<SplashHit>? Splashes = null,
    int ComboCount = 0,
    string? HealedOwnerId = null,
    int Healed = 0,
    int PoisonStacks = 0,
    bool Poison = false,
    int PatronPrice = 0,
    bool BossAhead = false,
    bool Staggered = false,
    MinigameKind? MinigameAhead = null,
    bool RoomCleared = false)
{
    /// <summary>All the damage the hit did: the monster in front, plus any splash on the others.</summary>
    public int TotalDamage => Damage + (Splashes?.Sum(s => s.Damage) ?? 0);
}

/// <summary>
/// A kill an adventurer watched: a teammate landed it (<paramref name="ByOwnerId"/>), or the party's poison did (null).
/// </summary>
public sealed record WatchedKill(DealtHit Hit, string? ByOwnerId, DateTimeOffset At);

/// <summary>A Wizard's fast answer, on one of the monsters waiting in the room.</summary>
public sealed record SplashHit(string MonsterName, string MonsterSprite, int Damage, int HpLeft, bool Killed);

/// <summary>A hit on the adventurer. <see cref="Number"/> counts up like <see cref="DealtHit.Number"/>.</summary>
/// <param name="Downed">It took them to 0 HP.</param>
/// <param name="HpLeft">Their HP after the hit.</param>
/// <param name="Blocked">A Shield took it: no damage.</param>
/// <param name="PatronPrice">A Warlock's slow (wrong) answer: the HP their patron took on top.</param>
/// <param name="Charged">A boss at full charge landed its big blow (a miss, or too slow, when it was strongest).</param>
/// <param name="LastStand">It would have downed them, but a Last Stand reward left them on 1 HP.</param>
/// <param name="Out">It downed them on their last life: they're out (until a continue).</param>
public sealed record TakenHit(int Number, int Damage, HitReason Reason, bool Downed, string MonsterName, string MonsterSprite, int HpLeft,
    bool Blocked = false, int PatronPrice = 0, bool Charged = false, bool LastStand = false, bool Out = false);

/// <summary>
/// One monster in the room a party is in: its hit points and any poison on it. The room's monsters
/// are <see cref="Delve.Foes"/>, the one being fought first.
/// </summary>
/// <param name="maxHp">Its HP for this party: the encounter's, made tougher for a party of several.</param>
public sealed class Foe(Encounter encounter, int maxHp)
{
    public Encounter Encounter { get; } = encounter;
    public MonsterDef Monster => Encounter.Monster;
    public int MaxHp { get; } = maxHp;
    public int Hp { get; private set; } = maxHp;
    public bool IsDead => Hp == 0;

    /// <summary>A Healer's poison: every stack does its damage each tick, until it dies.</summary>
    public int PoisonStacks { get; private set; }

    /// <summary>When the poison next does its damage (null with no poison).</summary>
    public DateTimeOffset? NextPoisonAt { get; private set; }

    public void TakeDamage(int damage) => Hp = Math.Max(0, Hp - damage);

    /// <summary>One more stack. The first starts the poison ticking one tick from now.</summary>
    public void AddPoison(DateTimeOffset now, double tickSeconds)
    {
        PoisonStacks++;
        NextPoisonAt ??= now + TimeSpan.FromSeconds(tickSeconds);
    }

    public void PoisonTicked(double tickSeconds) => NextPoisonAt += TimeSpan.FromSeconds(tickSeconds);

    /// <summary>Back to full health, and the poison wears off (the whole party went down).</summary>
    public void Recover()
    {
        Hp = MaxHp;
        PoisonStacks = 0;
        NextPoisonAt = null;
    }
}

/// <summary>
/// One party's trip down the dungeon during a Dungeon-format match: the floor and room they're in and the
/// monsters in it. A party is a team, or a single player (a solo game is a party of one).
/// Every member's correct answers hit the same monster, the one in front of the room's pack; each member has
/// their own hit points (see <see cref="Adventurer"/>). Parties race each other down.
///
/// This class only keeps the delve's state; the combat rules are in <see cref="DungeonRun"/>.
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class Delve
{
    private readonly DungeonMap _map;
    private readonly double _hpScale;
    private readonly List<Foe> _foes = [];

    /// <param name="hpScale">How much tougher every monster is for this party (1 for one player; see DungeonRun.HpScaleFor).</param>
    public Delve(string partyId, DungeonMap map, double hpScale = 1)
    {
        PartyId = partyId;
        _map = map;
        _hpScale = hpScale;
        EnterRoom(1, 1);
    }

    /// <summary>A team id, or a station id for a player on their own.</summary>
    public string PartyId { get; }

    /// <summary>The monsters still alive in the room, the one being fought first. Never empty.</summary>
    public IReadOnlyList<Foe> Foes => _foes;

    /// <summary>The monster being fought (the front of the room's pack): its hourglass and its attacks.</summary>
    public Encounter Encounter => _foes[0].Encounter;
    public int MonsterHp => _foes[0].Hp;

    public int Kills { get; private set; }
    public int DamageDealt { get; private set; }

    /// <summary>Deepest floor reached (1-based). Ranking in Dungeon format is by this, then score.</summary>
    public int Floor => Encounter.Floor;

    /// <summary>The last hit on the monster, by anyone in the party (or the poison, when it killed).</summary>
    public DealtHit? LastHit { get; private set; }

    /// <summary>The last hit the monster landed on anyone in the party, numbered for the party (the main screen's party lane).</summary>
    public TakenHit? LastTaken { get; private set; }

    /// <summary>When the party last pulled off a party combo. The next one needs a fresh right answer from everyone.</summary>
    public DateTimeOffset LastPartyComboAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Every monster of the Depth they're in, to its boss (see <see cref="DungeonMap.DepthPath"/>).</summary>
    public IReadOnlyList<Encounter> DepthPath() => _map.DepthPath(Floor);

    /// <summary>
    /// Hits the monster in front, and with <paramref name="splashDamage"/> (a Wizard's fast answer) every other
    /// monster in the room too. The dead are cleared away; when the room is empty, the party moves on.
    /// </summary>
    /// <param name="partyCombo">Recorded on the hit, and the party's combo starts over.</param>
    /// <param name="revivedOwnerId">Recorded on the hit, for the screens.</param>
    public DealtHit Hit(int damage, bool crit, bool combo, bool partyCombo, string? revivedOwnerId, DateTimeOffset now, int splashDamage = 0)
    {
        if (partyCombo)
            LastPartyComboAt = now;

        var front = _foes[0];
        var splashes = new List<SplashHit>();
        if (splashDamage > 0)
        {
            foreach (var waiting in _foes.Skip(1))
            {
                waiting.TakeDamage(splashDamage);
                DamageDealt += splashDamage;
                splashes.Add(new SplashHit(waiting.Monster.Name, waiting.Monster.Sprite, splashDamage, waiting.Hp, waiting.IsDead));
            }
        }

        front.TakeDamage(damage);
        DamageDealt += damage;
        var (killed, floorCleared, roomCleared, bossAhead, minigame) = ClearTheDead();

        LastHit = new DealtHit((LastHit?.Number ?? 0) + 1, damage, crit, combo, killed, floorCleared, front.Monster.Name, front.Monster.Sprite,
            front.Monster.IsBoss, front.Hp, front.MaxHp, partyCombo, revivedOwnerId, Splashes: splashes.Count > 0 ? splashes : null, BossAhead: bossAhead,
            RoomCleared: roomCleared,
            MinigameAhead: minigame);
        return LastHit;
    }

    /// <summary>
    /// The hit just made, completed with what happened around it (a power-up found, a heal, poison...), so the
    /// party's <see cref="LastHit"/> says everything the hitter's own copy does.
    /// </summary>
    public void Complete(DealtHit hit)
    {
        if (LastHit?.Number == hit.Number)
            LastHit = hit;
    }

    /// <summary>A Healer's poison on the monster in front: one more stack. Returns how many it has now.</summary>
    public int Poison(DateTimeOffset now, double tickSeconds)
    {
        _foes[0].AddPoison(now, tickSeconds);
        return _foes[0].PoisonStacks;
    }

    /// <summary>
    /// The poison does its damage: every stack, every tick that's due (one per <paramref name="tickSeconds"/>).
    /// Returns the damage done (0 = nothing changed), and the hit if it killed the monster in front.
    /// </summary>
    public (int Damage, DealtHit? Kill) PoisonTick(DateTimeOffset now, int damagePerStack, double tickSeconds)
    {
        var total = 0;
        foreach (var foe in _foes.Where(f => f.NextPoisonAt <= now))
        {
            while (foe.NextPoisonAt <= now && !foe.IsDead)
            {
                var damage = foe.PoisonStacks * damagePerStack;
                foe.TakeDamage(damage);
                foe.PoisonTicked(tickSeconds);
                DamageDealt += damage;
                total += damage;
            }
        }
        if (total == 0)
            return (0, null);

        var front = _foes[0];
        if (!_foes.Any(f => f.IsDead))
            return (total, null);

        var (killed, floorCleared, roomCleared, bossAhead, minigame) = ClearTheDead();
        if (!killed)
            return (total, null);
        LastHit = new DealtHit((LastHit?.Number ?? 0) + 1, 0, false, false, true, floorCleared, front.Monster.Name, front.Monster.Sprite,
            front.Monster.IsBoss, 0, front.MaxHp, false, null, Poison: true, BossAhead: bossAhead, MinigameAhead: minigame,
            RoomCleared: roomCleared);
        return (total, LastHit);
    }

    /// <summary>Keeps a party-numbered copy of a hit a member took, so the party lane can animate each one once.</summary>
    public void RecordTaken(TakenHit hit) => LastTaken = hit with { Number = (LastTaken?.Number ?? 0) + 1 };

    /// <summary>The whole party is down, so the room's monsters get their breath back (and the poison wears off).</summary>
    public void HealRoom()
    {
        foreach (var foe in _foes)
            foe.Recover();
    }

    /// <summary>
    /// Clears the dead away, counting the kills. When nobody's left in the room, the party moves on to the
    /// next room, or the next floor after the last one. Returns whether the one in front died, whether the floor was
    /// cleared, whether they walked on to the floor's next room, whether the room they've just walked into is a boss's
    /// (its entrance plays), and the floor's mini game if the room they've just cleared leads to it.
    /// </summary>
    private (bool FrontDied, bool FloorCleared, bool RoomCleared, bool BossAhead, MinigameKind? Minigame) ClearTheDead()
    {
        var room = Encounter;
        var frontDied = _foes[0].IsDead;
        Kills += _foes.RemoveAll(f => f.IsDead);
        if (_foes.Count > 0)
            return (frontDied, false, false, false, null);

        var floorCleared = room.Room == room.RoomsOnFloor;
        var minigame = floorCleared ? null : _map.MinigameAfter(room.Floor, room.Room);
        if (floorCleared)
            EnterRoom(room.Floor + 1, 1);
        else
            EnterRoom(room.Floor, room.Room + 1);
        return (frontDied, floorCleared, !floorCleared, _foes[0].Monster.IsBoss, minigame);
    }

    private void EnterRoom(int floor, int room)
    {
        _foes.Clear();
        _foes.AddRange(_map.RoomAt(floor, room).Select(e => new Foe(e, (int)Math.Round(e.MaxHp * _hpScale))));
    }
}
