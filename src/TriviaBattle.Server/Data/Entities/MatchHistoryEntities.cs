namespace TriviaBattle.Server.Data.Entities;

// Match history: one row per match, plus its teams, its players and every answer.
// Leaderboards are calculated from these tables (there is no separate leaderboard table),
// so deleting a match removes it from the leaderboards too.
// Times are stored as UTC DateTime because SQLite can't sort DateTimeOffset values.

/// <summary>Table: Matches.</summary>
public class MatchEntity
{
    /// <summary>The engine's match id (8 hex characters).</summary>
    public string Id { get; set; } = "";

    /// <summary>"FreeForAll", "TeamSharedAnswer" or "TeamCombinedScore".</summary>
    public string Mode { get; set; } = "";

    public int CategoryId { get; set; }

    /// <summary>Copied at the time, so history still reads right if the category is renamed or deleted.</summary>
    public string CategoryName { get; set; } = "";

    public string Difficulty { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }

    /// <summary>Aborted matches are kept for the record but never appear on leaderboards.</summary>
    public bool WasAborted { get; set; }

    public int QuestionsPlayed { get; set; }

    public List<MatchTeamEntity> Teams { get; set; } = new();
    public List<MatchParticipantEntity> Participants { get; set; } = new();
    public List<MatchAnswerEntity> Answers { get; set; } = new();
}

/// <summary>Table: MatchTeams. Empty for free-for-all.</summary>
public class MatchTeamEntity
{
    public int Id { get; set; }
    public string MatchId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int? Rank { get; set; }
}

/// <summary>Table: MatchParticipants. One row per player in the match.</summary>
public class MatchParticipantEntity
{
    public int Id { get; set; }
    public string MatchId { get; set; } = "";
    public string StationId { get; set; } = "";
    public string PlayerName { get; set; } = "";

    /// <summary>Null for guests (no RFID card).</summary>
    public int? PlayerProfileId { get; set; }

    public string? TeamId { get; set; }

    /// <summary>Own points. In team modes this is the player's contribution to the team.</summary>
    public int Score { get; set; }

    public int CorrectAnswers { get; set; }

    /// <summary>Final placing in free-for-all; null in team modes.</summary>
    public int? Rank { get; set; }
}

/// <summary>Table: MatchAnswers. One row per question per answer owner (useful for question statistics).</summary>
public class MatchAnswerEntity
{
    public int Id { get; set; }
    public string MatchId { get; set; } = "";
    public int RoundNumber { get; set; }
    public int QuestionId { get; set; }

    /// <summary>Station id, or team id in 2v2 Group.</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>Null if nobody answered.</summary>
    public string? PressedByStationId { get; set; }
    public int? AnswerIndex { get; set; }

    public bool IsCorrect { get; set; }
    public int Points { get; set; }
    public int? ResponseMs { get; set; }
}
