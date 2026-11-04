using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Bosses: their entrance, and their charge cycle (DungeonSettings.BossChargeSteps). The default cycle is 3
// steps: the boss hits for 50% of its damage, then 100%, then at full charge 250% (a right answer then
// staggers it for 50% more damage). Easy questions do 20; slow presses (4 seconds) don't crit.
public class DungeonBossTests
{
    /// <summary>Floor 1: a rat's room (20 HP: one easy hit), then the boss's room (a boss that hits for 20). Floor 2: a rat.</summary>
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["boss"]]), new FloorDef(["rat"])])],
        [
            new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "goblin-boss", Hp: 1000, Damage: 20, AttackSeconds: 10, IsBoss: true),
        ]);

    private static TestMatch Match(GameModeKind mode = GameModeKind.FreeForAll, bool battleBeats = false)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1, Dungeon = { AdventurerHp = 200, PartyHpPerMember = 0 } };
        var match = new TestMatch(mode, settings, stations: mode == GameModeKind.FreeForAll ? ["A1"] : null, dungeon: Map, battleBeats: battleBeats);
        match.StartPlaying();
        return match;
    }

    /// <summary>Kills the rat, so the boss is up.</summary>
    private static void ReachTheBoss(TestMatch match, string stationId = "A1") => match.Press(stationId, 0, afterSeconds: 4);

    private static DelveView DelveFor(TestMatch match, string stationId) =>
        SnapshotBuilder.Build(match.Engine, Viewer.Player(stationId)).You!.Delve!;

    [Fact]
    public void A_kill_that_opens_the_boss_s_room_plays_its_entrance()
    {
        var match = Match(battleBeats: true);

        var hit = Assert.Single(match.Press("A1", 0, afterSeconds: 4).OfType<MonsterHit>()).Hit;

        Assert.True(hit.BossAhead);
        Assert.False(hit.FloorCleared); // the boss is in this floor's last room
        Assert.Equal("boss", SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Beat!.Kind);
        Assert.Equal(match.Now.AddSeconds(3), match.StreamOf("A1").CurrentShownAt); // the long beat, like a floor's
    }

    [Fact]
    public void The_boss_starts_weak_builds_up_and_lands_a_big_blow_at_full_charge_then_starts_over()
    {
        var match = Match();
        ReachTheBoss(match);

        var damage = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            match.Press("A1", 1, afterSeconds: 4);
            damage.Add(match.AdventurerOf("A1").LastTaken!.Damage);
        }

        Assert.Equal([10, 20, 50, 10], damage); // 50%, 100%, 250%, and round again
    }

    [Fact]
    public void A_miss_at_full_charge_is_the_charged_blow()
    {
        var match = Match();
        ReachTheBoss(match);
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // right answers build the charge too
        Assert.Equal((2, 3), (DelveFor(match, "A1").BossCharge, DelveFor(match, "A1").BossChargeSteps));

        match.Press("A1", 1, afterSeconds: 4);

        var taken = match.AdventurerOf("A1").LastTaken!;
        Assert.True(taken.Charged);
        Assert.Equal(50, taken.Damage);
        Assert.Equal(0, DelveFor(match, "A1").BossCharge);
    }

    [Fact]
    public void A_right_answer_at_full_charge_staggers_the_boss_and_resets_its_charge()
    {
        var match = Match();
        ReachTheBoss(match);
        match.Press("A1", 1, afterSeconds: 4);
        match.Press("A1", 1, afterSeconds: 4); // full charge now

        match.Press("A1", 0, afterSeconds: 4);

        var hit = match.AdventurerOf("A1").LastDealt!;
        Assert.True(hit.Staggered);
        Assert.Equal(30, hit.Damage); // 20, 50% more
        Assert.Equal(0, DelveFor(match, "A1").BossCharge);
        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(10, match.AdventurerOf("A1").LastTaken!.Damage); // weak again
    }

    [Fact]
    public void Each_adventurer_has_their_own_cycle()
    {
        var match = Match(GameModeKind.TeamCombinedScore); // Red = A1 + A3, one boss between them
        match.Press("A1", 0, afterSeconds: 1);         // the rat: the boss is up

        match.Press("A1", 0, afterSeconds: 1);
        match.Press("A1", 0, afterSeconds: 1);         // quick, so Cat's own hourglass never runs out (that would move hers on)

        Assert.Equal(2, DelveFor(match, "A1").BossCharge);
        Assert.Equal(0, DelveFor(match, "A3").BossCharge);
        Assert.Equal(3, DelveFor(match, "A3").BossChargeSteps); // facing the same boss
    }

    [Fact]
    public void There_is_no_charge_away_from_a_boss()
    {
        var match = Match();

        Assert.Equal((0, 0), (DelveFor(match, "A1").BossCharge, DelveFor(match, "A1").BossChargeSteps));
        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(10, match.AdventurerOf("A1").LastTaken!.Damage); // the rat hits as written
    }

    [Theory]
    [InlineData(2, new[] { 0.5, 2.5 })]
    [InlineData(3, new[] { 0.5, 1, 2.5 })]
    [InlineData(4, new[] { 0.5, 0.75, 1, 2.5 })]
    public void The_cycle_builds_from_weak_to_normal_then_unleashes(int steps, double[] hits)
    {
        var settings = new DungeonSettings { BossChargeSteps = steps };

        Assert.Equal(hits, Enumerable.Range(0, steps).Select(settings.BossHitAt));
    }

    [Fact]
    public void Without_a_cycle_a_boss_always_hits_as_written()
    {
        var settings = new DungeonSettings { BossChargeSteps = 0 };

        Assert.Equal(1, settings.BossHitAt(0));
        Assert.Equal(1, settings.BossHitAt(5));
    }
}
