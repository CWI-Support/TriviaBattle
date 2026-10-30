using TriviaBattle.Core.Questions;

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
public sealed record DealtHit(int Number, int Damage, bool Crit, bool Combo, bool Killed, bool FloorCleared, string MonsterName);

/// <summary>A hit on the adventurer. <see cref="Number"/> counts up like <see cref="DealtHit.Number"/>.</summary>
/// <param name="Downed">It took them to 0 HP.</param>
public sealed record TakenHit(int Number, int Damage, HitReason Reason, bool Downed, string MonsterName);

/// <summary>
/// One answer owner's trip down the dungeon during a Dungeon-format match: a player, or a party
/// in 2v2 Group. It has its own floor, monster and hit points, so parties race each other down.
///
/// The rules:
/// <list type="bullet">
///   <item>A correct answer hits the monster. Fast = crit; a streak = combo. At 0 HP the next monster steps up.</item>
///   <item>A wrong answer, or the monster's hourglass running out, and the monster hits back.</item>
///   <item>At 0 HP the adventurer is downed for a few seconds, then gets up at full HP; the monster heals too.</item>
/// </list>
/// The match engine decides when the hourglass runs (it pauses while buttons are dead).
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class Delve
{
    private readonly DungeonMap _map;
    private readonly DungeonSettings _settings;

    public Delve(string ownerId, DungeonMap map, DungeonSettings settings)
    {
        OwnerId = ownerId;
        _map = map;
        _settings = settings;
        MaxHp = settings.AdventurerHp;
        Hp = MaxHp;
        Meet(_map.EncounterAt(1, 1));
    }

    public string OwnerId { get; }

    public Encounter Encounter { get; private set; } = null!;
    public int MonsterHp { get; private set; }

    public int Hp { get; private set; }
    public int MaxHp { get; }

    /// <summary>Correct answers in a row.</summary>
    public int Streak { get; private set; }

    public int Kills { get; private set; }
    public int DamageDealt { get; private set; }

    /// <summary>Deepest floor reached (1-based). Ranking in Dungeon format is by this, then score.</summary>
    public int Floor => Encounter.Floor;

    /// <summary>When the monster attacks unless answered first. Null while the hourglass is stopped.</summary>
    public DateTimeOffset? AttackAt { get; private set; }
    public DateTimeOffset? AttackStartedAt { get; private set; }

    /// <summary>Set while downed (buttons dead); null otherwise.</summary>
    public DateTimeOffset? DownedUntil { get; private set; }

    public DealtHit? LastDealt { get; private set; }
    public TakenHit? LastTaken { get; private set; }

    /// <summary>Turns the hourglass over: the monster attacks after its AttackSeconds.</summary>
    public void StartHourglass(DateTimeOffset now)
    {
        AttackStartedAt = now;
        AttackAt = now + TimeSpan.FromSeconds(Encounter.Monster.AttackSeconds);
    }

    public void StopHourglass()
    {
        AttackStartedAt = null;
        AttackAt = null;
    }

    public bool IsAttackDue(DateTimeOffset now) => AttackAt != null && now >= AttackAt;

    /// <summary>Every monster of the Depth they're in, to its boss (see <see cref="DungeonMap.DepthPath"/>).</summary>
    public IReadOnlyList<Encounter> DepthPath() => _map.DepthPath(Floor);

    /// <summary>A correct answer: hits the monster, and moves on to the next one if it dies.</summary>
    public DealtHit Strike(Difficulty difficulty, TimeSpan responseTime)
    {
        Streak++;
        var crit = responseTime.TotalSeconds <= _settings.CritWithinSeconds;
        var combo = Streak >= _settings.ComboStreak;

        var damage = (double)_settings.DamageFor(difficulty);
        if (crit)
            damage *= _settings.CritMultiplier;
        if (combo)
            damage *= 1 + _settings.ComboBonus;
        var dealt = (int)Math.Round(damage);

        var monsterName = Encounter.Monster.Name;
        MonsterHp = Math.Max(0, MonsterHp - dealt);
        DamageDealt += dealt;

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

        LastDealt = new DealtHit((LastDealt?.Number ?? 0) + 1, dealt, crit, combo, killed, floorCleared, monsterName);
        return LastDealt;
    }

    /// <summary>The monster hits back. At 0 HP: downed until <paramref name="now"/> + DownedSeconds.</summary>
    public TakenHit TakeHit(HitReason reason, DateTimeOffset now)
    {
        Streak = 0;
        var damage = Encounter.Monster.Damage;
        Hp = Math.Max(0, Hp - damage);

        var downed = Hp == 0;
        if (downed)
        {
            DownedUntil = now + TimeSpan.FromSeconds(_settings.DownedSeconds);
            StopHourglass();
        }

        LastTaken = new TakenHit((LastTaken?.Number ?? 0) + 1, damage, reason, downed, Encounter.Monster.Name);
        return LastTaken;
    }

    /// <summary>Back on their feet after being downed: full HP, and the monster has healed too.</summary>
    public void GetUp()
    {
        DownedUntil = null;
        Hp = MaxHp;
        MonsterHp = Encounter.MaxHp;
    }

    private void Meet(Encounter encounter)
    {
        Encounter = encounter;
        MonsterHp = encounter.MaxHp;
    }
}
