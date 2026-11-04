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
/// <param name="MatchSeconds">How long the clock runs when it starts, for "60 seconds: go!" before it starts.</param>
/// <param name="EarnsTime">A Dungeon game (solo or group): every monster slain adds time to the clock.</param>
/// <param name="ContinueSeconds">Dungeon games: a continue's clock, a second paid session (0 = no continues), for "swipe for 140 more seconds".</param>
/// <param name="ContinuesUsed">Dungeon games: how many times the players have continued.</param>
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
    bool EarnsTime,
    double ContinueSeconds,
    int ContinuesUsed,
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
/// <param name="Delve">Dungeon format: their party's delve and their own HP. Null in Classic, and in 2v2 Group (the team is one adventurer: <see cref="TeamView.Delve"/>).</param>
/// <param name="Photo">Dungeon format: their hero photo's number (0 = none). Screens fetch it from /api/photos/{matchId}/{stationId}.</param>
/// <param name="Hero">The hero they play ("tank", "wizard"...: their hero class): which 3D model and pictures the screens show.</param>
public sealed record PlayerView(
    string StationId,
    string Name,
    string? TeamId,
    int Score,
    int CorrectAnswers,
    int WrongAnswers,
    AnswerOutcomeView? LastAnswer,
    long? LockedOutUntilMs,
    DelveView? Delve,
    int Photo,
    string Hero);

/// <param name="LastAnswer">Only in 2v2 Group, where the team answers as one.</param>
/// <param name="Delve">
/// Dungeon format: the team's fight, for its lane on the main screen. In 2v2 Group the team is one
/// adventurer; otherwise its HP adds up the teammates' and each one is in <see cref="DelveView.Party"/>.
/// </param>
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
/// <param name="Hidden">Dungeon format: answers a 50/50 took away (always wrong ones; the correct one is never sent).</param>
/// <param name="PeekFromMs">A Ranger's own screen: show the question from this time, before <paramref name="ShownAtMs"/> (during the
/// battle beat). Presses only count from ShownAtMs, as for everyone. Null for everyone else.</param>
/// <param name="Revealed">A Warlock's own screen, once their patron has shown it: the right answer's index. The one time a
/// question's answer is sent while it's on screen, and only to that player.</param>
public sealed record QuestionView(
    int Number,
    int Id,
    string Category,
    string Text,
    IReadOnlyList<string> Answers,
    string? MediaUrl,
    long? ShownAtMs,
    IReadOnlyList<int>? Hidden,
    long? PeekFromMs = null,
    int? Revealed = null);

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
/// <param name="Delve">Dungeon format: their party's delve and their own HP (the team's in 2v2 Group).</param>
/// <param name="Helping">Dungeon format, while they're downed in a party: a standing teammate's question, so they can shout the answer.</param>
/// <param name="Beat">Dungeon format: the battle beat after their last answer (the next question waits until it ends).</param>
/// <param name="PhotoOfferUntilMs">Photos asked for first: the intro offers one ("press the action button to put your face on your hero") until this time. Null once they've answered, or with no offer.</param>
/// <param name="PhotoAtMs">A hero photo is coming (or was taken): the station takes it at this time, after a countdown. The action button in the intro says no.</param>
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
    DelveView? Delve,
    HelpView? Helping,
    BeatView? Beat,
    long? PhotoOfferUntilMs,
    long? PhotoAtMs);

/// <summary>
/// The fight shown between two questions. The screen plays it from <see cref="StartedAtMs"/> to
/// <see cref="EndsAtMs"/>, when the next question appears. What exactly happened is in the delve's
/// LastDealt (your hit) or LastTaken (the monster's).
/// </summary>
/// <param name="Kind">"hit", "kill", "floor" (a floor cleared), "boss" (cleared, and the next floor's boss steps up), "hurt" or "downed".</param>
public sealed record BeatView(string Kind, long StartedAtMs, long EndsAtMs);

/// <summary>A teammate's question shown to a downed player. Never includes which answer is correct.</summary>
public sealed record HelpView(string Name, QuestionView Question);

/// <summary>
/// Dungeon format: one adventurer's fight. The floor, monster, kills, path and <see cref="LastHit"/> are
/// their party's (teammates share them); HP, streak, hourglass, downed and the other hits are their own.
/// </summary>
/// <param name="Floor">1-based, counting from the top of the dungeon.</param>
/// <param name="Room">Which monster of the floor this is (1-based), of <paramref name="RoomsOnFloor"/>.</param>
/// <param name="AttackAtMs">The monster's hourglass runs out at this time. Null while it's stopped (buttons dead).</param>
/// <param name="DownedUntilMs">Set while they're downed.</param>
/// <param name="LastHit">The last hit on the monster by anyone in the party; its Number changes with each hit, so screens can animate it once.</param>
/// <param name="LastDealt">The last hit they landed themselves (for "you hit it for 30").</param>
/// <param name="LastTaken">The last hit they took; numbered like LastHit.</param>
/// <param name="Path">Every monster of the Depth they're in, to its boss, in order. Ones before (Floor, Room) are beaten.</param>
/// <param name="ReviveProgress">While they're downed in a party: teammates' right answers so far, of <paramref name="ReviveNeeded"/>.</param>
/// <param name="Party">Everyone in their party, them included (just them in a party of one).</param>
/// <param name="PowerUp">The power-up they're holding, for the action button (null if none).</param>
/// <param name="FireballReady">Their next right answer is a fireball.</param>
/// <param name="Shielded">The next hit they take does no damage.</param>
/// <param name="FrozenUntilMs">Their hourglass is frozen until then.</param>
/// <param name="LastPowerUp">The last power-up they used; its Number changes with each use.</param>
/// <param name="Foes">Every monster still alive in the room, the one being fought (<paramref name="Monster"/>) first.</param>
/// <param name="HeroClass">The hero class they fight as ("tank", "healer"...: the hero keys).</param>
/// <param name="BossCharge">Fighting a boss: its charge for their question now, 0-based, of <paramref name="BossChargeSteps"/>
/// (the last step is full charge: a miss takes its big blow, a right answer staggers it).</param>
/// <param name="BossChargeSteps">How many steps the boss's charge cycle has; 0 when they aren't fighting a boss.</param>
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
    DealtHit? LastHit,
    DealtHit? LastDealt,
    TakenHit? LastTaken,
    IReadOnlyList<PathStepView> Path,
    int ReviveProgress,
    int ReviveNeeded,
    IReadOnlyList<PartyMemberView> Party,
    PowerUp? PowerUp,
    bool FireballReady,
    bool Shielded,
    long? FrozenUntilMs,
    PowerUpUse? LastPowerUp,
    IReadOnlyList<MonsterView> Foes,
    string HeroClass,
    int BossCharge = 0,
    int BossChargeSteps = 0);

/// <summary>One member of a party: a player, or a team in 2v2 Group.</summary>
/// <param name="ReviveProgress">While downed: teammates' right answers so far towards reviving them.</param>
public sealed record PartyMemberView(string OwnerId, string Name, int Hp, int MaxHp, long? DownedUntilMs, int ReviveProgress);

/// <summary>One monster on the path through a Depth. A pack's monsters share a Room; Slot is each one's place in it (1 = in front).</summary>
public sealed record PathStepView(int Floor, int Room, string Name, string Sprite, bool IsBoss, int Slot = 1);

/// <param name="Sprite">A built-in placeholder sprite key, or a picture path once real art exists.</param>
/// <param name="PoisonStacks">A Healer's poison on it: each stack hurts it every tick.</param>
public sealed record MonsterView(string Name, string Sprite, int Hp, int MaxHp, bool IsBoss, int PoisonStacks = 0);

/// <summary>Admin only: where one player/team is up to.</summary>
public sealed record StreamView(string OwnerId, int Number, string QuestionText, string CorrectAnswer, int Answered, int Correct);
