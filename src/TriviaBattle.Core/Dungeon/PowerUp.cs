namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The power-ups (see docs/DUNGEON-QUIZ-QUEST.md). An adventurer holds one at a time and uses it
/// with the action button, the fifth button on their podium. Monsters drop them and a streak earns
/// them; the rules are in <see cref="DungeonRun"/>. Some only drop where they make sense
/// (<see cref="DungeonRun"/>'s CanFind): a Time Crystal in a solo game, Phone a Friend with teammates, and the ones
/// aimed at a rival (Fog Bomb, Tiny Hourglass) with another party in the match (a race or a team battle).
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

    /// <summary>The next hit the monster lands on them does no damage, and their streak survives it. It also soaks a rival's Fog Bomb or Tiny Hourglass.</summary>
    Shield,

    /// <summary><see cref="DungeonSettings.TimeCrystalSeconds"/> more on the clock. Solo games only (the clock is everyone's).</summary>
    TimeCrystal,

    /// <summary>
    /// Co-op: their question also goes up on a free teammate's screen, whose own question waits meanwhile. Whoever
    /// presses first answers it, and it counts as the caller's (the hit is theirs). Over once it's answered or the
    /// caller's hourglass runs out. Kept in hand when no teammate is free.
    /// </summary>
    PhoneAFriend,

    /// <summary>Aimed at the leading rival: their answers blur for <see cref="DungeonSettings.FogBombSeconds"/> (from when their next question is up, if they're between two).</summary>
    FogBomb,

    /// <summary>Aimed at the leading rival: their hourglass runs out twice as fast, for one question (the one they're on, or their next).</summary>
    TinyHourglass,
}

/// <summary>A power-up used. <see cref="Number"/> counts up for each adventurer, so screens can show each use once.</summary>
/// <param name="TargetOwnerId">Who it went to: a Potion goes to whoever needs it most; Phone a Friend, the teammate
/// answering; a Fog Bomb or Tiny Hourglass, the rival it was thrown at; anything else, the user.</param>
/// <param name="Amount">HP healed (Potion) or seconds (Freeze, Time Crystal, Fog Bomb); 0 for the others.</param>
/// <param name="Revived">A Potion got a downed teammate back on their feet.</param>
/// <param name="Blocked">A Fog Bomb or Tiny Hourglass the rival's Shield soaked.</param>
public sealed record PowerUpUse(int Number, PowerUp PowerUp, string TargetOwnerId, int Amount, bool Revived, bool Blocked = false);

/// <summary>
/// A rival's Fog Bomb or Tiny Hourglass that came at this adventurer. <see cref="Number"/> counts up, so their screen
/// can show each once ("FOGGED by Ann!", or "BLOCKED!" when their Shield soaked it).
/// </summary>
public sealed record Sabotage(int Number, PowerUp PowerUp, string FromOwnerId, bool Blocked, DateTimeOffset At);
