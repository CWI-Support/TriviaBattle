namespace TriviaBattle.Core.Matches;

/// <summary>
/// One side's arcade clock in a match with several sides (a race: each player; a team battle: each team). Each side
/// plays on its own time: its kills and Time rewards add to it, and its mini games and reward picks hold it still,
/// without touching the other sides'. When it runs out (or the whole side is out of lives) that side is done for the
/// match: there's no continue in a race or a battle. A match with one side (solo, a party) keeps the match's own clock
/// (<see cref="MatchEngine.PhaseEndsAt"/>) instead, and its "Continue?".
/// </summary>
public sealed class PartyClock(string partyId, DateTimeOffset endsAt)
{
    public string PartyId { get; } = partyId;

    /// <summary>When this side's time runs out (pushed back while it's held still).</summary>
    public DateTimeOffset EndsAt { get; private set; } = endsAt;

    /// <summary>While it's held still: the time it shows (what was left when it stopped). Null while it runs.</summary>
    public TimeSpan? StoppedLeft { get; private set; }

    /// <summary>How long it's been held still so far (screens tell time held from time earned by it).</summary>
    public TimeSpan HeldFor { get; private set; }

    /// <summary>Out of time, or out of lives: this side's done for the match.</summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Time earned (a kill, a Time reward). Not once its time has run out (<paramref name="now"/>): a kill landing in that
    /// moment, before the tick that finishes the side, mustn't bring it back. While it's held still, the time it shows
    /// goes up too (it's earned now, not when the clock runs again).
    /// </summary>
    public void Add(double seconds, DateTimeOffset now)
    {
        if (IsDone || seconds <= 0 || now >= EndsAt)
            return;
        EndsAt += TimeSpan.FromSeconds(seconds);
        if (StoppedLeft is { } left)
            StoppedLeft = left + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// A tick: the end is pushed back by <paramref name="held"/> (the time since the last tick while one of the side is in
    /// a mini game or picking a reward, or the opening's part of it), so it stands still for that long; screens show it
    /// stopped while <paramref name="stopped"/>. Returns true if it just stopped or started.
    /// </summary>
    public bool Hold(bool stopped, TimeSpan held, DateTimeOffset now)
    {
        if (IsDone)
            return false;
        if (held > TimeSpan.Zero)
        {
            EndsAt += held;
            HeldFor += held;
        }
        if (stopped == (StoppedLeft != null))
            return false;
        StoppedLeft = stopped ? EndsAt - now : null;
        return true;
    }

    /// <summary>
    /// The side's done: its clock stops where it is (out of lives with time left, it shows that time standing still, not
    /// counting on while the side waits for the others).
    /// </summary>
    public void Finish(DateTimeOffset now)
    {
        IsDone = true;
        StoppedLeft = EndsAt > now ? EndsAt - now : TimeSpan.Zero;
    }
}
