using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The dungeon side of one Dungeon-format match, and its combat rules. <see cref="MatchEngine"/> runs
/// the match (phases, question streams, scoring) and hands every answer and tick to this class.
///
/// Who fights what:
/// <list type="bullet">
///   <item>A party is a team, or a player on their own. A solo game is a party of one, so solo and group
///     play run the same rules. Each party has one <see cref="Delve"/>: one floor, one monster.</item>
///   <item>Each answer owner is an <see cref="Adventurer"/> with their own HP, streak and hourglass. Every
///     adventurer in a party hits the same monster. In 2v2 Group teammates share their question, so the
///     team is one adventurer.</item>
/// </list>
///
/// The rules:
/// <list type="bullet">
///   <item>A room holds one monster or a pack. The party fights the one in front; the others wait their turn.</item>
///   <item>A correct answer hits the monster in front. Fast (within a set time) = crit. At 0 HP the next monster
///     steps up: the next in the room, then the next room.</item>
///   <item>Each adventurer's hero class changes the fight (see <see cref="HeroClass"/>): the Rogue's crits hit
///     harder, the Tank has longer to be fast and takes less damage, the Healer's fast answers heal (or poison the
///     monster when nobody's hurt), the Wizard's hit the whole room, the Pugilist builds a combo, the Warlock's
///     patron may show the answer (slow answers cost HP), and the Ranger sees questions early (snapshots only).</item>
///   <item>A wrong answer, or the adventurer's hourglass running out, and the monster hits back.</item>
///   <item>At 0 HP an adventurer is downed, then gets up at full HP. On their own that takes a few seconds.
///     In a party they wait for their teammates: a few right answers from them revive them (with a longer
///     time limit as a safety net). When the whole party is down, the monster heals to full and everyone
///     gets up after the short time.</item>
///   <item>Party combo: when everyone standing in a party answers right within a few seconds of each
///     other, the hit that completes it is a bonus hit. Then everyone needs a fresh right answer.</item>
///   <item>The hourglass only runs while the adventurer's buttons work and their question is showing.</item>
///   <item>After every answer the next question waits for a battle beat (<see cref="BeatSecondsAfter"/>):
///     the screens show the fight meanwhile.</item>
///   <item>Every game runs on an arcade clock: every kill earns time (<see cref="SecondsEarnedBy"/>), scaled so a
///     party (which faces tougher monsters, <see cref="DungeonSettings.PartyHpPerMember"/>) earns about what a solo
///     player does, and so several parties sharing the clock in a race earn it together.</item>
///   <item>Each floor asks its own mix of easy, medium and hard questions (<see cref="NextDifficulty"/>).</item>
///   <item>Power-ups: a kill may drop one (a boss always does) and a streak earns one; each adventurer
///     holds one at a time and uses it with the action button (<see cref="UsePowerUp"/>).</item>
/// </list>
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class DungeonRun
{
    private readonly DungeonSettings _settings;
    private readonly IReadOnlyDictionary<string, QuestionStream> _streams;
    private readonly Dictionary<string, Delve> _delves = new();
    private readonly Dictionary<string, Adventurer> _adventurers = new();
    private readonly DungeonMap _map;
    private readonly Random _random;

    /// <param name="streams">The match's question streams, by answer owner. Downed adventurers' buttons are
    /// dead through their stream's lockout, so reviving someone ends it. Their first questions are picked here,
    /// from the first floor's mix.</param>
    /// <param name="random">Power-up drops and picks, and question difficulties. Tests pass a seeded one.</param>
    public DungeonRun(MatchRoster roster, IGameMode mode, IReadOnlyDictionary<string, QuestionStream> streams, DungeonMap map, DungeonSettings settings, Random random)
    {
        _settings = settings;
        _streams = streams;
        _map = map;
        _random = random;
        IsSolo = roster.Players.Count == 1;

        foreach (var player in roster.Players)
        {
            // In 2v2 Group teammates answer as one adventurer: it fights as the first teammate's hero.
            var ownerId = mode.GetAnswerOwnerId(player);
            var partyId = player.TeamId ?? player.StationId;
            if (_adventurers.ContainsKey(ownerId))
                continue;
            var heroClass = Arcade.Heroes.ClassOf(player.Hero);
            var maxHp = (int)Math.Round(settings.AdventurerHp * settings.Classes.StatsFor(heroClass).Hp);
            _adventurers[ownerId] = new Adventurer(ownerId, partyId, Math.Max(1, maxHp), heroClass);
        }

        // A party's monsters are tougher, by how many adventurers it has (answer owners: a 2v2 Group team is one).
        foreach (var partyId in _adventurers.Values.Select(a => a.PartyId).Distinct())
            _delves[partyId] = new Delve(partyId, map, HpScaleFor(PartyOf(partyId).Count));

        foreach (var adventurer in _adventurers.Values)
            if (NextDifficulty(adventurer.OwnerId) is { } difficulty)
                streams[adventurer.OwnerId].Prefer(difficulty);
    }

    /// <summary>One player on their own.</summary>
    public bool IsSolo { get; }

    /// <summary>How much tougher monsters are for a party of <paramref name="members"/> (1 for a solo player).</summary>
    public double HpScaleFor(int members) => 1 + _settings.PartyHpPerMember * Math.Max(0, members - 1);

    /// <summary>
    /// The difficulty of an adventurer's next question: drawn from their floor's mix (<see cref="FloorDef.Questions"/>).
    /// Null when the floor has no mix (the match's chosen difficulty comes first, as in Classic).
    /// </summary>
    public Difficulty? NextDifficulty(string ownerId)
    {
        var floor = DelveOf(_adventurers[ownerId]).Floor;
        if (_map.QuestionsOn(floor) is not { Count: > 0 } mix)
            return null;

        var roll = _random.NextDouble() * mix.Values.Sum();
        foreach (var (difficulty, weight) in mix)
        {
            roll -= weight;
            if (roll < 0)
                return difficulty;
        }
        return mix.Keys.Last();
    }

    public IReadOnlyCollection<Delve> Delves => _delves.Values;
    public IReadOnlyCollection<Adventurer> Adventurers => _adventurers.Values;

    public Adventurer? FindAdventurer(string ownerId) => _adventurers.GetValueOrDefault(ownerId);

    /// <summary>The delve an adventurer's party is on.</summary>
    public Delve DelveOf(Adventurer adventurer) => _delves[adventurer.PartyId];

    /// <summary>Everyone in a party (just the one adventurer for a party of one), in roster order.</summary>
    public IReadOnlyList<Adventurer> PartyOf(string partyId) =>
        _adventurers.Values.Where(a => a.PartyId == partyId).ToList();

    /// <summary>A teammate still standing, whose question a downed adventurer can help with (null if none).</summary>
    public Adventurer? StandingTeammateOf(Adventurer adventurer) =>
        PartyOf(adventurer.PartyId).FirstOrDefault(a => a != adventurer && !a.IsDowned);

    /// <summary>How many right answers from teammates bring a downed adventurer back.</summary>
    public int ReviveAnswers => _settings.ReviveAnswers;

    /// <summary>The clock starts: every hourglass turns over.</summary>
    public void Start(DateTimeOffset now)
    {
        foreach (var adventurer in _adventurers.Values)
            StartHourglass(adventurer, now);
    }

    /// <summary>The hero class an answer owner fights as (null if they aren't in this match).</summary>
    public HeroClass? ClassOf(string ownerId) => _adventurers.GetValueOrDefault(ownerId)?.Class;

    /// <summary>The clock has stopped: no more attacks.</summary>
    public void Stop()
    {
        foreach (var adventurer in _adventurers.Values)
            adventurer.StopHourglass();
    }

    /// <summary>
    /// A correct answer: hits the monster in front (a Wizard's fast one, the whole room), maybe completing a
    /// party combo, and counts towards reviving a downed teammate (<see cref="DealtHit.RevivedOwnerId"/> says
    /// if it brought one back). A Healer's fast answer also heals, or poisons; a Warlock's slow one costs HP.
    /// </summary>
    public DealtHit Strike(string ownerId, Difficulty difficulty, TimeSpan responseTime, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var classes = _settings.Classes;
        var streak = adventurer.AddToStreak();
        var fast = IsFast(adventurer, responseTime);
        var combo = adventurer.Class == HeroClass.Pugilist && streak >= 2;
        var partyCombo = IsPartyCombo(adventurer, delve, now);

        var fireball = adventurer.ThrowFireball();

        var damage = _settings.DamageFor(difficulty) * classes.StatsFor(adventurer.Class).DamageDealt;
        if (fast)
            damage *= adventurer.Class == HeroClass.Rogue ? classes.RogueCritMultiplier : _settings.CritMultiplier;
        if (combo)
            damage *= 1 + Math.Min(classes.PugilistComboStep * (streak - 1), classes.PugilistComboMax);
        if (partyCombo)
            damage *= 1 + _settings.PartyComboBonus;
        if (fireball)
            damage *= _settings.FireballMultiplier;

        // A right answer against a boss at full charge breaks its blow: a stagger, for extra damage.
        var boss = BossFacing(delve);
        var staggered = boss != null && IsFullCharge(adventurer.ChargeAgainst(boss));
        if (staggered)
            damage *= 1 + _settings.StaggerBonus;

        var hitDamage = (int)Math.Round(damage);
        var splash = adventurer.Class == HeroClass.Wizard && fast ? (int)Math.Round(hitDamage * classes.WizardSplash) : 0;

        // The Healer's fast answer heals whoever standing is lowest on HP (before the hit, so it's the party as it was).
        var healed = adventurer.Class == HeroClass.Healer && fast ? MostHurt(adventurer) : null;
        var healedHp = healed?.Heal(classes.HealerHeal) ?? 0;
        var patronPrice = adventurer.Class == HeroClass.Warlock && responseTime.TotalSeconds > classes.WarlockSlowSeconds
            ? adventurer.PayPatron(classes.WarlockSlowDamage)
            : 0;

        var revived = HelpRevive(adventurer, now);
        var hit = delve.Hit(hitDamage, fast, combo, partyCombo, revived?.OwnerId, now, splash);

        // With nobody to heal, the Healer's fast answer poisons the monster in front (the next one, if this hit killed it).
        var poisonStacks = adventurer.Class == HeroClass.Healer && fast && healed == null
            ? delve.Poison(now, classes.PoisonTickSeconds)
            : 0;

        hit = hit with
        {
            Fireball = fireball,
            Found = FindPowerUp(adventurer, hit, streak),
            ComboCount = combo ? streak : 0,
            HealedOwnerId = healed?.OwnerId,
            Healed = healedHp,
            PoisonStacks = poisonStacks,
            PatronPrice = patronPrice,
            Staggered = staggered,
        };
        delve.Complete(hit);
        adventurer.Landed(hit, now);
        if (boss != null)
            adventurer.ChargeUp(boss, _settings.BossChargeSteps);
        return hit;
    }

    /// <summary>The boss in front of a party, if it's fighting one (its charge cycle applies), or null.</summary>
    private static Foe? BossFacing(Delve delve) => delve.Foes[0].Monster.IsBoss ? delve.Foes[0] : null;

    /// <summary>The last step of a boss's charge cycle: its big blow (or a stagger) comes now.</summary>
    private bool IsFullCharge(int step) => _settings.BossChargeSteps >= 2 && step >= _settings.BossChargeSteps - 1;

    /// <summary>
    /// The step of the boss's charge cycle for an adventurer's question now (0 = weak), and how many steps it has.
    /// (0, 0) when they aren't fighting a boss, or bosses don't charge.
    /// </summary>
    public (int Step, int Steps) BossChargeOf(string ownerId)
    {
        var adventurer = _adventurers[ownerId];
        if (BossFacing(DelveOf(adventurer)) is not { } boss || _settings.BossChargeSteps < 2)
            return (0, 0);
        return (adventurer.ChargeAgainst(boss), _settings.BossChargeSteps);
    }

    /// <summary>
    /// A fast answer: within <see cref="DungeonSettings.CritWithinSeconds"/> of the question showing, a set
    /// time (the Tank's is longer: <see cref="ClassSettings.TankFastSeconds"/>).
    /// </summary>
    private bool IsFast(Adventurer adventurer, TimeSpan responseTime) =>
        responseTime.TotalSeconds <= (adventurer.Class == HeroClass.Tank ? _settings.Classes.TankFastSeconds : _settings.CritWithinSeconds);

    /// <summary>Whoever standing in the healer's party (them included) is lowest on HP, for their share of their max. Null if nobody's hurt.</summary>
    private Adventurer? MostHurt(Adventurer healer) =>
        PartyOf(healer.PartyId)
            .Where(a => !a.IsDowned && a.Hp < a.MaxHp)
            .MinBy(a => (double)a.Hp / a.MaxHp);

    /// <summary>
    /// The Healer's poison does its damage (every stack, every tick). Returns each party whose monsters it
    /// hurt, and the hit when it killed the one in front (the next one steps up, as for any kill).
    /// </summary>
    public IReadOnlyList<(string PartyId, int Damage, DealtHit? Kill)> TickPoison(DateTimeOffset now)
    {
        var results = new List<(string, int, DealtHit?)>();
        foreach (var delve in _delves.Values)
        {
            var (damage, kill) = delve.PoisonTick(now, _settings.Classes.PoisonDamage, _settings.Classes.PoisonTickSeconds);
            if (damage > 0)
                results.Add((delve.PartyId, damage, kill));
        }
        return results;
    }

    /// <summary>
    /// The Warlocks whose patron shows them the right answer now (their hourglass got low enough). Their
    /// snapshot then carries it (<see cref="Adventurer.RevealedOnQuestion"/>).
    /// </summary>
    public IReadOnlyList<Adventurer> RevealsDue(DateTimeOffset now)
    {
        var due = _adventurers.Values.Where(a => a.RevealAt <= now && a.AttackAt != null).ToList();
        foreach (var warlock in due)
            warlock.Reveal(_streams[warlock.OwnerId].CurrentNumber);
        return due;
    }

    /// <summary>
    /// A Ranger sees their next question this many seconds before the battle beat ends (0 for everyone else).
    /// Only what the screen shows changes: buttons, the hourglass and the fast window all start when the beat ends.
    /// </summary>
    public double PeekSecondsFor(string ownerId) =>
        _adventurers.GetValueOrDefault(ownerId)?.Class == HeroClass.Ranger ? _settings.Classes.RangerPeekSeconds : 0;

    /// <summary>
    /// A kill may drop a power-up (a boss always does), and every <see cref="DungeonSettings.PowerUpStreak"/>
    /// right answers in a row earn one. Only when their hands are free: they hold one at a time.
    /// </summary>
    private PowerUp? FindPowerUp(Adventurer adventurer, DealtHit hit, int streak)
    {
        if (adventurer.Held != null)
            return null;

        var dropped = hit.Killed && (hit.IsBoss || _random.NextDouble() < _settings.PowerUpDropChance);
        var earned = _settings.PowerUpStreak > 0 && streak % _settings.PowerUpStreak == 0;
        if (!dropped && !earned)
            return null;

        var choices = Enum.GetValues<PowerUp>();
        var found = choices[_random.Next(choices.Length)];
        adventurer.PickUp(found);
        return found;
    }

    /// <summary>Hands an adventurer a power-up, replacing any they held (the dev tools, for testing).</summary>
    public void GivePowerUp(string ownerId, PowerUp powerUp) => _adventurers[ownerId].PickUp(powerUp);

    /// <summary>
    /// The action button: uses the power-up they're holding. Returns null if they hold none or are
    /// downed. A Time Crystal's seconds are for the match engine to add to the clock.
    /// </summary>
    public PowerUpUse? UsePowerUp(string ownerId, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        if (adventurer.IsDowned || adventurer.Held is not { } powerUp)
            return null;

        adventurer.DropHeld();
        switch (powerUp)
        {
            case PowerUp.Potion:
                var (target, healed, revived) = DrinkPotion(adventurer, now);
                return adventurer.RecordPowerUp(powerUp, target.OwnerId, healed, revived);
            case PowerUp.Fireball:
                adventurer.ReadyFireball();
                break;
            case PowerUp.Freeze:
                adventurer.Freeze(now + TimeSpan.FromSeconds(_settings.FreezeSeconds));
                if (adventurer.AttackAt != null)
                    StartHourglass(adventurer, now); // it waits out the freeze, then starts over
                return adventurer.RecordPowerUp(powerUp, ownerId, (int)_settings.FreezeSeconds, revived: false);
            case PowerUp.FiftyFifty:
                HideTwoWrongAnswers(adventurer);
                break;
            case PowerUp.Shield:
                adventurer.RaiseShield();
                break;
            case PowerUp.TimeCrystal:
                return adventurer.RecordPowerUp(powerUp, ownerId, (int)_settings.TimeCrystalSeconds, revived: false);
        }
        return adventurer.RecordPowerUp(powerUp, ownerId, 0, revived: false);
    }

    /// <summary>
    /// A Potion goes to whoever in the party needs it most: a downed teammate gets straight back up (at
    /// full HP), otherwise whoever standing is lowest on HP, for their share of their max, is healed.
    /// </summary>
    private (Adventurer Target, int Healed, bool Revived) DrinkPotion(Adventurer user, DateTimeOffset now)
    {
        var party = PartyOf(user.PartyId);
        var downed = party.Where(a => a.IsDowned).OrderBy(a => a.DownedUntil).FirstOrDefault();
        if (downed != null)
        {
            var healed = downed.MaxHp - downed.Hp;
            _streams[downed.OwnerId].EndLockout();
            downed.GetUp();
            StartHourglass(downed, now);
            return (downed, healed, true);
        }

        var target = party.Where(a => !a.IsDowned).MinBy(a => (double)a.Hp / a.MaxHp)!;
        return (target, target.Heal(_settings.PotionHeal), false);
    }

    /// <summary>A 50/50: two wrong answers of their current question go (always leaving at least two to choose from).</summary>
    private void HideTwoWrongAnswers(Adventurer adventurer)
    {
        var stream = _streams[adventurer.OwnerId];
        var question = stream.CurrentQuestion;
        var hidden = Enumerable.Range(0, question.Answers.Count)
            .Where(i => i != question.CorrectIndex)
            .OrderBy(_ => _random.Next())
            .Take(Math.Min(2, question.Answers.Count - 2))
            .Order()
            .ToList();
        adventurer.HideAnswers(stream.CurrentNumber, hidden);
    }

    /// <summary>True if a 50/50 took this answer away from the question they're on (pressing it does nothing).</summary>
    public bool IsHidden(string ownerId, int questionNumber, int answerIndex) =>
        _adventurers[ownerId] is { } a && a.HiddenOnQuestion == questionNumber && a.HiddenAnswers.Contains(answerIndex);

    /// <summary>
    /// This right answer completes a party combo if every other standing member of the party has also
    /// answered right within <see cref="DungeonSettings.PartyComboSeconds"/>, since the last party combo.
    /// </summary>
    private bool IsPartyCombo(Adventurer adventurer, Delve delve, DateTimeOffset now)
    {
        var others = PartyOf(adventurer.PartyId).Where(a => a != adventurer && !a.IsDowned).ToList();
        var window = TimeSpan.FromSeconds(_settings.PartyComboSeconds);
        return others.Count > 0 && others.All(a =>
            a.LastCorrectAt is { } at && at > delve.LastPartyComboAt && now - at <= window);
    }

    /// <summary>
    /// A teammate's right answer counts towards reviving whoever in the party has been down longest.
    /// Returns them if that was the last answer needed: they're back up, buttons working.
    /// </summary>
    private Adventurer? HelpRevive(Adventurer helper, DateTimeOffset now)
    {
        var downed = PartyOf(helper.PartyId)
            .Where(a => a != helper && a.IsDowned)
            .OrderBy(a => a.DownedUntil)
            .FirstOrDefault();
        if (downed == null || downed.AddReviveProgress() < _settings.ReviveAnswers)
            return null;

        _streams[downed.OwnerId].EndLockout();
        downed.GetUp();
        StartHourglass(downed, now);
        return downed;
    }

    /// <summary>
    /// The time a hit earns on the clock: each monster it killed earns <see cref="DungeonSettings.SecondsPerKill"/>
    /// (a Wizard's splash kills too), a boss <see cref="DungeonSettings.SecondsPerBoss"/>. Scaled two ways, so the
    /// clock runs about as long whoever plays:
    /// <list type="bullet">
    ///   <item>a party kills faster than one player but faces tougher monsters: its time per kill is
    ///     <see cref="HpScaleFor"/> over its size (two players: 1.75 / 2, so 87.5% each);</item>
    ///   <item>parties racing share one clock, so each party's kills earn their share of it.</item>
    /// </list>
    /// The match engine adds it to the clock.
    /// </summary>
    public double SecondsEarnedBy(string partyId, DealtHit hit)
    {
        var kills = (hit.Killed ? 1 : 0) + (hit.Splashes?.Count(s => s.Killed) ?? 0);
        if (kills == 0)
            return 0;

        var seconds = hit.Killed && hit.IsBoss
            ? _settings.SecondsPerBoss + (kills - 1) * _settings.SecondsPerKill
            : kills * _settings.SecondsPerKill;
        var members = PartyOf(partyId).Count;
        return seconds * HpScaleFor(members) / members / _delves.Count;
    }

    /// <summary>
    /// A wrong answer, or the hourglass ran out: the party's monster hits this adventurer. Downed on
    /// their own, they're out for <see cref="DungeonSettings.DownedSeconds"/>; in a party, until their
    /// teammates revive them (or <see cref="DungeonSettings.PartyDownedSeconds"/> pass). A Tank takes less (60%
    /// by default; every class takes its <see cref="ClassStats.DamageTaken"/> share); a Warlock's slow wrong answer
    /// costs the patron's price on top.
    /// </summary>
    /// <param name="responseTime">A wrong answer: how long they took (a Warlock pays for slow ones).</param>
    public TakenHit TakeHit(string ownerId, HitReason reason, DateTimeOffset now, TimeSpan? responseTime = null)
    {
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var party = PartyOf(adventurer.PartyId);
        var classes = _settings.Classes;
        var downedSeconds = party.Count > 1 ? _settings.PartyDownedSeconds : _settings.DownedSeconds;

        var patronPrice = adventurer.Class == HeroClass.Warlock && reason == HitReason.WrongAnswer && responseTime?.TotalSeconds > classes.WarlockSlowSeconds
            ? adventurer.PayPatron(classes.WarlockSlowDamage)
            : 0;
        // A boss hits as hard as its charge cycle says: weak at first, its big blow at full charge.
        var damageTaken = classes.StatsFor(adventurer.Class).DamageTaken;
        var boss = BossFacing(delve);
        var step = boss != null ? adventurer.ChargeAgainst(boss) : 0;
        if (boss != null)
            damageTaken *= _settings.BossHitAt(step);
        var hit = adventurer.TakeHit(delve.Encounter, reason, now, downedSeconds, damageTaken, patronPrice, charged: boss != null && IsFullCharge(step));
        if (boss != null)
            adventurer.ChargeUp(boss, _settings.BossChargeSteps);
        delve.RecordTaken(hit);

        // While anyone in the party is still standing, the fight goes on. When nobody is, the room's
        // monsters heal and everyone gets up after the short downed time (nobody is left to revive them).
        if (hit.Downed && party.All(a => a.IsDowned))
        {
            delve.HealRoom();
            var upAt = now + TimeSpan.FromSeconds(_settings.DownedSeconds);
            foreach (var member in party)
            {
                member.ShortenDowned(upAt);
                _streams[member.OwnerId].LockOut(member.DownedUntil!.Value);
            }
        }

        return hit;
    }

    /// <summary>
    /// After an answer or an attack: a downed adventurer's buttons go dead until they get up, and the
    /// hourglass runs for the next question unless the buttons are dead (it restarts when they work again).
    /// </summary>
    public void AfterTurn(QuestionStream stream, DateTimeOffset now)
    {
        var adventurer = _adventurers[stream.OwnerId];
        if (adventurer.DownedUntil is { } downedUntil && (stream.LockedOutUntil == null || stream.LockedOutUntil < downedUntil))
            stream.LockOut(downedUntil);

        if (stream.IsLockedOutAt(now))
            adventurer.StopHourglass();
        else
            StartHourglass(adventurer, now);
    }

    /// <summary>
    /// Back to Playing after a continue: if their downed time ran out meanwhile they get up, and the
    /// hourglass runs again unless their buttons are still dead.
    /// </summary>
    public void Resume(QuestionStream stream, DateTimeOffset now)
    {
        var adventurer = _adventurers[stream.OwnerId];
        if (adventurer.DownedUntil <= now)
            adventurer.GetUp();

        AfterTurn(stream, now);
    }

    /// <summary>
    /// An adventurer's buttons work again (their lockout ran out): if they were downed they get up,
    /// and the hourglass turns over. Returns true if they got up.
    /// </summary>
    public bool ButtonsWorkAgain(string ownerId, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        var gotUp = adventurer.IsDowned;
        if (gotUp)
            adventurer.GetUp();

        StartHourglass(adventurer, now);
        return gotUp;
    }

    /// <summary>
    /// How long the next question waits after an answer, while the screen shows the fight: a hit, a
    /// kill, a floor cleared (the longest), or the monster's attack (<paramref name="dealt"/> null).
    /// </summary>
    public double BeatSecondsAfter(DealtHit? dealt) =>
        dealt == null ? _settings.HurtBeatSeconds
        : dealt.FloorCleared || dealt.BossAhead ? _settings.FloorBeatSeconds
        : dealt.Killed ? _settings.KillBeatSeconds
        : _settings.HitBeatSeconds;

    /// <summary>The adventurers whose hourglass has run out: their monster attacks.</summary>
    public IReadOnlyList<Adventurer> AttacksDue(DateTimeOffset now) =>
        _adventurers.Values.Where(a => a.IsAttackDue(now)).ToList();

    /// <summary>
    /// Turns the hourglass over from now, or from when their question appears if it's still to come (a
    /// battle beat), or from when a Freeze ends.
    /// </summary>
    private void StartHourglass(Adventurer adventurer, DateTimeOffset now)
    {
        var shownAt = _streams[adventurer.OwnerId].CurrentShownAt ?? now;
        var from = shownAt > now ? shownAt : now;
        if (adventurer.FrozenUntil > from)
            from = adventurer.FrozenUntil.Value;
        adventurer.StartHourglass(from, DelveOf(adventurer).Encounter.Monster.AttackSeconds);
        PlanReveal(adventurer, from);
    }

    /// <summary>
    /// A Warlock's patron decides once per question whether to show the answer
    /// (<see cref="ClassSettings.WarlockRevealChance"/>); if so it shows when this hourglass is down to
    /// <see cref="ClassSettings.WarlockRevealSecondsLeft"/> (at once on a shorter one).
    /// </summary>
    private void PlanReveal(Adventurer adventurer, DateTimeOffset hourglassFrom)
    {
        if (adventurer.Class != HeroClass.Warlock || adventurer.AttackAt is not { } attackAt)
            return;

        var classes = _settings.Classes;
        var question = _streams[adventurer.OwnerId].CurrentNumber;
        if (adventurer.RevealDecidedFor != question)
            adventurer.DecideReveal(question, _random.NextDouble() < classes.WarlockRevealChance);

        if (!adventurer.RevealComing || adventurer.RevealedOnQuestion == question)
        {
            adventurer.ScheduleReveal(null);
            return;
        }
        var at = attackAt - TimeSpan.FromSeconds(classes.WarlockRevealSecondsLeft);
        adventurer.ScheduleReveal(at > hourglassFrom ? at : hourglassFrom);
    }
}
