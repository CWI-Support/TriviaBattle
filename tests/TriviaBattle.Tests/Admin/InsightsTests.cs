using TriviaBattle.Server.Admin;

namespace TriviaBattle.Tests.Admin;

/// <summary>The design doc's indicators from match history: time in use, visits and dwell time, returning players.</summary>
public class InsightsTests
{
    private static readonly DateTime Day1 = new(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);

    /// <summary>A match starting minutes after start, lasting length minutes, with these players (null = a guest).</summary>
    private static PlayedMatch Match(DateTime start, int minutes, int length, params int?[] players) =>
        new(start.AddMinutes(minutes), start.AddMinutes(minutes + length), players);

    private static InsightsReport Report(IReadOnlyList<PlayedMatch> matches, params int[] playedBefore) =>
        Insights.Calculate(matches, Day1.Date, Day2.Date.AddDays(1), playedBefore.ToHashSet(), TimeZoneInfo.Utc);

    [Fact]
    public void Time_in_use_counts_side_by_side_games_once()
    {
        var report = Report([
            Match(Day1, 0, 10, 1),
            Match(Day1, 5, 10, 2),   // overlaps the first: 0-15 in all
            Match(Day1, 60, 5, 3),   // on its own: 5 more
        ]);

        Assert.Equal(20, report.MinutesInUse);
        Assert.Equal(3, report.Matches);
    }

    [Fact]
    public void A_visit_runs_until_a_card_holder_is_away_for_more_than_half_an_hour()
    {
        var report = Report([
            Match(Day1, 0, 5, 1),
            Match(Day1, 10, 5, 1),   // back after 5 minutes: same visit, 0-15
            Match(Day1, 90, 5, 1),   // back after 75 minutes: a new visit, 5 long
        ]);

        Assert.Equal(2, report.Visits);
        Assert.Equal(10, report.AverageDwellMinutes);    // (15 + 5) / 2
        Assert.Equal(1.5, report.AverageMatchesPerVisit); // (2 + 1) / 2
    }

    [Fact]
    public void Card_holders_who_came_on_another_day_count_as_returning_and_guests_are_only_counted_as_seats()
    {
        var report = Report([
            Match(Day1, 0, 5, 1, 2, null),
            Match(Day2, 0, 5, 1),          // player 1 came back the next day
            Match(Day2, 10, 5, 3),         // player 3 is new here, but played before the period
        ], playedBefore: 3);

        Assert.Equal(3, report.CardHolders);
        Assert.Equal(2, report.ReturningCardHolders); // 1 and 3, not 2
        Assert.Equal(5, report.Plays);
        Assert.Equal(1, report.GuestPlays);
        Assert.Equal(2, report.Days.Count);
    }

    [Fact]
    public void An_empty_period_reports_zeros()
    {
        var report = Report([]);

        Assert.Equal(0, report.Matches);
        Assert.Equal(0, report.AverageDwellMinutes);
        Assert.Empty(report.Days);
    }
}
