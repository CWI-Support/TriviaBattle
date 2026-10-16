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
    string CategoryName, // "Movies, Music" when several categories were mixed
    string Difficulty,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool WasAborted,
    int QuestionsPlayed, // answers given in total, by all players/teams
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

        // Every answer given, by every owner, in the order each owner gave them.
        var answers = match.Streams
            .SelectMany(stream => stream.Answers.Select(a => new AnswerResult(
                a.Number,
                a.Question.Id,
                a.OwnerId,
                a.PressedByStationId,
                a.AnswerIndex,
                a.IsCorrect,
                a.Points,
                (int?)a.ResponseTime.TotalMilliseconds)))
            .ToList();

        return new MatchResult(
            match.MatchId,
            match.Mode.Kind.ToString(),
            match.Setup.CategoryIds[0], // history keeps one id; CategoryName lists them all
            match.CategoryName,
            match.Setup.Difficulty.ToString(),
            match.StartedAt,
            endedAt,
            match.WasAborted,
            answers.Count,
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

/// <param name="RoundNumber">The owner's 1st, 2nd, 3rd... answer of the match.</param>
/// <param name="OwnerId">Station id, or team id in 2v2 Group.</param>
/// <param name="Points">Negative for a wrong answer with the LosePoints penalty.</param>
public sealed record AnswerResult(
    int RoundNumber,
    int QuestionId,
    string OwnerId,
    string? PressedByStationId,
    int? AnswerIndex,
    bool IsCorrect,
    int Points,
    int? ResponseMs);
