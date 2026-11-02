using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Uses TestMatch.Dungeon: floor 1 = two rats (40 HP, hit for 10), floor 2 = a boss (60 HP, hits for 50).
// Easy questions do 20 damage; an answer within 3 seconds crits (x1.5); 3 in a row is a combo (+50%).
// TestMatch.Press waits 1 second by default (a crit), so "slow" presses below wait 4.
public class DungeonCombatTests
{
    private static TestMatch Solo(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"]);
        match.StartPlaying();
        return match;
    }

    [Fact]
    public void A_correct_answer_hits_the_monster_and_scores_the_damage()
    {
        var match = Solo();

        var events = match.Press("A1", 0, afterSeconds: 4);

        var delve = match.DelveOf("A1");
        Assert.Equal(20, delve.MonsterHp);
        Assert.Equal(20, match.ScoreOf("A1"));
        var hit = Assert.Single(events.OfType<MonsterHit>()).Hit;
        Assert.False(hit.Crit);
        Assert.Equal("Rat", hit.MonsterName);
    }

    [Fact]
    public void A_fast_answer_is_a_critical_hit()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 1);

        Assert.True(match.AdventurerOf("A1").LastDealt!.Crit);
        Assert.Equal(30, match.AdventurerOf("A1").LastDealt!.Damage);
    }

    [Fact]
    public void Three_in_a_row_is_a_combo()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.True(hit.Combo);
        Assert.Equal(30, hit.Damage);
    }

    [Fact]
    public void Killing_a_monster_brings_on_the_next_and_the_last_one_clears_the_floor()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // first rat dead
        Assert.Equal((1, 2), (match.DelveOf("A1").Floor, match.DelveOf("A1").Encounter.Room));
        Assert.True(match.DelveOf("A1").LastHit!.Killed);

        match.Press("A1", 0, afterSeconds: 4); // combo from here: 30 each
        match.Press("A1", 0, afterSeconds: 4); // second rat dead

        var delve = match.DelveOf("A1");
        Assert.True(delve.LastHit!.FloorCleared);
        Assert.Equal(2, delve.Floor);
        Assert.Equal("Boss", delve.Encounter.Monster.Name);
        Assert.Equal(60, delve.MonsterHp);
        Assert.Equal(2, delve.Kills);
    }

    [Fact]
    public void A_wrong_answer_gets_you_hit_and_ends_the_streak()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);

        var events = match.Press("A1", 1, afterSeconds: 4);

        var adventurer = match.AdventurerOf("A1");
        Assert.Equal(90, adventurer.Hp);
        Assert.Equal(0, adventurer.Streak);
        var hit = Assert.Single(events.OfType<AdventurerHit>()).Hit;
        Assert.Equal(HitReason.WrongAnswer, hit.Reason);
        Assert.NotNull(match.StreamOf("A1").LockedOutUntil); // still stunned, as in Classic
    }

    [Fact]
    public void When_the_hourglass_runs_out_the_monster_attacks_and_the_question_is_lost()
    {
        var match = Solo();

        var events = match.Wait(10);

        Assert.Equal(90, match.AdventurerOf("A1").Hp);
        Assert.Equal(HitReason.TooSlow, Assert.Single(events.OfType<AdventurerHit>()).Hit.Reason);
        var lost = match.StreamOf("A1").LastAnswer!;
        Assert.True(lost.TimedOut);
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber);
        Assert.Equal(0, match.ScoreOf("A1"));
    }

    [Fact]
    public void The_hourglass_waits_while_the_buttons_are_dead()
    {
        var match = Solo(s => s.LockoutSeconds = 2);

        match.Press("A1", 1, afterSeconds: 4); // wrong: stunned for 2 seconds
        Assert.Null(match.AdventurerOf("A1").AttackAt);

        match.Wait(2);
        Assert.Equal(match.Now.AddSeconds(10), match.AdventurerOf("A1").AttackAt);
    }

    [Fact]
    public void At_zero_HP_you_are_downed_the_monster_heals_and_you_get_up_at_full_HP()
    {
        var match = Solo(s => { s.Dungeon.AdventurerHp = 10; s.Dungeon.DownedSeconds = 5; });
        match.Press("A1", 0, afterSeconds: 4); // rat down to 20

        var events = match.Press("A1", 1, afterSeconds: 4); // rat hits for 10: downed

        var adventurer = match.AdventurerOf("A1");
        Assert.True(Assert.Single(events.OfType<AdventurerHit>()).Hit.Downed);
        Assert.NotNull(adventurer.DownedUntil);
        Assert.Equal(adventurer.DownedUntil, match.StreamOf("A1").LockedOutUntil);
        Assert.Equal(40, match.DelveOf("A1").MonsterHp); // a party of one: everyone is down

        events = match.Wait(5);

        Assert.Single(events.OfType<AdventurerGotUp>());
        Assert.Null(adventurer.DownedUntil);
        Assert.Equal(10, adventurer.Hp);
        Assert.NotNull(adventurer.AttackAt);
    }

    [Fact]
    public void In_2v2_Group_the_party_shares_one_delve()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 });
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A3", 0, afterSeconds: 4); // Red's other player: same rat

        Assert.Same(match.DelveOf("A1"), match.DelveOf("A3"));
        Assert.Same(match.AdventurerOf("A1"), match.AdventurerOf("A3")); // and one adventurer: they share the question
        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.Equal(40, match.DelveOf("A2").MonsterHp); // Blue hasn't touched theirs
    }

    [Fact]
    public void In_2v2_Combined_teammates_hit_the_same_monster_with_their_own_HP()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 });
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 4); // Red: rat down to 20
        match.Press("A3", 0, afterSeconds: 4); // Red's other player finishes it off
        match.Press("A3", 1, afterSeconds: 1); // and the next rat hits them

        Assert.Same(match.DelveOf("A1"), match.DelveOf("A3"));
        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.True(match.DelveOf("A1").LastHit!.Killed);         // the party's last hit killed it...
        Assert.False(match.AdventurerOf("A1").LastDealt!.Killed); // ...but it wasn't A1's
        Assert.Equal(100, match.AdventurerOf("A1").Hp);
        Assert.Equal(90, match.AdventurerOf("A3").Hp);
        Assert.Equal(40, match.DelveOf("A2").MonsterHp); // Blue hasn't touched theirs
    }

    [Fact]
    public void The_monster_heals_only_when_the_whole_party_is_down()
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        settings.Dungeon.AdventurerHp = 10;
        var match = new TestMatch(GameModeKind.TeamCombinedScore, settings);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // rat down to 20

        match.Press("A1", 1, afterSeconds: 1); // A1 is downed; A3 is still standing
        Assert.True(match.AdventurerOf("A1").IsDowned);
        Assert.Equal(20, match.DelveOf("A1").MonsterHp);

        match.Press("A3", 1, afterSeconds: 1); // now the whole party is down
        Assert.Equal(40, match.DelveOf("A1").MonsterHp);
    }

    [Fact]
    public void Screens_get_the_delve()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);

        var snapshot = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1"));

        Assert.Equal("Dungeon", snapshot.Format);
        var delve = snapshot.You!.Delve!;
        Assert.Equal("Rat", delve.Monster.Name);
        Assert.Equal(20, delve.Monster.Hp);
        Assert.Equal(100, delve.Hp);
        Assert.NotNull(delve.AttackAtMs);
        Assert.Equal(["Rat", "Rat", "Boss"], delve.Path.Select(s => s.Name));
        var (noPath, noParty) = (Array.Empty<PathStepView>(), Array.Empty<PartyMemberView>());
        Assert.Equal(delve with { Path = noPath, Party = noParty }, snapshot.Players[0].Delve! with { Path = noPath, Party = noParty });
    }

    // ---------------------------------------------------------------- Parties: revives and party combos
    // 2v2 Combined: Red = A1 (Ann) + A3 (Cat), each answering their own questions, sharing one monster.

    private static TestMatch Party(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.TeamCombinedScore, settings);
        match.StartPlaying();
        return match;
    }

    [Fact]
    public void In_a_party_you_stay_down_until_your_teammates_revive_you()
    {
        var match = Party(s => s.Dungeon.AdventurerHp = 10);
        match.Press("A1", 1); // the rat's hit downs Ann
        var ann = match.AdventurerOf("A1");
        Assert.Equal(match.Now.AddSeconds(20), ann.DownedUntil); // PartyDownedSeconds, the safety net

        match.Wait(5); // longer than a solo player would be down
        Assert.True(ann.IsDowned);

        match.Press("A3", 0);
        match.Press("A3", 0);
        Assert.Equal(2, ann.ReviveProgress);
        var events = match.Press("A3", 0); // Cat's third right answer

        Assert.Equal("A1", Assert.Single(events.OfType<AdventurerGotUp>()).OwnerId);
        Assert.Equal("A1", match.DelveOf("A1").LastHit!.RevivedOwnerId);
        Assert.False(ann.IsDowned);
        Assert.Equal(10, ann.Hp);
        Assert.Null(match.StreamOf("A1").LockedOutUntil);
        Assert.Single(match.Press("A1", 0, afterSeconds: 0.5).OfType<MonsterHit>()); // her buttons work again
    }

    [Fact]
    public void When_the_whole_party_is_down_everyone_gets_up_soon()
    {
        var match = Party(s => { s.Dungeon.AdventurerHp = 10; s.Dungeon.DownedSeconds = 5; });
        match.Press("A1", 1); // Ann down, waiting for Cat
        match.Press("A3", 1); // Cat down too: nobody is left to revive anyone

        Assert.Equal(match.Now.AddSeconds(5), match.AdventurerOf("A1").DownedUntil);
        Assert.Equal(match.Now.AddSeconds(5), match.StreamOf("A1").LockedOutUntil);

        var events = match.Wait(5);

        Assert.Equal(["A1", "A3"], events.OfType<AdventurerGotUp>().Select(e => e.OwnerId).Order());
    }

    [Fact]
    public void Answering_right_together_is_a_party_combo()
    {
        var match = Party();
        match.Press("A1", 0, afterSeconds: 4); // Ann hits: rat down to 20

        var hit = Assert.Single(match.Press("A3", 0, afterSeconds: 1).OfType<MonsterHit>()).Hit; // Cat, a second later

        Assert.True(hit.PartyCombo);
        Assert.Equal(40, hit.Damage); // 20, doubled
        hit = Assert.Single(match.Press("A3", 0, afterSeconds: 1).OfType<MonsterHit>()).Hit;
        Assert.False(hit.PartyCombo); // Ann needs a fresh right answer for the next one
    }

    [Fact]
    public void Answers_far_apart_are_no_party_combo()
    {
        var match = Party();
        match.Press("A1", 0, afterSeconds: 4);

        var hit = Assert.Single(match.Press("A3", 0, afterSeconds: 5).OfType<MonsterHit>()).Hit;

        Assert.False(hit.PartyCombo);
    }

    [Fact]
    public void A_downed_player_sees_a_teammates_question_to_shout_the_answer()
    {
        var match = Party(s => s.Dungeon.AdventurerHp = 10);
        match.Press("A1", 1);

        var ann = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        var cat = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3")).You!;

        Assert.Equal("Cat", ann.Helping!.Name);
        Assert.Equal(match.StreamOf("A3").CurrentQuestion.Text, ann.Helping.Question.Text);
        Assert.Equal(3, ann.Delve!.ReviveNeeded);
        Assert.Null(cat.Helping);
        Assert.Contains(cat.Delve!.Party, member => member.Name == "Ann" && member.DownedUntilMs != null);
    }

    [Fact]
    public void In_2v2_Combined_the_main_screen_gets_a_lane_per_party()
    {
        var match = Party();

        var red = SnapshotBuilder.Build(match.Engine, Viewer.Main).Teams.Single(t => t.Id == "red").Delve!;

        Assert.Equal(200, red.MaxHp);
        Assert.Equal(["Ann", "Cat"], red.Party.Select(m => m.Name));
    }

    [Fact]
    public void Classic_format_has_no_delves_and_scores_as_before()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, stations: ["A1"]);
        match.StartPlaying();

        match.Press("A1", 0);

        Assert.Null(match.Engine.Dungeon);
        Assert.Equal(100, match.ScoreOf("A1"));
        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve);
    }

    [Fact]
    public void Past_the_last_floor_the_dungeon_loops_with_tougher_monsters()
    {
        var map = TestMatch.Dungeon; // 2 floors; +50% HP per loop

        var third = map.EncounterAt(3, 1);

        Assert.Equal("Rat", third.Monster.Name);
        Assert.Equal(60, third.MaxHp);
        Assert.Equal(3, third.Floor);
    }

    [Fact]
    public void The_depth_path_lists_every_monster_of_the_depth_on_any_loop()
    {
        var map = new DungeonMap(
            [
                new DepthDef("one", "One", [new FloorDef(["rat"]), new FloorDef(["rat", "boss"])]),
                new DepthDef("two", "Two", [new FloorDef(["boss"])]),
            ],
            [new MonsterDef("rat", "Rat", "rat", 40, 10, 10), new MonsterDef("boss", "Boss", "boss", 60, 50, 10, IsBoss: true)]);

        Assert.Equal([(1, 1), (2, 1), (2, 2)], map.DepthPath(2).Select(e => (e.Floor, e.Room)));
        Assert.Equal([(3, 1)], map.DepthPath(3).Select(e => (e.Floor, e.Room)));
        Assert.Equal([(4, 1), (5, 1), (5, 2)], map.DepthPath(5).Select(e => (e.Floor, e.Room)));
    }

    [Fact]
    public void A_floor_using_an_undefined_monster_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(["ghost"])])],
            [new MonsterDef("rat", "Rat", "rat", 40, 10, 10)]));

        Assert.Contains("ghost", error.Message);
    }
}
