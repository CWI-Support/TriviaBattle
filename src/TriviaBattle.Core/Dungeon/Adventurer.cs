using TriviaBattle.Core.Dungeon.Minigames;
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
    /// <param name="lives">How many times they can be downed and get up (the last downing puts them out); 0 = no limit.</param>
    public Adventurer(string ownerId, string partyId, int maxHp, HeroClass heroClass = HeroClass.Tank, int lives = 0)
    {
        MaxLives = lives;
        Lives = lives;
        OwnerId = ownerId;
        PartyId = partyId;
        MaxHp = maxHp;
        StartingMaxHp = maxHp;
        Hp = maxHp;
        Class = heroClass;
    }

    /// <summary>Lives left, of <see cref="MaxLives"/> (0 there = no limit). Each downing costs one.</summary>
    public int Lives { get; private set; }

    public int MaxLives { get; }

    /// <summary>Downed on their last life: they don't get up (until a continue brings them back with all their lives).</summary>
    public bool IsOut => MaxLives > 0 && Lives <= 0;

    /// <summary>What they can do in a fight: the hero they picked (see <see cref="HeroClass"/>).</summary>
    public HeroClass Class { get; }

    /// <summary>The answer owner: a station id, or a team id in 2v2 Group.</summary>
    public string OwnerId { get; }

    /// <summary>The party they fight with (whose <see cref="Delve"/> they're on): their team, or just themselves.</summary>
    public string PartyId { get; }

    public int Hp { get; private set; }
    public int MaxHp { get; private set; }

    /// <summary>Their max HP when the match started (Vitality rewards add a share of this).</summary>
    public int StartingMaxHp { get; }

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

    /// <summary>A Warlock: the question number their patron's offer was decided for (decided once per question).</summary>
    public int RevealDecidedFor { get; private set; }

    /// <summary>A Warlock: their patron will show the right answer to that question as the hourglass runs low.</summary>
    public bool RevealComing { get; private set; }

    /// <summary>A Warlock: when their patron shows it, on the hourglass running now (null: not coming, or it's stopped).</summary>
    public DateTimeOffset? RevealAt { get; private set; }

    /// <summary>A Warlock: the question number their patron has shown the right answer to (0 = none).</summary>
    public int RevealedOnQuestion { get; private set; }

    // Floor rewards (see Reward): how they're doing on this floor, what's on offer after its boss, what they've got.

    private readonly List<RewardTaken> _rewards = [];

    /// <summary>Their answers on the floor they're on so far (the floor's reward needs enough of them right).</summary>
    public FloorTrial Trial { get; private set; } = new(1, 0, 0);

    /// <summary>How they did on the last floor cleared, and whether it earned a reward (null before the first is cleared).</summary>
    public FloorTrial? LastTrial { get; private set; }

    /// <summary>Rewards they're choosing between now (their question waits meanwhile), or null.</summary>
    public RewardOffer? Offer { get; private set; }

    public bool IsChoosing => Offer != null;

    /// <summary>The mini game they're playing (from its intro until the moment after it ends), or null.</summary>
    public Minigame? Minigame { get; private set; }

    /// <summary>Mini games they've won.</summary>
    public int MinigamesWon { get; private set; }

    /// <summary>
    /// Away from the fight: choosing a reward or playing a mini game. Their question waits, the monster's hourglass
    /// stops, and the arcade clock stops for everyone (DungeonRun.ClockStopped).
    /// </summary>
    public bool IsAway => IsChoosing || Minigame != null;

    public void StartMinigame(Minigame minigame) => Minigame = minigame;

    /// <summary>The mini game's over (its result has been shown): back to the fight.</summary>
    public void EndMinigame()
    {
        if (Minigame?.Won == true)
            MinigamesWon++;
        Minigame = null;
    }

    /// <summary>Every reward they've picked, in order.</summary>
    public IReadOnlyList<RewardTaken> Rewards => _rewards;

    public RewardTaken? LastReward => _rewards.Count > 0 ? _rewards[^1] : null;

    /// <summary>How many times they've picked this reward (they stack).</summary>
    public int Has(Reward reward) => _rewards.Count(r => r.Reward == reward);

    /// <summary>Last Stands still to come: the next hit that would down them leaves them on 1 HP instead.</summary>
    public int LastStands { get; private set; }

    /// <summary>A question answered on <paramref name="floor"/> (right, wrong or too slow). A new floor starts a fresh count.</summary>
    public void CountAnswer(int floor, bool right)
    {
        var trial = Trial.Floor == floor ? Trial : new FloorTrial(floor, 0, 0);
        Trial = trial with { Right = trial.Right + (right ? 1 : 0), Answered = trial.Answered + 1 };
    }

    /// <summary>
    /// Their party cleared <paramref name="floor"/>: was enough of it right (at least <paramref name="accuracy"/> of
    /// their answers on it)? The count starts again for the next floor.
    /// </summary>
    public FloorTrial EndFloor(int floor, double accuracy)
    {
        var trial = Trial.Floor == floor ? Trial : new FloorTrial(floor, 0, 0);
        var earned = trial.Answered > 0 && trial.Right >= accuracy * trial.Answered - 1e-9;
        LastTrial = trial with { Earned = earned };
        Trial = new FloorTrial(floor + 1, 0, 0);
        return LastTrial;
    }

    public void OfferRewards(RewardOffer offer) => Offer = offer;

    /// <summary>
    /// A reward is theirs (picked from an offer, or handed over by the dev tools). Most of the rules read it from
    /// <see cref="Rewards"/> (see DungeonRun); a Last Stand is counted here.
    /// </summary>
    /// <param name="amount">What its card said it gives (kept for the screens).</param>
    /// <param name="floor">The floor it was earned on.</param>
    public RewardTaken TakeReward(Reward reward, double amount, int floor, DateTimeOffset now, bool picked)
    {
        var taken = new RewardTaken(_rewards.Count + 1, reward, amount, floor, now, picked);
        _rewards.Add(taken);
        if (reward == Dungeon.Reward.LastStand)
            LastStands++;
        return taken;
    }

    /// <summary>They've picked: the offer's gone.</summary>
    public void CloseOffer() => Offer = null;

    /// <summary>A Vitality reward: more max HP, and healed to full (unless they're down: they get up at full anyway).</summary>
    public void RaiseMaxHp(int amount)
    {
        MaxHp += amount;
        if (!IsDowned)
            Hp = MaxHp;
    }

    /// <summary>A Continue: the time to pick starts again, so nobody comes back to a reward picked for them.</summary>
    public void ExtendOffer(DateTimeOffset now, double pickSeconds)
    {
        if (Offer != null)
            Offer = Offer with { OpensAt = now, EndsAt = now + TimeSpan.FromSeconds(pickSeconds) };
    }

    /// <summary>
    /// When the party's poison last killed the monster in front while they were fighting: their question waited for
    /// a kill beat from then (see DungeonRun.PoisonKilled). Null if it never has.
    /// </summary>
    public DateTimeOffset? PoisonKillAt { get; private set; }

    public void SawPoisonKill(DateTimeOffset now) => PoisonKillAt = now;

    /// <summary>The boss their charge cycle is against (each adventurer has their own cycle, as each has their own questions).</summary>
    private Foe? _chargingBoss;
    private int _bossCharge;

    /// <summary>The step of a boss's charge cycle for their question now (0 = weak; a boss they haven't faced yet starts at 0).</summary>
    public int ChargeAgainst(Foe boss) => _chargingBoss == boss ? _bossCharge : 0;

    /// <summary>
    /// They answered (or ran out of time) against this boss: its charge moves on a step, or after the last step
    /// (full charge, whatever the answer) starts over.
    /// </summary>
    public void ChargeUp(Foe boss, int steps)
    {
        var step = ChargeAgainst(boss);
        _chargingBoss = boss;
        _bossCharge = step >= steps - 1 ? 0 : step + 1;
    }

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
        RevealAt = null; // planned again when it turns over
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
        if (!IsOut && DownedUntil > until)
            DownedUntil = until;
    }

    /// <summary>
    /// The monster hits them, ending the streak. At 0 HP they're downed for <paramref name="downedSeconds"/>.
    /// A Shield takes the whole hit instead (and the streak survives), then it's gone.
    /// </summary>
    /// <param name="encounter">The monster hitting them, with its damage for that floor.</param>
    /// <param name="damageTaken">The share of the monster's damage they take (their class's DamageTaken), rounded.</param>
    /// <param name="patronPrice">A Warlock's slow answer: what their patron already took (recorded on the hit).</param>
    /// <param name="charged">A boss at full charge landed its big blow (recorded on the hit; the damage is already in damageTaken).</param>
    public TakenHit TakeHit(Encounter encounter, HitReason reason, DateTimeOffset now, double downedSeconds, double damageTaken = 1, int patronPrice = 0,
        bool charged = false)
    {
        var blocked = Shielded;
        Shielded = false;
        var damage = blocked ? 0 : (int)Math.Round(encounter.Damage * damageTaken);
        if (!blocked)
            Streak = 0;

        // A Last Stand (a floor reward): the hit that would down them leaves them on 1 HP instead, once.
        var lastStand = damage >= Hp && Hp > 1 && LastStands > 0;
        if (lastStand)
        {
            LastStands--;
            damage = Hp - 1;
        }
        Hp = Math.Max(0, Hp - damage);

        var downed = Hp == 0;
        if (downed)
        {
            if (MaxLives > 0)
                Lives--;
            // Out of lives: down for good (DateTimeOffset.MaxValue), until a continue.
            DownedUntil = IsOut ? DateTimeOffset.MaxValue : now + TimeSpan.FromSeconds(downedSeconds);
            ReviveProgress = 0;
            StopHourglass();
        }

        LastTaken = new TakenHit((LastTaken?.Number ?? 0) + 1, damage, reason, downed, encounter.Monster.Name, encounter.Monster.Sprite, Hp, blocked, patronPrice,
            charged && !blocked, lastStand, downed && IsOut);
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

    /// <summary>
    /// A Warlock's slow answer: their patron takes up to <paramref name="amount"/> HP. It never downs them (it
    /// stops at 1 HP). Returns what it took.
    /// </summary>
    public int PayPatron(int amount)
    {
        var paid = Math.Min(amount, Hp - 1);
        if (paid <= 0)
            return 0;
        Hp -= paid;
        return paid;
    }

    /// <summary>A Warlock's patron decided, for question <paramref name="questionNumber"/>, whether it will show the answer.</summary>
    public void DecideReveal(int questionNumber, bool coming)
    {
        RevealDecidedFor = questionNumber;
        RevealComing = coming;
    }

    /// <summary>When the patron's answer shows on the hourglass that just turned over (null: not on this one).</summary>
    public void ScheduleReveal(DateTimeOffset? at) => RevealAt = at;

    /// <summary>Their patron shows them the right answer to the question they're on.</summary>
    public void Reveal(int questionNumber)
    {
        RevealedOnQuestion = questionNumber;
        RevealAt = null;
    }

    /// <summary>Back on their feet after being downed (their time ran out, or teammates revived them), at full HP.</summary>
    public void GetUp()
    {
        if (IsOut)
            return; // only a continue brings them back (RestoreLives)
        DownedUntil = null;
        ReviveProgress = 0;
        Hp = MaxHp;
    }

    /// <summary>A continue (a new paid session): all their lives back, and if they were out, back on their feet at full HP.</summary>
    public void RestoreLives()
    {
        var wasOut = IsOut;
        Lives = MaxLives;
        if (wasOut)
            GetUp();
    }
}
