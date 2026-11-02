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
///   <item>Nobody but Admin is ever told the correct answer to a question that's still on screen.</item>
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
            match.HasSoloClock,
            match.HasSoloClock ? match.Settings.Dungeon.SoloContinueSeconds : 0,
            match.ContinuesUsed,
            players,
            teams,
            match.GetStandings(),
            viewer.Role == ViewerRole.Player ? BuildYou(match, viewer.StationId!) : null,
            viewer.Role == ViewerRole.Admin ? match.Streams.Select(BuildStream).ToList() : null);
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
        var question = match.Phase == MatchPhase.Playing ? stream.CurrentQuestion : null;

        return new YouView(
            player.StationId,
            player.Name,
            team?.Id,
            team?.Name,
            player.Tally.Score,
            team?.Tally.Score,
            question == null ? null : ToQuestion(match, stream),
            ToUnixMs(stream.LockedOutUntil),
            ToYourAnswer(match, stream.LastAnswer),
            ToDelve(match, stream.OwnerId),
            ToHelping(match, stream),
            ToBeat(match, stream),
            player.PhotoAt == null && !player.PhotoDeclined ? ToUnixMs(match.PhotoOfferEndsAt) : null,
            ToUnixMs(player.PhotoAt));
    }

    /// <summary>
    /// Dungeon format: the battle beat after their last answer, while the next question waits. Null when
    /// there is none (Classic, beats turned off, or no answer yet).
    /// </summary>
    private static BeatView? ToBeat(MatchEngine match, QuestionStream stream)
    {
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        if (adventurer == null || stream.LastAnswer is not { } answer || stream.CurrentShownAt is not { } shownAt || shownAt <= answer.PressedAt)
            return null;

        var kind = !answer.IsCorrect ? (adventurer.LastTaken?.Downed == true ? "downed" : "hurt")
            : adventurer.LastDealt is not { } hit ? "hit"
            : hit.FloorCleared ? (match.Dungeon!.DelveOf(adventurer).Encounter.Monster.IsBoss ? "boss" : "floor")
            : hit.Killed ? "kill"
            : "hit";
        return new BeatView(kind, answer.PressedAt.ToUnixTimeMilliseconds(), shownAt.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// The question on a stream's screen now. Never includes which answer is correct; a 50/50's hidden
    /// answers are wrong ones, so sending those is fine.
    /// </summary>
    private static QuestionView ToQuestion(MatchEngine match, QuestionStream stream)
    {
        var question = stream.CurrentQuestion;
        var adventurer = match.Dungeon?.FindAdventurer(stream.OwnerId);
        var hidden = adventurer != null && adventurer.HiddenOnQuestion == stream.CurrentNumber ? adventurer.HiddenAnswers : null;
        return new QuestionView(
            stream.CurrentNumber,
            question.Id,
            question.Category,
            question.Text,
            question.Answers,
            question.MediaUrl,
            ToUnixMs(stream.CurrentShownAt),
            hidden);
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
        return new DelveView(
            encounter.Floor,
            encounter.Room,
            encounter.RoomsOnFloor,
            encounter.Depth.Id,
            encounter.Depth.Name,
            new MonsterView(encounter.Monster.Name, encounter.Monster.Sprite, delve.MonsterHp, encounter.MaxHp, encounter.Monster.IsBoss),
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
                .Select(e => new PathStepView(e.Floor, e.Room, e.Monster.Name, e.Monster.Sprite, e.Monster.IsBoss))
                .ToList(),
            adventurer.ReviveProgress,
            match.Dungeon.ReviveAnswers,
            match.Dungeon.PartyOf(adventurer.PartyId)
                .Select(a => new PartyMemberView(a.OwnerId, NameOf(match, a.OwnerId), a.Hp, a.MaxHp, ToUnixMs(a.DownedUntil), a.ReviveProgress))
                .ToList(),
            adventurer.Held,
            adventurer.FireballReady,
            adventurer.Shielded,
            ToUnixMs(adventurer.FrozenUntil), // the screen shows it frozen while that is still to come
            adventurer.LastPowerUp);
    }

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
