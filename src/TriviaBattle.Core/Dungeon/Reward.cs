namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The rewards an adventurer can choose between after a floor's boss, if they answered enough of that floor's
/// questions right (<see cref="RewardSettings.Accuracy"/>). Each is offered with a few others at random
/// (<see cref="RewardSettings.Choices"/>), picked with the answer buttons, and lasts the rest of the match.
/// The same one can be picked again on a later floor: they stack. The numbers are in <see cref="RewardSettings"/>;
/// the rules that use them are in <see cref="DungeonRun"/>.
/// </summary>
public enum Reward
{
    /// <summary>More max HP (<see cref="RewardSettings.HpBonus"/> of their starting max), and a full heal.</summary>
    Vitality,

    /// <summary>Their right answers do more damage (<see cref="RewardSettings.DamageBonus"/>).</summary>
    Might,

    /// <summary>Monsters' hits do less to them (<see cref="RewardSettings.ArmorBonus"/> less each time it's picked).</summary>
    Armor,

    /// <summary>More time on the clock (<see cref="RewardSettings.TimeSeconds"/>, shared out like a kill's).</summary>
    Time,

    /// <summary>Longer to count as fast (<see cref="RewardSettings.FastSeconds"/> more): more crits, and more of their class's power.</summary>
    Focus,

    /// <summary>Their hero class's own power gets stronger (each class's number is in <see cref="RewardSettings"/>).</summary>
    Mastery,

    /// <summary>The next hit that would down them leaves them on 1 HP instead (one per pick).</summary>
    LastStand,
}

/// <summary>
/// The rewards on offer to an adventurer after a floor's boss. Their question waits (and the monster's hourglass
/// with it) until they pick one, or <see cref="EndsAt"/> picks one at random for them.
/// </summary>
/// <param name="Floor">The floor they cleared.</param>
/// <param name="OpensAt">The cards are up (after the boss's death beat): presses pick from shortly after this.</param>
/// <param name="EndsAt">Nothing picked by then: one is picked for them.</param>
/// <param name="ForMinigame">Won in a mini game (rather than earned on the floor, after its boss).</param>
public sealed record RewardOffer(int Floor, IReadOnlyList<Reward> Choices, DateTimeOffset OpensAt, DateTimeOffset EndsAt, bool ForMinigame = false);

/// <summary>A reward picked. <see cref="Number"/> counts up for each adventurer, so screens can show each pick once.</summary>
/// <param name="Amount">What it gave, as the card said (see DungeonRun.RewardAmount).</param>
/// <param name="At">When it was picked: the short beat showing it starts then.</param>
/// <param name="Picked">False when time ran out and it was picked for them.</param>
/// <param name="Offer">The offer it was picked from (null when it was given without one: the dev tools).</param>
/// <param name="Choice">Which of the offer's cards it was (0-based).</param>
public sealed record RewardTaken(int Number, Reward Reward, double Amount, int Floor, DateTimeOffset At, bool Picked,
    RewardOffer? Offer = null, int? Choice = null);

/// <summary>How an adventurer did on a floor: the floor's reward needs enough of its questions answered right.</summary>
/// <param name="Right">Right answers on the floor.</param>
/// <param name="Answered">Every question on it: right, wrong, or the hourglass ran out.</param>
/// <param name="Earned">Enough were right: a reward after the floor's boss (only known once it's cleared).</param>
public sealed record FloorTrial(int Floor, int Right, int Answered, bool Earned = false);
