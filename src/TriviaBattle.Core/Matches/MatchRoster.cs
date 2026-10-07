namespace TriviaBattle.Core.Matches;

/// <summary>A running total of points. Players and teams each have one.</summary>
public sealed class Tally
{
    public int Score { get; private set; }
    public int CorrectAnswers { get; private set; }

    /// <summary>Total time taken on correct answers. Used to break ties (faster wins).</summary>
    public TimeSpan TimeOnCorrectAnswers { get; private set; }

    public void AddCorrectAnswer(int points, TimeSpan responseTime)
    {
        Score += points;
        CorrectAnswers++;
        TimeOnCorrectAnswers += responseTime;
    }
}

/// <summary>A player in a running match.</summary>
public sealed class Player(string stationId, string name, int? playerProfileId, string? teamId)
{
    public string StationId { get; } = stationId;
    public string Name { get; } = name;
    public int? PlayerProfileId { get; } = playerProfileId;
    public string? TeamId { get; } = teamId;
    public Tally Tally { get; } = new();
}

/// <summary>A team in a running match.</summary>
public sealed class Team(string id, string name, IReadOnlyList<string> stationIds)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public IReadOnlyList<string> StationIds { get; } = stationIds;
    public Tally Tally { get; } = new();
}

/// <summary>The players and teams in one match, with lookups.</summary>
public sealed class MatchRoster
{
    public MatchRoster(MatchSetup setup)
    {
        Teams = setup.Teams.Select(t => new Team(t.TeamId, t.Name.Trim(), t.StationIds)).ToList();

        Players = setup.Seats
            .Select(s => new Player(
                s.StationId,
                s.PlayerName.Trim(),
                s.PlayerProfileId,
                Teams.FirstOrDefault(t => t.StationIds.Contains(s.StationId))?.Id))
            .ToList();
    }

    public IReadOnlyList<Player> Players { get; }
    public IReadOnlyList<Team> Teams { get; }

    public Player? FindPlayer(string stationId) => Players.FirstOrDefault(p => p.StationId == stationId);

    public Team? FindTeam(string teamId) => Teams.FirstOrDefault(t => t.Id == teamId);

    public Team? TeamOf(Player player) => player.TeamId == null ? null : FindTeam(player.TeamId);
}
