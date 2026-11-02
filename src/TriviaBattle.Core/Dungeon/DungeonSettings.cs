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

    // A solo game runs on an arcade clock: it starts at SoloStartSeconds and every monster slain adds
    // time, up to SoloMaxSeconds of play in all. When it runs out the player can continue (a swipe)
    // for SoloContinueSeconds more. Groups play a fixed round (MatchSettings.MatchSeconds) so everyone
    // starts and ends together. All of these can be changed on the Operator screen.

    public double SoloStartSeconds { get; set; } = 90;
    public double SoloSecondsPerKill { get; set; } = 10;

    /// <summary>A boss kill adds this instead of <see cref="SoloSecondsPerKill"/>.</summary>
    public double SoloSecondsPerBoss { get; set; } = 30;

    /// <summary>The hard cap: a solo game's clock never runs longer than this in all, continues included.</summary>
    public double SoloMaxSeconds { get; set; } = 480;

    /// <summary>
    /// The time a continue adds. A continue is only offered while a whole one still fits under
    /// <see cref="SoloMaxSeconds"/>. 0 = no continues.
    /// </summary>
    public double SoloContinueSeconds { get; set; } = 60;

    /// <summary>How long "Continue?" waits for a swipe before going to the results.</summary>
    public double SoloContinueCountdownSeconds { get; set; } = 10;

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

    /// <summary>The time a Time Crystal adds to a solo game's clock (up to the hard cap).</summary>
    public double TimeCrystalSeconds { get; set; } = 20;

    /// <summary>
    /// Hero photos: during the intro, the action button puts your face on your hero (the station's camera
    /// takes it after a countdown). Off for a venue without station cameras, or one that doesn't want photos.
    /// </summary>
    public bool HeroPhotos { get; set; } = true;

    /// <summary>The "3, 2, 1" before the station takes a hero photo.</summary>
    public double PhotoCountdownSeconds { get; set; } = 3;

    public int DamageFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => EasyDamage,
        Difficulty.Hard => HardDamage,
        _ => MediumDamage,
    };

    public DungeonSettings Copy() => (DungeonSettings)MemberwiseClone();
}
