using TriviaBattle.Core.Dungeon;

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
/// A player or team made a leaderboard. Unlike the events above, this one isn't raised by the
/// engine: the server raises it after saving the match result, while Results is on screen.
/// </summary>
/// <param name="Board">"all-time" or "today".</param>
public sealed record LeaderboardPlacement(string OwnerId, string Names, int Score, int Rank, string Board) : MatchEvent;
