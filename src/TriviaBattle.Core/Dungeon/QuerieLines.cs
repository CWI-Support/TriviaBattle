namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The moments Q.U.E.R.I.E. (the dungeon's host) has something to say about at a station (<see cref="Querie"/> decides
/// when). Each has its own lines in config/querie.json, under these names in camelCase ("wrongStreak").
/// </summary>
public enum QuerieMoment
{
    /// <summary>A wrong answer.</summary>
    Wrong,

    /// <summary>The hourglass ran out, or they answered (right or wrong) in its last seconds.</summary>
    Slow,

    /// <summary>Wrong (or too slow) several times in a row: a big moment.</summary>
    WrongStreak,

    /// <summary>A right answer while they're safe: rarely remarked on.</summary>
    RightSafe,

    /// <summary>A right answer with death getting closer (hurt, a boss, deep down): sarcastic enthusiasm.</summary>
    RightUneasy,

    /// <summary>A right answer with death close (badly hurt, the last life, a boss): sarcastic enthusiasm, laid on thick.</summary>
    RightDoomed,

    /// <summary>Downed to their last life: a big moment.</summary>
    LastLife,

    /// <summary>In a party: the others got theirs right and this one didn't. A big moment the whole party hears.</summary>
    OddOneOut,

    /// <summary>In a party: right while the others flounder.</summary>
    Carrying,

    /// <summary>Downed in a party (not on their last life): a big moment the whole party hears.</summary>
    DownedInParty,

    /// <summary>A race or a team battle: a rival is a floor (or more) ahead.</summary>
    BehindRival,
}

/// <summary>
/// One of Q.U.E.R.I.E.'s lines. <see cref="Audio"/>: a recorded clip of it (a path under media/audio), for when the lines
/// are voiced; null until then (the line is shown, not heard). A line with {name} can't be recorded for everyone.
/// </summary>
public sealed record QuerieLine(string Text, string? Audio = null);

/// <summary>
/// Q.U.E.R.I.E.'s lines for each moment: config/querie.json, loaded by the server at startup (QuerieContent). A line
/// may use the <see cref="Placeholders"/>, filled in when it's said. Replace or add lines there; no code changes.
/// </summary>
public sealed class QuerieLines(IReadOnlyDictionary<QuerieMoment, IReadOnlyList<QuerieLine>> lines)
{
    /// <summary>{name}: who it's said about; {monster}: the monster they're fighting; {floor}: their floor; {rival}: the rival ahead.</summary>
    public static readonly IReadOnlyList<string> Placeholders = ["name", "monster", "floor", "rival"];

    /// <summary>No lines at all: Q.U.E.R.I.E. stays quiet at the stations (the tests' default).</summary>
    public static readonly QuerieLines None = new(new Dictionary<QuerieMoment, IReadOnlyList<QuerieLine>>());

    public IReadOnlyList<QuerieLine> For(QuerieMoment moment) => lines.GetValueOrDefault(moment) ?? [];

    public bool IsEmpty => lines.Values.All(l => l.Count == 0);
}
