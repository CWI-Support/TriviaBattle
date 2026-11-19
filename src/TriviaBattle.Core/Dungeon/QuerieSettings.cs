namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// How chatty Q.U.E.R.I.E. is at the stations (<see cref="Querie"/>). Bound from "Game:Dungeon:Querie"; his lines are
/// config/querie.json. Ordinary moments (a miss, a slow answer, a right answer) only get a remark now and then; big
/// ones (a third miss in a row, the last life, the odd one out in a party) whenever they happen.
/// </summary>
public sealed class QuerieSettings
{
    /// <summary>Off: he says nothing at the stations (the plain line shows).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>After a remark to a player, the least time before an ordinary one to them again.</summary>
    public double QuipEverySeconds { get; set; } = 11;

    /// <summary>Big moments don't wait for that, but never come closer together than this.</summary>
    public double BigGapSeconds { get; set; } = 4;

    /// <summary>In a party, the least time between two remarks the whole party hears.</summary>
    public double PartyCalloutEverySeconds { get; set; } = 15;

    /// <summary>How likely an ordinary moment is to get a remark (once the gap has passed): 0-1.</summary>
    public double Chance { get; set; } = 0.33;

    /// <summary>The same for a right answer while safe: he mostly saves his enthusiasm for when death is near.</summary>
    public double SafeChance { get; set; } = 0.12;

    /// <summary>An answer this long after its question came up (or later) is a slow one (the hourglass is 8 s).</summary>
    public double SlowSeconds { get; set; } = 5.5;

    /// <summary>Wrong (or too slow) this many times in a row is a big moment.</summary>
    public int WrongStreakAt { get; set; } = 3;

    /// <summary>A party member's last answer counts as "just now" (for the odd one out, carrying) within this long.</summary>
    public double PartyWindowSeconds { get; set; } = 15;

    /// <summary>From this floor down, every right answer is a little closer to death (it counts towards doomed).</summary>
    public int DeepFloor { get; set; } = 4;

    public QuerieSettings Copy() => (QuerieSettings)MemberwiseClone();
}
