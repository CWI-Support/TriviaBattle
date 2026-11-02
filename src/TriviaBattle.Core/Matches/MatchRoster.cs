namespace TriviaBattle.Core.Matches;

/// <summary>A running total of points. Players and teams each have one.</summary>
public sealed class Tally
{
    public int Score { get; private set; }
    public int CorrectAnswers { get; private set; }
    public int WrongAnswers { get; private set; }

    /// <summary>Total time taken on correct answers. Used to break ties (faster wins).</summary>
    public TimeSpan TimeOnCorrectAnswers { get; private set; }

    public void Add(GivenAnswer answer)
    {
        Score += answer.Points; // negative for a wrong answer with the LosePoints penalty

        if (answer.IsCorrect)
        {
            CorrectAnswers++;
            TimeOnCorrectAnswers += answer.ResponseTime;
        }
        else
        {
            WrongAnswers++;
        }
    }
}

/// <summary>A player in a running match.</summary>
public sealed class Player(string stationId, string name, int? playerProfileId, string? teamId, string hero, bool? lobbyPhoto = null)
{
    public string StationId { get; } = stationId;
    public string Name { get; } = name;
    public int? PlayerProfileId { get; } = playerProfileId;
    public string? TeamId { get; } = teamId;

    /// <summary>The hero they play (<see cref="Arcade.Heroes"/>): how they look on the screens.</summary>
    public string Hero { get; } = hero;
    public Tally Tally { get; } = new();

    /// <summary>
    /// Dungeon format: when their station takes their hero photo (their face on their hero), after a
    /// countdown. Set when the intro starts (photos on by default), or when they ask for one. Null if none is coming.
    /// </summary>
    public DateTimeOffset? PhotoAt { get; private set; }

    /// <summary>Goes up when their hero photo arrives (0 = none). Screens fetch the picture from the server by this number.</summary>
    public int Photo { get; private set; } = lobbyPhoto == true ? 1 : 0;

    /// <summary>Their station's camera is on for the photo, so its countdown is heard. A station without a camera stays quiet.</summary>
    public bool PhotoCameraOn { get; private set; }

    /// <summary>They said no to a hero photo (the action button in the intro, or none in the lobby): none this match, and any taken is gone.</summary>
    public bool PhotoDeclined { get; private set; } = lobbyPhoto == false;

    /// <summary>Their photo was settled in the lobby (brought one, or chose none): the intro takes none.</summary>
    public bool PhotoFromLobby { get; } = lobbyPhoto != null;

    public void SchedulePhoto(DateTimeOffset takeAt, bool cameraOn)
    {
        PhotoAt = takeAt;
        PhotoCameraOn = cameraOn;
    }

    public void CameraOn() => PhotoCameraOn = true;

    public void PhotoArrived() => Photo++;

    public void DeclinePhoto()
    {
        PhotoAt = null;
        Photo = 0;
        PhotoCameraOn = false;
        PhotoDeclined = true;
    }
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
                Teams.FirstOrDefault(t => t.StationIds.Contains(s.StationId))?.Id,
                s.Hero ?? Arcade.Heroes.Default,
                s.Photo))
            .ToList();
    }

    public IReadOnlyList<Player> Players { get; }
    public IReadOnlyList<Team> Teams { get; }

    public Player? FindPlayer(string stationId) => Players.FirstOrDefault(p => p.StationId == stationId);

    public Team? FindTeam(string teamId) => Teams.FirstOrDefault(t => t.Id == teamId);

    public Team? TeamOf(Player player) => player.TeamId == null ? null : FindTeam(player.TeamId);
}
