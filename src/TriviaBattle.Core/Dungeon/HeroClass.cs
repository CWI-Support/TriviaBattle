namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// What a hero can do in a Dungeon fight. Every player picks one in the lobby (<see cref="Arcade.Heroes"/>:
/// the hero's key is the class's name in lower case). The rules for each are in <see cref="DungeonRun"/>, the
/// numbers in <see cref="ClassSettings"/>. A "fast" answer is one within a set time
/// (<see cref="DungeonSettings.CritWithinSeconds"/>, 3 seconds), the same for everyone except the Tank.
/// </summary>
public enum HeroClass
{
    /// <summary>A longer fast window, and takes less damage from monsters.</summary>
    Tank,

    /// <summary>Fast answers heal the party member who most needs it; with nobody hurt, they poison the monster instead (it stacks).</summary>
    Healer,

    /// <summary>Fast answers hit every monster in the room, not just the one in front.</summary>
    Wizard,

    /// <summary>Fast answers (crits) do much more damage.</summary>
    Rogue,

    /// <summary>Sees the next question before the battle beat ends; can only press when everyone else could.</summary>
    Ranger,

    /// <summary>Builds a combo: every right answer in a row does more damage. (Only the Pugilist combos.)</summary>
    Pugilist,

    /// <summary>Their patron sometimes shows the right answer as the hourglass runs low; slow answers cost them HP.</summary>
    Warlock,
}
