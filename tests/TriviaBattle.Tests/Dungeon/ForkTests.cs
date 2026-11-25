using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The fork before a boss: once the boss's entrance has played, the party votes for one of two doors that look the same
// (A the first, B the second; presses count after a second, 8 s in all), the door taken opens (2 s) on the special room
// behind it, and everyone who voted plays it. Then the boss. Here floor 1 is a rat (one hit kills it) and the boss, and
// with battle beats off the doors come up the moment the rat dies.
public class ForkTests
{
    private static readonly MonsterDef[] Monsters =
    [
        new MonsterDef("rat", "Rat", "rat", Hp: 5, Damage: 10, AttackSeconds: 10),
        new MonsterDef("boss", "Boss", "boss", Hp: 500, Damage: 10, AttackSeconds: 10, IsBoss: true),
    ];

    /// <summary>
    /// A floor of a rat then the boss, with a fork before the boss whose door A leads to <paramref name="first"/>: the first
    /// fork seed that makes one.
    /// </summary>
    public static DungeonMap MapWith(MinigameKind first)
    {
        for (var seed = 0; seed < 10_000; seed++)
        {
            var map = new DungeonMap([new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["boss"]])])], Monsters, forkChance: 1, forkSeed: seed);
            if (map.ForkOn(1) is { } plan && plan.Rooms[0] == first)
                return map;
        }
        throw new InvalidOperationException("No seed makes that fork.");
    }

    /// <summary>A solo player who has just killed the rat: they're at the fork, its doors up (votes count in a second).</summary>
    public static TestMatch AtTheFork(DungeonMap map, bool rewards = false, QuerieLines? querie = null, HighStakesQuestions? highStakes = null)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], dungeon: map, rewards: rewards, querie: querie, highStakes: highStakes);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat dies: the boss steps up, the fork before it
        return match;
    }

    /// <summary>A party of three (Ann, Bob, Cat) who've just killed the rat.</summary>
    private static TestMatch PartyAtTheFork(DungeonMap map)
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1", "A2", "A3"], teams: [new TeamSetup("red", "Red", ["A1", "A2", "A3"])], dungeon: map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4);
        return match;
    }

    [Fact]
    public void Killing_the_last_monster_before_a_boss_brings_up_the_fork()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], dungeon: MapWith(MinigameKind.Treasure));
        match.StartPlaying();

        var events = match.Press("A1", 0, afterSeconds: 4);

        Assert.Contains(events, e => e is MonsterHit { Hit: { BossAhead: true, ForkAhead: true } });
        var opened = Assert.Single(events.OfType<ForkOpened>());
        Assert.Equal(2, opened.Fork.Doors);
        Assert.Equal(MinigameKind.Treasure, opened.Fork.Plan.Rooms[0]);
        var ann = match.AdventurerOf("A1");
        Assert.NotNull(ann.Fork);
        Assert.True(ann.IsAway);
        Assert.True(match.Engine.Dungeon!.ClockStopped);
        Assert.Null(ann.AttackAt); // the boss's hourglass waits
    }

    [Fact]
    public void Without_a_fork_the_boss_comes_straight_up()
    {
        var map = new DungeonMap([new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["boss"]])])], Monsters, forkChance: 0);
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], dungeon: map);
        match.StartPlaying();

        var events = match.Press("A1", 0, afterSeconds: 4);

        Assert.DoesNotContain(events, e => e is ForkOpened);
        Assert.Null(match.AdventurerOf("A1").Fork);
        Assert.Equal("Boss", match.DelveOf("A1").Encounter.Monster.Name);
    }

    [Fact]
    public void Forks_are_only_ever_before_a_boss()
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]]), new FloorDef(Rooms: [["rat"], ["boss"]])])], Monsters, forkChance: 1);

        Assert.Null(map.ForkOn(1)); // no boss on floor 1
        Assert.NotNull(map.ForkOn(2));
        Assert.Null(map.ForkOn(3)); // the loop back to the top: floor 1 again
    }

    [Fact]
    public void Every_party_in_a_match_meets_the_same_doors_with_two_different_rooms_behind_them()
    {
        var plan = new DungeonPlan([new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["boss"]])])], Monsters, forkChance: 1);

        var one = plan.Build(new Random(5));
        var other = plan.Build(new Random(5));

        for (var floor = 1; floor <= 6; floor++)
        {
            var rooms = one.ForkOn(floor)!.Rooms;
            Assert.Equal(rooms, other.ForkOn(floor)!.Rooms);
            Assert.Equal(2, rooms.Distinct().Count());
            Assert.All(rooms, room => Assert.Contains(room, MinigameCatalog.Rooms));
        }
    }

    [Fact]
    public void Votes_count_once_the_doors_are_up()
    {
        var match = AtTheFork(MapWith(MinigameKind.Treasure));

        Assert.Empty(match.Press("A1", 0, afterSeconds: 0.5)); // too soon
        Assert.Empty(match.Press("A1", ButtonPress.Action, afterSeconds: 0.6)); // the action button picks no door
        var events = match.Press("A1", 0, afterSeconds: 0);

        Assert.Contains(events, e => e is DoorVoted { Door: 0 });
        Assert.Contains(events, e => e is ForkDecided { Door: 0, Room: MinigameKind.Treasure }); // everyone's voted
    }

    [Fact]
    public void The_door_taken_opens_then_the_room_behind_it_starts()
    {
        var match = AtTheFork(MapWith(MinigameKind.HighStakes));
        match.Press("A1", 0, afterSeconds: 1.1);

        match.Wait(1.5);
        Assert.NotNull(match.AdventurerOf("A1").Fork); // the door's still opening
        var events = match.Wait(0.6);

        Assert.Contains(events, e => e is MinigameStarted { Kind: MinigameKind.HighStakes });
        var ann = match.AdventurerOf("A1");
        Assert.Null(ann.Fork);
        Assert.IsType<HighStakesRoom>(ann.Minigame);
        Assert.True(match.Engine.Dungeon!.ClockStopped);
    }

    [Fact]
    public void B_takes_the_second_door()
    {
        var map = MapWith(MinigameKind.Treasure);
        var second = map.ForkOn(1)!.Rooms[1];
        var match = AtTheFork(map);

        var events = match.Press("A1", 1, afterSeconds: 1.1);

        Assert.Contains(events, e => e is ForkDecided { Door: 1 } f && f.Room == second);
    }

    [Fact]
    public void Nobody_voting_still_takes_a_door_when_time_is_up()
    {
        var match = AtTheFork(MapWith(MinigameKind.Treasure));

        var events = match.Wait(8.1);

        Assert.Single(events.OfType<ForkDecided>());
        match.Wait(2.1);
        Assert.NotNull(match.AdventurerOf("A1").Minigame);
    }

    [Fact]
    public void A_party_takes_the_door_with_the_most_votes()
    {
        var match = PartyAtTheFork(MapWith(MinigameKind.Treasure));

        match.Press("A1", 1, afterSeconds: 1.1); // Ann first, for the second door
        Assert.Empty(match.Press("A1", 0, afterSeconds: 0.1).OfType<DoorVoted>()); // once each
        match.Press("A2", 0, afterSeconds: 0.1);
        var events = match.Press("A3", 0, afterSeconds: 0.1);

        Assert.Contains(events, e => e is ForkDecided { Door: 0 });
        match.Wait(2.1);
        Assert.All(new[] { "A1", "A2", "A3" }, id => Assert.IsType<TreasureRoom>(match.AdventurerOf(id).Minigame)); // each their own go
        Assert.NotSame(match.AdventurerOf("A1").Minigame, match.AdventurerOf("A2").Minigame);
    }

    [Fact]
    public void A_tie_goes_to_the_door_voted_for_first()
    {
        var match = PartyAtTheFork(MapWith(MinigameKind.Treasure));

        match.Press("A2", 1, afterSeconds: 1.1);
        match.Press("A1", 0, afterSeconds: 0.1);
        var events = match.Wait(8); // Cat never votes: time's up

        Assert.Contains(events, e => e is ForkDecided { Door: 1 });
    }

    [Fact]
    public void What_s_behind_the_doors_is_a_secret_until_one_opens()
    {
        var match = AtTheFork(MapWith(MinigameKind.Trap));

        var before = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        Assert.Null(before.Question); // the answer buttons vote
        Assert.Equal(2, before.Fork!.Doors);
        Assert.Null(before.Fork.Room);
        Assert.Null(before.Fork.Taken);

        match.Press("A1", 0, afterSeconds: 1.1);
        var after = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Fork!;

        Assert.Equal(0, after.Taken);
        Assert.Equal(0, after.YourVote);
        Assert.Equal("Trap", after.Room);
        Assert.Equal("A1", Assert.Single(after.Voters).StationId);
        match.Wait(2.1);
        Assert.IsType<TrapRoom>(match.AdventurerOf("A1").Minigame);
    }

    [Fact]
    public void The_main_screen_sees_them_at_the_fork()
    {
        var match = AtTheFork(MapWith(MinigameKind.Treasure));

        var lane = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!;

        Assert.Equal("Fork", lane.Minigame);
    }

    [Fact]
    public void After_the_room_the_boss_fight_starts()
    {
        var match = AtTheFork(MapWith(MinigameKind.HighStakes));
        match.Press("A1", 0, afterSeconds: 1.1);
        match.Wait(2.1);
        match.Press("A1", 3, afterSeconds: 3.6); // passes on the bet

        match.Wait(3.3); // its result

        var ann = match.AdventurerOf("A1");
        Assert.False(ann.IsAway);
        Assert.Equal("Boss", match.DelveOf("A1").Encounter.Monster.Name);
        Assert.NotNull(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Question);
        Assert.NotNull(ann.AttackAt); // its hourglass is running
    }

    /// <summary>A solo player with battle beats on (kill 2 s, the boss beat 5 s), at a fork whose door A is <paramref name="first"/>: the rat's just died.</summary>
    private static TestMatch WithBeats(MinigameKind first, bool rewards = false)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], dungeon: MapWith(first), battleBeats: true, rewards: rewards);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat dies
        return match;
    }

    [Fact]
    public void The_boss_isn_t_seen_before_the_doors_the_kill_before_them_is_a_plain_one()
    {
        var match = WithBeats(MinigameKind.HighStakes);

        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;

        Assert.Equal("kill", you.Beat!.Kind); // no WARNING! curtain, no boss
        Assert.False(you.Beat.Entrance);
        Assert.Equal(2000, you.Beat.EndsAtMs - you.Beat.StartedAtMs);
        Assert.Equal(you.Beat.EndsAtMs, you.Fork!.OpensAtMs); // the doors come up as the kill beat ends
    }

    [Fact]
    public void After_the_room_the_boss_makes_its_entrance_then_its_question_comes_up()
    {
        var match = WithBeats(MinigameKind.HighStakes);
        match.Press("A1", 0, afterSeconds: 3.1); // door A (votes count a second after the doors come up, at 2 s)
        match.Wait(2.1);                         // it opens: High Stakes
        match.Press("A1", 3, afterSeconds: 3.6); // passes

        var events = match.Wait(3.3); // its result: over

        Assert.Contains(events, e => e is BossEntered { OwnerId: "A1" });
        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        var now = match.Now.ToUnixTimeMilliseconds();
        Assert.Equal(("boss", true), (you.Beat!.Kind, you.Beat.Entrance));
        Assert.Equal(now - 1280, you.Beat.StartedAtMs); // the boss beat from its curtain (as if it began then)
        Assert.Equal(now + 3720, you.Beat.EndsAtMs);
        Assert.Equal(you.Beat.EndsAtMs, match.StreamOf("A1").CurrentShownAt!.Value.ToUnixTimeMilliseconds()); // its question then
        Assert.Equal(you.Beat.StartedAtMs, you.Delve!.BossEntranceAtMs); // the main screen announces the boss now
        Assert.Equal("Boss", match.DelveOf("A1").Encounter.Monster.Name);

        match.Wait(3.8);
        match.Press("A1", 0, afterSeconds: 1);
        Assert.False(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Beat!.Entrance); // the next beat is the fight's own
    }

    [Fact]
    public void A_reward_won_in_the_room_comes_up_after_the_boss_s_entrance()
    {
        var match = WithBeats(MinigameKind.HighStakes, rewards: true);
        match.Press("A1", 0, afterSeconds: 3.1);
        match.Wait(2.1);
        match.Press("A1", 0, afterSeconds: 3.6); // bets
        match.Press("A1", 0, afterSeconds: 1.1); // and wins (a hard one from the stream: its right answer is A)

        match.Wait(1.2 + 3.2 + 0.1); // the right answer shows, then the result: over

        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        Assert.True(you.Beat!.Entrance);
        Assert.Equal(you.Beat.EndsAtMs, you.Reward!.OpensAtMs); // the cards come up once the boss has landed
    }

    [Fact]
    public void Mini_games_off_means_no_fork()
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        settings.Dungeon.Minigames.Enabled = false;
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: MapWith(MinigameKind.Treasure));
        match.StartPlaying();

        var events = match.Press("A1", 0, afterSeconds: 4);

        Assert.DoesNotContain(events, e => e is ForkOpened);
        Assert.Null(match.AdventurerOf("A1").Fork);
    }

    [Fact]
    public void A_special_room_is_never_a_rolled_mini_game()
    {
        var random = new Random(3);

        var picked = Enumerable.Range(0, 2000).Select(_ => MinigameCatalog.Pick(random)).ToHashSet();

        Assert.DoesNotContain(MinigameKind.Treasure, picked);
        Assert.DoesNotContain(MinigameKind.Trap, picked);
        Assert.DoesNotContain(MinigameKind.HighStakes, picked);
    }
}
