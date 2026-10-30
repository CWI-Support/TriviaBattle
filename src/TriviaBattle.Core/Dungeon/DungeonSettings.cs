using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The combat numbers for Dungeon format. Bound from "Game:Dungeon" in appsettings.json.
/// Monsters' own numbers (HP, damage, hourglass) are in config/dungeon.json.
/// </summary>
public sealed class DungeonSettings
{
    /// <summary>Each adventurer's (or party's, in 2v2 Group) hit points.</summary>
    public int AdventurerHp { get; set; } = 100;

    // Damage a correct answer does, by the question's difficulty.
    public int EasyDamage { get; set; } = 20;
    public int MediumDamage { get; set; } = 30;
    public int HardDamage { get; set; } = 40;

    /// <summary>A correct answer this fast is a critical hit.</summary>
    public double CritWithinSeconds { get; set; } = 3;

    /// <summary>A crit does this many times the damage.</summary>
    public double CritMultiplier { get; set; } = 1.5;

    /// <summary>From this many correct answers in a row, every hit is a combo.</summary>
    public int ComboStreak { get; set; } = 3;

    /// <summary>A combo adds this share of the damage on top (0.5 = +50%).</summary>
    public double ComboBonus { get; set; } = 0.5;

    /// <summary>
    /// At 0 HP an adventurer is downed: their buttons are dead this long, then they get back up at
    /// full HP. The monster heals to full meanwhile. Nobody is knocked out of a paid game.
    /// </summary>
    public double DownedSeconds { get; set; } = 5;

    public int DamageFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => EasyDamage,
        Difficulty.Hard => HardDamage,
        _ => MediumDamage,
    };
}
