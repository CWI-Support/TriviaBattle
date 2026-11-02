namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// One answer owner in a Dungeon-format match: a player, or a team in 2v2 Group (teammates share
/// one question there, so they are one adventurer). They fight their party's monster (see
/// <see cref="Delve"/>) with their own hit points, streak and hourglass.
///
/// This class only keeps the adventurer's state; the rules that change it are in <see cref="DungeonRun"/>.
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class Adventurer
{
    public Adventurer(string ownerId, string partyId, int maxHp)
    {
        OwnerId = ownerId;
        PartyId = partyId;
        MaxHp = maxHp;
        Hp = maxHp;
    }

    /// <summary>The answer owner: a station id, or a team id in 2v2 Group.</summary>
    public string OwnerId { get; }

    /// <summary>The party they fight with (whose <see cref="Delve"/> they're on): their team, or just themselves.</summary>
    public string PartyId { get; }

    public int Hp { get; private set; }
    public int MaxHp { get; }

    /// <summary>Correct answers in a row.</summary>
    public int Streak { get; private set; }

    /// <summary>When the monster attacks unless answered first. Null while the hourglass is stopped.</summary>
    public DateTimeOffset? AttackAt { get; private set; }
    public DateTimeOffset? AttackStartedAt { get; private set; }

    /// <summary>Set while downed (buttons dead); null otherwise.</summary>
    public DateTimeOffset? DownedUntil { get; private set; }

    public bool IsDowned => DownedUntil != null;

    /// <summary>While downed in a party: teammates' right answers so far towards reviving them.</summary>
    public int ReviveProgress { get; private set; }

    /// <summary>When they last answered right (party combos need everyone's to be recent).</summary>
    public DateTimeOffset? LastCorrectAt { get; private set; }

    /// <summary>The last hit they landed themselves. A teammate may have hit the party's monster since.</summary>
    public DealtHit? LastDealt { get; private set; }

    /// <summary>The last hit they took. <see cref="TakenHit.Number"/> counts up so screens can tell a new hit from an old one.</summary>
    public TakenHit? LastTaken { get; private set; }

    /// <summary>Turns the hourglass over: the monster attacks after <paramref name="attackSeconds"/>.</summary>
    public void StartHourglass(DateTimeOffset now, double attackSeconds)
    {
        AttackStartedAt = now;
        AttackAt = now + TimeSpan.FromSeconds(attackSeconds);
    }

    public void StopHourglass()
    {
        AttackStartedAt = null;
        AttackAt = null;
    }

    public bool IsAttackDue(DateTimeOffset now) => AttackAt != null && now >= AttackAt;

    /// <summary>A correct answer: one more in the streak. Returns the streak including this one.</summary>
    public int AddToStreak() => ++Streak;

    public void Landed(DealtHit hit, DateTimeOffset now)
    {
        LastDealt = hit;
        LastCorrectAt = now;
    }

    /// <summary>A teammate answered right while they're down. Returns the progress so far.</summary>
    public int AddReviveProgress() => ++ReviveProgress;

    /// <summary>Their downed time ends sooner (the whole party is down, so everyone gets up quickly).</summary>
    public void ShortenDowned(DateTimeOffset until)
    {
        if (DownedUntil > until)
            DownedUntil = until;
    }

    /// <summary>The monster hits them, ending the streak. At 0 HP they're downed for <paramref name="downedSeconds"/>.</summary>
    public TakenHit TakeHit(MonsterDef monster, HitReason reason, DateTimeOffset now, double downedSeconds)
    {
        Streak = 0;
        Hp = Math.Max(0, Hp - monster.Damage);

        var downed = Hp == 0;
        if (downed)
        {
            DownedUntil = now + TimeSpan.FromSeconds(downedSeconds);
            ReviveProgress = 0;
            StopHourglass();
        }

        LastTaken = new TakenHit((LastTaken?.Number ?? 0) + 1, monster.Damage, reason, downed, monster.Name, monster.Sprite, Hp);
        return LastTaken;
    }

    /// <summary>Back on their feet after being downed (their time ran out, or teammates revived them), at full HP.</summary>
    public void GetUp()
    {
        DownedUntil = null;
        ReviveProgress = 0;
        Hp = MaxHp;
    }
}
