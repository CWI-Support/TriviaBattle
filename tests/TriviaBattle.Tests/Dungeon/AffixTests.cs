using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Monster affixes, deeper down: from AffixFromFloor on, a monster that isn't a boss may have one. Speedy: a shorter
// hourglass (60% of its own). Armored: a slow right answer does half (a fast one, a crit, gets through in full).
// Vampiric: it heals by what its hits take. Right answers do 20 here (30 as a crit, within 3 s); the rat has 40 HP, hits
// for 10, and its hourglass is 10 s. Solo, the Ranger (TestMatch.PlainHero).
public class AffixTests
{
    private static readonly MonsterDef Rat = new("rat", "Rat", "rat", Hp: 40, Damage: 10, AttackSeconds: 10);
    private static readonly MonsterDef Boss = new("boss", "Boss", "boss", Hp: 60, Damage: 50, AttackSeconds: 10, IsBoss: true);

    /// <summary>Two floors of a rat each (the second with a boss after it): every monster from floor 2 that can have the affix has it.</summary>
    private static DungeonMap MapWith(Affix affix, int fromFloor = 1) => new(
        [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"]]), new FloorDef(Rooms: [["rat", "rat"], ["boss"]])])],
        [Rat, Boss],
        affixChance: 1, affixFromFloor: fromFloor, affixSeed: 5, affixes: [affix]);

    private static TestMatch Solo(Affix affix)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            questionCount: 30, stations: ["A1"], dungeon: MapWith(affix));
        match.StartPlaying();
        return match;
    }

    [Fact]
    public void Only_monsters_from_the_floor_given_on_have_one_and_never_a_boss()
    {
        var map = MapWith(Affix.Vampiric, fromFloor: 2);

        Assert.Null(map.RoomAt(1, 1)[0].Affix);
        Assert.All(map.RoomAt(2, 1), e => Assert.Equal(Affix.Vampiric, e.Affix));
        Assert.Null(map.RoomAt(2, 2)[0].Affix); // the boss
        Assert.Equal(Affix.Vampiric, map.RoomAt(3, 1)[0].Affix); // a loop back round: floor 1's rooms, but deeper than floor 2
    }

    [Fact]
    public void Each_place_s_roll_is_fixed_by_the_match_so_every_party_meets_the_same_ones()
    {
        DungeonMap Map(int seed) => new(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat", "rat", "rat"], ["rat", "rat", "rat"]])])],
            [Rat], affixChance: 0.5, affixFromFloor: 1, affixSeed: seed);
        IEnumerable<Affix?> Rolls(DungeonMap map) =>
            Enumerable.Range(1, 30).SelectMany(floor => new[] { 1, 2 }.SelectMany(room => map.RoomAt(floor, room).Select(e => e.Affix))).ToList();

        var rolls = Rolls(Map(7)).ToList();

        Assert.Equal(rolls, Rolls(Map(7))); // the same seed: the same monsters, wherever they're met from
        Assert.NotEqual(rolls, Rolls(Map(8)));
        Assert.InRange(rolls.Count(a => a != null), 60, 120); // about half of 180
        Assert.Equal(3, rolls.OfType<Affix>().Distinct().Count()); // all three turn up
    }

    [Fact]
    public void A_speedy_monster_s_hourglass_is_shorter()
    {
        var match = Solo(Affix.Speedy);
        var adventurer = match.AdventurerOf("A1");

        Assert.Equal(6, (adventurer.AttackAt - adventurer.AttackStartedAt)!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void An_armored_monster_takes_half_from_a_slow_answer_and_all_of_a_fast_one()
    {
        var match = Solo(Affix.Armored);

        match.Press("A1", 0, afterSeconds: 4); // slow
        var slow = match.AdventurerOf("A1").LastDealt!;
        match.Press("A1", 0, afterSeconds: 1); // fast: a crit

        var fast = match.AdventurerOf("A1").LastDealt!;
        Assert.Equal((10, true), (slow.Damage, slow.Armored));
        Assert.Equal((30, false), (fast.Damage, fast.Armored));
    }

    [Fact]
    public void A_vampiric_monster_heals_by_what_its_hit_took()
    {
        var match = Solo(Affix.Vampiric);
        match.Press("A1", 0, afterSeconds: 4); // 20 of its 40

        match.Press("A1", 1, afterSeconds: 1); // wrong: it hits for 10, and drinks it

        Assert.Equal(10, match.AdventurerOf("A1").LastTaken!.Drained);
        Assert.Equal(30, match.DelveOf("A1").MonsterHp);
        Assert.Equal(90, match.AdventurerOf("A1").Hp);
    }

    [Fact]
    public void A_vampiric_monster_gets_nothing_from_a_hit_a_shield_took()
    {
        var match = Solo(Affix.Vampiric);
        match.Press("A1", 0, afterSeconds: 4);
        match.Engine.GivePowerUp("A1", PowerUp.Shield);
        match.Press("A1", Core.Input.ButtonPress.Action, afterSeconds: 0.5);

        match.Press("A1", 1, afterSeconds: 1);

        Assert.Equal(0, match.AdventurerOf("A1").LastTaken!.Drained);
        Assert.Equal(20, match.DelveOf("A1").MonsterHp);
    }

    [Fact]
    public void Screens_see_a_monster_s_affix()
    {
        var match = Solo(Affix.Armored);

        var delve = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!;

        Assert.Equal(Affix.Armored, delve.Monster.Affix);
        Assert.All(delve.Foes, f => Assert.Equal(Affix.Armored, f.Affix));
    }
}
