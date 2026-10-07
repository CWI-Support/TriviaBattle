namespace TriviaBattle.Core.Matches;

/// <summary>One line of the scoreboard: a player (free-for-all) or a team (team modes).</summary>
/// <param name="Id">Station id for a player, team id for a team.</param>
public sealed record Standing(int Rank, string Id, string Name, int Score, int CorrectAnswers, bool IsTeam);

public static class Standings
{
    /// <summary>
    /// Ranks by score (highest first). Ties are broken by total time spent on correct
    /// answers (fastest first). Exactly equal entries share a rank.
    /// </summary>
    public static IReadOnlyList<Standing> Calculate(MatchRoster roster, bool byTeam)
    {
        var entries = byTeam
            ? roster.Teams.Select(t => (t.Id, t.Name, t.Tally, IsTeam: true))
            : roster.Players.Select(p => (Id: p.StationId, p.Name, p.Tally, IsTeam: false));

        var ordered = entries
            .OrderByDescending(e => e.Tally.Score)
            .ThenBy(e => e.Tally.TimeOnCorrectAnswers)
            .ToList();

        var standings = new List<Standing>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var tiedWithPrevious = i > 0
                && ordered[i - 1].Tally.Score == e.Tally.Score
                && ordered[i - 1].Tally.TimeOnCorrectAnswers == e.Tally.TimeOnCorrectAnswers;

            var rank = tiedWithPrevious ? standings[i - 1].Rank : i + 1;
            standings.Add(new Standing(rank, e.Id, e.Name, e.Tally.Score, e.Tally.CorrectAnswers, e.IsTeam));
        }

        return standings;
    }
}
