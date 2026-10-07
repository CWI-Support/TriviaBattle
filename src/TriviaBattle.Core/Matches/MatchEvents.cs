namespace TriviaBattle.Core.Matches;

/// <summary>
/// Something noteworthy that just happened in a match. The engine returns these from every
/// call so the server knows when to push new state to screens and which one-off animations
/// to trigger. Screens never need events to be correct; the state snapshot is the source of truth.
/// </summary>
public abstract record MatchEvent;

public sealed record PhaseChanged(MatchPhase Phase) : MatchEvent;

public sealed record AnswerLocked(LockedAnswer Answer) : MatchEvent;

public sealed record RoundRevealed(int RoundNumber, IReadOnlyList<RoundResult> Results) : MatchEvent;

public sealed record MatchFinished(bool WasAborted) : MatchEvent;

/// <summary>
/// A player or team made a leaderboard. Unlike the events above, this one isn't raised by the
/// engine: the server raises it after saving the match result, while Results is on screen.
/// </summary>
/// <param name="Board">"all-time" or "today".</param>
public sealed record LeaderboardPlacement(string OwnerId, string Names, int Score, int Rank, string Board) : MatchEvent;
