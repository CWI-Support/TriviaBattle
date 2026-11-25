using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Snapshots;

/// <summary>
/// Turns a <see cref="MatchEngine"/> into a <see cref="MatchSnapshot"/> for one viewer.
///
/// Visibility rules (all in one place on purpose):
/// <list type="bullet">
///   <item>A player screen sees only its own current question (its team's in 2v2 Group), and only while Playing.
///     The one exception: while downed in a Dungeon party, it also sees a standing teammate's question, so
///     the player can shout the answer to them.</item>
///   <item>Nobody but Admin is ever told the correct answer to a question that's still on screen. The one
///     exception: a Dungeon Warlock's own screen, once their patron has shown it (<see cref="QuestionView.Revealed"/>).</item>
///   <item>A Dungeon Ranger's own screen gets its next question a moment early, during the battle beat
///     (<see cref="QuestionView.PeekFromMs"/>); it's their own question, and presses still only count once it's up.</item>
///   <item>After answering, a player screen sees that answer's correct answer (the question is behind them).</item>
///   <item>Main screen and others see scores and right/wrong, never which answer anyone pressed.</item>
///   <item>Admin sees everything, always.</item>
/// </list>
/// </summary>
public static class SnapshotBuilder
{
    public static MatchSnapshot Build(MatchEngine match, Viewer viewer)
    {
        var players = match.Roster.Players.Select(p => BuildPlayer(match, p)).ToList();
        var teams = match.Roster.Teams.Select(t => BuildTeam(match, t)).ToList();

        // A race or a team battle has a clock per side: a station's match clock is its own side's (the screens read the
        // same fields either way). Room screens get the match's, which runs until the last side's done.
        var viewerPlayer = viewer.Role == ViewerRole.Player ? match.Roster.FindPlayer(viewer.StationId!) : null;
        var sideClock = viewerPlayer != null ? match.ClockOf(match.StreamFor(viewerPlayer).OwnerId) : null;
        var clockEndsAt = sideClock != null && match.Phase == MatchPhase.Playing ? sideClock.EndsAt : match.PhaseEndsAt;
        var clockStoppedLeft = sideClock != null ? sideClock.StoppedLeft : match.ClockStoppedAt;
        var clockHeldFor = sideClock?.HeldFor ?? match.ClockHeldFor;

        return new MatchSnapshot(
            match.Version,
            match.MatchId,
            match.Settings.Format.ToString(),
            match.Phase,
            match.PhaseStartedAt.ToUnixTimeMilliseconds(),
            ToUnixMs(clockEndsAt),
            match.Mode.Kind.ToString(),
            match.Mode.DisplayName,
            match.Mode.UsesTeams,
            match.CategoryName,
            match.Setup.Difficulty.ToString(),
            match.RoundSeconds,
            match.EarnsTime,
            match.EarnsTime ? match.Settings.Dungeon.ContinueSeconds : 0,
            match.ContinuesUsed,
            players,
            teams,
            match.GetStandings(),
            viewer.Role == ViewerRole.Player ? BuildYou(match, viewer.StationId!) : null,
            viewer.Role == ViewerRole.Admin ? match.Streams.Select(BuildStream).ToList() : null,
            clockStoppedLeft is { } left ? (long)left.TotalMilliseconds : null,
            (long)clockHeldFor.TotalMilliseconds);
    }

    private static PlayerView BuildPlayer(MatchEngine match, Player player)
    {
        var stream = match.StreamFor(player);
        var ownsStream = stream.OwnerId == player.StationId; // false in 2v2 Group: the team owns it

        return new PlayerView(
            player.StationId,
            player.Name,
            player.TeamId,
            player.Tally.Score,
            player.Tally.CorrectAnswers,
            player.Tally.WrongAnswers,
            ownsStream ? ToOutcome(stream.LastAnswer) : null,
            ToUnixMs(stream.LockedOutUntil),
            ownsStream ? ToDelve(match, stream.OwnerId) : null,
            player.Photo,
            player.Hero);
    }

    private static TeamView BuildTeam(MatchEngine match, Team team)
    {
        var stream = match.FindStream(team.Id); // only 2v2 Group has team-owned streams

        return new TeamView(
            team.Id,
            team.Name,
            team.StationIds,
            team.Tally.Score,
            team.Tally.CorrectAnswers,
            team.Tally.WrongAnswers,
            ToOutcome(stream?.LastAnswer),
            stream != null ? ToDelve(match, stream.OwnerId) : ToPartyDelve(match, team.Id));
    }

    private static YouView? BuildYou(MatchEngine match, string stationId)
    {
        var player = match.Roster.FindPlayer(stationId);
        if (player == null)
            return null; // this station isn't playing in this match

        var team = match.Roster.TeamOf(player);
        var stream = match.StreamFor(player);
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        // Away from the fight (choosing a reward, playing a mini game): the answer buttons do that, so no question is up.
        var question = match.Phase == MatchPhase.Playing && adventurer is not { IsAway: true } ? stream.CurrentQuestion : null;

        return new YouView(
            player.StationId,
            player.Name,
            team?.Id,
            team?.Name,
            player.Tally.Score,
            team?.Tally.Score,
            question == null ? null : ToQuestion(match, stream, ownScreen: true),
            ToUnixMs(stream.LockedOutUntil),
            ToYourAnswer(match, stream.LastAnswer),
            ToDelve(match, stream.OwnerId),
            ToHelping(match, stream),
            ToBeat(match, stream),
            player.PhotoAt == null && !player.PhotoDeclined && !player.PhotoFromLobby ? ToUnixMs(match.PhotoOfferEndsAt) : null,
            ToUnixMs(player.PhotoAt),
            ToRewardOffer(match, adventurer),
            match.Phase == MatchPhase.Playing ? ToMinigame(match, adventurer?.Minigame, adventurer) : null,
            adventurer?.CalledFriend is { } friend ? NameOf(match, friend) : null,
            adventurer?.LastQuip is { } quip
                ? new QuipView(quip.Number, quip.Moment.ToString(), quip.Text, quip.At.ToUnixTimeMilliseconds(), quip.AboutOwnerId == stream.OwnerId)
                : null,
            match.Phase == MatchPhase.Playing && adventurer?.Fork is { } fork ? ToFork(match, fork, stream.OwnerId) : null);
    }

    /// <summary>
    /// Dungeon format: the fork they're at before a boss, as the station draws it: the doors, everyone's votes, and once it's
    /// decided the door taken and the room behind it (only then: what's behind the doors is a secret until one opens).
    /// </summary>
    private static ForkView ToFork(MatchEngine match, Dungeon.Fork fork, string ownerId) => new(
        fork.Floor,
        fork.Doors,
        fork.OpensAt.ToUnixTimeMilliseconds(),
        fork.VotesFrom.ToUnixTimeMilliseconds(),
        fork.EndsAt.ToUnixTimeMilliseconds(),
        fork.Voters.Select(id => new ForkVoterView(id, NameOf(match, id), StationOf(match, id), fork.VoteOf(id))).ToList(),
        fork.VoteOf(ownerId),
        fork.Taken,
        ToUnixMs(fork.TakenAt),
        ToUnixMs(fork.RoomFrom),
        fork.Room?.ToString());

    /// <summary>
    /// Dungeon format: the mini game they're playing, as the station draws it. Null when they aren't in one. The hot
    /// potato's fuse is a secret until it goes off: its time limit says the longest it could be until then. A special
    /// room's questions go without their right answers until they've been answered.
    /// </summary>
    private static MinigameView? ToMinigame(MatchEngine match, Dungeon.Minigames.Minigame? game, Dungeon.Adventurer? adventurer)
    {
        if (game == null)
            return null;
        var runeLock = game is Dungeon.Minigames.RuneLock s
            ? new RuneLockView(s.Hits, s.HitsNeeded, ToUnixMs(s.LockedUntil), s.TurnStartedAt.ToUnixTimeMilliseconds(), s.TurnAngle, s.DegreesPerSecond,
                s.MarkAngle, s.MarkDegrees, s.Presses, s.LastWasHit)
            : null;
        var runeEcho = game is Dungeon.Minigames.RuneEcho e
            ? new RuneEchoView(e.Round, e.Rounds, e.Sequence, e.ShowFrom.ToUnixTimeMilliseconds(), Ms(e.StepSeconds), Ms(e.LitSeconds),
                e.InputFrom.ToUnixTimeMilliseconds(), e.Entered, e.Presses, e.LastRune, e.LastWasRight)
            : null;
        var chestShuffle = game is Dungeon.Minigames.ChestShuffle c
            ? new ChestShuffleView(c.Round, c.Rounds, c.CoinChest, c.RoundStartsAt.ToUnixTimeMilliseconds(), Ms(c.RevealSeconds), Ms(c.SwapSeconds),
                c.Swaps.Select(s => new[] { s.A, s.B }).ToList(), c.PickFrom.ToUnixTimeMilliseconds(), c.FinalPlaces, c.Picks, c.LastPick, c.LastWasRight, c.FoundAt, ToUnixMs(c.LastPickAt))
            : null;
        var ringPuzzle = game is Dungeon.Minigames.RingPuzzle r
            ? new RingPuzzleView(r.Round, r.Rounds, r.Rings, r.Steps, r.Offsets.ToList(), r.Selected, r.RoundStartsAt.ToUnixTimeMilliseconds(),
                r.SolvedRings, r.Presses, ToUnixMs(r.WholeAt))
            : null;
        var potato = game is Dungeon.Minigames.HotPotato p ? ToHotPotato(match, p) : null;
        var endsAt = potato != null && game.FinishedAt == null ? game.PlaysFrom.ToUnixTimeMilliseconds() + potato.FuseLongestMs : game.EndsAt.ToUnixTimeMilliseconds();
        return new MinigameView(game.Kind.ToString(), game.IntroAt.ToUnixTimeMilliseconds(), game.PlaysFrom.ToUnixTimeMilliseconds(),
            endsAt, ToUnixMs(game.FinishedAt), game.Won, ToUnixMs(game.ResultEndsAt), runeLock, runeEcho, chestShuffle,
            ringPuzzle, ToUnixMs(game.ResultFrom),
            game is Dungeon.Minigames.Tuner t
                ? new TunerView(t.BarList.Select(b => new TunerBarView(b.Level, b.At.ToUnixTimeMilliseconds(), b.Rate, b.ZoneLow, b.ZoneHigh, b.InSince?.ToUnixTimeMilliseconds())).ToList(),
                    Ms(t.HoldSeconds), t.Presses)
                : null,
            game is Dungeon.Minigames.PopQuiz q ? ToPopQuiz(q) : null,
            potato,
            game is Dungeon.Minigames.TreasureRoom treasure ? ToTreasure(treasure) : null,
            game is Dungeon.Minigames.TrapRoom trap ? ToTrap(trap) : null,
            game is Dungeon.Minigames.HighStakesRoom bet ? ToHighStakes(bet, adventurer) : null);
    }

    /// <summary>The treasure room: the question up goes without its right answer.</summary>
    private static TreasureView ToTreasure(Dungeon.Minigames.TreasureRoom room)
    {
        var question = room.Current;
        return new TreasureView(room.RightNeeded, room.Right, room.Wrong, room.Answered + 1, question.Category, question.Text, question.Answers,
            ToUnixMs(room.LockedUntil), room.Last?.WasRight, room.Last?.At.ToUnixTimeMilliseconds(), room.Prize, room.Healed);
    }

    /// <summary>The trap room: the statement up goes without whether it's true; the last one answered goes with it.</summary>
    private static TrapView ToTrap(Dungeon.Minigames.TrapRoom trap)
    {
        var up = trap.Current;
        var last = trap.Last is { } a
            ? new TrapAnswerView(a.Statement + 1, trap.Statements[a.Statement].Question.Text, trap.Statements[a.Statement].ShownAnswer,
                trap.Statements[a.Statement].Question.Answers[trap.Statements[a.Statement].Question.CorrectIndex], trap.Statements[a.Statement].IsTrue,
                a.Said, a.WasRight, a.At.ToUnixTimeMilliseconds(), a.WasRight ? 0 : trap.LastDamage)
            : null;
        return new TrapView(trap.Statements.Count, trap.Right, trap.Misses, trap.Asking + 1, up.Question.Category, up.Question.Text,
            up.ShownAnswer, trap.AskedAt.ToUnixTimeMilliseconds(), trap.AnswerBy.ToUnixTimeMilliseconds(), last, trap.Damage, trap.Healed);
    }

    /// <summary>High Stakes: its question only once they've bet, and its right answer only once it's been answered.</summary>
    private static HighStakesView ToHighStakes(Dungeon.Minigames.HighStakesRoom room, Dungeon.Adventurer? adventurer)
    {
        var bet = room.BetAt != null;
        var question = room.Question;
        return new HighStakesView(
            room.DecideBy.ToUnixTimeMilliseconds(),
            adventurer != null ? (int)Math.Round(adventurer.MaxHp * room.PenaltyShare) : 0,
            room.Passed,
            ToUnixMs(room.BetAt),
            ToUnixMs(room.AskedAt),
            ToUnixMs(room.AnswerBy),
            bet ? question.Category : null,
            bet ? question.Text : null,
            bet ? question.Answers : null,
            room.Answer?.Chosen,
            room.Answer != null ? question.CorrectIndex : null,
            room.Answer?.At.ToUnixTimeMilliseconds(),
            room.Penalty);
    }

    /// <summary>The hot potato, from one seat: everyone round the table, the bomb, and (once it's gone off) the blast.</summary>
    private static HotPotatoView ToHotPotato(MatchEngine match, Dungeon.Minigames.HotPotato seat)
    {
        var bomb = seat.Bomb;
        var rules = match.Settings.Dungeon.Minigames.HotPotato;
        return new HotPotatoView(
            bomb.Seats.Select(ownerId => new HotPotatoSeatView(ownerId, NameOf(match, ownerId), StationOf(match, ownerId))).ToList(),
            seat.Seat,
            bomb.Holder,
            bomb.CaughtAt.ToUnixTimeMilliseconds(),
            bomb.ThrowFrom.ToUnixTimeMilliseconds(),
            bomb.Sequence,
            bomb.Entered,
            bomb.Passes,
            bomb.Fumbles,
            ToUnixMs(bomb.FumbledUntil),
            Ms(rules.FuseShortest),
            Ms(Math.Max(rules.FuseShortest, rules.FuseLongest)),
            bomb.Blast?.OwnerId,
            bomb.Blast?.Damage ?? 0);
    }

    /// <summary>The station an answer owner plays at (a 2v2 Group team: its first player's).</summary>
    private static string StationOf(MatchEngine match, string ownerId) =>
        match.Roster.Players.FirstOrDefault(p => match.Mode.GetAnswerOwnerId(p) == ownerId)?.StationId ?? ownerId;

    /// <summary>The pop quiz. The question up goes without its right answer; the last one answered goes with it.</summary>
    private static PopQuizView ToPopQuiz(Dungeon.Minigames.PopQuiz quiz)
    {
        var answered = quiz.Right + quiz.Wrong;
        var last = quiz.Last is { } a
            ? new PopQuizAnswerView(answered, quiz.Questions[a.Question].Topic, quiz.Questions[a.Question].Answers, a.Chosen,
                quiz.Questions[a.Question].CorrectIndex, a.WasRight, a.At.ToUnixTimeMilliseconds())
            : null;
        return new PopQuizView(quiz.RightNeeded, quiz.Strikes, quiz.Right, quiz.Wrong, quiz.FinishedAt != null ? answered : answered + 1,
            quiz.Current.Topic, quiz.Current.Answers, quiz.AskedAt.ToUnixTimeMilliseconds(), last);
    }

    private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);


    /// <summary>A reward they picked; the newest also carries the offer it was picked from (the moment after a pick shows it).</summary>
    private static RewardTakenView ToRewardTaken(MatchEngine match, Dungeon.Adventurer adventurer, Dungeon.RewardTaken taken)
    {
        var offer = taken == adventurer.LastReward && taken.Offer is { } from ? ToRewardOffer(match, adventurer, from, taken) : null;
        return new RewardTakenView(taken.Number, taken.Reward, taken.Amount, taken.Floor, taken.At.ToUnixTimeMilliseconds(), taken.Picked,
            offer, offer != null ? taken.Choice : null);
    }

    /// <summary>Dungeon format: the rewards they're choosing between after a floor's boss, each with the number its card shows. Null when they aren't.</summary>
    private static RewardOfferView? ToRewardOffer(MatchEngine match, Dungeon.Adventurer? adventurer) =>
        adventurer?.Offer is { } offer && match.Phase == MatchPhase.Playing ? ToRewardOffer(match, adventurer, offer) : null;

    /// <summary>An offer's cards (<paramref name="taken"/>: picked from it already, so that card shows what it gave).</summary>
    private static RewardOfferView ToRewardOffer(MatchEngine match, Dungeon.Adventurer adventurer, Dungeon.RewardOffer offer, Dungeon.RewardTaken? taken = null)
    {
        var trial = adventurer.LastTrial;
        return new RewardOfferView(
            offer.Floor,
            offer.Choices.Select((r, i) => new RewardChoiceView(r, i == taken?.Choice ? taken.Amount : match.Dungeon!.RewardAmount(adventurer, r))).ToList(),
            offer.OpensAt.ToUnixTimeMilliseconds(),
            (offer.OpensAt + TimeSpan.FromSeconds(match.Settings.Dungeon.Rewards.PickDelaySeconds)).ToUnixTimeMilliseconds(),
            offer.EndsAt.ToUnixTimeMilliseconds(),
            trial?.Right ?? 0,
            trial?.Answered ?? 0,
            offer.ForMinigame);
    }

    /// <summary>
    /// Dungeon format: the battle beat after their last answer, while the next question waits. Null when
    /// there is none (Classic, beats turned off, or no answer yet).
    /// </summary>
    private static BeatView? ToBeat(MatchEngine match, QuestionStream stream)
    {
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        if (adventurer == null || stream.CurrentShownAt is not { } shownAt)
            return null;
        var answeredAt = stream.LastAnswer?.PressedAt ?? DateTimeOffset.MinValue;
        var reward = adventurer.LastReward;

        // After the room behind a fork's door: the boss's entrance on its own (the kill before the doors was a plain kill,
        // so it wasn't seen before them), until its first question. A reward picked after it has its own beat.
        if (adventurer.BossEntranceAt is { } entrance && entrance >= answeredAt && shownAt > entrance && !(reward?.At > entrance)
            && !(adventurer.WatchedKill?.At > entrance))
            return new BeatView("boss", entrance.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds(), Entrance: true);

        // A teammate's blow or the party's poison killed the monster since they last answered (and since any reward):
        // its kill beat, the same one the killer's screen plays. The hit is the killer's, carried on the beat.
        if (adventurer.WatchedKill is { } watched && watched.At >= answeredAt && shownAt > watched.At && !(reward?.At > watched.At))
        {
            var kill = watched.Hit;
            var killKind = kill.BossAhead && !kill.ForkAhead ? "boss" : kill.FloorCleared ? "floor" : "kill"; // with a fork first, the boss waits
            return new BeatView(killKind, watched.At.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds(),
                ByPoison: watched.ByOwnerId == null, Hit: kill, By: watched.ByOwnerId is { } by ? NameOf(match, by) : null);
        }

        // The opening, before their first question (the hero runs into the first room): the same for everyone.
        if (stream.LastAnswer == null && match.OpeningFrom is { } from && match.OpeningEndsAt is { } until && shownAt == until)
            return new BeatView("enter", from.ToUnixTimeMilliseconds(), until.ToUnixTimeMilliseconds());

        if (stream.LastAnswer is not { } answer || shownAt <= answer.PressedAt)
            return null;

        // A reward just picked: a short beat showing what it gives, then their question comes back.
        if (reward != null && reward.At >= answer.PressedAt && shownAt > reward.At)
            return new BeatView("reward", reward.At.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds());

        var kind = !answer.IsCorrect ? (adventurer.LastTaken?.Downed == true ? "downed" : "hurt")
            : adventurer.LastDealt is not { } hit ? "hit"
            : hit.BossAhead && !hit.ForkAhead ? "boss" // the room it emptied leads to a boss's: its entrance (with a fork first: after its room)
            : hit.FloorCleared ? "floor"
            : hit.Killed ? "kill"
            : "hit";
        return new BeatView(kind, answer.PressedAt.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// The question on a stream's screen now. Never includes which answer is correct; a 50/50's hidden
    /// answers are wrong ones, so sending those is fine. The one exception: on a Warlock's own screen, once
    /// their patron has shown it (<see cref="Dungeon.Adventurer.RevealedOnQuestion"/>). A Ranger's own screen
    /// also gets the time to start showing it, before it counts (<see cref="QuestionView.PeekFromMs"/>).
    /// </summary>
    /// <param name="ownScreen">The question is for the screen of whoever answers it (not a downed teammate helping).</param>
    private static QuestionView ToQuestion(MatchEngine match, QuestionStream stream, bool ownScreen = false)
    {
        var question = stream.CurrentQuestion;
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        var hidden = adventurer != null && adventurer.HiddenOnQuestion == stream.CurrentNumber ? adventurer.HiddenAnswers : null;
        var revealed = ownScreen && adventurer != null && adventurer.RevealedOnQuestion == stream.CurrentNumber ? question.CorrectIndex : (int?)null;
        return new QuestionView(
            stream.CurrentNumber,
            question.Id,
            question.Category,
            question.Text,
            question.Answers,
            question.MediaUrl,
            ToUnixMs(stream.CurrentShownAt),
            hidden,
            ownScreen ? ToUnixMs(PeekFrom(match, stream)) : null,
            revealed);
    }

    /// <summary>
    /// A Ranger's next question shows a moment before their battle beat ends (never before the beat starts).
    /// Null when there's no beat on, or they aren't a Ranger.
    /// </summary>
    private static DateTimeOffset? PeekFrom(MatchEngine match, QuestionStream stream)
    {
        var peek = match.Dungeon?.PeekSecondsFor(stream.OwnerId) ?? 0;
        if (peek <= 0 || stream.LastAnswer is not { } answer || stream.CurrentShownAt is not { } shownAt || shownAt <= answer.PressedAt)
            return null;
        var from = shownAt - TimeSpan.FromSeconds(peek);
        return from > answer.PressedAt ? from : answer.PressedAt;
    }

    /// <summary>
    /// A teammate's Phone a Friend call: their question, which this player's answer buttons answer. Or, while a player
    /// is downed in a Dungeon party: a standing teammate's question, so they can shout the
    /// answer to them (their own buttons are dead). Null otherwise.
    /// </summary>
    private static HelpView? ToHelping(MatchEngine match, QuestionStream stream)
    {
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        if (match.Phase != MatchPhase.Playing || adventurer == null)
            return null;

        // Phone a Friend: a teammate called them, so they see that question (their buttons answer it), and its hourglass.
        if (match.Dungeon!.CallerFor(stream.OwnerId) is { } callerId)
        {
            var caller = match.Dungeon.FindAdventurer(callerId)!;
            return new HelpView(NameOf(match, callerId), ToQuestion(match, match.FindStream(callerId)!), Phoned: true,
                ToUnixMs(caller.AttackStartedAt), ToUnixMs(caller.AttackAt));
        }

        if (!adventurer.IsDowned)
            return null;

        var teammate = match.Dungeon!.StandingTeammateOf(adventurer);
        return teammate == null ? null : new HelpView(NameOf(match, teammate.OwnerId), ToQuestion(match, match.FindStream(teammate.OwnerId)!));
    }

    /// <summary>A player's name, or a team's in 2v2 Group (where the team answers as one).</summary>
    private static string NameOf(MatchEngine match, string ownerId) =>
        match.Roster.FindPlayer(ownerId)?.Name ?? match.Roster.FindTeam(ownerId)?.Name ?? ownerId;

    private static StreamView BuildStream(QuestionStream stream)
    {
        var question = stream.CurrentQuestion;
        return new StreamView(
            stream.OwnerId,
            stream.CurrentNumber,
            question.Text,
            question.Answers[question.CorrectIndex],
            stream.Answers.Count,
            stream.Answers.Count(a => a.IsCorrect));
    }

    private static AnswerOutcomeView? ToOutcome(GivenAnswer? answer) =>
        answer == null ? null : new AnswerOutcomeView(answer.Number, answer.IsCorrect, answer.Points, answer.PressedByStationId, answer.TimedOut);

    /// <summary>An answer owner's fight: their party's delve, with their own HP and hourglass. Null in Classic.</summary>
    private static DelveView? ToDelve(MatchEngine match, string ownerId)
    {
        var adventurer = match.Dungeon?.FindAdventurer(ownerId);
        if (adventurer == null)
            return null;

        var delve = match.Dungeon!.DelveOf(adventurer);
        var encounter = delve.Encounter;
        var charge = match.Dungeon.BossChargeOf(ownerId);
        var sideClock = match.ClockOf(ownerId); // a race or a team battle: their side's own clock
        return new DelveView(
            encounter.Floor,
            encounter.Room,
            encounter.RoomsOnFloor,
            encounter.Depth.Id,
            encounter.Depth.Name,
            ToMonster(delve.Foes[0]),
            adventurer.Hp,
            adventurer.MaxHp,
            adventurer.Streak,
            delve.Kills,
            ToUnixMs(adventurer.AttackStartedAt),
            ToUnixMs(adventurer.AttackAt),
            ToUnixMs(adventurer.DownedUntil),
            delve.LastHit,
            adventurer.LastDealt,
            adventurer.LastTaken,
            delve.DepthPath()
                .Select(e => new PathStepView(e.Floor, e.Room, e.Monster.Name, e.Monster.Sprite, e.Monster.IsBoss, e.Slot))
                .ToList(),
            adventurer.ReviveProgress,
            match.Dungeon.ReviveAnswers,
            match.Dungeon.PartyOf(adventurer.PartyId)
                .Select(a => new PartyMemberView(a.OwnerId, NameOf(match, a.OwnerId), a.Hp, a.MaxHp, ToUnixMs(a.DownedUntil), a.ReviveProgress, a.Lives, a.IsOut))
                .ToList(),
            adventurer.Held,
            adventurer.FireballReady,
            adventurer.Shielded,
            ToUnixMs(adventurer.FrozenUntil), // the screen shows it frozen while that is still to come
            adventurer.LastPowerUp,
            delve.Foes.Select(ToMonster).ToList(),
            adventurer.Class.ToString().ToLowerInvariant(),
            charge.Step,
            charge.Steps,
            adventurer.Trial,
            adventurer.LastTrial,
            match.Settings.Dungeon.Rewards.Enabled ? match.Settings.Dungeon.Rewards.Accuracy : 0,
            adventurer.Rewards.Select(r => ToRewardTaken(match, adventurer, r)).ToList(),
            adventurer.LastStands,
            adventurer.Minigame?.Kind.ToString() ?? (adventurer.Fork != null ? "Fork" : null),
            adventurer.MinigamesWon,
            adventurer.Lives,
            adventurer.MaxLives,
            adventurer.IsOut,
            CountdownFrom(match, adventurer),
            ToUnixMs(adventurer.FogFrom),
            ToUnixMs(adventurer.FogUntil),
            adventurer.LastSabotage is { } s ? ToSabotage(match, s) : null,
            ToUnixMs(sideClock?.EndsAt),
            sideClock?.StoppedLeft is { } stoppedLeft ? (long)stoppedLeft.TotalMilliseconds : null,
            sideClock?.IsDone == true,
            adventurer.Mirrored,
            adventurer.DiceReady,
            ToUnixMs(adventurer.BossEntranceAt));
    }

    private static SabotageView ToSabotage(MatchEngine match, Dungeon.Sabotage s) =>
        new(s.Number, s.PowerUp, NameOf(match, s.FromOwnerId), s.Blocked, s.At.ToUnixTimeMilliseconds(), s.Reflected, s.Bounced);

    /// <summary>Where the hourglass's last seconds start (the station winds up for the attack from then), or null.</summary>
    private static long? CountdownFrom(MatchEngine match, Dungeon.Adventurer adventurer)
    {
        var seconds = match.Settings.Dungeon.HourglassCountdownSeconds;
        return adventurer.AttackAt is { } at && seconds > 0 ? (at - TimeSpan.FromSeconds(seconds)).ToUnixTimeMilliseconds() : null;
    }

    private static MonsterView ToMonster(Dungeon.Foe foe) =>
        new(foe.Monster.Name, foe.Monster.Sprite, foe.Hp, foe.MaxHp, foe.Monster.IsBoss, foe.PoisonStacks, foe.Affix);

    /// <summary>
    /// A whole party's fight, for its lane on the main screen when teammates are separate adventurers
    /// (2v2 Combined): the fight as the next one to be attacked sees it, with the party's HP added up and
    /// the party's hits. Each member is in <see cref="DelveView.Party"/>.
    /// </summary>
    private static DelveView? ToPartyDelve(MatchEngine match, string partyId)
    {
        var party = match.Dungeon?.PartyOf(partyId) ?? [];
        if (party.Count == 0)
            return null;

        var delve = match.Dungeon!.DelveOf(party[0]);
        var nextAttacked = party.Where(a => a.AttackAt != null).MinBy(a => a.AttackAt) ?? party[0];
        return ToDelve(match, nextAttacked.OwnerId)! with
        {
            Hp = party.Sum(a => a.Hp),
            MaxHp = party.Sum(a => a.MaxHp),
            Streak = 0,
            DownedUntilMs = null,
            ReviveProgress = 0,
            LastDealt = delve.LastHit,
            LastTaken = delve.LastTaken,
            // The newest rival's Fog Bomb or Tiny Hourglass that came at any of them (the room's banner shows it).
            LastSabotage = party.Where(a => a.LastSabotage != null).MaxBy(a => a.LastSabotage!.At) is { LastSabotage: { } s } ? ToSabotage(match, s) : null,
        };
    }

    private static YourAnswerView? ToYourAnswer(MatchEngine match, GivenAnswer? answer)
    {
        if (answer == null)
            return null;

        return new YourAnswerView(
            answer.Number,
            answer.AnswerIndex,
            answer.Question.CorrectIndex,
            answer.Question.Answers[answer.Question.CorrectIndex],
            answer.IsCorrect,
            answer.Points,
            answer.PressedByStationId,
            match.Roster.FindPlayer(answer.PressedByStationId)?.Name ?? "",
            answer.TimedOut);
    }

    private static long? ToUnixMs(DateTimeOffset? time) =>
        time == null || time == DateTimeOffset.MaxValue ? null : time.Value.ToUnixTimeMilliseconds();
}
