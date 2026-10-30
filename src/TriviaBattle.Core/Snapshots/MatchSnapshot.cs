using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Snapshots;

// The shapes below are exactly what screens receive (as camelCase JSON) every time a match changes.
// They are deliberately flat and display-ready so the browser code stays simple.
// All times are milliseconds since 1970-01-01 UTC on the SERVER's clock; screens correct for
// their own clock offset (see wwwroot/js/screen-connection.js) before counting down.

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
/// <param name="PhaseEndsAtMs">While Playing, this is the match clock running out.</param>
/// <param name="MatchSeconds">How long the clock runs, for "60 seconds: go!" before it starts.</param>
/// <param name="You">Only for player screens.</param>
/// <param name="Streams">Only for admin: every player's/team's current question, with its answer.</param>
/// <param name="Format">"Classic" or "Dungeon": which game the screens draw.</param>
public sealed record MatchSnapshot(
    int Version,
    string MatchId,
    string Format,
    MatchPhase Phase,
    long PhaseStartedAtMs,
    long? PhaseEndsAtMs,
    string Mode,
    string ModeName,
    bool UsesTeams,
    string Category,
    string Difficulty,
    double MatchSeconds,
    IReadOnlyList<PlayerView> Players,
    IReadOnlyList<TeamView> Teams,
    IReadOnlyList<Standing> Standings,
    YouView? You,
    IReadOnlyList<StreamView>? Streams);

/// <param name="LastAnswer">
/// Their most recent answer, for a right/wrong flash. Null if they haven't answered yet, or if
/// their answers belong to their team (2v2 Group: see <see cref="TeamView.LastAnswer"/>).
/// </param>
/// <param name="LockedOutUntilMs">Set while a wrong-answer lockout is running for whoever they answer as.</param>
/// <param name="Delve">Dungeon format: their own delve. Null in Classic, and in 2v2 Group (the team delves: <see cref="TeamView.Delve"/>).</param>
public sealed record PlayerView(
    string StationId,
    string Name,
    string? TeamId,
    int Score,
    int CorrectAnswers,
    int WrongAnswers,
    AnswerOutcomeView? LastAnswer,
    long? LockedOutUntilMs,
    DelveView? Delve);

/// <param name="LastAnswer">Only in 2v2 Group, where the team answers as one.</param>
/// <param name="Delve">Dungeon format, 2v2 Group only: the party's shared delve.</param>
public sealed record TeamView(
    string Id,
    string Name,
    IReadOnlyList<string> StationIds,
    int Score,
    int CorrectAnswers,
    int WrongAnswers,
    AnswerOutcomeView? LastAnswer,
    DelveView? Delve);

/// <summary>Whether an answer was right, without saying which answer it was (safe for every screen).</summary>
/// <param name="Number">Their 1st, 2nd, 3rd... answer: changes with each answer, so screens can tell when to flash.</param>
/// <param name="TimedOut">Dungeon format: the hourglass ran out; nobody pressed.</param>
public sealed record AnswerOutcomeView(int Number, bool IsCorrect, int Points, string PressedByStationId, bool TimedOut);

/// <summary>A question on a player's screen. Never includes which answer is correct.</summary>
/// <param name="Number">Their 1st, 2nd, 3rd... question.</param>
/// <param name="Category">The question's own category (a match can mix several).</param>
public sealed record QuestionView(
    int Number,
    int Id,
    string Category,
    string Text,
    IReadOnlyList<string> Answers,
    string? MediaUrl,
    long? ShownAtMs);

/// <summary>
/// The answer just given for a player screen's feedback ("Correct! +100" / "Nope, it was B").
/// The question is already behind them, so the correct answer may be shown.
/// </summary>
public sealed record YourAnswerView(
    int Number,
    int ChosenIndex,
    int CorrectIndex,
    string CorrectAnswer,
    bool IsCorrect,
    int Points,
    string PressedByStationId,
    string PressedByName,
    bool TimedOut);

/// <summary>
/// Only sent to player screens: "your" view, already worked out for the current mode, so the
/// screen doesn't need to know mode rules. In 2v2 Group, the question and last answer are the
/// TEAM's (shared by both teammates); otherwise they're the player's own.
/// </summary>
/// <param name="Question">The question to answer now. Null outside Playing.</param>
/// <param name="Delve">Dungeon format: the delve they're on (their party's in 2v2 Group).</param>
public sealed record YouView(
    string StationId,
    string Name,
    string? TeamId,
    string? TeamName,
    int Score,
    int? TeamScore,
    QuestionView? Question,
    long? LockedOutUntilMs,
    YourAnswerView? LastAnswer,
    DelveView? Delve);

/// <summary>Dungeon format: where one adventurer (or party) is, what they're fighting, and how it's going.</summary>
/// <param name="Floor">1-based, counting from the top of the dungeon.</param>
/// <param name="Room">Which monster of the floor this is (1-based), of <paramref name="RoomsOnFloor"/>.</param>
/// <param name="AttackAtMs">The monster's hourglass runs out at this time. Null while it's stopped (buttons dead).</param>
/// <param name="DownedUntilMs">Set while they're downed.</param>
/// <param name="LastDealt">The last hit they landed; its Number changes with each hit, so screens can animate it once.</param>
/// <param name="LastTaken">The last hit they took; same idea.</param>
/// <param name="Path">Every monster of the Depth they're in, to its boss, in order. Ones before (Floor, Room) are beaten.</param>
public sealed record DelveView(
    int Floor,
    int Room,
    int RoomsOnFloor,
    string DepthId,
    string DepthName,
    MonsterView Monster,
    int Hp,
    int MaxHp,
    int Streak,
    int Kills,
    long? AttackStartedAtMs,
    long? AttackAtMs,
    long? DownedUntilMs,
    DealtHit? LastDealt,
    TakenHit? LastTaken,
    IReadOnlyList<PathStepView> Path);

/// <summary>One monster on the path through a Depth.</summary>
public sealed record PathStepView(int Floor, int Room, string Name, string Sprite, bool IsBoss);

/// <param name="Sprite">A built-in placeholder sprite key, or a picture path once real art exists.</param>
public sealed record MonsterView(string Name, string Sprite, int Hp, int MaxHp, bool IsBoss);

/// <summary>Admin only: where one player/team is up to.</summary>
public sealed record StreamView(string OwnerId, int Number, string QuestionText, string CorrectAnswer, int Answered, int Correct);
