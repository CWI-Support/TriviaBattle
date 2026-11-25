using TriviaBattle.Core.Dungeon.Minigames;
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
///     player does; in a race or a team battle each side has its own clock (MatchEngine's PartyClock).</item>
///   <item>Each floor asks its own mix of easy, medium and hard questions (<see cref="NextDifficulty"/>).</item>
///   <item>Floor rewards: answer enough of a floor right and, once its boss dies, pick a reward that lasts the rest of
///     the match (<see cref="OpenRewards"/>, <see cref="ChooseReward"/>). Rewards raise the numbers below where they're used.</item>
///   <item>Power-ups: a kill may drop one (a boss always does) and a streak earns one; each adventurer
///     holds one at a time and uses it with the action button (<see cref="UsePowerUp"/>).</item>
///   <item>Affixes: deeper down some monsters have a twist (<see cref="Affix"/>): a Speedy one's hourglass is shorter, an
///     Armored one takes less from slow answers, a Vampiric one heals by what its hits take.</item>
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
    private readonly Querie? _querie; // null with no lines: he says nothing at the stations
    private readonly Dictionary<string, string> _names = new();       // by answer owner: the player's (or 2v2 team's) name
    private readonly Dictionary<string, string> _partyNames = new();  // by party: the team's name, or the solo player's

    /// <param name="streams">The match's question streams, by answer owner. Downed adventurers' buttons are
    /// dead through their stream's lockout, so reviving someone ends it. Their first questions are picked here,
    /// from the first floor's mix.</param>
    /// <param name="random">Power-up drops and picks, and question difficulties. Tests pass a seeded one.</param>
    /// <param name="querie">Q.U.E.R.I.E.'s lines for the stations (config/querie.json); none, and he keeps quiet there.</param>
    /// <param name="highStakes">High Stakes' brutal questions for this match (its own bank, the match's categories first:
    /// <see cref="HighStakesQuestions.For"/>), asked in a shuffled order; none, and it asks a hard one from the player's stream.</param>
    public DungeonRun(MatchRoster roster, IGameMode mode, IReadOnlyDictionary<string, QuestionStream> streams, DungeonMap map, DungeonSettings settings, Random random,
        QuerieLines? querie = null, IReadOnlyList<Question>? highStakes = null)
    {
        _settings = settings;
        _highStakes = (highStakes ?? []).OrderBy(_ => random.Next()).ToList();
        _streams = streams;
        _map = map;
        _random = random;
        // His own dice, so how much he says never changes the fight's (drops, questions).
        _querie = querie is { IsEmpty: false } ? new Querie(querie, settings.Querie, new Random(random.Next())) : null;
        IsSolo = roster.Players.Count == 1;

        foreach (var player in roster.Players)
        {
            // In 2v2 Group teammates answer as one adventurer: it fights as the first teammate's hero.
            var ownerId = mode.GetAnswerOwnerId(player);
            var partyId = player.TeamId ?? player.StationId;
            var teamName = player.TeamId is { } teamId ? roster.FindTeam(teamId)?.Name : null;
            _partyNames.TryAdd(partyId, teamName ?? player.Name);
            _names.TryAdd(ownerId, ownerId == player.TeamId ? teamName ?? player.Name : player.Name);
            if (_adventurers.ContainsKey(ownerId))
                continue;
            var heroClass = Arcade.Heroes.ClassOf(player.Hero);
            var maxHp = (int)Math.Round(settings.AdventurerHp * settings.Classes.StatsFor(heroClass).Hp);
            _adventurers[ownerId] = new Adventurer(ownerId, partyId, Math.Max(1, maxHp), heroClass, settings.Lives);
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

    /// <summary>Everyone in the match is out of lives (<see cref="DungeonSettings.Lives"/>): the game is over, whatever the clock says.</summary>
    public bool EveryoneOut => _settings.Lives > 0 && _adventurers.Values.All(a => a.IsOut);

    /// <summary>
    /// A continue (a new paid session): everyone has all their lives back, and the ones who were out are back on their
    /// feet at full HP, buttons working (the out ones' buttons were dead for good).
    /// </summary>
    public void RestoreLives()
    {
        foreach (var adventurer in _adventurers.Values)
        {
            if (adventurer.IsOut)
                _streams[adventurer.OwnerId].EndLockout();
            adventurer.RestoreLives();
        }
    }

    /// <summary>
    /// A continue: play was paused (Continue?, waiting for a swipe), so the poison carries on from now, not with every
    /// bite it missed landing at once.
    /// </summary>
    public void ResumePoison(DateTimeOffset now)
    {
        foreach (var delve in _delves.Values)
            delve.HoldPoison(now + TimeSpan.FromSeconds(_settings.Classes.PoisonTickSeconds));
    }

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
    public DealtHit Strike(string ownerId, TimeSpan responseTime, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var classes = _settings.Classes;
        var streak = adventurer.AddToStreak();
        var fast = IsFast(adventurer, responseTime);
        var combo = adventurer.Class == HeroClass.Pugilist && streak >= 2;
        var partyCombo = IsPartyCombo(adventurer, delve, now);
        adventurer.CountAnswer(delve.Floor, right: true);

        var fireball = adventurer.ThrowFireball();
        // Lucky Dice: this answer rolls them (null: none were ready).
        bool? dice = adventurer.TakeDice() ? _random.NextDouble() < _settings.LuckyDiceChance : null;

        var damage = _settings.HitDamage * DamageShare(adventurer);
        if (fast)
            damage *= adventurer.Class == HeroClass.Rogue ? RogueCritOf(adventurer) : _settings.CritMultiplier;
        if (combo)
            damage *= 1 + Math.Min(ComboStepOf(adventurer) * (streak - 1), ComboMaxOf(adventurer));
        if (partyCombo)
            damage *= 1 + _settings.PartyComboBonus;
        if (fireball)
            damage *= _settings.FireballMultiplier;
        if (dice is { } lucky)
            damage *= lucky ? _settings.LuckyDiceMultiplier : 0;

        // A right answer against a boss at full charge breaks its blow: a stagger, for extra damage.
        var boss = BossFacing(delve);
        var staggered = boss != null && IsFullCharge(adventurer.ChargeAgainst(boss));
        if (staggered)
            damage *= 1 + _settings.StaggerBonus;

        // An Armored monster: only a fast answer gets through its armour in full.
        var armored = delve.Foes[0].Affix == Affix.Armored && !fast && damage > 0;
        if (armored)
            damage *= _settings.Affixes.ArmoredShare;

        var hitDamage = (int)Math.Round(damage);
        var splash = adventurer.Class == HeroClass.Wizard && fast ? (int)Math.Round(hitDamage * SplashOf(adventurer)) : 0;

        // The Healer's fast answer heals whoever standing is lowest on HP (before the hit, so it's the party as it was).
        var healed = adventurer.Class == HeroClass.Healer && fast ? MostHurt(adventurer) : null;
        var healedHp = healed?.Heal(HealOf(adventurer)) ?? 0;
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
            Dice = dice,
            Armored = armored,
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
    /// A fast answer: within <see cref="FastSecondsOf"/> of the question showing.
    /// </summary>
    private bool IsFast(Adventurer adventurer, TimeSpan responseTime) => responseTime.TotalSeconds <= FastSecondsOf(adventurer);

    // ------------------------------------------------------------------
    // An adventurer's numbers now: their class's, raised by the rewards they've picked (see Reward).
    // ------------------------------------------------------------------

    private RewardSettings Rewards => _settings.Rewards;

    /// <summary>
    /// How long they have to be fast: a set time, <see cref="DungeonSettings.CritWithinSeconds"/> (the Tank's is
    /// longer: <see cref="ClassSettings.TankFastSeconds"/>), plus any Focus rewards.
    /// </summary>
    public double FastSecondsOf(Adventurer adventurer) =>
        (adventurer.Class == HeroClass.Tank ? _settings.Classes.TankFastSeconds : _settings.CritWithinSeconds)
        + Rewards.FastSeconds * adventurer.Has(Reward.Focus);

    /// <summary>The share of the normal damage their right answers do: their class's, with any Might rewards.</summary>
    private double DamageShare(Adventurer adventurer) =>
        _settings.Classes.StatsFor(adventurer.Class).DamageDealt * (1 + Rewards.DamageBonus * adventurer.Has(Reward.Might));

    /// <summary>
    /// The share of a monster's damage they take: their class's, less for each Armor reward (and for a Tank's Mastery).
    /// </summary>
    private double DamageTakenShare(Adventurer adventurer)
    {
        var share = _settings.Classes.StatsFor(adventurer.Class).DamageTaken * Math.Pow(1 - Rewards.ArmorBonus, adventurer.Has(Reward.Armor));
        if (adventurer.Class == HeroClass.Tank)
            share *= Math.Pow(1 - Rewards.TankArmor, adventurer.Has(Reward.Mastery));
        return share;
    }

    private double RogueCritOf(Adventurer a) => _settings.Classes.RogueCritMultiplier + Rewards.RogueCrit * a.Has(Reward.Mastery);
    private int HealOf(Adventurer a) => _settings.Classes.HealerHeal + Rewards.HealerHeal * a.Has(Reward.Mastery);
    private double SplashOf(Adventurer a) => Math.Min(1, _settings.Classes.WizardSplash + Rewards.WizardSplash * a.Has(Reward.Mastery));
    private double ComboStepOf(Adventurer a) => _settings.Classes.PugilistComboStep + Rewards.PugilistComboStep * a.Has(Reward.Mastery);
    private double ComboMaxOf(Adventurer a) => _settings.Classes.PugilistComboMax + 10 * Rewards.PugilistComboStep * a.Has(Reward.Mastery);
    private double RevealChanceOf(Adventurer a) => Math.Min(1, _settings.Classes.WarlockRevealChance + Rewards.WarlockReveal * a.Has(Reward.Mastery));
    private double PeekOf(Adventurer a) => _settings.Classes.RangerPeekSeconds + Rewards.RangerPeek * a.Has(Reward.Mastery);

    /// <summary>
    /// What a reward would give this adventurer, the number its card shows: Vitality the HP, Might and Armor the
    /// percent, Time and Focus the seconds, Last Stand 1, and Mastery their class's power as it would be after it
    /// (a Rogue's crit multiplier, a Tank's percent less damage, a Healer's heal, a Wizard's splash percent, a
    /// Pugilist's percent per answer in a row, a Warlock's reveal percent, a Ranger's seconds early).
    /// </summary>
    public double RewardAmount(Adventurer adventurer, Reward reward)
    {
        var mastery = adventurer.Has(Reward.Mastery) + 1;
        var classes = _settings.Classes;
        return reward switch
        {
            Reward.Vitality => VitalityHp(adventurer),
            Reward.Might => Math.Round(Rewards.DamageBonus * 100),
            Reward.Armor => Math.Round(Rewards.ArmorBonus * 100),
            Reward.Time => Math.Round(RewardTimeSeconds(adventurer)),
            Reward.Focus => Rewards.FastSeconds,
            Reward.LastStand => 1,
            Reward.Mastery => adventurer.Class switch
            {
                HeroClass.Rogue => classes.RogueCritMultiplier + Rewards.RogueCrit * mastery,
                HeroClass.Tank => Math.Round(Rewards.TankArmor * 100),
                HeroClass.Healer => classes.HealerHeal + Rewards.HealerHeal * mastery,
                HeroClass.Wizard => Math.Round(Math.Min(1, classes.WizardSplash + Rewards.WizardSplash * mastery) * 100),
                HeroClass.Pugilist => Math.Round((classes.PugilistComboStep + Rewards.PugilistComboStep * mastery) * 100),
                HeroClass.Warlock => Math.Round(Math.Min(1, classes.WarlockRevealChance + Rewards.WarlockReveal * mastery) * 100),
                HeroClass.Ranger => classes.RangerPeekSeconds + Rewards.RangerPeek * mastery,
                _ => 0,
            },
            _ => 0,
        };
    }

    private int VitalityHp(Adventurer adventurer) => (int)Math.Round(adventurer.StartingMaxHp * Rewards.HpBonus);

    /// <summary>A Time reward's seconds, shared out like a kill's (<see cref="SecondsEarnedBy"/>): a party's are scaled to its size.</summary>
    private double RewardTimeSeconds(Adventurer adventurer)
    {
        var members = PartyOf(adventurer.PartyId).Count;
        return Rewards.TimeSeconds * HpScaleFor(members) / members;
    }

    // ------------------------------------------------------------------
    // Floor rewards: earned on a floor, offered after its boss, picked with the answer buttons.
    // ------------------------------------------------------------------

    /// <summary>
    /// A party has just cleared a floor (its boss died): everyone in it who answered enough of that floor right
    /// (<see cref="RewardSettings.Accuracy"/>) is offered rewards. Their question waits, and the monster's hourglass
    /// with it, until they pick. The cards come up when the boss's death beat ends. Returns who was offered some.
    /// </summary>
    public IReadOnlyList<Adventurer> OpenRewards(string partyId, DateTimeOffset now)
    {
        var cleared = _delves[partyId].Floor - 1;
        var opensAt = now + TimeSpan.FromSeconds(_settings.FloorBeatSeconds);
        var offered = new List<Adventurer>();
        foreach (var member in PartyOf(partyId))
        {
            var trial = member.EndFloor(cleared, Rewards.Accuracy);
            if (!Rewards.Enabled || !trial.Earned || member.IsAway || member.IsOut)
                continue;

            OfferRewardsTo(member, cleared, opensAt);
            offered.Add(member);
        }
        return offered;
    }

    /// <summary>Puts <see cref="RewardSettings.Choices"/> random rewards in front of an adventurer, the cards up from <paramref name="opensAt"/>.</summary>
    /// <param name="forMinigame">A mini game's prize (rather than the floor's, after its boss).</param>
    private void OfferRewardsTo(Adventurer member, int floor, DateTimeOffset opensAt, bool forMinigame = false)
    {
        var choices = Enum.GetValues<Reward>().OrderBy(_ => _random.Next()).Take(Math.Clamp(Rewards.Choices, 1, 4)).ToList();
        member.OfferRewards(new RewardOffer(floor, choices, opensAt, opensAt + TimeSpan.FromSeconds(Rewards.PickSeconds), forMinigame));
        member.StopHourglass();
    }

    /// <summary>
    /// An adventurer picks one of the rewards on offer with an answer button (<paramref name="choice"/>, 0-based), or
    /// time ran out and one is picked for them (null). Then their question comes back after a short beat showing it.
    /// Returns null if nothing was picked (no offer, a button with no card, or pressed before the cards could be read),
    /// otherwise the reward and the seconds to add to the clock (a Time reward's). The clock was stopped while they chose
    /// (<see cref="ClockStopped"/>), so choosing doesn't cost paid time.
    /// </summary>
    public (RewardTaken Taken, double ClockSeconds)? ChooseReward(string ownerId, int? choice, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        if (adventurer.Offer is not { } offer)
            return null;
        if (choice is { } index && (index < 0 || index >= offer.Choices.Count || now < offer.OpensAt + TimeSpan.FromSeconds(Rewards.PickDelaySeconds)))
            return null;

        var picked = choice ?? _random.Next(offer.Choices.Count);
        var (taken, seconds) = Grant(adventurer, offer.Choices[picked], offer.Floor, now, picked: choice != null, offer, picked);
        adventurer.CloseOffer();

        var stream = _streams[ownerId];
        stream.Start(now + TimeSpan.FromSeconds(Rewards.BeatSeconds)); // the question they were on comes back, fresh
        AfterTurn(stream, now);
        return (taken, seconds);
    }

    /// <summary>
    /// Hands an adventurer a reward without an offer (the dev tools, for testing). Returns it, and the seconds for
    /// the clock (a Time reward's).
    /// </summary>
    public (RewardTaken Taken, double ClockSeconds) GiveReward(string ownerId, Reward reward, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        return Grant(adventurer, reward, DelveOf(adventurer).Floor, now, picked: true);
    }

    /// <summary>
    /// A reward goes on an adventurer's list (most read it from there: see <see cref="DamageShare"/> and the others);
    /// Vitality raises their max HP now. Returns it, and the seconds for the clock (a Time reward's).
    /// </summary>
    private (RewardTaken Taken, double ClockSeconds) Grant(Adventurer adventurer, Reward reward, int floor, DateTimeOffset now, bool picked,
        RewardOffer? offer = null, int? choice = null)
    {
        var taken = adventurer.TakeReward(reward, RewardAmount(adventurer, reward), floor, now, picked, offer, choice);
        if (reward == Reward.Vitality)
            adventurer.RaiseMaxHp(VitalityHp(adventurer));
        return (taken, reward == Reward.Time ? RewardTimeSeconds(adventurer) : 0);
    }

    // ------------------------------------------------------------------
    // Mini games: a floor may have one between two of its rooms (DealtHit.MinigameAhead). Everyone in the party plays
    // their own go at it on their own station; each goes back to the fight when theirs is over (a win first earns a
    // reward pick). The arcade clock stops meanwhile.
    // ------------------------------------------------------------------

    private MinigameSettings MinigameRules => _settings.Minigames;

    /// <summary>
    /// The arcade clock stops while anyone in the match is away from the fight (choosing a reward or playing a mini
    /// game): neither is meant to cost paid time. The match engine holds the clock still meanwhile.
    /// </summary>
    public bool ClockStopped => _adventurers.Values.Any(a => a.IsAway);

    // ------------------------------------------------------------------
    // Sides with their own clock (a race, a team battle: see MatchEngine's PartyClock): each party is a side.
    // ------------------------------------------------------------------

    private readonly HashSet<string> _doneParties = [];

    /// <summary>Every party in the match (a solo player, a party or a battle team, each racer).</summary>
    public IReadOnlyList<string> PartyIds => _delves.Keys.ToList();

    /// <summary>A side's clock stands still while any of it is in a mini game or picking a reward.</summary>
    public bool ClockStoppedFor(string partyId) => PartyOf(partyId).Any(a => a.IsAway);

    /// <summary>Everyone in the party is out of lives.</summary>
    public bool PartyOut(string partyId) => _settings.Lives > 0 && PartyOf(partyId).All(a => a.IsOut);

    /// <summary>A side done for the match (its time ran out, or it's all out of lives).</summary>
    public bool IsDone(string partyId) => _doneParties.Contains(partyId);

    /// <summary>
    /// A side's done for the match (its own clock ran out, or everyone in it is out): its monsters stop attacking (no
    /// hourglasses), its poison stops, and any Phone a Friend call is over. The other sides play on.
    /// </summary>
    public void EndParty(string partyId)
    {
        _doneParties.Add(partyId);
        foreach (var adventurer in PartyOf(partyId))
        {
            adventurer.HangUp();
            adventurer.StopHourglass();
        }
    }

    /// <summary>
    /// A party has cleared the room before the floor's mini game (<paramref name="kill"/>'s <c>MinigameAhead</c>): everyone in
    /// it not already away starts their go, its intro once the kill beat that emptied the room has played. Their
    /// question and hourglass wait. A group's game (the hot potato) they all play together, at one table; with fewer than
    /// two to play it, they get the floor's stand-in instead (<see cref="DungeonMap.MinigameFor"/>). Returns who's playing.
    /// </summary>
    public IReadOnlyList<Adventurer> StartMinigames(string partyId, DealtHit kill, DateTimeOffset now)
    {
        if (!MinigameRules.Enabled || kill.MinigameAhead is not { } planned)
            return [];

        var introAt = now + TimeSpan.FromSeconds(BeatSecondsAfter(kill));
        var players = PartyOf(partyId).Where(a => !a.IsAway && !a.IsOut).ToList();
        // The floor the room was on (a kill that emptied the room before a mini game never clears the floor).
        var kind = _map.MinigameFor(_delves[partyId].Floor, planned, players.Count);
        var seats = kind == MinigameKind.HotPotato ? HotPotato.Deal(introAt, MinigameRules, _random, players.Select(p => p.OwnerId).ToList()) : null;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var facts = kind == MinigameKind.PopQuiz ? PopQuizFactsFor(player, kill) : null;
            player.StartMinigame(seats?[i] ?? MinigameCatalog.Create(kind, introAt, MinigameRules, _random, facts));
            player.StopHourglass();
        }
        return players;
    }

    /// <summary>What the pop quiz can ask an adventurer about their run, just after <paramref name="kill"/> emptied the room.</summary>
    private PopQuizFacts PopQuizFactsFor(Adventurer adventurer, DealtHit kill)
    {
        var delve = DelveOf(adventurer);
        var name = _names.GetValueOrDefault(adventurer.OwnerId, adventurer.OwnerId);
        var others = _names.Values.Where(n => n != name).ToList();
        var madeUp = new List<string>();
        for (var i = 0; i < 3; i++)
            madeUp.Add(Arcade.HeroNames.Make(_random, [name, .. others, .. madeUp]));
        var floor = delve.Floor;
        var lastBoss = floor > 1
            ? _map.RoomAt(floor - 1, _map.RoomsOn(floor - 1)).FirstOrDefault(e => e.Monster.IsBoss)?.Monster.Name
            : null;
        return new PopQuizFacts(
            name,
            others,
            madeUp,
            floor,
            kill.MonsterName,
            _map.Monsters.Where(m => !m.IsBoss).Select(m => m.Name).ToList(),
            adventurer.Class.ToString(),
            Enum.GetNames<HeroClass>(),
            delve.Kills,
            _settings.Lives > 0 ? adventurer.Lives : null,
            adventurer.MaxLives,
            _streams[adventurer.OwnerId].LastAnswer?.Question,
            lastBoss,
            _map.Monsters.Where(m => m.IsBoss).Select(m => m.Name).ToList());
    }

    /// <summary>An answer button in a mini game (pressed at <paramref name="at"/>). What it did; nothing outside one.</summary>
    public MinigamePress PlayMinigame(string ownerId, int answerIndex, DateTimeOffset at)
    {
        var adventurer = _adventurers[ownerId];
        if (adventurer.Minigame is not { } game)
            return MinigamePress.Ignored;
        var trapMisses = (game as TrapRoom)?.Misses ?? 0;
        var result = game.Press(answerIndex, at);
        AfterRoomPlay(adventurer, game, result, trapMisses);
        return result;
    }

    /// <summary>
    /// The mini games' own clocks inside their time limits (a trap's statement, High Stakes' choice or question) that have
    /// run out by <paramref name="now"/>: each counts as a press would (a miss, walking on, a loss), and what it did.
    /// </summary>
    public IReadOnlyList<(Adventurer Adventurer, MinigamePress Result)> MinigameClocksDue(DateTimeOffset now)
    {
        var due = new List<(Adventurer, MinigamePress)>();
        foreach (var adventurer in _adventurers.Values.Where(a => a.Minigame != null))
        {
            var game = adventurer.Minigame!;
            while (true) // a slow tick may be past more than one (a trap's statements)
            {
                var trapMisses = (game as TrapRoom)?.Misses ?? 0;
                var result = game.Due(now);
                if (result == MinigamePress.Ignored)
                    break;
                AfterRoomPlay(adventurer, game, result, trapMisses);
                due.Add((adventurer, result));
            }
        }
        return due;
    }

    /// <summary>
    /// What a special room does to the player as it's played: each time a trap springs it hurts, and getting through it
    /// without a miss heals them fully; a lost bet at High Stakes hurts more (neither ever takes their last HP); an opened
    /// chest puts its prize in their hands.
    /// </summary>
    /// <param name="trapMissesBefore">A trap's misses before this press (or clock): one more means it sprang.</param>
    private void AfterRoomPlay(Adventurer adventurer, Minigame game, MinigamePress result, int trapMissesBefore)
    {
        switch (game)
        {
            case TrapRoom trap when trap.Misses > trapMissesBefore:
                trap.Hurt(adventurer.Blast((int)Math.Round(adventurer.MaxHp * MinigameRules.Trap.MissShare)));
                break;
            case TrapRoom trap when result == MinigamePress.Won:
                trap.Heal(adventurer.IsDowned ? 0 : adventurer.Heal(adventurer.MaxHp)); // every one right: a full heal
                break;
            case HighStakesRoom bet when result == MinigamePress.Lost:
                bet.Punish(adventurer.Blast((int)Math.Round(adventurer.MaxHp * MinigameRules.HighStakes.PenaltyShare)));
                break;
            case TreasureRoom treasure when result == MinigamePress.Won:
                OpenChest(adventurer, treasure);
                break;
        }
    }

    /// <summary>
    /// The treasure room's chest opens: a power-up in their hands (any that can drop for them, each as likely), or, with
    /// their hands already full, a potion drunk there and then (<see cref="DungeonSettings.PotionHeal"/>; nothing for the downed).
    /// </summary>
    private void OpenChest(Adventurer adventurer, TreasureRoom treasure)
    {
        if (adventurer.Held == null)
        {
            var choices = PowerUpsFor(adventurer.OwnerId);
            var found = choices[_random.Next(choices.Count)];
            adventurer.PickUp(found);
            treasure.Award(found, 0);
            return;
        }
        treasure.Award(null, adventurer.IsDowned ? 0 : adventurer.Heal(_settings.PotionHeal));
    }

    /// <summary>
    /// Mini games moving on: the ones won by how things stand (the tuner's bars all held in place: <paramref name="wonByPlay"/>),
    /// the ones whose time limit just ran out are lost (<paramref name="lost"/>), and the ones whose
    /// result has been shown are over (<paramref name="over"/>): a winner is offered rewards (when that's on), everyone
    /// else gets their question back with a fresh hourglass.
    /// </summary>
    /// <param name="bossEntrances">Of those over, the ones whose game was the room behind a fork's door: the boss's entrance
    /// plays now (<see cref="BossEnters"/>), then their question (or a winner's reward cards).</param>
    public void MinigamesDue(DateTimeOffset now, out IReadOnlyList<Adventurer> wonByPlay, out IReadOnlyList<Adventurer> lost, out IReadOnlyList<Adventurer> over,
        out IReadOnlyList<Adventurer> bossEntrances)
    {
        var wonNow = new List<Adventurer>();
        var timedOut = new List<Adventurer>();
        var finished = new List<Adventurer>();
        var entrances = new List<Adventurer>();
        foreach (var adventurer in _adventurers.Values.Where(a => a.Minigame != null))
        {
            var game = adventurer.Minigame!;
            if (game.WinDue(now))
                wonNow.Add(adventurer);
            if (game.TimeUp(now))
            {
                timedOut.Add(adventurer);
                // The hot potato went off in their hands: the blast takes a share of their HP (never the last of it).
                if (game is HotPotato { Holding: true } potato)
                    potato.Bomb.WentOff(adventurer.OwnerId, adventurer.Blast((int)Math.Round(adventurer.MaxHp * MinigameRules.HotPotato.BlastShare)));
            }
            if (game.ResultEndsAt is not { } resultEnds || resultEnds > now)
                continue;

            var won = game.Won;
            adventurer.EndMinigame();
            finished.Add(adventurer);
            var stream = _streams[adventurer.OwnerId];
            // The room behind a fork's door: the boss is next, and it hasn't been seen yet. Its entrance plays first.
            var backAt = now;
            if (adventurer.BossAfterRoom)
            {
                backAt = BossEnters(adventurer, now);
                entrances.Add(adventurer);
            }
            if (won && game.WinEarnsReward && MinigameRules.WinEarnsReward && Rewards.Enabled)
            {
                OfferRewardsTo(adventurer, DelveOf(adventurer).Floor, backAt, forMinigame: true); // the cards come up after any entrance
                if (backAt > now)
                    stream.Start(backAt); // the entrance's beat ends then
                continue;
            }
            stream.Start(backAt); // the question they were on comes back, fresh (after any entrance)
            AfterTurn(stream, now);
        }
        wonByPlay = wonNow;
        lost = timedOut;
        over = finished;
        bossEntrances = entrances;
    }

    /// <summary>
    /// The boss's entrance on its own, after the room behind a fork's door (the kill before the doors was a plain kill, so
    /// it wasn't seen before them): the boss beat from its curtain on, as if it began <see cref="DungeonSettings.BossCurtainSeconds"/>
    /// ago, so every screen and sound keeps the boss beat's timing. Returns when it's over (their question then).
    /// </summary>
    private DateTimeOffset BossEnters(Adventurer adventurer, DateTimeOffset now)
    {
        var seconds = _settings.BossBeatSeconds - _settings.BossCurtainSeconds;
        if (seconds <= 0)
        {
            adventurer.BossEnters(null); // battle beats off (the tests): straight to the boss's question
            return now;
        }
        adventurer.BossEnters(now - TimeSpan.FromSeconds(_settings.BossCurtainSeconds));
        return now + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>The adventurers whose time to pick a reward has run out: one is picked for them.</summary>
    public IReadOnlyList<Adventurer> RewardsDue(DateTimeOffset now) =>
        _adventurers.Values.Where(a => a.Offer?.EndsAt <= now).ToList();

    // ------------------------------------------------------------------
    // Forks: some floors have one right before the boss (DealtHit.ForkAhead, DungeonMap.ForkOn). Once the boss's entrance
    // has played, the party votes for one of two doors (which room is behind which is a secret until one opens), and
    // everyone who voted plays the special room behind it (a mini game: MinigameCatalog.Rooms), then fights the boss. The
    // arcade clock stops meanwhile.
    // ------------------------------------------------------------------

    /// <summary>
    /// A kill brought a floor's boss up, and there's a fork before it (<paramref name="kill"/>'s <c>ForkAhead</c>): its doors
    /// come up, once the boss's entrance has played, for everyone in the party not away (a mini game, a reward pick) or
    /// out of lives. Their question and hourglass wait. Returns who's at the fork.
    /// </summary>
    public IReadOnlyList<Adventurer> OpenFork(string partyId, DealtHit kill, DateTimeOffset now)
    {
        var floor = _delves[partyId].Floor;
        if (!MinigameRules.Enabled || !kill.ForkAhead || _map.ForkOn(floor) is not { } plan)
            return [];

        var voters = PartyOf(partyId).Where(a => !a.IsAway && !a.IsOut).ToList();
        if (voters.Count == 0)
            return [];
        var opensAt = now + TimeSpan.FromSeconds(BeatSecondsAfter(kill));
        var fork = new Fork(partyId, floor, plan, voters.Select(v => v.OwnerId).ToList(), opensAt, MinigameRules.Fork);
        foreach (var voter in voters)
        {
            voter.ArriveAtFork(fork);
            voter.StopHourglass();
        }
        return voters;
    }

    /// <summary>
    /// A vote at a fork: an answer button, A for the first door and B for the second. Returns whether it counted. Once
    /// everyone there has voted, it's decided (<see cref="Fork.Decide"/>).
    /// </summary>
    public bool VoteDoor(string ownerId, int answerIndex, DateTimeOffset at)
    {
        if (_adventurers[ownerId].Fork is not { } fork || !fork.Vote(ownerId, answerIndex, at))
            return false;
        if (fork.EveryoneVoted)
            fork.Decide(at, _random);
        return true;
    }

    /// <summary>
    /// Forks moving on: the ones whose time to vote is up are decided by the votes so far (<paramref name="decided"/>), and
    /// once the door taken has opened, everyone still at the fork goes through into the room behind it: their go at it
    /// starts (<paramref name="entered"/>).
    /// </summary>
    public void ForksDue(DateTimeOffset now, out IReadOnlyList<Fork> decided, out IReadOnlyList<Adventurer> entered)
    {
        var decidedNow = new List<Fork>();
        var enteredNow = new List<Adventurer>();
        foreach (var fork in _adventurers.Values.Select(a => a.Fork).OfType<Fork>().Distinct().ToList())
        {
            if (!fork.IsDecided && now >= fork.EndsAt)
            {
                fork.Decide(fork.EndsAt, _random);
                decidedNow.Add(fork);
            }
            if (fork.RoomFrom is not { } roomFrom || now < roomFrom)
                continue;

            foreach (var adventurer in PartyOf(fork.PartyId).Where(a => a.Fork == fork))
            {
                adventurer.LeaveFork();
                StartRoom(adventurer, fork.Room!.Value, now);
                enteredNow.Add(adventurer);
            }
        }
        decided = decidedNow;
        entered = enteredNow;
    }

    private readonly List<Question> _highStakes;
    private int _highStakesAsked;

    /// <summary>
    /// An adventurer's go at a special room, its intro from <paramref name="introAt"/>, with questions from their own stream;
    /// High Stakes' from its own bank (the next in this match's shuffled order, round again if they run out) when it has any.
    /// </summary>
    private void StartRoom(Adventurer adventurer, MinigameKind room, DateTimeOffset introAt)
    {
        var (difficulty, count) = MinigameCatalog.RoomQuestions(room, MinigameRules);
        var questions = room == MinigameKind.HighStakes && _highStakes.Count > 0
            ? [_highStakes[_highStakesAsked++ % _highStakes.Count].WithShuffledAnswers(_random)]
            : _streams[adventurer.OwnerId].TakeExtra(difficulty, count);
        adventurer.StartMinigame(MinigameCatalog.CreateRoom(room, introAt, MinigameRules, _random, questions));
        adventurer.StopHourglass();
    }

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
        var tick = TimeSpan.FromSeconds(_settings.Classes.PoisonTickSeconds);
        var results = new List<(string, int, DealtHit?)>();
        foreach (var delve in _delves.Values.Where(d => !IsDone(d.PartyId))) // a side whose time is up is done: no more poison
        {
            // Nobody in the room (all off at a mini game, or picking a reward: their clock's stopped too): it waits for
            // them, rather than killing monsters nobody sees die.
            if (PartyOf(delve.PartyId).All(a => a.IsAway))
            {
                delve.HoldPoison(now + tick);
                continue;
            }
            var (damage, kill) = delve.PoisonTick(now, _settings.Classes.PoisonDamage, _settings.Classes.PoisonTickSeconds);
            if (damage > 0)
                results.Add((delve.PartyId, damage, kill));
        }
        return results;
    }

    /// <summary>
    /// The monster in front of a party died (<paramref name="kill"/>): by a teammate's answer (<paramref name="killerId"/>,
    /// whose own kill beat is already playing) or by the Healer's poison (null, from <see cref="TickPoison"/>). Everyone
    /// else in the party standing and not away watches it: they get the same kill beat from the same moment (a room
    /// or floor cleared: the longer one), and their question comes back after it, with a fresh hourglass for the
    /// monster that's up now. So every screen in the party moves on to the next monster together; without this the
    /// next one just swaps in under the question they're on while the killer's screen is still playing the death.
    /// </summary>
    public void PartyKilled(string partyId, DealtHit kill, string? killerId, DateTimeOffset now)
    {
        var shownAt = now + TimeSpan.FromSeconds(BeatSecondsAfter(kill));
        foreach (var member in PartyOf(partyId).Where(a => a.OwnerId != killerId && !a.IsDowned && !a.IsAway))
        {
            var stream = _streams[member.OwnerId];
            if (stream.CurrentShownAt >= shownAt)
                continue; // a longer beat of their own is playing (a floor cleared): it shows the kill too
            member.Watched(kill, killerId, now);
            stream.Start(shownAt); // the question they were on comes back after the beat
            AfterTurn(stream, now);
        }
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
        _adventurers.GetValueOrDefault(ownerId) is { Class: HeroClass.Ranger } ranger ? PeekOf(ranger) : 0;

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

        var choices = PowerUpsFor(adventurer.OwnerId);
        var found = choices[_random.Next(choices.Count)];
        adventurer.PickUp(found);
        return found;
    }

    /// <summary>The power-ups that can drop for an adventurer in this match (see <see cref="CanFind"/>), each as likely.</summary>
    public IReadOnlyList<PowerUp> PowerUpsFor(string ownerId) =>
        Enum.GetValues<PowerUp>().Where(p => CanFind(_adventurers[ownerId], p)).ToList();

    /// <summary>
    /// Whether a power-up can drop for them: a Time Crystal only in a solo game (the clock is everyone's), Phone a
    /// Friend only with teammates, and the ones aimed at a rival only with another party in the match (a race, a
    /// team battle). The rest drop anywhere.
    /// </summary>
    private bool CanFind(Adventurer adventurer, PowerUp powerUp) => powerUp switch
    {
        PowerUp.TimeCrystal => IsSolo,
        PowerUp.PhoneAFriend => PartyOf(adventurer.PartyId).Count > 1,
        PowerUp.FogBomb or PowerUp.TinyHourglass => _adventurers.Values.Any(a => a.PartyId != adventurer.PartyId),
        _ => true,
    };

    /// <summary>
    /// A rival a Fog Bomb or Tiny Hourglass can be aimed at: still playing, on their feet and in the fight (not in a
    /// mini game or picking a reward). The match engine aims at the one of these with the most points.
    /// </summary>
    public bool CanBeSabotaged(Adventurer rival) => !rival.IsOut && !rival.IsDowned && !rival.IsAway && !IsDone(rival.PartyId);

    /// <summary>
    /// Phone a Friend: a teammate free to take the call, or null. The caller needs their question up (not between
    /// two, in a battle beat), and so does the friend: standing, in the fight, and not already on a call.
    /// </summary>
    private Adventurer? FreeFriendOf(Adventurer caller, DateTimeOffset now)
    {
        bool QuestionUp(Adventurer a) => _streams[a.OwnerId].CurrentShownAt <= now;
        bool Free(Adventurer a) => !a.IsDowned && !a.IsOut && !a.IsAway && a.CalledFriend == null && a.AnsweringFor == null && QuestionUp(a);
        if (!Free(caller) || caller.AttackAt == null)
            return null;
        return PartyOf(caller.PartyId).FirstOrDefault(a => a != caller && Free(a));
    }

    /// <summary>
    /// Phone a Friend: whose question this adventurer is answering (their answer buttons answer it for them), or
    /// null. A call one of them can't go on with is over (see <see cref="EndStaleCalls"/>).
    /// </summary>
    public string? CallerFor(string ownerId) =>
        _adventurers.GetValueOrDefault(ownerId) is { AnsweringFor: { } callerId } friend && CallStillOn(_adventurers[callerId], friend)
            ? callerId
            : null;

    /// <summary>Both still able to go on with the call: on their feet and in the fight.</summary>
    private static bool CallStillOn(Adventurer caller, Adventurer friend) =>
        caller.CalledFriend == friend.OwnerId && friend.AnsweringFor == caller.OwnerId
        && !caller.IsDowned && !caller.IsOut && !caller.IsAway
        && !friend.IsDowned && !friend.IsOut && !friend.IsAway;

    /// <summary>The calls one of them can't go on with (downed, off to a mini game...) are over: the friend's own question goes on.</summary>
    public void EndStaleCalls(DateTimeOffset now)
    {
        foreach (var caller in _adventurers.Values.Where(a => a.CalledFriend != null).ToList())
            if (!CallStillOn(caller, _adventurers[caller.CalledFriend!]))
                HangUp(caller, now);
    }

    /// <summary>The call's over: the friend's own question comes back, with a fresh hourglass (unless their buttons are dead).</summary>
    private void HangUp(Adventurer caller, DateTimeOffset now)
    {
        var friend = _adventurers[caller.CalledFriend!];
        caller.HangUp();
        friend.HangUp();
        var stream = _streams[friend.OwnerId];
        if (!friend.IsAway)
            ShowOwnQuestionAgain(friend, now);
        if (stream.IsLockedOutAt(now) || friend.IsAway || friend.IsDowned)
            friend.StopHourglass();
        else
            StartHourglass(friend, now);
    }

    /// <summary>
    /// Their own question is on their screen again (a call's over, or they're back on their feet): it counts as shown
    /// from now, so a press meant for what was there a moment ago can't answer it unseen (the new-question grace), and
    /// how fast they answer is timed from now. One still held back by a battle beat shows when that ends anyway.
    /// </summary>
    private void ShowOwnQuestionAgain(Adventurer adventurer, DateTimeOffset now)
    {
        var stream = _streams[adventurer.OwnerId];
        if (stream.CurrentShownAt is not { } shownAt || shownAt <= now)
            stream.Start(now);
    }

    /// <summary>Hands an adventurer a power-up, replacing any they held (the dev tools, for testing).</summary>
    public void GivePowerUp(string ownerId, PowerUp powerUp) => _adventurers[ownerId].PickUp(powerUp);

    /// <summary>
    /// The action button: uses the power-up they're holding. Returns null if they hold none or are
    /// downed, or it has nobody to go to (Phone a Friend with no teammate free, a rival's one with no rival to
    /// aim at): then they keep it. A Time Crystal's seconds are for the match engine to add to the clock.
    /// </summary>
    /// <param name="rivalOwnerId">For a Fog Bomb or Tiny Hourglass: the leading rival, picked by the match engine (it
    /// keeps the scores); null if there's none to aim at (see <see cref="CanBeSabotaged"/>).</param>
    public PowerUpUse? UsePowerUp(string ownerId, DateTimeOffset now, string? rivalOwnerId = null)
    {
        var adventurer = _adventurers[ownerId];
        if (adventurer.IsDowned || adventurer.Held is not { } powerUp)
            return null;

        var friend = powerUp == PowerUp.PhoneAFriend ? FreeFriendOf(adventurer, now) : null;
        if (powerUp == PowerUp.PhoneAFriend && friend == null)
            return null; // nobody free to call: they keep it
        var rival = rivalOwnerId != null ? _adventurers.GetValueOrDefault(rivalOwnerId) : null;
        if (powerUp is PowerUp.FogBomb or PowerUp.TinyHourglass && rival == null)
            return null; // nobody to aim it at: they keep it

        adventurer.DropHeld();
        switch (powerUp)
        {
            case PowerUp.PhoneAFriend:
                adventurer.Call(friend!.OwnerId);
                friend.AnswerFor(ownerId);
                friend.StopHourglass(); // their own question waits while they help
                return adventurer.RecordPowerUp(powerUp, friend.OwnerId, 0, revived: false);
            case PowerUp.FogBomb or PowerUp.TinyHourglass:
            {
                // The rival's Shield soaks it; their Mirror Shield sends it back to land on the thrower.
                var outcome = rival!.MeetSabotage(powerUp, ownerId, now);
                if (outcome == SabotageOutcome.Reflected)
                    adventurer.BouncedBack(powerUp, rival.OwnerId, now);
                if (outcome != SabotageOutcome.Blocked)
                    LandSabotage(powerUp, outcome == SabotageOutcome.Reflected ? adventurer : rival, now);
                var seconds = powerUp == PowerUp.FogBomb ? (int)_settings.FogBombSeconds : 0;
                return adventurer.RecordPowerUp(powerUp, rival.OwnerId, seconds, revived: false,
                    blocked: outcome == SabotageOutcome.Blocked, reflected: outcome == SabotageOutcome.Reflected);
            }
            case PowerUp.MirrorShield:
                adventurer.RaiseMirror();
                break;
            case PowerUp.LuckyDice:
                adventurer.ReadyDice();
                break;
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
    /// A Fog Bomb or Tiny Hourglass lands on <paramref name="victim"/> (the rival it was thrown at, or the thrower when a
    /// Mirror Shield sent it back). A Fog Bomb blurs from when their question is up: thrown during their battle beat, it
    /// waits for the next one.
    /// </summary>
    private void LandSabotage(PowerUp powerUp, Adventurer victim, DateTimeOffset now)
    {
        if (powerUp == PowerUp.TinyHourglass)
        {
            victim.HalveHourglass(now);
            return;
        }
        var shownAt = _streams[victim.OwnerId].CurrentShownAt ?? now;
        var from = shownAt > now ? shownAt : now;
        victim.Fog(from, from + TimeSpan.FromSeconds(_settings.FogBombSeconds));
    }

    /// <summary>
    /// A Potion goes to whoever in the party needs it most: a downed teammate gets straight back up (at
    /// full HP), otherwise whoever standing is lowest on HP, for their share of their max, is healed.
    /// </summary>
    private (Adventurer Target, int Healed, bool Revived) DrinkPotion(Adventurer user, DateTimeOffset now)
    {
        var party = PartyOf(user.PartyId);
        var downed = party.Where(a => a.IsDowned && !a.IsOut).OrderBy(a => a.DownedUntil).FirstOrDefault(); // the out stay out
        if (downed != null)
        {
            var healed = downed.MaxHp - downed.Hp;
            _streams[downed.OwnerId].EndLockout();
            downed.GetUp();
            ShowOwnQuestionAgain(downed, now);
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
            .Where(a => a != helper && a.IsDowned && !a.IsOut) // out of lives: no reviving them
            .OrderBy(a => a.DownedUntil)
            .FirstOrDefault();
        if (downed == null || downed.AddReviveProgress() < _settings.ReviveAnswers)
            return null;

        _streams[downed.OwnerId].EndLockout();
        downed.GetUp();
        ShowOwnQuestionAgain(downed, now);
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
    ///   <item>sides racing (a race, a team battle) each have their own clock (MatchEngine's PartyClock), so each
    ///     side's kills earn it the full time.</item>
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
        return seconds * HpScaleFor(members) / members;
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
        adventurer.CountAnswer(delve.Floor, right: false);
        var damageTaken = DamageTakenShare(adventurer);
        var boss = BossFacing(delve);
        var step = boss != null ? adventurer.ChargeAgainst(boss) : 0;
        if (boss != null)
            damageTaken *= _settings.BossHitAt(step);
        var hit = adventurer.TakeHit(delve.Encounter, reason, now, downedSeconds, damageTaken, patronPrice, charged: boss != null && IsFullCharge(step));
        if (boss != null)
            adventurer.ChargeUp(boss, _settings.BossChargeSteps);

        // A Mirror Shield bounces the blow back at the monster (it never finishes it); a Vampiric monster drinks what it took.
        var front = delve.Foes[0];
        var blow = delve.Encounter.Damage * (boss != null ? _settings.BossHitAt(step) : 1);
        var reflected = hit.Mirrored ? delve.Reflect((int)Math.Round(blow * _settings.MirrorShare)) : 0;
        var drained = front.Affix == Affix.Vampiric && hit.Damage > 0 ? front.Heal((int)Math.Round(hit.Damage * _settings.Affixes.VampiricShare)) : 0;
        if (reflected > 0 || drained > 0)
        {
            hit = hit with { Reflected = reflected, Drained = drained };
            adventurer.Complete(hit);
        }
        delve.RecordTaken(hit);

        // While anyone in the party is still standing, the fight goes on. When nobody is, the room's
        // monsters heal and everyone gets up after the short downed time (nobody is left to revive them).
        if (hit.Downed && party.All(a => a.IsDowned))
        {
            delve.HealRoom();
            var upAt = now + TimeSpan.FromSeconds(_settings.DownedSeconds);
            foreach (var member in party.Where(a => !a.IsOut)) // the ones out of lives stay down
            {
                member.ShortenDowned(upAt);
                _streams[member.OwnerId].LockOut(member.DownedUntil!.Value);
            }
        }

        return hit;
    }

    /// <summary>
    /// Q.U.E.R.I.E. after an answer (or the hourglass running out, a timed-out <paramref name="answer"/>): whether he has
    /// something to say about it, and what (<see cref="Querie"/>). Null when he keeps quiet. His remark goes on their
    /// screen, and on the whole party's when it's a callout they should all hear (the odd one out, a teammate downed).
    /// </summary>
    public Quip? Commentate(string ownerId, GivenAnswer answer, DealtHit? dealt, TakenHit? taken, DateTimeOffset now)
    {
        if (_querie == null)
            return null;
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var party = PartyOf(adventurer.PartyId);
        // The rest of the party's answers just now: each one's last, if it was recent (and they're still in the game).
        var window = TimeSpan.FromSeconds(_settings.Querie.PartyWindowSeconds);
        var teammates = party
            .Where(m => m != adventurer && !m.IsOut)
            .Select(m => _streams[m.OwnerId].LastAnswer)
            .Where(last => last != null && now - last.PressedAt <= window)
            .ToList();
        // A race or a team battle: the rival side furthest ahead, if any is on a deeper floor.
        var rivalAhead = _delves.Values
            .Where(d => d.PartyId != adventurer.PartyId && d.Floor > delve.Floor)
            .MaxBy(d => d.Floor);

        var quip = _querie.React(new QuerieScene(
            ownerId,
            adventurer.PartyId,
            _names.GetValueOrDefault(ownerId, ownerId),
            answer,
            taken,
            dealt?.MonsterName ?? taken?.MonsterName ?? delve.Encounter.Monster.Name,
            delve.Floor,
            dealt?.IsBoss ?? delve.Encounter.Monster.IsBoss,
            adventurer.MaxHp > 0 ? (double)adventurer.Hp / adventurer.MaxHp : 1,
            adventurer.MaxLives > 0 ? adventurer.Lives : 0,
            Enumerable.Reverse(_streams[ownerId].Answers).TakeWhile(a => !a.IsCorrect).Count(),
            party.Count > 1,
            teammates.Count(a => a!.IsCorrect),
            teammates.Count(a => !a!.IsCorrect),
            rivalAhead == null ? null : _partyNames.GetValueOrDefault(rivalAhead.PartyId)), now);
        if (quip == null)
            return null;
        foreach (var listener in quip.PartyWide ? party : [adventurer])
            listener.Hear(quip);
        return quip;
    }

    /// <summary>
    /// Q.U.E.R.I.E. on a moment in someone's mini game (<paramref name="beat"/>: it opened, a press hit or missed, it was
    /// won or lost), at <paramref name="at"/>: his remark, if he has one, for them alone. Null when he keeps quiet.
    /// </summary>
    public Quip? CommentateMinigame(string ownerId, MinigameBeat beat, DateTimeOffset at)
    {
        var adventurer = _adventurers[ownerId];
        if (_querie == null || adventurer.Minigame is not { } game)
            return null;
        // The pop quiz: the question a press just answered (not an older one, when the time runs out).
        var asked = beat != MinigameBeat.Start && game is PopQuiz quiz && quiz.Last is { } last && last.At == at
            ? quiz.Questions[last.Question]
            : null;
        // The hot potato: who's holding the bomb (once it's gone off, who it went off on).
        var holder = game is HotPotato potato ? _names.GetValueOrDefault(potato.Bomb.HolderOwnerId, potato.Bomb.HolderOwnerId) : null;
        var quip = _querie.ReactToMinigame(new MinigameScene(ownerId, _names.GetValueOrDefault(ownerId, ownerId), game.Kind, beat, asked, at, holder));
        if (quip != null)
            adventurer.Hear(quip);
        return quip;
    }

    /// <summary>Q.U.E.R.I.E. as the doors of a fork come up (at <paramref name="at"/>): his remark for them, if he has one.</summary>
    public Quip? CommentateFork(string ownerId, DateTimeOffset at)
    {
        var adventurer = _adventurers[ownerId];
        if (_querie == null || adventurer.Fork == null)
            return null;
        var quip = _querie.ReactToFork(ownerId, _names.GetValueOrDefault(ownerId, ownerId), at);
        if (quip != null)
            adventurer.Hear(quip);
        return quip;
    }

    /// <summary>
    /// After an answer or an attack: a downed adventurer's buttons go dead until they get up, and the
    /// hourglass runs for the next question unless the buttons are dead (it restarts when they work again).
    /// </summary>
    public void AfterTurn(QuestionStream stream, DateTimeOffset now)
    {
        var adventurer = _adventurers[stream.OwnerId];
        if (adventurer.CalledFriend != null)
            HangUp(adventurer, now); // Phone a Friend: their question's answered (by either of them) or ran out
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
        adventurer.ExtendOffer(now, Rewards.PickSeconds);

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
        {
            adventurer.GetUp();
            // In a party they were shown a teammate's question meanwhile (alone, their own stayed up, dimmed).
            if (PartyOf(adventurer.PartyId).Count > 1)
                ShowOwnQuestionAgain(adventurer, now);
        }

        StartHourglass(adventurer, now);
        return gotUp;
    }

    /// <summary>
    /// How long the next question waits after an answer, while the screen shows the fight: a hit, a
    /// kill, a floor cleared (the longest), or the monster's attack (<paramref name="dealt"/> null).
    /// </summary>
    public double BeatSecondsAfter(DealtHit? dealt) =>
        dealt == null ? _settings.HurtBeatSeconds
        : dealt.BossAhead && !dealt.ForkAhead ? _settings.BossBeatSeconds // the boss's entrance (with a fork first: after its room)
        : dealt.FloorCleared ? _settings.FloorBeatSeconds
        : dealt.RoomCleared && dealt.MinigameAhead == null && !dealt.ForkAhead ? _settings.RoomBeatSeconds // the walk to the next room
        : dealt.Killed ? _settings.KillBeatSeconds
        : _settings.HitBeatSeconds;

    /// <summary>
    /// The hourglasses in their last seconds (<see cref="DungeonSettings.HourglassCountdownSeconds"/>): each adventurer
    /// that has just reached another whole second left, and that second (3, 2, 1). The room hears each one.
    /// </summary>
    public IReadOnlyList<(Adventurer Adventurer, int SecondsLeft)> HourglassCountdowns(DateTimeOffset now) =>
        _adventurers.Values
            .Select(a => (Adventurer: a, SecondsLeft: a.CountDown(now, _settings.HourglassCountdownSeconds)))
            .Where(c => c.SecondsLeft != null)
            .Select(c => (c.Adventurer, c.SecondsLeft!.Value))
            .ToList();

    /// <summary>The adventurers whose hourglass has run out: their monster attacks.</summary>
    public IReadOnlyList<Adventurer> AttacksDue(DateTimeOffset now) =>
        _adventurers.Values.Where(a => a.IsAttackDue(now)).ToList();

    /// <summary>
    /// Turns the hourglass over from now, or from when their question appears if it's still to come (a
    /// battle beat), or from when a Freeze ends.
    /// </summary>
    private void StartHourglass(Adventurer adventurer, DateTimeOffset now)
    {
        if (adventurer.IsAway || adventurer.AnsweringFor != null || IsDone(adventurer.PartyId))
        {
            // It waits while they pick a reward, play a mini game or answer a teammate's call; their side's time is up: no more.
            adventurer.StopHourglass();
            return;
        }
        var shownAt = _streams[adventurer.OwnerId].CurrentShownAt ?? now;
        var from = shownAt > now ? shownAt : now;
        if (adventurer.FrozenUntil > from)
            from = adventurer.FrozenUntil.Value;
        adventurer.StartHourglass(from, HourglassSecondsOf(DelveOf(adventurer).Foes[0]));
        PlanReveal(adventurer, from);
    }

    /// <summary>How long a monster's hourglass runs: its own time, shorter for a Speedy one.</summary>
    private double HourglassSecondsOf(Foe foe) =>
        foe.Monster.AttackSeconds * (foe.Affix == Affix.Speedy ? _settings.Affixes.SpeedyHourglass : 1);

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
            adventurer.DecideReveal(question, _random.NextDouble() < RevealChanceOf(adventurer));

        if (!adventurer.RevealComing || adventurer.RevealedOnQuestion == question)
        {
            adventurer.ScheduleReveal(null);
            return;
        }
        var at = attackAt - TimeSpan.FromSeconds(classes.WarlockRevealSecondsLeft);
        adventurer.ScheduleReveal(at > hourglassFrom ? at : hourglassFrom);
    }
}
