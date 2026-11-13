using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Runs one match: moves through the phases, accepts button presses, and scores answers.
/// This is the single source of truth for a match. Screens only display what it says.
///
/// Every match is a timed rapid-fire round. While the clock runs (Playing), each answer owner
/// (a player, or a team in 2v2 Group) has their own <see cref="QuestionStream"/>: a press answers
/// their current question and the next one appears straight away. Wrong answers cost a lockout
/// or points, depending on <see cref="MatchSettings.WrongAnswerPenalty"/>.
///
/// How it's driven:
/// <list type="bullet">
///   <item><see cref="Tick"/> is called several times a second; it ends lockouts and phases when their time is up.</item>
///   <item><see cref="HandlePress"/> is called for each button press at one of this match's stations.</item>
/// </list>
/// Both return the <see cref="MatchEvent"/>s that happened (empty if nothing changed).
///
/// In <see cref="GameFormat.Dungeon"/> the match also has a <see cref="DungeonRun"/>, which this class
/// tells about every answer and tick: correct answers hit the party's monster, wrong ones (or its
/// hourglass running out) get the answer owner hit back. The combat rules are all in there.
///
/// The current time is always passed in rather than read from a clock, which keeps the
/// engine predictable and easy to test.
///
/// NOT thread-safe on purpose: the server's MatchHost makes sure only one call runs at a time.
/// </summary>
public sealed class MatchEngine
{
    private static readonly IReadOnlyList<MatchEvent> NothingHappened = [];

    private readonly Dictionary<string, QuestionStream> _streams;
    private readonly DungeonRun? _dungeon;

    /// <summary>The time of the last Tick, so a tick can tell which questions appeared since (<see cref="QuestionShown"/>).</summary>
    private DateTimeOffset _lastTickAt;

    /// <param name="questions">The question pool. Every answer owner gets their own order of it.</param>
    /// <param name="random">Shuffles each owner's question order. Tests pass a seeded one.</param>
    /// <param name="dungeon">The floors and monsters. Required when <see cref="MatchSettings.Format"/> is Dungeon.</param>
    public MatchEngine(
        string matchId,
        MatchSetup setup,
        IReadOnlyList<Question> questions,
        IGameMode mode,
        MatchSettings settings,
        DateTimeOffset now,
        Random random,
        DungeonMap? dungeon = null)
    {
        if (questions.Count == 0)
            throw new ArgumentException("A match needs at least one question.", nameof(questions));

        MatchId = matchId;
        Setup = setup;
        Mode = mode;
        Settings = settings;
        // A photo from the lobby only counts in a game with photos (not Classic, not with them switched off).
        Roster = new MatchRoster(setup, takesPhotos: settings.Format == GameFormat.Dungeon && settings.Dungeon.HeroPhotos);
        StartedAt = now;
        CategoryName = string.Join(", ", questions.Select(q => q.Category).Distinct().Order());

        // The chosen difficulty comes first; other difficulties only fill in for the very fast.
        _streams = Roster.Players
            .Select(Mode.GetAnswerOwnerId)
            .Distinct()
            .ToDictionary(ownerId => ownerId, ownerId => new QuestionStream(ownerId, questions
                .OrderBy(q => q.Difficulty == setup.Difficulty ? 0 : 1)
                .ThenBy(_ => random.Next())
                .ToList()));

        if (settings.Format == GameFormat.Dungeon)
        {
            if (dungeon == null)
                throw new ArgumentException("Dungeon format needs the dungeon map.", nameof(dungeon));
            _dungeon = new DungeonRun(Roster, Mode, _streams, dungeon, settings.Dungeon, random);
        }

        EarnsTime = _dungeon != null;
        RoundSeconds = EarnsTime
            ? settings.Dungeon.ClockStartSeconds
            : settings.MatchSeconds;

        EnterPhase(MatchPhase.Intro, now, settings.IntroSeconds);
        _lastTickAt = now;

        // Hero photos on by default: everyone's station takes one after a moment and the countdown, unless they
        // say no. Not for players whose photo was settled in the lobby (they brought one, or chose none).
        if (OffersPhotos && settings.Dungeon.PhotoByDefault)
            foreach (var player in Roster.Players.Where(p => !p.PhotoFromLobby))
                player.SchedulePhoto(now + PhotoLead + TimeSpan.FromSeconds(settings.Dungeon.PhotoCountdownSeconds), cameraOn: false);
    }

    public string MatchId { get; }
    public MatchSetup Setup { get; }
    public IGameMode Mode { get; }
    public MatchSettings Settings { get; }
    public MatchRoster Roster { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>The category name(s), taken from the questions (the setup only has ids): "Movies, Music".</summary>
    public string CategoryName { get; }

    /// <summary>
    /// A Dungeon game (solo or group): the clock is an arcade clock. A paid session starts at
    /// <see cref="DungeonSettings.ClockStartSeconds"/> and every kill adds time (see
    /// <see cref="DungeonRun.SecondsEarnedBy"/>), with no cap: it lasts until it runs out. Then a swipe continues
    /// (a new paid session). Classic plays a fixed round of <see cref="MatchSettings.MatchSeconds"/>.
    /// </summary>
    public bool EarnsTime { get; }

    /// <summary>How long the clock runs when Playing starts (a Dungeon game's grows from there).</summary>
    public double RoundSeconds { get; }

    /// <summary>Dungeon games: how many times the players continued (a swipe: another paid session).</summary>
    public int ContinuesUsed { get; private set; }

    public MatchPhase Phase { get; private set; }
    public DateTimeOffset PhaseStartedAt { get; private set; }

    /// <summary>When the current phase is scheduled to end. Screens use this for countdowns (the match clock while Playing).</summary>
    public DateTimeOffset PhaseEndsAt { get; private set; }

    /// <summary>Goes up by one every time anything changes. Screens use it to ignore stale updates.</summary>
    public int Version { get; private set; }

    public bool WasAborted { get; private set; }

    public bool IsFinished => Phase == MatchPhase.Finished;

    /// <summary>Every answer owner's question stream, in roster order.</summary>
    public IReadOnlyCollection<QuestionStream> Streams => _streams.Values;

    /// <summary>The stream a player answers in: their own, or their team's in 2v2 Group.</summary>
    public QuestionStream StreamFor(Player player) => _streams[Mode.GetAnswerOwnerId(player)];

    public QuestionStream? FindStream(string ownerId) => _streams.GetValueOrDefault(ownerId);

    public bool IsDungeon => Settings.Format == GameFormat.Dungeon;

    /// <summary>Dungeon format: the parties' delves and the adventurers. Null in Classic format.</summary>
    public DungeonRun? Dungeon => _dungeon;

    public bool HasStation(string stationId) => Roster.FindPlayer(stationId) != null;

    public IReadOnlyList<Standing> GetStandings() => Standings.Calculate(Roster, Mode.UsesTeams);

    // ------------------------------------------------------------------
    // Inputs
    // ------------------------------------------------------------------

    /// <summary>
    /// Ends any lockouts that have run out, lets monsters whose hourglass ran out attack (Dungeon
    /// format), and moves to the next phase when the current one's time is up.
    /// </summary>
    public IReadOnlyList<MatchEvent> Tick(DateTimeOffset now)
    {
        if (IsFinished)
            return NothingHappened;

        var events = new List<MatchEvent>();

        foreach (var stream in _streams.Values.Where(s => s.LockedOutUntil != null && now >= s.LockedOutUntil))
        {
            stream.EndLockout();
            Version++;
            events.Add(new LockoutEnded(stream.OwnerId));

            if (_dungeon != null && Phase == MatchPhase.Playing && _dungeon.ButtonsWorkAgain(stream.OwnerId, now))
                events.Add(new AdventurerGotUp(stream.OwnerId));
        }

        HoldTheClock(now);

        if (_dungeon != null && Phase == MatchPhase.Playing && now < PhaseEndsAt)
        {
            // A battle beat ran out since the last tick: that owner's next question is up.
            foreach (var stream in _streams.Values.Where(s => s.CurrentShownAt > _lastTickAt && s.CurrentShownAt <= now))
                events.Add(new QuestionShown(stream.OwnerId));

            // A Warlock's patron shows the answer as the hourglass runs low (before the monster can attack).
            foreach (var warlock in _dungeon.RevealsDue(now))
            {
                Version++;
                events.Add(new PatronRevealed(warlock.OwnerId));
            }

            foreach (var adventurer in _dungeon.AttacksDue(now))
                events.AddRange(MonsterAttacks(adventurer.OwnerId, now));

            // A Healer's poison: damage every tick, and a kill like any other (it earns time on the clock).
            foreach (var (partyId, damage, kill) in _dungeon.TickPoison(now))
            {
                Version++;
                events.Add(new MonsterPoisoned(partyId, damage, kill));
                if (kill != null)
                {
                    AddTime(_dungeon.SecondsEarnedBy(partyId, kill));
                    _dungeon.PartyKilled(partyId, kill, killerId: null, now); // a kill beat for the party, as for any kill
                    events.AddRange(StartMinigames(partyId, kill, now));
                }
                if (kill?.FloorCleared == true)
                    events.AddRange(OfferRewards(partyId, now));
            }

            // Too long choosing a reward: one is picked for them.
            foreach (var chooser in _dungeon.RewardsDue(now))
                events.AddRange(ChooseReward(chooser.OwnerId, choice: null, now));

            // Mini games: lost when their time runs out; over once their result has been shown.
            _dungeon.MinigamesDue(now, out var lost, out var over);
            foreach (var loser in lost)
                events.Add(new MinigameLost(loser.OwnerId));
            foreach (var done in over)
            {
                Version++;
                events.Add(new MinigameOver(done.OwnerId));
                if (done.Offer is { } offer)
                    events.Add(new RewardOffered(done.OwnerId, offer)); // won: a reward to pick
            }
            if (lost.Count > 0)
                Version++;
        }

        // Hero photos: each second of the station's countdown, and the moment it's taken (0), where the camera is on.
        foreach (var player in Roster.Players.Where(p => p.PhotoAt != null && p.PhotoCameraOn && p.Photo == 0))
        {
            for (var left = (int)Settings.Dungeon.PhotoCountdownSeconds; left >= 0; left--)
            {
                var moment = player.PhotoAt!.Value - TimeSpan.FromSeconds(left);
                if (moment > _lastTickAt && moment <= now)
                    events.Add(new PhotoCountdown(player.StationId, left));
            }
        }
        _lastTickAt = now;

        // The clock running out ends Playing, and so does everyone being out of lives (DungeonSettings.Lives).
        var everyoneOut = Phase == MatchPhase.Playing && _dungeon?.EveryoneOut == true;
        if (now >= PhaseEndsAt || everyoneOut)
            events.AddRange(AdvanceToNextPhase(now));

        return events;
    }

    /// <summary>
    /// Handles a button press: answers that owner's current question, or (the action button, Dungeon
    /// format) uses their power-up. In the intro, the action button is for the hero photo (see
    /// <see cref="PhotoButton"/>). Other presses outside Playing are ignored.
    /// </summary>
    public IReadOnlyList<MatchEvent> HandlePress(ButtonPress press, DateTimeOffset now)
    {
        var player = Roster.FindPlayer(press.StationId);
        if (Phase == MatchPhase.Intro && player != null && press.IsAction)
            return PhotoButton(player, now);
        if (Phase != MatchPhase.Playing || player == null || press.PressedAt > PhaseEndsAt)
            return NothingHappened;

        var stream = StreamFor(player);
        if (_dungeon?.FindAdventurer(stream.OwnerId) is { Minigame: not null })
            return PlayMinigame(stream.OwnerId, press); // between two rooms: the buttons play the mini game (the action button too)
        if (press.IsAction)
            return UsePowerUp(stream.OwnerId, now);
        if (_dungeon?.FindAdventurer(stream.OwnerId) is { IsChoosing: true })
            return ChooseReward(stream.OwnerId, press.AnswerIndex, now); // after a boss: the answer buttons pick a reward

        var shownAt = stream.CurrentShownAt!.Value;

        if (stream.IsLockedOutAt(press.PressedAt))
            return NothingHappened;
        if (press.PressedAt < shownAt + TimeSpan.FromSeconds(Settings.NewQuestionGraceSeconds))
            return NothingHappened; // pressed before the new question could have been read
        if (_dungeon?.IsHidden(stream.OwnerId, stream.CurrentNumber, press.AnswerIndex) == true)
            return NothingHappened; // a 50/50 took that answer away

        var question = stream.CurrentQuestion;
        var isCorrect = press.AnswerIndex == question.CorrectIndex;
        var losesPoints = !isCorrect && Settings.WrongAnswerPenalty == WrongAnswerPenalty.LosePoints;

        // Dungeon format: a correct answer is an attack, and its points are what it did: all the damage
        // (a Wizard's splash included) plus any HP a Healer's answer healed.
        var dealt = isCorrect ? _dungeon?.Strike(stream.OwnerId, question.Difficulty, press.PressedAt - shownAt, now) : null;
        var correctPoints = dealt != null ? dealt.TotalDamage + dealt.Healed : Settings.CorrectPoints;

        var answer = new GivenAnswer(
            stream.OwnerId,
            stream.CurrentNumber,
            question,
            press.StationId,
            press.AnswerIndex,
            isCorrect,
            Points: isCorrect ? correctPoints : losesPoints ? -Settings.WrongAnswerPoints : 0,
            press.PressedAt,
            ResponseTime: press.PressedAt - shownAt);

        // Dungeon format: the next question waits while the screen shows the fight (a battle beat).
        stream.Record(answer, now + TimeSpan.FromSeconds(_dungeon?.BeatSecondsAfter(dealt) ?? 0), _dungeon?.NextDifficulty(stream.OwnerId));
        Mode.ScoreAnswer(answer, player, Roster);

        if (!isCorrect && Settings.WrongAnswerPenalty == WrongAnswerPenalty.Lockout)
            stream.LockOut(now + TimeSpan.FromSeconds(Settings.LockoutSeconds));

        Version++;
        List<MatchEvent> events = [new AnswerGiven(answer)];
        if (_dungeon == null)
            return events;

        if (dealt != null)
        {
            events.Add(new MonsterHit(stream.OwnerId, dealt));
            if (dealt.Found is { } found)
                events.Add(new PowerUpFound(stream.OwnerId, found));
            if (dealt.RevivedOwnerId is { } revived)
                events.Add(new AdventurerGotUp(revived)); // their teammates' right answers brought them back
            var partyId = _dungeon.FindAdventurer(stream.OwnerId)!.PartyId;
            AddTime(_dungeon.SecondsEarnedBy(partyId, dealt));
            if (dealt.Killed)
                _dungeon.PartyKilled(partyId, dealt, stream.OwnerId, now); // the teammates watch it: the same kill beat, together
            if (dealt.FloorCleared)
                events.AddRange(OfferRewards(partyId, now));
            events.AddRange(StartMinigames(partyId, dealt, now));
        }
        else
        {
            events.Add(new AdventurerHit(stream.OwnerId, _dungeon.TakeHit(stream.OwnerId, HitReason.WrongAnswer, now, answer.ResponseTime)));
        }

        _dungeon.AfterTurn(stream, now);
        return events;
    }

    /// <summary>A kill emptied the room before the floor's mini game: everyone in the party starts their go at it.</summary>
    private IEnumerable<MatchEvent> StartMinigames(string partyId, DealtHit kill, DateTimeOffset now) =>
        _dungeon!.StartMinigames(partyId, kill, now).Select(a => new MinigameStarted(a.OwnerId, a.Minigame!.Kind)).ToList();

    /// <summary>An answer button in a mini game, timed by when it was pressed (the needle's where it was then).</summary>
    private IReadOnlyList<MatchEvent> PlayMinigame(string ownerId, ButtonPress press)
    {
        var result = _dungeon!.PlayMinigame(ownerId, press.AnswerIndex, press.PressedAt);
        if (result == MinigamePress.Ignored)
            return NothingHappened;
        Version++;
        var showsIn = result is MinigamePress.Won or MinigamePress.Cleared ? _dungeon.FindAdventurer(ownerId)?.Minigame?.WinPauseSeconds ?? 0 : 0;
        return [new MinigamePlayed(ownerId, result, showsIn)];
    }

    /// <summary>
    /// Arcade clock time spent with the clock stopped so far (mini games, reward picks), so screens can tell time
    /// earned from the clock being held: see <see cref="HoldTheClock"/>.
    /// </summary>
    public TimeSpan ClockHeldFor { get; private set; }

    /// <summary>While the clock is stopped: the time it shows (what was left when it stopped). Null while it runs.</summary>
    public TimeSpan? ClockStoppedAt { get; private set; }

    /// <summary>
    /// While anyone is away from the fight (a mini game, a reward to pick: <see cref="DungeonRun.ClockStopped"/>) the arcade
    /// clock stands still: every tick pushes the end of Playing back by the time since the last one. Everything that
    /// checks the clock keeps working as normal, and screens show the time it stopped at.
    /// </summary>
    private void HoldTheClock(DateTimeOffset now)
    {
        var stopped = Phase == MatchPhase.Playing && _dungeon?.ClockStopped == true;
        if (stopped)
        {
            var held = now - _lastTickAt;
            PhaseEndsAt += held;
            ClockHeldFor += held;
        }
        if (stopped == (ClockStoppedAt != null))
            return;
        ClockStoppedAt = stopped ? PhaseEndsAt - now : null;
        Version++;
    }

    /// <summary>A party cleared a floor: whoever in it answered enough of the floor right is offered rewards.</summary>
    private IEnumerable<MatchEvent> OfferRewards(string partyId, DateTimeOffset now) =>
        _dungeon!.OpenRewards(partyId, now).Select(a => new RewardOffered(a.OwnerId, a.Offer!)).ToList();

    /// <summary>
    /// An answer button picks a reward (<paramref name="choice"/>, 0-based), or time ran out (null: one is picked for
    /// them). A Time reward, and the time spent choosing, go on the clock.
    /// </summary>
    private IReadOnlyList<MatchEvent> ChooseReward(string ownerId, int? choice, DateTimeOffset now)
    {
        if (_dungeon!.ChooseReward(ownerId, choice, now) is not { } pick)
            return NothingHappened;

        AddTime(pick.ClockSeconds);
        Version++;
        return [new RewardChosen(ownerId, pick.Taken)];
    }

    /// <summary>The action button: uses the power-up the owner holds, if any (a Time Crystal adds to the clock).</summary>
    private IReadOnlyList<MatchEvent> UsePowerUp(string ownerId, DateTimeOffset now)
    {
        if (_dungeon?.UsePowerUp(ownerId, now) is not { } use)
            return NothingHappened;

        if (use.PowerUp == PowerUp.TimeCrystal)
            AddTime(use.Amount);

        Version++;
        List<MatchEvent> events = [new PowerUpUsed(ownerId, use)];
        if (use.Revived)
            events.Add(new AdventurerGotUp(use.TargetOwnerId));
        return events;
    }

    /// <summary>
    /// Dev tools: hands the player at a station a power-up (replacing any they held). Does nothing
    /// outside Playing, outside Dungeon format, or for a station not in this match.
    /// </summary>
    public IReadOnlyList<MatchEvent> GivePowerUp(string stationId, PowerUp powerUp)
    {
        var player = Roster.FindPlayer(stationId);
        if (_dungeon == null || Phase != MatchPhase.Playing || player == null)
            return NothingHappened;

        var ownerId = Mode.GetAnswerOwnerId(player);
        _dungeon.GivePowerUp(ownerId, powerUp);
        Version++;
        return [new PowerUpFound(ownerId, powerUp)];
    }

    /// <summary>
    /// Dev tools: hands the player at a station a floor reward, as if they'd picked it (a Time reward adds to the
    /// clock). Does nothing outside Playing, outside Dungeon format, or for a station not in this match.
    /// </summary>
    public IReadOnlyList<MatchEvent> GiveReward(string stationId, Reward reward, DateTimeOffset now)
    {
        var player = Roster.FindPlayer(stationId);
        if (_dungeon == null || Phase != MatchPhase.Playing || player == null)
            return NothingHappened;

        var ownerId = Mode.GetAnswerOwnerId(player);
        var (taken, seconds) = _dungeon.GiveReward(ownerId, reward, now);
        AddTime(seconds);
        Version++;
        return [new RewardChosen(ownerId, taken)];
    }

    /// <summary>With hero photos on by default, a moment to see yourself on camera before the countdown starts.</summary>
    public static readonly TimeSpan PhotoLead = TimeSpan.FromSeconds(1);

    /// <summary>Hero photos: a Dungeon game, and the venue allows them (<see cref="DungeonSettings.HeroPhotos"/>).</summary>
    public bool OffersPhotos => _dungeon != null && Settings.Dungeon.HeroPhotos;

    /// <summary>
    /// When photos are asked for first (<see cref="DungeonSettings.PhotoByDefault"/> off): the intro takes a yes
    /// until this time, the last moment the whole countdown still fits in it, so the photo never covers the
    /// first question. Null when there's no such offer.
    /// </summary>
    public DateTimeOffset? PhotoOfferEndsAt =>
        OffersPhotos && !Settings.Dungeon.PhotoByDefault && Phase == MatchPhase.Intro
            ? PhaseEndsAt - TimeSpan.FromSeconds(Settings.Dungeon.PhotoCountdownSeconds)
            : null;

    /// <summary>
    /// The action button in the intro. With a photo coming (or already taken) it says no: none this match,
    /// and any taken is dropped. Otherwise (photos asked for first) it says yes, while the countdown still fits.
    /// </summary>
    private IReadOnlyList<MatchEvent> PhotoButton(Player player, DateTimeOffset now)
    {
        // Settled in the lobby: a "ready" pressed twice must not take off the photo they just kept.
        if (!OffersPhotos || player.PhotoDeclined || player.PhotoFromLobby)
            return NothingHappened;

        if (player.PhotoAt != null || player.Photo > 0)
        {
            player.DeclinePhoto();
            Version++;
            return [new PhotoDeclined(player.StationId)];
        }

        if (PhotoOfferEndsAt is not { } offerEndsAt || now > offerEndsAt)
            return NothingHappened;
        // They pressed to say yes, so they're at a station with a camera: the countdown can be heard straight away.
        player.SchedulePhoto(now + TimeSpan.FromSeconds(Settings.Dungeon.PhotoCountdownSeconds), cameraOn: true);
        Version++;
        return [new PhotoAsked(player.StationId)];
    }

    /// <summary>
    /// The station's camera came on for a photo that's coming, so its countdown can be heard
    /// (<see cref="PhotoCountdown"/>). False if no photo is coming there.
    /// </summary>
    public bool PhotoCameraOn(string stationId)
    {
        var player = Roster.FindPlayer(stationId);
        if (player?.PhotoAt == null || IsFinished)
            return false;
        player.CameraOn();
        return true;
    }

    /// <summary>
    /// The station sent the hero photo it took (the server keeps the picture; the match only counts it, so
    /// screens know to fetch it). Only for a player who asked for one, once, and not once the match has finished.
    /// </summary>
    public IReadOnlyList<MatchEvent> PhotoTaken(string stationId)
    {
        var player = Roster.FindPlayer(stationId);
        if (player?.PhotoAt == null || player.Photo > 0 || IsFinished)
            return NothingHappened;

        player.PhotoArrived();
        Version++;
        return [new PhotoTaken(stationId)];
    }

    /// <summary>
    /// The arcade clock: pushes the end of Playing back. There's no cap: as long as they keep earning time,
    /// they keep playing.
    /// </summary>
    private void AddTime(double seconds)
    {
        if (EarnsTime && seconds > 0)
            PhaseEndsAt += TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// A continue: a card swipe at any station in the match (or the operator giving one), paying for a second
    /// session. While the match waits on <see cref="MatchPhase.Continue"/>, everyone goes back to Playing on a
    /// fresh <see cref="DungeonSettings.ContinueSeconds"/> clock, on the same floor with the same HP. Ignored at
    /// any other time.
    /// </summary>
    public IReadOnlyList<MatchEvent> Continue(DateTimeOffset now)
    {
        if (Phase != MatchPhase.Continue)
            return NothingHappened;

        ContinuesUsed++;
        EnterPhase(MatchPhase.Playing, now, Settings.Dungeon.ContinueSeconds);
        _dungeon!.RestoreLives(); // a new paid session: everyone's lives back
        foreach (var stream in _streams.Values)
        {
            stream.Start(now); // the question they were on shows again, with a fresh hourglass
            _dungeon!.Resume(stream, now);
        }
        return [new PhaseChanged(Phase)];
    }

    /// <summary>Dungeon games, when the clock has run out: continues are on. Each is a paid session of its own, so there's no limit.</summary>
    private bool CanContinue => EarnsTime && Settings.Dungeon.ContinueSeconds > 0;

    /// <summary>An owner's hourglass ran out: the monster attacks, and the question it was guarding is lost.</summary>
    private IReadOnlyList<MatchEvent> MonsterAttacks(string ownerId, DateTimeOffset now)
    {
        var stream = _streams[ownerId];
        var lost = new GivenAnswer(
            stream.OwnerId,
            stream.CurrentNumber,
            stream.CurrentQuestion,
            PressedByStationId: "",
            AnswerIndex: -1,
            IsCorrect: false,
            Points: 0,
            now,
            ResponseTime: now - stream.CurrentShownAt!.Value,
            TimedOut: true);
        stream.Record(lost, now + TimeSpan.FromSeconds(_dungeon!.BeatSecondsAfter(dealt: null)), _dungeon.NextDifficulty(ownerId));

        var hit = _dungeon.TakeHit(ownerId, HitReason.TooSlow, now);
        _dungeon.AfterTurn(stream, now);
        Version++;
        return [new AnswerGiven(lost), new AdventurerHit(ownerId, hit)];
    }

    /// <summary>Ends the match immediately (operator abort from the admin page).</summary>
    public IReadOnlyList<MatchEvent> Abort(DateTimeOffset now)
    {
        if (IsFinished)
            return NothingHappened;

        WasAborted = true;
        EnterPhase(MatchPhase.Finished, now, seconds: null);
        return [new PhaseChanged(MatchPhase.Finished), new MatchFinished(WasAborted: true)];
    }

    // ------------------------------------------------------------------
    // Phase flow (see MatchPhase for the diagram)
    // ------------------------------------------------------------------

    private IReadOnlyList<MatchEvent> AdvanceToNextPhase(DateTimeOffset now)
    {
        switch (Phase)
        {
            case MatchPhase.Intro:
                foreach (var stream in _streams.Values)
                    stream.Start(now);
                _dungeon?.Start(now);
                EnterPhase(MatchPhase.Playing, now, RoundSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Playing:
                _dungeon?.Stop();
                if (CanContinue)
                {
                    EnterPhase(MatchPhase.Continue, now, Settings.Dungeon.ContinueCountdownSeconds);
                    return [new PhaseChanged(Phase)];
                }
                EnterPhase(MatchPhase.Results, now, Settings.ResultsSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Continue: // nobody swiped in time
                EnterPhase(MatchPhase.Results, now, Settings.ResultsSeconds);
                return [new PhaseChanged(Phase)];

            case MatchPhase.Results:
                EnterPhase(MatchPhase.Finished, now, seconds: null);
                return [new PhaseChanged(Phase), new MatchFinished(WasAborted: false)];

            default:
                return NothingHappened;
        }
    }

    /// <param name="seconds">How long the phase lasts; null = no time limit.</param>
    private void EnterPhase(MatchPhase phase, DateTimeOffset now, double? seconds)
    {
        Phase = phase;
        PhaseStartedAt = now;
        PhaseEndsAt = seconds == null ? DateTimeOffset.MaxValue : now + TimeSpan.FromSeconds(seconds.Value);
        Version++;
    }
}
