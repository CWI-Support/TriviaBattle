namespace TriviaBattle.Server.Admin;

/// <summary>One saved match, as the insights need it: when it ran and who played (a profile id, or null for a guest).</summary>
public sealed record PlayedMatch(DateTime StartedAtUtc, DateTime EndedAtUtc, IReadOnlyList<int?> PlayerProfileIds);

/// <summary>One day of the report (the venue's local date).</summary>
public sealed record InsightsDay(DateOnly Date, int Matches, int Plays, double MinutesInUse);

/// <summary>
/// The design doc's indicators, worked out from the match history (see <see cref="Insights.Calculate"/>).
/// Dwell time and returns can only be seen for card holders: a guest has no identity to follow.
/// </summary>
/// <param name="Plays">Seats filled: one per player per match (guests included).</param>
/// <param name="CardHolders">Different card holders who played in the period.</param>
/// <param name="ReturningCardHolders">Of those, the ones who came back: they also played on an earlier day (in or before the period).</param>
/// <param name="Visits">Card holders' visits: a run of matches with no more than <see cref="Insights.VisitGap"/> between them.</param>
/// <param name="AverageDwellMinutes">A visit's length, from its first match's start to its last match's end.</param>
public sealed record InsightsReport(
    DateTime FromUtc,
    DateTime ToUtc,
    int Matches,
    int Plays,
    int GuestPlays,
    double MinutesInUse,
    int CardHolders,
    int ReturningCardHolders,
    int Visits,
    double AverageDwellMinutes,
    double AverageMatchesPerVisit,
    IReadOnlyList<InsightsDay> Days);

/// <summary>
/// Works out the design doc's indicators from saved matches: time in use (how long any game was running,
/// overlaps counted once), dwell time (how long a card holder stays per visit), and returning players.
/// Food and drink sales aren't in the game's data; they'd come from the venue's tills.
/// </summary>
public static class Insights
{
    /// <summary>A card holder who starts another match within this long of the last one ending is still on the same visit.</summary>
    public static readonly TimeSpan VisitGap = TimeSpan.FromMinutes(30);

    /// <param name="matches">The matches in the period.</param>
    /// <param name="playedBefore">Card holders who played at some time before the period started.</param>
    /// <param name="zone">The venue's time zone, for which day a match was on.</param>
    public static InsightsReport Calculate(IReadOnlyList<PlayedMatch> matches, DateTime fromUtc, DateTime toUtc, IReadOnlySet<int> playedBefore, TimeZoneInfo zone)
    {
        DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

        var days = matches
            .GroupBy(m => DayOf(m.StartedAtUtc))
            .OrderBy(g => g.Key)
            .Select(g => new InsightsDay(g.Key, g.Count(), g.Sum(m => m.PlayerProfileIds.Count), Round(MinutesCovered(g))))
            .ToList();

        // Each card holder's matches in order, split into visits wherever they were away for longer than VisitGap.
        var byPlayer = matches
            .SelectMany(m => m.PlayerProfileIds.OfType<int>().Distinct().Select(id => (Id: id, Match: m)))
            .GroupBy(x => x.Id, x => x.Match)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.StartedAtUtc).ToList());

        var visits = new List<(TimeSpan Length, int Matches)>();
        var returning = 0;
        foreach (var (id, played) in byPlayer)
        {
            var visitStart = played[0].StartedAtUtc;
            var visitEnd = played[0].EndedAtUtc;
            var visitMatches = 1;
            foreach (var match in played.Skip(1))
            {
                if (match.StartedAtUtc - visitEnd > VisitGap)
                {
                    visits.Add((visitEnd - visitStart, visitMatches));
                    visitStart = match.StartedAtUtc;
                    visitMatches = 0;
                }
                visitEnd = match.EndedAtUtc > visitEnd ? match.EndedAtUtc : visitEnd;
                visitMatches++;
            }
            visits.Add((visitEnd - visitStart, visitMatches));

            var daysPlayed = played.Select(m => DayOf(m.StartedAtUtc)).Distinct().Count();
            if (playedBefore.Contains(id) || daysPlayed > 1)
                returning++;
        }

        return new InsightsReport(
            fromUtc,
            toUtc,
            matches.Count,
            matches.Sum(m => m.PlayerProfileIds.Count),
            matches.Sum(m => m.PlayerProfileIds.Count(id => id == null)),
            Round(days.Sum(d => d.MinutesInUse)),
            byPlayer.Count,
            returning,
            visits.Count,
            visits.Count == 0 ? 0 : Round(visits.Average(v => v.Length.TotalMinutes)),
            visits.Count == 0 ? 0 : Round(visits.Average(v => v.Matches)),
            days);
    }

    /// <summary>Minutes when at least one of these matches was running: matches side by side count once.</summary>
    private static double MinutesCovered(IEnumerable<PlayedMatch> matches)
    {
        var total = TimeSpan.Zero;
        DateTime? spanStart = null, spanEnd = null;
        foreach (var match in matches.OrderBy(m => m.StartedAtUtc))
        {
            if (spanEnd == null || match.StartedAtUtc > spanEnd)
            {
                if (spanEnd != null) total += spanEnd.Value - spanStart!.Value;
                spanStart = match.StartedAtUtc;
                spanEnd = match.EndedAtUtc;
            }
            else if (match.EndedAtUtc > spanEnd)
            {
                spanEnd = match.EndedAtUtc;
            }
        }
        if (spanEnd != null) total += spanEnd.Value - spanStart!.Value;
        return total.TotalMinutes;
    }

    private static double Round(double value) => Math.Round(value, 1);
}
