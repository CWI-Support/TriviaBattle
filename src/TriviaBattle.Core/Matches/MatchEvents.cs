using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Something noteworthy that just happened in a match. The engine returns these from every
/// call so the server knows when to push new state to screens and which one-off animations
/// to trigger. Screens never need events to be correct; the state snapshot is the source of truth.
/// </summary>
public abstract record MatchEvent;

public sealed record PhaseChanged(MatchPhase Phase) : MatchEvent;

/// <summary>Someone answered; their next question is now showing.</summary>
public sealed record AnswerGiven(GivenAnswer Answer) : MatchEvent;

/// <summary>A wrong-answer lockout ran out, so that owner's buttons work again.</summary>
public sealed record LockoutEnded(string OwnerId) : MatchEvent;

public sealed record MatchFinished(bool WasAborted) : MatchEvent;

/// <summary>Dungeon format: a correct answer hit that owner's monster.</summary>
public sealed record MonsterHit(string OwnerId, DealtHit Hit) : MatchEvent;

/// <summary>Dungeon format: the monster hit back (a wrong answer, or its hourglass ran out).</summary>
public sealed record AdventurerHit(string OwnerId, TakenHit Hit) : MatchEvent;

/// <summary>Dungeon format: a downed adventurer is back on their feet (their time ran out, or teammates revived them).</summary>
public sealed record AdventurerGotUp(string OwnerId) : MatchEvent;

/// <summary>
/// Dungeon format: a battle beat ended and the owner's next question is showing, so their buttons count
/// again (the button LEDs come back on). Raised by Tick; nothing in the snapshot changes.
/// </summary>
public sealed record QuestionShown(string OwnerId) : MatchEvent;

/// <summary>Dungeon format: an adventurer picked up a power-up (a monster dropped it, or their streak earned it).</summary>
public sealed record PowerUpFound(string OwnerId, PowerUp PowerUp) : MatchEvent;

/// <summary>Dungeon format: an adventurer used their power-up with the action button.</summary>
public sealed record PowerUpUsed(string OwnerId, PowerUpUse Use) : MatchEvent;

/// <summary>
/// Dungeon format: a Healer's poison hurt a party's monsters (raised by Tick, once per tick). With
/// <paramref name="Kill"/> it killed the one in front and the next steps up. Nobody scores for poison damage.
/// </summary>
public sealed record MonsterPoisoned(string PartyId, int Damage, DealtHit? Kill) : MatchEvent;

/// <summary>Dungeon format: a Warlock's patron is showing them the right answer (raised by Tick; only their own screen sees it).</summary>
public sealed record PatronRevealed(string OwnerId) : MatchEvent;

/// <summary>Dungeon format: a floor's boss died and this adventurer answered enough of the floor right: they're choosing a reward (see <see cref="Reward"/>).</summary>
public sealed record RewardOffered(string OwnerId, RewardOffer Offer) : MatchEvent;

/// <summary>Dungeon format: a kill emptied the room before the floor's mini game, and this adventurer's go at it begins (its intro first).</summary>
public sealed record MinigameStarted(string OwnerId, MinigameKind Kind) : MatchEvent;

/// <summary>Dungeon format: a press in a mini game counted (a hit, a miss, a round won, or the hit that won it).</summary>
/// <param name="ShowsInSeconds">A win (or a round won): how long until the screen shows it (<see cref="Minigame.WinPauseSeconds"/>), so its sound lands then.</param>
public sealed record MinigamePlayed(string OwnerId, MinigamePress Result, double ShowsInSeconds = 0) : MatchEvent;

/// <summary>
/// Dungeon format: an adventurer's hourglass reached another of its last seconds (3, 2, 1: DungeonSettings.HourglassCountdownSeconds),
/// raised by Tick. The room counts it down; the screens draw the same moment from DelveView.CountdownFromMs.
/// </summary>
public sealed record HourglassCountdown(string OwnerId, int SecondsLeft) : MatchEvent;

/// <summary>Dungeon format: a mini game's time limit ran out before it was won (raised by Tick).</summary>
public sealed record MinigameLost(string OwnerId) : MatchEvent;

/// <summary>Dungeon format: a mini game's result has been shown: back to the fight, or (won) to a reward pick (raised by Tick).</summary>
public sealed record MinigameOver(string OwnerId) : MatchEvent;

/// <summary>Dungeon format: an adventurer picked a reward (or time ran out and one was picked for them).</summary>
public sealed record RewardChosen(string OwnerId, RewardTaken Taken) : MatchEvent;

/// <summary>Dungeon format: in the intro, a player said yes to a hero photo (when photos are asked for first).</summary>
public sealed record PhotoAsked(string StationId) : MatchEvent;

/// <summary>Dungeon format: a player said no to a hero photo (the action button in the intro). Any taken is dropped.</summary>
public sealed record PhotoDeclined(string StationId) : MatchEvent;

/// <summary>
/// Dungeon format: a second of the station's hero photo countdown (3, 2, 1), or the moment it's taken (0).
/// Raised by Tick, only where the camera is on; nothing in the snapshot changes.
/// </summary>
public sealed record PhotoCountdown(string StationId, int SecondsLeft) : MatchEvent;

/// <summary>Dungeon format: a player's hero photo arrived, so screens can put their face on their hero.</summary>
public sealed record PhotoTaken(string StationId) : MatchEvent;

/// <summary>
/// A player or team made a leaderboard. Unlike the events above, this one isn't raised by the
/// engine: the server raises it after saving the match result, while Results is on screen.
/// </summary>
/// <param name="Board">"all-time" or "today".</param>
public sealed record LeaderboardPlacement(string OwnerId, string Names, int Score, int Rank, string Board) : MatchEvent;
