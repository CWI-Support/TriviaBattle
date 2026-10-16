using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Snapshots;

/// <summary>
/// Turns a <see cref="MatchEngine"/> into a <see cref="MatchSnapshot"/> for one viewer.
///
/// Visibility rules (all in one place on purpose):
/// <list type="bullet">
///   <item>A player screen sees only its own current question (its team's in 2v2 Group), and only while Playing.</item>
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
            match.Phase,
            match.PhaseStartedAt.ToUnixTimeMilliseconds(),
            ToUnixMs(match.PhaseEndsAt),
            match.Mode.Kind.ToString(),
            match.Mode.DisplayName,
            match.Mode.UsesTeams,
            match.CategoryName,
            match.Setup.Difficulty.ToString(),
            match.Settings.MatchSeconds,
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
            ToUnixMs(stream.LockedOutUntil));
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
            ToOutcome(stream?.LastAnswer));
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
            question == null ? null : new QuestionView(
                stream.CurrentNumber,
                question.Id,
                question.Category,
                question.Text,
                question.Answers,
                question.MediaUrl,
                ToUnixMs(stream.CurrentShownAt)),
            ToUnixMs(stream.LockedOutUntil),
            ToYourAnswer(match, stream.LastAnswer));
    }

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
        answer == null ? null : new AnswerOutcomeView(answer.Number, answer.IsCorrect, answer.Points, answer.PressedByStationId);

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
            match.Roster.FindPlayer(answer.PressedByStationId)?.Name ?? "");
    }

    private static long? ToUnixMs(DateTimeOffset? time) =>
        time == null || time == DateTimeOffset.MaxValue ? null : time.Value.ToUnixTimeMilliseconds();
}
