namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// A hero class's basic stats, as shares of the normal numbers (1 = normal, 1.2 = 20% more, 0.6 = 40% less).
/// Every class has one (<see cref="ClassSettings.StatsFor"/>), so they can all be tuned the same way, and later
/// upgrades between floors can raise the same numbers.
/// </summary>
public sealed class ClassStats
{
    /// <summary>Max HP, as a share of <see cref="DungeonSettings.AdventurerHp"/>.</summary>
    public double Hp { get; set; } = 1;

    /// <summary>Damage their right answers do, as a share of the normal (by difficulty, before crits and combos; a splash follows it).</summary>
    public double DamageDealt { get; set; } = 1;

    /// <summary>Damage they take from a monster's hit, as a share of the monster's (rounded).</summary>
    public double DamageTaken { get; set; } = 1;

    public ClassStats Copy() => (ClassStats)MemberwiseClone();
}

/// <summary>
/// The numbers behind each <see cref="HeroClass"/>: its basic stats (HP, damage dealt, damage taken) and its
/// power. Bound from "Game:Dungeon:Classes" in appsettings.json, e.g. <c>"Tank": { "Hp": 1.2, "DamageTaken": 0.6 }</c>
/// (every one has a default here). The rules that use them are in <see cref="DungeonRun"/>.
/// </summary>
public sealed class ClassSettings
{
    // Each class's basic stats. All normal, except that monsters hit the Tank softer.

    public ClassStats Tank { get; set; } = new() { DamageTaken = 0.6 };
    public ClassStats Healer { get; set; } = new();
    public ClassStats Wizard { get; set; } = new();
    public ClassStats Rogue { get; set; } = new();
    public ClassStats Ranger { get; set; } = new();
    public ClassStats Pugilist { get; set; } = new();
    public ClassStats Warlock { get; set; } = new();

    /// <summary>A class's basic stats.</summary>
    public ClassStats StatsFor(HeroClass heroClass) => heroClass switch
    {
        HeroClass.Tank => Tank,
        HeroClass.Healer => Healer,
        HeroClass.Wizard => Wizard,
        HeroClass.Rogue => Rogue,
        HeroClass.Ranger => Ranger,
        HeroClass.Pugilist => Pugilist,
        HeroClass.Warlock => Warlock,
        _ => throw new ArgumentOutOfRangeException(nameof(heroClass), heroClass, "A hero class needs its stats here."),
    };

    // Rogue: fast answers (crits) hit much harder.

    /// <summary>A Rogue's crit does this many times the damage (everyone else: <see cref="DungeonSettings.CritMultiplier"/>).</summary>
    public double RogueCritMultiplier { get; set; } = 2.5;

    // Tank: more time to count as fast (and it takes less damage: Tank.DamageTaken above).

    /// <summary>A Tank's answer this fast is still a fast answer (a crit). Everyone else: <see cref="DungeonSettings.CritWithinSeconds"/>.</summary>
    public double TankFastSeconds { get; set; } = 5;

    // Healer: a fast answer heals whoever standing in the party is lowest on HP (themselves included).
    // With nobody standing hurt, it poisons the monster in front instead: one more stack each time. Every
    // stack does PoisonDamage every PoisonTickSeconds until that monster dies. Poison can kill.

    public int HealerHeal { get; set; } = 15;
    public int PoisonDamage { get; set; } = 2;
    public double PoisonTickSeconds { get; set; } = 1;

    // Wizard: a fast answer hits every monster in the room. The one in front takes the full hit, every
    // other one this share of it.

    public double WizardSplash { get; set; } = 0.5;

    // Warlock: when their hourglass gets down to WarlockRevealSecondsLeft, their patron shows the right
    // answer WarlockRevealChance of the time (decided once per question). Any answer (right or wrong)
    // after WarlockSlowSeconds costs them WarlockSlowDamage HP: the patron's price. It never downs them.

    public double WarlockRevealChance { get; set; } = 0.5;
    public double WarlockRevealSecondsLeft { get; set; } = 4;
    public double WarlockSlowSeconds { get; set; } = 6;
    public int WarlockSlowDamage { get; set; } = 8;

    // Pugilist: the only class with a combo. Each right answer in a row after the first adds
    // PugilistComboStep to the damage (0.15 = +15%), up to PugilistComboMax on top.

    public double PugilistComboStep { get; set; } = 0.15;
    public double PugilistComboMax { get; set; } = 1.5;

    // Ranger: during the battle beat after an answer, their next question shows this many seconds before
    // the beat ends. Buttons still only work, and the hourglass and the fast window only start, when it ends.

    public double RangerPeekSeconds { get; set; } = 1.5;

    /// <summary>A copy with its own stats, so changing one match's numbers never changes another's.</summary>
    public ClassSettings Copy()
    {
        var copy = (ClassSettings)MemberwiseClone();
        copy.Tank = Tank.Copy();
        copy.Healer = Healer.Copy();
        copy.Wizard = Wizard.Copy();
        copy.Rogue = Rogue.Copy();
        copy.Ranger = Ranger.Copy();
        copy.Pugilist = Pugilist.Copy();
        copy.Warlock = Warlock.Copy();
        return copy;
    }
}
