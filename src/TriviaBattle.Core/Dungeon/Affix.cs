namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// A twist a monster can carry deeper down (see docs/DUNGEON-QUIZ-QUEST.md, "Elite affixes"). Which monsters have one
/// is content: from config/dungeon.json's <c>AffixFromFloor</c> on, each monster that isn't a boss has
/// <c>AffixChance</c> of one (<see cref="DungeonMap.RoomAt"/>), the same for every party in the match. What each does
/// is in <see cref="DungeonRun"/>, its numbers in <see cref="AffixSettings"/>. Bosses have their charge cycle instead.
/// </summary>
public enum Affix
{
    /// <summary>Its hourglass is shorter (<see cref="AffixSettings.SpeedyHourglass"/> of its own).</summary>
    Speedy,

    /// <summary>Only fast answers get through its armour in full: a slow right answer does <see cref="AffixSettings.ArmoredShare"/> of its damage.</summary>
    Armored,

    /// <summary>When it hits someone it heals by what the hit took (<see cref="AffixSettings.VampiricShare"/> of it), up to its max.</summary>
    Vampiric,
}

/// <summary>What a monster's affix does. Bound from "Game:Dungeon:Affixes" in appsettings.json.</summary>
public sealed class AffixSettings
{
    /// <summary>A Speedy monster's hourglass, as a share of its own (0.6: 8 seconds become 4.8).</summary>
    public double SpeedyHourglass { get; set; } = 0.6;

    /// <summary>An Armored monster takes this share of a slow right answer's damage (fast answers, crits, get through in full).</summary>
    public double ArmoredShare { get; set; } = 0.5;

    /// <summary>A Vampiric monster heals by this share of the HP its hit took.</summary>
    public double VampiricShare { get; set; } = 1;

    public AffixSettings Copy() => (AffixSettings)MemberwiseClone();
}
