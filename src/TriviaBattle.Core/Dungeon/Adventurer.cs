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

    /// <summary>The power-up they're holding, for the action button. One at a time.</summary>
    public PowerUp? Held { get; private set; }

    /// <summary>A Fireball is ready: their next right answer does extra damage.</summary>
    public bool FireballReady { get; private set; }

    /// <summary>A Shield is up: the next hit they take does no damage.</summary>
    public bool Shielded { get; private set; }

    /// <summary>A Freeze: their hourglass doesn't run until then.</summary>
    public DateTimeOffset? FrozenUntil { get; private set; }

    /// <summary>A 50/50: the answers taken away from their question number <see cref="HiddenOnQuestion"/>.</summary>
    public IReadOnlyList<int> HiddenAnswers { get; private set; } = [];
    public int HiddenOnQuestion { get; private set; }

    /// <summary>The last power-up they used. Its Number counts up so screens can tell a new use from an old one.</summary>
    public PowerUpUse? LastPowerUp { get; private set; }

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

    /// <summary>
    /// The monster hits them, ending the streak. At 0 HP they're downed for <paramref name="downedSeconds"/>.
    /// A Shield takes the whole hit instead (and the streak survives), then it's gone.
    /// </summary>
    public TakenHit TakeHit(MonsterDef monster, HitReason reason, DateTimeOffset now, double downedSeconds)
    {
        var blocked = Shielded;
        Shielded = false;
        var damage = blocked ? 0 : monster.Damage;
        if (!blocked)
            Streak = 0;
        Hp = Math.Max(0, Hp - damage);

        var downed = Hp == 0;
        if (downed)
        {
            DownedUntil = now + TimeSpan.FromSeconds(downedSeconds);
            ReviveProgress = 0;
            StopHourglass();
        }

        LastTaken = new TakenHit((LastTaken?.Number ?? 0) + 1, damage, reason, downed, monster.Name, monster.Sprite, Hp, blocked);
        return LastTaken;
    }

    /// <summary>Picks up a power-up (or is handed one), replacing any they held.</summary>
    public void PickUp(PowerUp powerUp) => Held = powerUp;

    /// <summary>Lets go of the power-up they're holding, to use it.</summary>
    public void DropHeld() => Held = null;

    public void ReadyFireball() => FireballReady = true;

    /// <summary>Their right answer is the fireball. Returns true if one was ready (it's used up).</summary>
    public bool ThrowFireball()
    {
        var ready = FireballReady;
        FireballReady = false;
        return ready;
    }

    public void RaiseShield() => Shielded = true;

    public void Freeze(DateTimeOffset until) => FrozenUntil = until;

    public void HideAnswers(int questionNumber, IReadOnlyList<int> answers)
    {
        HiddenOnQuestion = questionNumber;
        HiddenAnswers = answers;
    }

    /// <summary>Heals up to <paramref name="amount"/>, never past their max. Returns how much it healed.</summary>
    public int Heal(int amount)
    {
        var healed = Math.Min(amount, MaxHp - Hp);
        Hp += healed;
        return healed;
    }

    public PowerUpUse RecordPowerUp(PowerUp powerUp, string targetOwnerId, int amount, bool revived)
    {
        LastPowerUp = new PowerUpUse((LastPowerUp?.Number ?? 0) + 1, powerUp, targetOwnerId, amount, revived);
        return LastPowerUp;
    }

    /// <summary>Back on their feet after being downed (their time ran out, or teammates revived them), at full HP.</summary>
    public void GetUp()
    {
        DownedUntil = null;
        ReviveProgress = 0;
        Hp = MaxHp;
    }
}
