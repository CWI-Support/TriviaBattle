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
///   <item>A correct answer hits the party's monster. Fast = crit; a streak = combo. At 0 HP the next monster steps up.</item>
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
///   <item>A solo game runs on an arcade clock: every kill earns time (<see cref="SecondsEarnedBy"/>).
///     Groups play a fixed round, so everyone starts and ends together.</item>
/// </list>
/// Not thread-safe; the engine is only ever called by one thread at a time.
/// </summary>
public sealed class DungeonRun
{
    private readonly DungeonSettings _settings;
    private readonly IReadOnlyDictionary<string, QuestionStream> _streams;
    private readonly Dictionary<string, Delve> _delves = new();
    private readonly Dictionary<string, Adventurer> _adventurers = new();

    /// <param name="streams">The match's question streams, by answer owner. Downed adventurers' buttons are
    /// dead through their stream's lockout, so reviving someone ends it.</param>
    public DungeonRun(MatchRoster roster, IGameMode mode, IReadOnlyDictionary<string, QuestionStream> streams, DungeonMap map, DungeonSettings settings)
    {
        _settings = settings;
        _streams = streams;
        IsSolo = roster.Players.Count == 1;

        foreach (var player in roster.Players)
        {
            var partyId = player.TeamId ?? player.StationId;
            if (!_delves.ContainsKey(partyId))
                _delves[partyId] = new Delve(partyId, map);

            var ownerId = mode.GetAnswerOwnerId(player);
            if (!_adventurers.ContainsKey(ownerId))
                _adventurers[ownerId] = new Adventurer(ownerId, partyId, settings.AdventurerHp);
        }
    }

    /// <summary>One player on their own: the game runs on the arcade clock.</summary>
    public bool IsSolo { get; }

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

    /// <summary>The clock has stopped: no more attacks.</summary>
    public void Stop()
    {
        foreach (var adventurer in _adventurers.Values)
            adventurer.StopHourglass();
    }

    /// <summary>
    /// A correct answer: hits the party's monster, maybe completing a party combo, and counts towards
    /// reviving a downed teammate (<see cref="DealtHit.RevivedOwnerId"/> says if it brought one back).
    /// </summary>
    public DealtHit Strike(string ownerId, Difficulty difficulty, TimeSpan responseTime, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var streak = adventurer.AddToStreak();
        var crit = responseTime.TotalSeconds <= _settings.CritWithinSeconds;
        var combo = streak >= _settings.ComboStreak;
        var partyCombo = IsPartyCombo(adventurer, delve, now);

        var damage = (double)_settings.DamageFor(difficulty);
        if (crit)
            damage *= _settings.CritMultiplier;
        if (combo)
            damage *= 1 + _settings.ComboBonus;
        if (partyCombo)
            damage *= 1 + _settings.PartyComboBonus;

        var revived = HelpRevive(adventurer, now);
        var hit = delve.Hit((int)Math.Round(damage), crit, combo, partyCombo, revived?.OwnerId, now);
        adventurer.Landed(hit, now);
        return hit;
    }

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
    /// The time a hit earns on a solo game's clock: a kill earns <see cref="DungeonSettings.SoloSecondsPerKill"/>,
    /// a boss kill <see cref="DungeonSettings.SoloSecondsPerBoss"/>. Always 0 in a group game.
    /// The match engine adds it, up to the hard cap.
    /// </summary>
    public double SecondsEarnedBy(DealtHit hit)
    {
        if (!IsSolo || !hit.Killed)
            return 0;
        return hit.IsBoss ? _settings.SoloSecondsPerBoss : _settings.SoloSecondsPerKill;
    }

    /// <summary>
    /// A wrong answer, or the hourglass ran out: the party's monster hits this adventurer. Downed on
    /// their own, they're out for <see cref="DungeonSettings.DownedSeconds"/>; in a party, until their
    /// teammates revive them (or <see cref="DungeonSettings.PartyDownedSeconds"/> pass).
    /// </summary>
    public TakenHit TakeHit(string ownerId, HitReason reason, DateTimeOffset now)
    {
        var adventurer = _adventurers[ownerId];
        var delve = DelveOf(adventurer);
        var party = PartyOf(adventurer.PartyId);
        var monster = delve.Encounter.Monster;
        var downedSeconds = party.Count > 1 ? _settings.PartyDownedSeconds : _settings.DownedSeconds;
        var hit = adventurer.TakeHit(monster, reason, now, downedSeconds);
        delve.RecordTaken(hit);

        // While anyone in the party is still standing, the fight goes on. When nobody is, the monster
        // heals and everyone gets up after the short downed time (nobody is left to revive them).
        if (hit.Downed && party.All(a => a.IsDowned))
        {
            delve.HealMonster();
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
        : dealt.FloorCleared ? _settings.FloorBeatSeconds
        : dealt.Killed ? _settings.KillBeatSeconds
        : _settings.HitBeatSeconds;

    /// <summary>The adventurers whose hourglass has run out: their monster attacks.</summary>
    public IReadOnlyList<Adventurer> AttacksDue(DateTimeOffset now) =>
        _adventurers.Values.Where(a => a.IsAttackDue(now)).ToList();

    /// <summary>Turns the hourglass over from now, or from when their question appears if it's still to come (a battle beat).</summary>
    private void StartHourglass(Adventurer adventurer, DateTimeOffset now)
    {
        var shownAt = _streams[adventurer.OwnerId].CurrentShownAt ?? now;
        adventurer.StartHourglass(shownAt > now ? shownAt : now, DelveOf(adventurer).Encounter.Monster.AttackSeconds);
    }
}
