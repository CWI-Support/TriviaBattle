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

        return new MatchSnapshot(
            match.Version,
            match.MatchId,
            match.Settings.Format.ToString(),
            match.Phase,
            match.PhaseStartedAt.ToUnixTimeMilliseconds(),
            ToUnixMs(match.PhaseEndsAt),
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
            match.ClockStoppedAt is { } left ? (long)left.TotalMilliseconds : null,
            (long)match.ClockHeldFor.TotalMilliseconds);
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
            match.Phase == MatchPhase.Playing ? ToMinigame(adventurer?.Minigame) : null);
    }

    /// <summary>Dungeon format: the mini game they're playing, as the station draws it. Null when they aren't in one.</summary>
    private static MinigameView? ToMinigame(Dungeon.Minigames.Minigame? game)
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
                c.Swaps.Select(s => new[] { s.A, s.B }).ToList(), c.PickFrom.ToUnixTimeMilliseconds(), c.FinalPlaces, c.Picks, c.LastPick, c.LastWasRight, c.FoundAt)
            : null;
        return new MinigameView(game.Kind.ToString(), game.IntroAt.ToUnixTimeMilliseconds(), game.PlaysFrom.ToUnixTimeMilliseconds(),
            game.EndsAt.ToUnixTimeMilliseconds(), ToUnixMs(game.FinishedAt), game.Won, ToUnixMs(game.ResultEndsAt), runeLock, runeEcho, chestShuffle);
    }

    private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);


    /// <summary>Dungeon format: the rewards they're choosing between after a floor's boss, each with the number its card shows. Null when they aren't.</summary>
    private static RewardOfferView? ToRewardOffer(MatchEngine match, Dungeon.Adventurer? adventurer)
    {
        if (adventurer?.Offer is not { } offer || match.Phase != MatchPhase.Playing)
            return null;

        var trial = adventurer.LastTrial;
        return new RewardOfferView(
            offer.Floor,
            offer.Choices.Select(r => new RewardChoiceView(r, match.Dungeon!.RewardAmount(adventurer, r))).ToList(),
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

        // The party's poison killed the monster since they last answered (and since any reward): its kill beat. The
        // hit is the party's (the delve's last hit), not one of theirs.
        if (adventurer.PoisonKillAt is { } poisonAt && poisonAt >= answeredAt && shownAt > poisonAt && !(reward?.At > poisonAt))
        {
            var kill = match.Dungeon!.DelveOf(adventurer).LastHit;
            var killKind = kill is not { Poison: true } ? "kill" : kill.BossAhead ? "boss" : kill.FloorCleared ? "floor" : "kill";
            return new BeatView(killKind, poisonAt.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds(), ByPoison: true);
        }

        if (stream.LastAnswer is not { } answer || shownAt <= answer.PressedAt)
            return null;

        // A reward just picked: a short beat showing what it gives, then their question comes back.
        if (reward != null && reward.At >= answer.PressedAt && shownAt > reward.At)
            return new BeatView("reward", reward.At.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds());

        var kind = !answer.IsCorrect ? (adventurer.LastTaken?.Downed == true ? "downed" : "hurt")
            : adventurer.LastDealt is not { } hit ? "hit"
            : hit.BossAhead ? "boss" // the room it emptied leads to a boss's: its entrance
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
    /// While a player is downed in a Dungeon party: a standing teammate's question, so they can shout the
    /// answer to them (their own buttons are dead). Null otherwise.
    /// </summary>
    private static HelpView? ToHelping(MatchEngine match, QuestionStream stream)
    {
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        if (match.Phase != MatchPhase.Playing || adventurer is not { IsDowned: true })
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
            adventurer.Rewards.Select(r => new RewardTakenView(r.Number, r.Reward, r.Amount, r.Floor, r.At.ToUnixTimeMilliseconds(), r.Picked)).ToList(),
            adventurer.LastStands,
            adventurer.Minigame?.Kind.ToString(),
            adventurer.MinigamesWon,
            adventurer.Lives,
            adventurer.MaxLives,
            adventurer.IsOut);
    }

    private static MonsterView ToMonster(Dungeon.Foe foe) =>
        new(foe.Monster.Name, foe.Monster.Sprite, foe.Hp, foe.MaxHp, foe.Monster.IsBoss, foe.PoisonStacks);

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
