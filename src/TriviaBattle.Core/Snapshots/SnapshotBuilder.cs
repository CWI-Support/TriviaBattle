using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Snapshots;

/// <summary>
/// Turns a <see cref="MatchEngine"/> into a <see cref="MatchSnapshot"/> for one viewer.
///
/// Visibility rules (all in one place on purpose):
/// <list type="bullet">
///   <item>The correct answer is hidden from everyone except Admin until Reveal.</item>
///   <item>Before Reveal, screens only see THAT someone answered, not WHAT they answered...</item>
///   <item>...except a player always sees the answer that counts for them: their own, or their team's in 2v2 Group.</item>
///   <item>Admin sees everything, always.</item>
/// </list>
/// </summary>
public static class SnapshotBuilder
{
    public static MatchSnapshot Build(MatchEngine match, Viewer viewer)
    {
        var round = match.CurrentRound;
        var revealed = round?.IsRevealed ?? false;

        var players = match.Roster.Players.Select(p => BuildPlayer(match, p, viewer, revealed)).ToList();
        var teams = match.Roster.Teams.Select(t => BuildTeam(match, t, viewer, revealed)).ToList();

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
            round?.Number ?? 0,
            match.TotalRounds,
            BuildQuestion(round, viewer, revealed),
            players,
            teams,
            match.GetStandings(),
            viewer.Role == ViewerRole.Player ? BuildYou(match, viewer.StationId!, revealed) : null);
    }

    private static QuestionView? BuildQuestion(Round? round, Viewer viewer, bool revealed)
    {
        if (round == null)
            return null;

        var showCorrect = revealed || viewer.Role == ViewerRole.Admin;
        var q = round.Question;

        return new QuestionView(
            q.Id,
            q.Text,
            q.Answers,
            q.MediaUrl,
            showCorrect ? q.CorrectIndex : null,
            ToUnixMs(round.OpenedAt),
            ToUnixMs(round.ClosesAt));
    }

    private static PlayerView BuildPlayer(MatchEngine match, Player player, Viewer viewer, bool revealed)
    {
        // In 2v2 Group, answers belong to teams, so individual players have none of their own.
        var answer = match.Mode.GetAnswerOwnerId(player) == player.StationId
            ? match.CurrentRound?.Answers.GetValueOrDefault(player.StationId)
            : null;

        var canSee = revealed || viewer.Role == ViewerRole.Admin || viewer.StationId == player.StationId;

        return new PlayerView(
            player.StationId,
            player.Name,
            player.TeamId,
            player.Tally.Score,
            HasAnswered: answer != null,
            canSee ? ToAnswerView(match, answer) : null,
            ResultFor(match, player.StationId));
    }

    private static TeamView BuildTeam(MatchEngine match, Team team, Viewer viewer, bool revealed)
    {
        // Only 2v2 Group has team-owned answers.
        var answer = match.CurrentRound?.Answers.GetValueOrDefault(team.Id);
        var viewerIsOnTeam = viewer.StationId != null && team.StationIds.Contains(viewer.StationId);
        var canSee = revealed || viewer.Role == ViewerRole.Admin || viewerIsOnTeam;

        return new TeamView(
            team.Id,
            team.Name,
            team.StationIds,
            team.Tally.Score,
            HasAnswered: answer != null,
            canSee ? ToAnswerView(match, answer) : null,
            ResultFor(match, team.Id));
    }

    private static YouView? BuildYou(MatchEngine match, string stationId, bool revealed)
    {
        var player = match.Roster.FindPlayer(stationId);
        if (player == null)
            return null; // this station isn't playing in this match

        var team = match.Roster.TeamOf(player);
        var ownerId = match.Mode.GetAnswerOwnerId(player);
        var answer = match.CurrentRound?.Answers.GetValueOrDefault(ownerId);

        return new YouView(
            player.StationId,
            player.Name,
            team?.Id,
            team?.Name,
            player.Tally.Score,
            team?.Tally.Score,
            ToAnswerView(match, answer),
            ResultFor(match, ownerId));
    }

    private static AnswerView? ToAnswerView(MatchEngine match, LockedAnswer? answer)
    {
        if (answer == null)
            return null;

        var presser = match.Roster.FindPlayer(answer.PressedByStationId);
        var responseTime = match.CurrentRound!.ResponseTimeOf(answer);

        return new AnswerView(answer.AnswerIndex, answer.PressedByStationId, presser?.Name ?? "", (int)responseTime.TotalMilliseconds);
    }

    private static ResultView? ResultFor(MatchEngine match, string ownerId)
    {
        var result = match.CurrentRound?.Results.FirstOrDefault(r => r.OwnerId == ownerId);
        return result == null ? null : new ResultView(result.IsCorrect, result.Points);
    }

    private static long? ToUnixMs(DateTimeOffset? time) =>
        time == null || time == DateTimeOffset.MaxValue ? null : time.Value.ToUnixTimeMilliseconds();
}
