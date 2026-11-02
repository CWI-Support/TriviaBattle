namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The power-ups (v1, see docs/DUNGEON-QUIZ-QUEST.md). An adventurer holds one at a time and uses it
/// with the action button, the fifth button on their podium. Monsters drop them and a streak earns
/// them; the rules are in <see cref="DungeonRun"/>.
/// </summary>
public enum PowerUp
{
    /// <summary>Goes to whoever in the party needs it most: a downed teammate gets straight back up, otherwise the lowest on HP is healed.</summary>
    Potion,

    /// <summary>Their next right answer does <see cref="DungeonSettings.FireballMultiplier"/> times the damage.</summary>
    Fireball,

    /// <summary>The monster's hourglass stops for <see cref="DungeonSettings.FreezeSeconds"/>, then starts over.</summary>
    Freeze,

    /// <summary>Two wrong answers of the question they're on disappear (and can't be pressed).</summary>
    FiftyFifty,

    /// <summary>The next hit the monster lands on them does no damage, and their streak survives it.</summary>
    Shield,

    /// <summary>Solo games only: <see cref="DungeonSettings.TimeCrystalSeconds"/> more on the clock.</summary>
    TimeCrystal,
}

/// <summary>A power-up used. <see cref="Number"/> counts up for each adventurer, so screens can show each use once.</summary>
/// <param name="TargetOwnerId">Who it went to: a Potion goes to whoever needs it most; anything else, the user.</param>
/// <param name="Amount">HP healed (Potion) or seconds (Freeze, Time Crystal); 0 for the others.</param>
/// <param name="Revived">A Potion got a downed teammate back on their feet.</param>
public sealed record PowerUpUse(int Number, PowerUp PowerUp, string TargetOwnerId, int Amount, bool Revived);
