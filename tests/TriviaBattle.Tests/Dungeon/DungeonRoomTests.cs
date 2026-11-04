using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Dungeon;

// Rooms with several monsters (packs): the party fights the one in front, the others wait their turn.
// Presses are slow (4 seconds: 20 damage, no crit) and players are the plain hero (TestMatch.PlainHero).
public class DungeonRoomTests
{
    /// <summary>Floor 1: a room with a brute (hits for 100) in front of a rat, then a room with one rat. Floor 2: a boss.</summary>
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["brute", "rat"], ["rat"]]), new FloorDef(["boss"])])],
        [
            new MonsterDef("brute", "Brute", "orc", Hp: 40, Damage: 100, AttackSeconds: 10),
            new MonsterDef("rat", "Rat", "rat", Hp: 40, Damage: 10, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "boss", Hp: 60, Damage: 50, AttackSeconds: 10, IsBoss: true),
        ]);

    private static TestMatch Solo()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], dungeon: Map);
        match.StartPlaying();
        return match;
    }

    [Fact]
    public void A_room_can_hold_a_pack()
    {
        var room = Map.RoomAt(1, 1);

        Assert.Equal(["Brute", "Rat"], room.Select(e => e.Monster.Name));
        Assert.Equal([1, 2], room.Select(e => e.Slot));
        Assert.All(room, e => Assert.Equal((1, 2, 2), (e.Room, e.RoomsOnFloor, e.MonstersInRoom)));
        Assert.Equal([(1, 1), (1, 1), (1, 2), (2, 1)], Map.DepthPath(1).Select(e => (e.Floor, e.Room)));
    }

    [Fact]
    public void Only_the_monster_in_front_is_hit_and_attacks()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal([20, 40], match.DelveOf("A1").Foes.Select(f => f.Hp));

        match.Press("A1", 1, afterSeconds: 4); // wrong: the brute (in front) hits
        Assert.Equal("Brute", match.AdventurerOf("A1").LastTaken!.MonsterName);
    }

    [Fact]
    public void When_the_one_in_front_dies_the_next_in_the_room_steps_up()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // the brute dies

        var delve = match.DelveOf("A1");
        Assert.True(delve.LastHit!.Killed);
        Assert.False(delve.LastHit.FloorCleared);
        Assert.Equal(("Rat", 1, 2), (delve.Encounter.Monster.Name, delve.Encounter.Room, delve.Encounter.Slot)); // same room
        Assert.Single(delve.Foes);

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // the room is empty: on to the next
        Assert.Equal((1, 2), (match.DelveOf("A1").Floor, match.DelveOf("A1").Encounter.Room));
        Assert.Equal(2, match.DelveOf("A1").Kills);
    }

    [Fact]
    public void When_the_whole_party_is_down_the_room_recovers()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4); // the brute: 20 of 40

        match.Press("A1", 1, afterSeconds: 4); // its hit downs them (100)

        Assert.True(match.AdventurerOf("A1").IsDowned);
        Assert.Equal([40, 40], match.DelveOf("A1").Foes.Select(f => f.Hp));
    }

    [Fact]
    public void Screens_get_every_monster_in_the_room()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);

        var delve = SnapshotBuilder.Build(match.Engine, Viewer.Main).Players[0].Delve!;

        Assert.Equal("Brute", delve.Monster.Name);
        Assert.Equal([("Brute", 20), ("Rat", 40)], delve.Foes.Select(f => (f.Name, f.Hp)));
        Assert.Equal([1, 2, 1, 1], delve.Path.Select(s => s.Slot));
    }

    [Fact]
    public void A_room_with_no_monsters_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], []])])],
            [new MonsterDef("rat", "Rat", "rat", 40, 10, 10)]));

        Assert.Contains("no monsters", error.Message);
    }

    [Fact]
    public void The_dungeon_the_server_ships_loads_and_has_packs()
    {
        var map = DungeonContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server")).Build(new Random(1));

        var rooms = Enumerable.Range(1, map.FloorCount)
            .SelectMany(floor => Enumerable.Range(1, map.RoomsOn(floor)).Select(room => map.RoomAt(floor, room)));
        Assert.Contains(rooms, room => room.Count > 1);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
