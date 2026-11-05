using Microsoft.Extensions.Configuration;
using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// What each hero class does in a fight (HeroClass, ClassSettings). Uses TestMatch.Dungeon unless a test needs
// a tougher monster: floor 1 = two rats (40 HP, hit for 10, 10-second hourglass), floor 2 = a boss.
// Easy questions do 20 damage; a fast answer (within 3 seconds; the Tank's 5) crits for x1.5.
// TestMatch.Press waits 1 second by default (fast), so slow presses wait 4.
public class HeroClassTests
{
    /// <summary>One monster with lots of HP that hits softly, so a test can keep hitting without killing it.</summary>
    private static readonly DungeonMap OgreDungeon = new(
        [new DepthDef("ogre", "Ogre Den", [new FloorDef(["ogre"])])],
        [new MonsterDef("ogre", "Ogre", "ogre", Hp: 1000, Damage: 10, AttackSeconds: 10)]);

    private static TestMatch Solo(string hero, Action<MatchSettings>? configure = null, DungeonMap? dungeon = null, bool battleBeats = false)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300, LockoutSeconds = 1 };
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], heroes: new Dictionary<string, string> { ["A1"] = hero },
            dungeon: dungeon, battleBeats: battleBeats);
        match.StartPlaying();
        return match;
    }

    /// <summary>2v2 Combined: Red = A1 + A3 share one monster, each with their own HP.</summary>
    private static TestMatch Party(Dictionary<string, string> heroes, Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300, LockoutSeconds = 1 };
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.TeamCombinedScore, settings, heroes: heroes);
        match.StartPlaying();
        return match;
    }

    private static QuestionView QuestionOn(TestMatch match, string stationId) =>
        SnapshotBuilder.Build(match.Engine, Viewer.Player(stationId)).You!.Question!;

    // ---------------------------------------------------------------- Every class's basic stats

    [Fact]
    public void A_class_s_stats_set_its_HP_the_damage_it_deals_and_the_damage_it_takes()
    {
        var match = Solo("wizard", s => s.Dungeon.Classes.Wizard = new ClassStats { Hp = 0.8, DamageDealt = 1.5, DamageTaken = 2 });
        Assert.Equal(80, match.AdventurerOf("A1").MaxHp); // 100 x 0.8

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal(30, match.AdventurerOf("A1").LastDealt!.Damage); // 20 x 1.5

        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(20, match.AdventurerOf("A1").LastTaken!.Damage); // a rat's 10 x 2
    }

    [Fact]
    public void Damage_dealt_comes_before_the_crit_and_the_splash_follows_it()
    {
        var match = Solo("wizard", s => s.Dungeon.Classes.Wizard.DamageDealt = 2, dungeon: RatPack);

        match.Press("A1", 0, afterSeconds: 1);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.Equal(60, hit.Damage);                             // 20 x 2, then x1.5 for the crit
        Assert.Equal([30, 30], hit.Splashes!.Select(s => s.Damage)); // half of that
    }

    [Fact]
    public void The_class_stats_in_appsettings_json_reach_the_game()
    {
        var file = Path.Combine(RepoRoot(), "src", "TriviaBattle.Server", "appsettings.json");
        var config = new ConfigurationBuilder().AddJsonFile(file).Build();

        var settings = config.GetSection("Game").Get<MatchSettings>()!;

        Assert.Equal(0.6, settings.Dungeon.Classes.Tank.DamageTaken);
        Assert.Equal(1, settings.Dungeon.Classes.Warlock.Hp);
        Assert.Equal(2.5, settings.Dungeon.Classes.RogueCritMultiplier); // not in the file: the default stays
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void Each_match_has_its_own_copy_of_the_class_stats()
    {
        var configured = new MatchSettings();

        var copy = configured.Copy();
        copy.Dungeon.Classes.Tank.DamageTaken = 0.1;

        Assert.Equal(0.6, configured.Dungeon.Classes.Tank.DamageTaken);
    }

    [Fact]
    public void Monsters_hit_harder_on_each_loop_through_the_dungeon()
    {
        var map = new DungeonMap([new DepthDef("d", "Depth", [new FloorDef(["rat"])])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 40, Damage: 10, AttackSeconds: 10)], loopHpBonus: 0.5, loopDamageBonus: 0.3);

        Assert.Equal(10, Assert.Single(map.RoomAt(1, 1)).Damage);
        Assert.Equal(13, Assert.Single(map.RoomAt(2, 1)).Damage); // the second loop: +30%
        Assert.Equal(16, Assert.Single(map.RoomAt(3, 1)).Damage);
        Assert.Equal(80, Assert.Single(map.RoomAt(3, 1)).MaxHp);
    }

    // ---------------------------------------------------------------- Rogue

    [Fact]
    public void A_rogue_s_fast_answer_hits_much_harder()
    {
        var match = Solo("rogue", dungeon: OgreDungeon);

        match.Press("A1", 0, afterSeconds: 1);
        Assert.Equal(50, match.AdventurerOf("A1").LastDealt!.Damage); // 20 x 2.5

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal(20, match.AdventurerOf("A1").LastDealt!.Damage); // slow: no crit
    }

    // ---------------------------------------------------------------- Tank

    [Fact]
    public void A_tank_has_longer_to_be_fast()
    {
        var match = Solo("tank", dungeon: OgreDungeon);

        match.Press("A1", 0, afterSeconds: 4.5);
        Assert.True(match.AdventurerOf("A1").LastDealt!.Crit);

        match.Press("A1", 0, afterSeconds: 6);
        Assert.False(match.AdventurerOf("A1").LastDealt!.Crit);
    }

    [Fact]
    public void A_tank_takes_less_damage()
    {
        var match = Solo("tank");

        match.Press("A1", 1, afterSeconds: 4);

        Assert.Equal(94, match.AdventurerOf("A1").Hp); // a rat hits for 10; the tank takes 60% of it
        Assert.Equal(6, match.AdventurerOf("A1").LastTaken!.Damage);
    }

    // ---------------------------------------------------------------- Healer

    [Fact]
    public void A_healer_s_fast_answer_heals_and_the_healing_scores()
    {
        var match = Solo("healer", dungeon: OgreDungeon);
        match.Press("A1", 1, afterSeconds: 4); // wrong: 90 HP

        match.Press("A1", 0, afterSeconds: 2);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.Equal(("A1", 10), (hit.HealedOwnerId, hit.Healed)); // up to 15, but only 10 was missing
        Assert.Equal(100, match.AdventurerOf("A1").Hp);
        Assert.Equal(0, hit.PoisonStacks);
        Assert.Equal(30 + 10, match.ScoreOf("A1")); // the damage and the healing
    }

    [Fact]
    public void A_healer_heals_the_teammate_who_most_needs_it()
    {
        var match = Party(new() { ["A1"] = "healer", ["A3"] = "rogue" });
        match.Press("A3", 1, afterSeconds: 1); // Cat is hit: 90 HP

        match.Press("A1", 0, afterSeconds: 1); // 2 seconds into Ann's question: fast

        Assert.Equal("A3", match.AdventurerOf("A1").LastDealt!.HealedOwnerId);
        Assert.Equal(100, match.AdventurerOf("A3").Hp);
    }

    [Fact]
    public void A_slow_answer_heals_nobody()
    {
        var match = Solo("healer", dungeon: OgreDungeon);
        match.Press("A1", 1, afterSeconds: 4);

        match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(0, match.AdventurerOf("A1").LastDealt!.Healed);
        Assert.Equal(90, match.AdventurerOf("A1").Hp);
    }

    [Fact]
    public void With_nobody_hurt_a_healer_s_fast_answers_poison_the_monster_and_the_poison_stacks()
    {
        var match = Solo("healer", dungeon: OgreDungeon);

        match.Press("A1", 0, afterSeconds: 1); // 30 damage, and 1 stack of poison
        Assert.Equal(1, match.AdventurerOf("A1").LastDealt!.PoisonStacks);
        Assert.Equal(970, match.DelveOf("A1").MonsterHp);

        var events = match.Wait(2); // a tick (every 2 seconds): 1 per stack
        Assert.Equal(1, Assert.Single(events.OfType<MonsterPoisoned>()).Damage);
        Assert.Equal(969, match.DelveOf("A1").MonsterHp);

        match.Press("A1", 0, afterSeconds: 0.5); // a second stack (and 30 more)
        Assert.Equal(2, match.DelveOf("A1").Foes[0].PoisonStacks);
        match.Wait(1.5);
        Assert.Equal(969 - 30 - 2, match.DelveOf("A1").MonsterHp);
        Assert.Equal(2, SnapshotBuilder.Build(match.Engine, Viewer.Main).Players[0].Delve!.Monster.PoisonStacks);
    }

    [Fact]
    public void Poison_can_kill_and_scores_nothing()
    {
        var match = Solo("healer", s => s.Dungeon.Classes.PoisonDamage = 10); // one tick finishes the rat
        match.Press("A1", 0, afterSeconds: 1); // 30 of the rat's 40, and a stack of poison
        var score = match.ScoreOf("A1");

        var events = match.Wait(2);

        var kill = Assert.Single(events.OfType<MonsterPoisoned>(), e => e.Kill != null).Kill!;
        Assert.True(kill.Poison);
        Assert.Equal("Rat", kill.MonsterName);
        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.Equal((1, 2), (match.DelveOf("A1").Floor, match.DelveOf("A1").Encounter.Room)); // the next rat is up
        Assert.Equal(0, match.DelveOf("A1").Foes[0].PoisonStacks);                              // and it's not poisoned
        Assert.Equal(score, match.ScoreOf("A1"));
    }

    [Fact]
    public void A_poison_kill_plays_a_kill_beat_then_the_question_comes_back_for_the_next_monster()
    {
        var match = Solo("healer", s => s.Dungeon.Classes.PoisonDamage = 10, battleBeats: true);
        match.Press("A1", 0, afterSeconds: 1); // a stack of poison, then the hit's beat
        var number = match.StreamOf("A1").CurrentNumber;

        match.Wait(2); // the tick kills the rat (the question after the hit is up by then)

        var beat = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Beat!;
        Assert.Equal(("kill", true), (beat.Kind, beat.ByPoison));
        Assert.Equal(match.Now.AddSeconds(3), match.StreamOf("A1").CurrentShownAt); // the room beat (it emptied the room: the walk on)
        Assert.Equal(number, match.StreamOf("A1").CurrentNumber);                   // the same question comes back
        Assert.Equal(match.Now.AddSeconds(13), match.AdventurerOf("A1").AttackAt); // the new rat's hourglass starts after it
        Assert.Empty(match.Press("A1", 0, afterSeconds: 1));                        // buttons wait for the beat to end
    }

    // ---------------------------------------------------------------- Wizard

    /// <summary>Floor 1: one room with three rats; floor 2: a boss.</summary>
    private static readonly DungeonMap RatPack = new(
        [new DepthDef("pack", "Rat Nest", [new FloorDef(Rooms: [["rat", "rat", "rat"]]), new FloorDef(["boss"])])],
        [
            new MonsterDef("rat", "Rat", "rat", Hp: 40, Damage: 10, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "boss", Hp: 60, Damage: 50, AttackSeconds: 10, IsBoss: true),
        ]);

    [Fact]
    public void A_wizard_s_fast_answer_hits_the_whole_room()
    {
        var match = Solo("wizard", dungeon: RatPack);

        match.Press("A1", 0, afterSeconds: 1);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.Equal(30, hit.Damage);
        Assert.Equal([15, 15], hit.Splashes!.Select(s => s.Damage));
        Assert.Equal([10, 25, 25], match.DelveOf("A1").Foes.Select(f => f.Hp));
        Assert.Equal(60, match.ScoreOf("A1")); // all the damage counts
    }

    [Fact]
    public void A_wizard_s_slow_answer_hits_only_the_one_in_front()
    {
        var match = Solo("wizard", dungeon: RatPack);

        match.Press("A1", 0, afterSeconds: 4);

        Assert.Null(match.AdventurerOf("A1").LastDealt!.Splashes);
        Assert.Equal([20, 40, 40], match.DelveOf("A1").Foes.Select(f => f.Hp));
    }

    [Fact]
    public void A_wizard_can_clear_a_room_with_splash()
    {
        var match = Solo("wizard", dungeon: RatPack);

        match.Press("A1", 0); // 10 / 25 / 25
        match.Press("A1", 0); // front dead; 10 / 10
        match.Press("A1", 0); // front dead, the splash kills the last: room (and floor) cleared

        var delve = match.DelveOf("A1");
        Assert.Equal(3, delve.Kills);
        Assert.True(delve.LastHit!.FloorCleared);
        Assert.Equal("Boss", delve.Encounter.Monster.Name);
        Assert.True(Assert.Single(delve.LastHit.Splashes!).Killed);
    }

    // ---------------------------------------------------------------- Pugilist

    [Fact]
    public void A_pugilist_builds_a_combo_with_every_right_answer_in_a_row()
    {
        var match = Solo("pugilist", s => { s.Dungeon.Classes.PugilistComboStep = 0.5; s.Dungeon.Classes.PugilistComboMax = 1; }, OgreDungeon);

        var damage = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            match.Press("A1", 0, afterSeconds: 4);
            damage.Add(match.AdventurerOf("A1").LastDealt!.Damage);
        }

        Assert.Equal([20, 30, 40, 40], damage); // +50% a step, up to +100%
        Assert.Equal((true, 4), (match.AdventurerOf("A1").LastDealt!.Combo, match.AdventurerOf("A1").LastDealt!.ComboCount));
    }

    [Fact]
    public void A_wrong_answer_ends_the_pugilist_s_combo()
    {
        var match = Solo("pugilist", dungeon: OgreDungeon);
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 1, afterSeconds: 4);

        match.Press("A1", 0, afterSeconds: 4);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.False(hit.Combo);
        Assert.Equal(20, hit.Damage);
    }

    // ---------------------------------------------------------------- Warlock

    [Fact]
    public void A_warlock_s_patron_can_show_the_answer_as_the_hourglass_runs_low()
    {
        var match = Solo("warlock", s => s.Dungeon.Classes.WarlockRevealChance = 1);

        Assert.Empty(match.Wait(5.9).OfType<PatronRevealed>());
        Assert.Null(QuestionOn(match, "A1").Revealed);

        var events = match.Wait(0.1); // 4 seconds left on a 10-second hourglass

        Assert.Equal("A1", Assert.Single(events.OfType<PatronRevealed>()).OwnerId);
        Assert.Equal(0, QuestionOn(match, "A1").Revealed);
    }

    [Fact]
    public void The_patron_s_answer_is_for_that_question_and_that_screen_only()
    {
        var match = Party(new() { ["A1"] = "warlock", ["A3"] = "rogue" }, s => s.Dungeon.Classes.WarlockRevealChance = 1);
        match.Wait(6);

        Assert.NotNull(QuestionOn(match, "A1").Revealed);
        Assert.Null(QuestionOn(match, "A3").Revealed);
        Assert.Null(QuestionOn(match, "A2").Revealed);

        match.Press("A1", 0, afterSeconds: 0.5);
        Assert.Null(QuestionOn(match, "A1").Revealed); // the next question starts without it
    }

    [Fact]
    public void Without_the_patron_s_favour_nothing_is_shown()
    {
        var match = Solo("warlock", s => s.Dungeon.Classes.WarlockRevealChance = 0);

        var events = match.Wait(9.9);

        Assert.Empty(events.OfType<PatronRevealed>());
        Assert.Null(QuestionOn(match, "A1").Revealed);
    }

    [Fact]
    public void A_warlock_pays_for_slow_answers_right_or_wrong()
    {
        var match = Solo("warlock", s => s.Dungeon.Classes.WarlockRevealChance = 0, OgreDungeon);

        match.Press("A1", 0, afterSeconds: 4); // in time: free
        Assert.Equal(100, match.AdventurerOf("A1").Hp);

        match.Press("A1", 0, afterSeconds: 7); // right, but slow
        Assert.Equal(8, match.AdventurerOf("A1").LastDealt!.PatronPrice);
        Assert.Equal(92, match.AdventurerOf("A1").Hp);

        match.Press("A1", 1, afterSeconds: 7); // wrong and slow: the price, then the ogre's hit
        Assert.Equal(8, match.AdventurerOf("A1").LastTaken!.PatronPrice);
        Assert.Equal(92 - 8 - 10, match.AdventurerOf("A1").Hp);
    }

    [Fact]
    public void The_patron_s_price_never_downs_a_warlock()
    {
        var warlock = new Adventurer("A1", "A1", maxHp: 5, HeroClass.Warlock);

        Assert.Equal(4, warlock.PayPatron(8));
        Assert.Equal(1, warlock.Hp);
        Assert.Equal(0, warlock.PayPatron(8));
        Assert.False(warlock.IsDowned);
    }

    // ---------------------------------------------------------------- Ranger

    [Fact]
    public void A_ranger_sees_the_next_question_before_the_beat_ends_but_can_t_answer_it_yet()
    {
        var match = Solo("ranger", s => s.Dungeon.Classes.RangerPeekSeconds = 1, battleBeats: true);
        match.Press("A1", 0, afterSeconds: 4); // a hit: a 1.5-second beat

        var question = QuestionOn(match, "A1");
        Assert.Equal(question.ShownAtMs - 1000, question.PeekFromMs);

        match.Press("A1", 0, afterSeconds: 0.75); // they can see it, but it isn't up yet
        Assert.Single(match.StreamOf("A1").Answers);
    }

    [Fact]
    public void Nobody_else_sees_the_next_question_early()
    {
        var match = Solo("rogue", battleBeats: true);
        match.Press("A1", 0, afterSeconds: 4);

        Assert.Null(QuestionOn(match, "A1").PeekFromMs);
    }
}
