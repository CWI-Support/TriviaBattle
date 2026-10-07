using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Server.Data;

namespace TriviaBattle.Server.Leaderboards;

public enum LeaderboardPeriod
{
    Today,
    Week,
    AllTime,
}

/// <summary>Bound from "Leaderboards" in appsettings.json.</summary>
public class LeaderboardSettings
{
    /// <summary>How many entries a board shows.</summary>
    public int Size { get; set; } = 10;

    /// <summary>Only show players who swiped an RFID card (in team modes: every teammate). Off = guests count too.</summary>
    public bool RegisteredPlayersOnly { get; set; } = false;
}

/// <param name="Names">The player, or teammates joined with " &amp; ".</param>
/// <param name="OwnerId">Station id (free-for-all) or team id, within <see cref="MatchId"/>.</param>
public sealed record LeaderboardEntry(
    int Rank,
    string Names,
    int Score,
    int CorrectAnswers,
    string Category,
    string Difficulty,
    DateTime PlayedAtUtc,
    string MatchId,
    string OwnerId);

public sealed record Leaderboard(GameModeKind Mode, string ModeName, LeaderboardPeriod Period, IReadOnlyList<LeaderboardEntry> Entries);

/// <summary>
/// Builds the high-score boards from match history. There is one board per game mode
/// (and per period: today, last 7 days, all time):
/// <list type="bullet">
///   <item>Free-for-all: best individual scores. A registered player appears once, with their best score.</item>
///   <item>Team modes: best team scores, showing both teammates' names.</item>
/// </list>
/// Aborted matches and zero scores never count.
/// </summary>
public class LeaderboardService(
    IDbContextFactory<TriviaDbContext> dbFactory,
    IOptionsMonitor<LeaderboardSettings> settings,
    TimeProvider clock)
{
    // Boards are built from the top candidates only; plenty for a top-10 even after removing duplicates.
    private const int CandidateLimit = 500;

    public async Task<Leaderboard> GetAsync(GameModeKind mode, LeaderboardPeriod period, int? size = null)
    {
        var limit = size ?? settings.CurrentValue.Size;
        var sinceUtc = StartOf(period);

        var entries = mode == GameModeKind.FreeForAll
            ? await IndividualBoardAsync(sinceUtc, limit)
            : await TeamBoardAsync(mode, sinceUtc, limit);

        return new Leaderboard(mode, GameModes.DisplayName(mode), period, entries);
    }

    /// <summary>
    /// Where a just-finished match landed. Returns one placement per player/team that made a board,
    /// using the all-time board if they made it, otherwise today's.
    /// </summary>
    public async Task<IReadOnlyList<LeaderboardPlacement>> FindPlacementsAsync(MatchResult result)
    {
        var mode = Enum.Parse<GameModeKind>(result.Mode);
        var allTime = await GetAsync(mode, LeaderboardPeriod.AllTime);
        var today = await GetAsync(mode, LeaderboardPeriod.Today);

        var placements = new List<LeaderboardPlacement>();
        var ownersPlaced = new HashSet<string>();

        foreach (var (board, label) in new[] { (allTime, "all-time"), (today, "today") })
        {
            foreach (var entry in board.Entries.Where(e => e.MatchId == result.MatchId && ownersPlaced.Add(e.OwnerId)))
                placements.Add(new LeaderboardPlacement(entry.OwnerId, entry.Names, entry.Score, entry.Rank, label));
        }

        return placements;
    }

    private async Task<List<LeaderboardEntry>> IndividualBoardAsync(DateTime sinceUtc, int limit)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var registeredOnly = settings.CurrentValue.RegisteredPlayersOnly;
        var freeForAll = GameModeKind.FreeForAll.ToString();

        var candidates = await (
            from p in db.MatchParticipants
            join m in db.Matches on p.MatchId equals m.Id
            where m.Mode == freeForAll && !m.WasAborted && m.EndedAtUtc >= sinceUtc && p.Score > 0
            where !registeredOnly || p.PlayerProfileId != null
            orderby p.Score descending, m.EndedAtUtc
            select new { p.PlayerName, p.PlayerProfileId, p.Score, p.CorrectAnswers, p.StationId, m.Id, m.CategoryName, m.Difficulty, m.EndedAtUtc })
            .Take(CandidateLimit)
            .ToListAsync();

        // Registered players appear once (their best score); every guest score is its own entry.
        var best = new List<LeaderboardEntry>();
        var profilesSeen = new HashSet<int>();
        foreach (var c in candidates)
        {
            if (c.PlayerProfileId != null && !profilesSeen.Add(c.PlayerProfileId.Value))
                continue;

            best.Add(new LeaderboardEntry(best.Count + 1, c.PlayerName, c.Score, c.CorrectAnswers, c.CategoryName, c.Difficulty, c.EndedAtUtc, c.Id, c.StationId));
            if (best.Count == limit)
                break;
        }

        return best;
    }

    private async Task<List<LeaderboardEntry>> TeamBoardAsync(GameModeKind mode, DateTime sinceUtc, int limit)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var modeName = mode.ToString();

        var teams = await (
            from t in db.MatchTeams
            join m in db.Matches on t.MatchId equals m.Id
            where m.Mode == modeName && !m.WasAborted && m.EndedAtUtc >= sinceUtc && t.Score > 0
            orderby t.Score descending, m.EndedAtUtc
            select new { t.TeamId, t.Score, t.CorrectAnswers, m.Id, m.CategoryName, m.Difficulty, m.EndedAtUtc })
            .Take(CandidateLimit)
            .ToListAsync();

        // Look up the teammates of every candidate team in one query.
        var matchIds = teams.Select(t => t.Id).Distinct().ToList();
        var members = await db.MatchParticipants
            .Where(p => matchIds.Contains(p.MatchId))
            .Select(p => new { p.MatchId, p.TeamId, p.PlayerName, p.PlayerProfileId, p.StationId })
            .ToListAsync();

        var entries = new List<LeaderboardEntry>();
        foreach (var team in teams)
        {
            var teammates = members.Where(p => p.MatchId == team.Id && p.TeamId == team.TeamId).OrderBy(p => p.StationId).ToList();
            if (settings.CurrentValue.RegisteredPlayersOnly && teammates.Any(p => p.PlayerProfileId == null))
                continue;

            var names = string.Join(" & ", teammates.Select(p => p.PlayerName));
            entries.Add(new LeaderboardEntry(entries.Count + 1, names, team.Score, team.CorrectAnswers, team.CategoryName, team.Difficulty, team.EndedAtUtc, team.Id, team.TeamId));
            if (entries.Count == limit)
                break;
        }

        return entries;
    }

    /// <summary>"Today" means since local midnight at the venue (the server's time zone).</summary>
    private DateTime StartOf(LeaderboardPeriod period)
    {
        var nowLocal = clock.GetLocalNow();
        return period switch
        {
            LeaderboardPeriod.Today => new DateTimeOffset(nowLocal.Date, nowLocal.Offset).UtcDateTime,
            LeaderboardPeriod.Week => nowLocal.AddDays(-7).UtcDateTime,
            _ => DateTime.MinValue,
        };
    }
}
