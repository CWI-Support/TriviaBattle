namespace TriviaBattle.Core.Matches;

/// <summary>
/// Everything worth keeping about a match once its scores are final: what's saved to
/// match history and what leaderboards are built from. A plain copy, safe to hand to
/// background work after MatchHost releases its lock.
/// </summary>
public sealed record MatchResult(
    string MatchId,
    string Mode,
    int CategoryId,
    string CategoryName,
    string Difficulty,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool WasAborted,
    int QuestionsPlayed,
    IReadOnlyList<ParticipantResult> Participants,
    IReadOnlyList<TeamResult> Teams,
    IReadOnlyList<AnswerResult> Answers)
{
    public static MatchResult From(MatchEngine match, DateTimeOffset endedAt)
    {
        var standings = match.GetStandings();
        int? RankOf(string id) => standings.FirstOrDefault(s => s.Id == id)?.Rank;

        var participants = match.Roster.Players.Select(p => new ParticipantResult(
            p.StationId,
            p.Name,
            p.PlayerProfileId,
            p.TeamId,
            p.Tally.Score,
            p.Tally.CorrectAnswers,
            match.Mode.UsesTeams ? null : RankOf(p.StationId))).ToList();

        var teams = match.Roster.Teams.Select(t => new TeamResult(
            t.Id,
            t.Name,
            t.Tally.Score,
            t.Tally.CorrectAnswers,
            RankOf(t.Id))).ToList();

        // Only questions that were actually revealed count (an abort can cut one short).
        var answers = match.Rounds
            .Where(r => r.IsRevealed)
            .SelectMany(r => r.Results.Select(result => new AnswerResult(
                r.Number,
                r.Question.Id,
                result.OwnerId,
                result.Answer?.PressedByStationId,
                result.Answer?.AnswerIndex,
                result.IsCorrect,
                result.Points,
                (int?)result.ResponseTime?.TotalMilliseconds)))
            .ToList();

        return new MatchResult(
            match.MatchId,
            match.Mode.Kind.ToString(),
            match.Setup.CategoryId,
            match.CategoryName,
            match.Setup.Difficulty.ToString(),
            match.StartedAt,
            endedAt,
            match.WasAborted,
            match.Rounds.Count(r => r.IsRevealed),
            participants,
            teams,
            answers);
    }
}

/// <param name="Rank">Final placing in free-for-all; null in team modes (teams are ranked instead).</param>
public sealed record ParticipantResult(
    string StationId,
    string Name,
    int? PlayerProfileId,
    string? TeamId,
    int Score,
    int CorrectAnswers,
    int? Rank);

public sealed record TeamResult(string TeamId, string Name, int Score, int CorrectAnswers, int? Rank);

/// <param name="OwnerId">Station id, or team id in 2v2 Group.</param>
public sealed record AnswerResult(
    int RoundNumber,
    int QuestionId,
    string OwnerId,
    string? PressedByStationId,
    int? AnswerIndex,
    bool IsCorrect,
    int Points,
    int? ResponseMs);
