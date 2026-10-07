using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Snapshots;

// The shapes below are exactly what screens receive (as camelCase JSON) every time a match changes.
// They are deliberately flat and display-ready so the browser code stays simple.
// All times are milliseconds since 1970-01-01 UTC on the SERVER's clock; screens correct for
// their own clock offset (see wwwroot/js/clock-sync.js) before counting down.

/// <summary>Who is looking. Decides what a snapshot is allowed to reveal (see <see cref="SnapshotBuilder"/>).</summary>
public enum ViewerRole
{
    Main,
    Player,
    Kiosk,
    Admin,
}

public sealed record Viewer(ViewerRole Role, string? StationId = null)
{
    public static readonly Viewer Main = new(ViewerRole.Main);
    public static readonly Viewer Kiosk = new(ViewerRole.Kiosk);
    public static readonly Viewer Admin = new(ViewerRole.Admin);
    public static Viewer Player(string stationId) => new(ViewerRole.Player, stationId);
}

/// <summary>The full state of one match, as one screen is allowed to see it.</summary>
public sealed record MatchSnapshot(
    int Version,
    string MatchId,
    MatchPhase Phase,
    long PhaseStartedAtMs,
    long? PhaseEndsAtMs,
    string Mode,
    string ModeName,
    bool UsesTeams,
    string Category,
    string Difficulty,
    int RoundNumber,
    int TotalRounds,
    QuestionView? Question,
    IReadOnlyList<PlayerView> Players,
    IReadOnlyList<TeamView> Teams,
    IReadOnlyList<Standing> Standings,
    YouView? You);

/// <param name="CorrectIndex">Null until the answer is revealed.</param>
/// <param name="OpenedAtMs">When buttons went live; null during lead-in.</param>
public sealed record QuestionView(
    int Id,
    string Text,
    IReadOnlyList<string> Answers,
    string? MediaUrl,
    int? CorrectIndex,
    long? OpenedAtMs,
    long? ClosesAtMs);

/// <param name="Answer">Null if not answered yet, or if this viewer isn't allowed to see it yet.</param>
/// <param name="Result">This question's outcome; null until Reveal.</param>
public sealed record PlayerView(
    string StationId,
    string Name,
    string? TeamId,
    int Score,
    bool HasAnswered,
    AnswerView? Answer,
    ResultView? Result);

public sealed record TeamView(
    string Id,
    string Name,
    IReadOnlyList<string> StationIds,
    int Score,
    bool HasAnswered,
    AnswerView? Answer,
    ResultView? Result);

public sealed record AnswerView(int Index, string PressedByStationId, string PressedByName, int ResponseMs);

public sealed record ResultView(bool IsCorrect, int Points);

/// <summary>
/// Only sent to player screens: "your" view, already worked out for the current mode, so the
/// screen doesn't need to know mode rules. In 2v2 Group, <see cref="Answer"/> is the TEAM's
/// locked answer (with who pressed it); otherwise it's the player's own.
/// </summary>
public sealed record YouView(
    string StationId,
    string Name,
    string? TeamId,
    string? TeamName,
    int Score,
    int? TeamScore,
    AnswerView? Answer,
    ResultView? Result);
