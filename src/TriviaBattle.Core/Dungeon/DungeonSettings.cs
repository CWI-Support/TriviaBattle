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

    /// <summary>
    /// A correct answer this fast is a fast answer: a critical hit, and what sets off the Healer's, Wizard's
    /// and Rogue's powers. A set time, the same for every question (the Tank gets longer: <see cref="ClassSettings.TankFastSeconds"/>).
    /// </summary>
    public double CritWithinSeconds { get; set; } = 3;

    /// <summary>A crit does this many times the damage (a Rogue's: <see cref="ClassSettings.RogueCritMultiplier"/>).</summary>
    public double CritMultiplier { get; set; } = 1.5;

    /// <summary>What each hero class can do (combos are the Pugilist's). Bound from "Game:Dungeon:Classes".</summary>
    public ClassSettings Classes { get; set; } = new();

    /// <summary>
    /// At 0 HP an adventurer is downed: their buttons are dead this long, then they get back up at
    /// full HP. When the whole party is down the monster heals. Nobody is knocked out of a paid game.
    /// </summary>
    public double DownedSeconds { get; set; } = 5;

    // Battle beats: after each answer the next question waits a moment while the screen shows the fight
    // (your hit, the monster's attack, a kill, the way down to the next floor). Buttons are dead and the
    // monster's hourglass waits meanwhile. The clocks keep running: time per kill and the round length
    // allow for it. 0 = no beat (the next question at once).

    public double HitBeatSeconds { get; set; } = 1.5;
    public double HurtBeatSeconds { get; set; } = 1.7;
    public double KillBeatSeconds { get; set; } = 2;

    /// <summary>A floor cleared: the way down, or the next floor's boss stepping up.</summary>
    public double FloorBeatSeconds { get; set; } = 3;

    // In a party of two or more, a downed adventurer waits for their teammates: ReviveAnswers right
    // answers from them bring them back. PartyDownedSeconds is the safety net if nobody manages it.
    // When the whole party is down, everyone gets up after DownedSeconds.

    public int ReviveAnswers { get; set; } = 3;
    public double PartyDownedSeconds { get; set; } = 20;

    /// <summary>A party combo: everyone standing answers right within this many seconds of each other.</summary>
    public double PartyComboSeconds { get; set; } = 4;

    /// <summary>The hit that completes a party combo adds this share of the damage on top (1 = double).</summary>
    public double PartyComboBonus { get; set; } = 1;

    // Bosses power up. Against a boss, each adventurer's questions go round a cycle of BossChargeSteps: the
    // boss starts weak (its hit does BossWeakHit of its damage), builds up question by question to its normal
    // damage, and on the last step is at full charge: a miss (or too slow) then takes BossChargedHit of its
    // damage, a right answer staggers it (StaggerBonus more damage). Either way the cycle starts over. With the
    // default 3 steps the boss hits 50%, then 100%, then 250%. 0 or 1 steps = no cycle (always normal).

    public int BossChargeSteps { get; set; } = 3;
    public double BossWeakHit { get; set; } = 0.5;
    public double BossChargedHit { get; set; } = 2.5;
    public double StaggerBonus { get; set; } = 0.5;

    /// <summary>
    /// How hard a boss hits on a step of its charge (0-based): from <see cref="BossWeakHit"/> on the first step up to
    /// normal (1) on the one before full, then <see cref="BossChargedHit"/> at full charge. 1 with no cycle.
    /// </summary>
    public double BossHitAt(int step)
    {
        if (BossChargeSteps < 2)
            return 1;
        if (step >= BossChargeSteps - 1)
            return BossChargedHit;
        var building = BossChargeSteps - 2; // steps before full, after the first
        return building == 0 ? BossWeakHit : BossWeakHit + (1 - BossWeakHit) * step / building;
    }

    // Every Dungeon game runs on an arcade clock: one paid session starts at ClockStartSeconds and every
    // monster slain adds time, so the better you play, the longer you play: there's no cap, the session lasts
    // until the clock runs out. The defaults aim at 3-4 minutes for an average player: about two floors, two bosses. A party
    // kills faster, so its time per kill is scaled down to match (DungeonRun.SecondsEarnedBy). When the clock
    // runs out, a card swipe continues: a second paid session, ContinueSeconds long, picking up where they
    // left off. All of these can be changed on the Operator screen.

    public double ClockStartSeconds { get; set; } = 140;
    public double SecondsPerKill { get; set; } = 5;

    /// <summary>A boss kill adds this instead of <see cref="SecondsPerKill"/>.</summary>
    public double SecondsPerBoss { get; set; } = 15;

    /// <summary>A continue's clock: a new paid session's starting time, on the same floor with the same HP. 0 = no continues.</summary>
    public double ContinueSeconds { get; set; } = 140;

    /// <summary>How long "Continue?" waits for a swipe before going to the results.</summary>
    public double ContinueCountdownSeconds { get; set; } = 15;

    /// <summary>
    /// Monsters are tougher for a party: each member after the first adds this share of a monster's HP
    /// (0.75: two players face 175%, four face 325%). Time per kill is scaled to match.
    /// </summary>
    public double PartyHpPerMember { get; set; } = 0.75;

    // Power-ups (see PowerUp). One held at a time, used with the action button. A kill drops one
    // PowerUpDropChance of the time (a boss always), and every PowerUpStreak right answers in a row earn one.

    public double PowerUpDropChance { get; set; } = 0.35;
    public int PowerUpStreak { get; set; } = 5;

    /// <summary>The HP a Potion heals.</summary>
    public int PotionHeal { get; set; } = 50;

    /// <summary>A Fireball multiplies the next right answer's damage by this.</summary>
    public double FireballMultiplier { get; set; } = 3;

    /// <summary>How long a Freeze stops the monster's hourglass.</summary>
    public double FreezeSeconds { get; set; } = 10;

    /// <summary>The time a Time Crystal adds to the clock.</summary>
    public double TimeCrystalSeconds { get; set; } = 20;

    /// <summary>
    /// Hero photos: the station's camera puts your face on your hero during the intro. Off for a venue without
    /// station cameras, or one that doesn't want photos.
    /// </summary>
    public bool HeroPhotos { get; set; } = true;

    /// <summary>
    /// True: every player's photo is taken unless they press the action button to say no (they see the camera
    /// and the countdown first). False: only players who press it to say yes get one. Some venues and regions
    /// need the second (ask first), especially where children play.
    /// </summary>
    public bool PhotoByDefault { get; set; } = true;

    /// <summary>The "3, 2, 1" before the station takes a hero photo.</summary>
    public double PhotoCountdownSeconds { get; set; } = 3;

    public int DamageFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => EasyDamage,
        Difficulty.Hard => HardDamage,
        _ => MediumDamage,
    };

    public DungeonSettings Copy()
    {
        var copy = (DungeonSettings)MemberwiseClone();
        copy.Classes = Classes.Copy(); // its own, so changing one match's numbers never changes another's
        return copy;
    }
}
