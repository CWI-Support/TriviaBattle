namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The numbers behind floor rewards (see <see cref="Reward"/>). Bound from "Game:Dungeon:Rewards" in appsettings.json.
///
/// After a floor's boss dies, every adventurer in the party who answered at least <see cref="Accuracy"/> of that
/// floor's questions right picks one of <see cref="Choices"/> rewards with their answer buttons. It's a share, not
/// a count, because killing the boss already takes a set number of right answers: a count would be met by
/// everyone who gets there. The arcade clock stops while they choose (DungeonRun.ClockStopped).
/// </summary>
public sealed class RewardSettings
{
    /// <summary>Rewards on or off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The share of a floor's questions they need right (0.8 = 80%; wrong answers and the hourglass running out count against it).</summary>
    public double Accuracy { get; set; } = 0.8;

    /// <summary>How many rewards are offered at once (the answer buttons pick them, so 2 to 4).</summary>
    public int Choices { get; set; } = 3;

    /// <summary>How long they have to pick. Then one is picked for them at random.</summary>
    public double PickSeconds { get; set; } = 12;

    /// <summary>Presses only pick once the cards have been up this long, so an answer meant for the last question doesn't take one.</summary>
    public double PickDelaySeconds { get; set; } = 1;

    /// <summary>
    /// The moment after a pick that shows what they got, before their next question: the card picked lights up as the
    /// others fall away, then the reward, big, with what it does (long enough to read it).
    /// </summary>
    public double BeatSeconds { get; set; } = 2.8;

    // What each reward does (each pick adds it again).

    /// <summary>Vitality: max HP goes up by this share of their starting max (and they're healed to full).</summary>
    public double HpBonus { get; set; } = 0.25;

    /// <summary>Might: right answers do this share more damage.</summary>
    public double DamageBonus { get; set; } = 0.2;

    /// <summary>Armor: monsters' hits do this share less (two picks: 0.85 x 0.85).</summary>
    public double ArmorBonus { get; set; } = 0.15;

    /// <summary>Time: seconds on the clock (a party's are shared out like a kill's: see DungeonRun.SecondsEarnedBy).</summary>
    public double TimeSeconds { get; set; } = 20;

    /// <summary>Focus: this many seconds longer to count as fast.</summary>
    public double FastSeconds { get; set; } = 1;

    // Mastery: each class's own power, per pick.

    /// <summary>A Rogue's crit multiplier goes up by this.</summary>
    public double RogueCrit { get; set; } = 0.5;

    /// <summary>A Tank takes this share less damage (on top of any Armor).</summary>
    public double TankArmor { get; set; } = 0.15;

    /// <summary>A Healer's heal does this much more HP.</summary>
    public int HealerHeal { get; set; } = 10;

    /// <summary>A Wizard's splash does this share more of the hit (never more than all of it).</summary>
    public double WizardSplash { get; set; } = 0.25;

    /// <summary>A Pugilist's combo adds this much more per answer in a row (and its cap goes up 10 times as much).</summary>
    public double PugilistComboStep { get; set; } = 0.05;

    /// <summary>A Warlock's patron is this much likelier to show the answer (never more than always).</summary>
    public double WarlockReveal { get; set; } = 0.2;

    /// <summary>A Ranger sees questions this many seconds sooner.</summary>
    public double RangerPeek { get; set; } = 1;

    public RewardSettings Copy() => (RewardSettings)MemberwiseClone();
}
