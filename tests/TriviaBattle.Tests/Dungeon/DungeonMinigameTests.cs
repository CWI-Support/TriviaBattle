using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Mini games between two rooms of a floor (Core/Dungeon/Minigames). The first is the rune lock: stop the needle on
// the glowing mark 6 times in a row; a miss starts the count again and locks the buttons for 2 seconds; 30 seconds to
// do it. The intro takes 3.5 seconds, the result 2.5. Easy questions do 20, so one hit kills a rat.
public class DungeonMinigameTests
{
    /// <summary>Floor 1: a rat, the lock, another rat, then a boss. Floor 2: a rat.</summary>
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [
            new FloorDef(Rooms: [["rat"], ["rat"], ["boss"]], Minigame: MinigameKind.RuneLock, MinigameAfterRoom: 1),
            new FloorDef(["rat"])])],
        [
            new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "goblin-boss", Hp: 1000, Damage: 10, AttackSeconds: 10, IsBoss: true),
        ]);

    private static TestMatch Match(GameModeKind mode = GameModeKind.FreeForAll, bool rewards = false)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1, Dungeon = { PartyHpPerMember = 0 } };
        var match = new TestMatch(mode, settings, stations: mode == GameModeKind.FreeForAll ? ["A1"] : null, dungeon: Map, rewards: rewards);
        match.StartPlaying();
        return match;
    }

    private static RuneLock LockOf(TestMatch match, string stationId = "A1") => (RuneLock)match.AdventurerOf(stationId).Minigame!;

    /// <summary>Kills the first rat (the room before the lock), then waits out the intro.</summary>
    private static IReadOnlyList<MatchEvent> ReachTheLock(TestMatch match, bool skipIntro = true)
    {
        var events = match.Press("A1", 0, afterSeconds: 4);
        if (skipIntro)
            match.Wait(3.5);
        return events;
    }

    /// <summary>Waits until the needle is on the middle of the mark, then presses.</summary>
    private static IReadOnlyList<MatchEvent> PressOnTheMark(TestMatch match, string stationId = "A1")
    {
        var game = LockOf(match, stationId);
        var needle = game.NeedleAt(match.Now);
        var ahead = game.DegreesPerSecond > 0 ? game.MarkAngle - needle : needle - game.MarkAngle;
        var degrees = ((ahead % 360) + 360) % 360;
        return match.Press(stationId, 0, afterSeconds: degrees / Math.Abs(game.DegreesPerSecond));
    }

    /// <summary>Waits until the needle is opposite the mark, then presses: a miss.</summary>
    private static IReadOnlyList<MatchEvent> PressOffTheMark(TestMatch match)
    {
        var game = LockOf(match);
        var needle = game.NeedleAt(match.Now);
        var opposite = (game.MarkAngle + 180) % 360;
        var ahead = game.DegreesPerSecond > 0 ? opposite - needle : needle - opposite;
        return match.Press("A1", 0, afterSeconds: (((ahead % 360) + 360) % 360) / Math.Abs(game.DegreesPerSecond));
    }

    [Fact]
    public void Clearing_the_room_before_it_starts_the_mini_game_with_its_intro()
    {
        var match = Match();

        var started = Assert.Single(ReachTheLock(match, skipIntro: false).OfType<MinigameStarted>());

        Assert.Equal(MinigameKind.RuneLock, started.Kind);
        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        Assert.Null(you.Question); // the answer buttons play it now
        Assert.Equal("RuneLock", you.Minigame!.Kind);
        Assert.Equal(you.Minigame.IntroAtMs + 3500, you.Minigame.PlaysFromMs);
        Assert.Equal(you.Minigame.PlaysFromMs + 30_000, you.Minigame.EndsAtMs);
        Assert.Equal(6, you.Minigame.RuneLock!.HitsNeeded);
        Assert.Empty(match.Press("A1", 0, afterSeconds: 1)); // the intro: presses don't count yet
    }

    [Fact]
    public void Six_hits_in_a_row_open_the_lock()
    {
        var match = Match();
        ReachTheLock(match);

        for (var i = 0; i < 5; i++)
            Assert.Equal(MinigamePress.Hit, Assert.Single(PressOnTheMark(match).OfType<MinigamePlayed>()).Result);
        var last = Assert.Single(PressOnTheMark(match).OfType<MinigamePlayed>());

        Assert.Equal(MinigamePress.Won, last.Result);
        Assert.True(LockOf(match).Won);
    }

    [Fact]
    public void Each_hit_sends_the_needle_back_the_other_way_faster()
    {
        var match = Match();
        ReachTheLock(match);
        var speed = LockOf(match).DegreesPerSecond;

        PressOnTheMark(match);

        Assert.Equal(-speed * 1.1, LockOf(match).DegreesPerSecond, precision: 6);
    }

    [Fact]
    public void The_mark_gets_narrower_with_each_hit_and_back_to_full_after_a_miss()
    {
        var match = Match();
        ReachTheLock(match);
        Assert.Equal(40, LockOf(match).MarkDegrees);

        PressOnTheMark(match);
        PressOnTheMark(match);
        Assert.Equal(40 * 0.88 * 0.88, LockOf(match).MarkDegrees, precision: 6);

        PressOffTheMark(match);
        Assert.Equal(40, LockOf(match).MarkDegrees);
    }

    [Fact]
    public void A_miss_puts_the_needle_back_to_its_first_speed()
    {
        var match = Match();
        ReachTheLock(match);
        PressOnTheMark(match);
        PressOnTheMark(match);
        PressOnTheMark(match); // three hits: 200 x 1.1 x 1.1 x 1.1, turning the other way
        var turning = Math.Sign(LockOf(match).DegreesPerSecond);

        PressOffTheMark(match);

        Assert.Equal(turning * 200, LockOf(match).DegreesPerSecond, precision: 6); // the way it was turning
        Assert.Equal(match.Now, LockOf(match).TurnStartedAt);                     // from where it was
    }

    [Fact]
    public void A_miss_starts_the_count_again_and_locks_the_buttons_for_two_seconds()
    {
        var match = Match();
        ReachTheLock(match);
        PressOnTheMark(match);
        PressOnTheMark(match);

        var miss = Assert.Single(PressOffTheMark(match).OfType<MinigamePlayed>());

        Assert.Equal(MinigamePress.Miss, miss.Result);
        Assert.Equal(0, LockOf(match).Hits);
        Assert.Equal(match.Now.AddSeconds(2), LockOf(match).LockedUntil);
        Assert.Empty(match.Press("A1", 0, afterSeconds: 1)); // still locked
    }

    [Fact]
    public void Running_out_of_time_loses_it_then_the_fight_goes_on()
    {
        var match = Match();
        ReachTheLock(match);

        Assert.Single(match.Wait(30).OfType<MinigameLost>());
        Assert.Contains("Rat", SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!.Monster.Name);

        Assert.Single(match.Wait(2.5).OfType<MinigameOver>());
        var adventurer = match.AdventurerOf("A1");
        Assert.Null(adventurer.Minigame);
        Assert.Equal(match.Now, match.StreamOf("A1").CurrentShownAt); // the question comes back
        Assert.Equal(match.Now.AddSeconds(10), adventurer.AttackAt);  // and the next rat's hourglass starts
    }

    [Fact]
    public void The_clock_stops_and_the_monster_waits_while_it_s_played()
    {
        var match = Match();
        ReachTheLock(match, skipIntro: false);
        var endsAt = match.Engine.PhaseEndsAt;

        var events = match.Wait(20); // the next rat would have attacked after 10

        Assert.Equal(endsAt.AddSeconds(20), match.Engine.PhaseEndsAt);
        Assert.Empty(events.OfType<AdventurerHit>());
        Assert.NotNull(SnapshotBuilder.Build(match.Engine, Viewer.Main).ClockStoppedLeftMs);
    }

    [Fact]
    public void Winning_earns_a_reward_pick()
    {
        var match = Match(rewards: true);
        ReachTheLock(match);
        for (var i = 0; i < 6; i++)
            PressOnTheMark(match);

        var events = match.Wait(2.5);

        Assert.Single(events.OfType<MinigameOver>());
        Assert.Single(events.OfType<RewardOffered>());
        Assert.True(match.AdventurerOf("A1").IsChoosing);
        Assert.Equal(1, match.AdventurerOf("A1").MinigamesWon);
    }

    [Fact]
    public void Everyone_in_the_party_plays_their_own_go()
    {
        var match = Match(GameModeKind.TeamCombinedScore); // Red = A1 + A3

        var started = match.Press("A1", 0, afterSeconds: 4).OfType<MinigameStarted>().Select(e => e.OwnerId);

        Assert.Equal(["A1", "A3"], started.Order());
        match.Wait(3.5);
        PressOnTheMark(match, "A3");
        Assert.Equal((0, 1), (LockOf(match, "A1").Hits, LockOf(match, "A3").Hits));
    }

    // ---------------------------------------------------------------- Where floors get one

    private static DungeonPlan Plan(double chance, int every = 0) => new(
        [new DepthDef("d", "Depth", Enumerable.Range(0, 20).Select(_ => new FloorDef(RoomCount: [2, 3], RoomHp: 20, Pool: ["rat"], Boss: "boss")).ToList())],
        [new MonsterDef("rat", "Rat", "rat", 20, 5, 10), new MonsterDef("boss", "Boss", "goblin-boss", 100, 5, 10, IsBoss: true)],
        minigameChance: chance, minigameEvery: every);

    [Fact]
    public void There_is_at_least_one_every_three_floors_however_the_rolls_go()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var map = Plan(chance: 0.4, every: 3).Build(new Random(seed));
            var floors = Enumerable.Range(1, 40).Select(floor => map.MinigameOn(floor) != null).ToList();

            for (var floor = 0; floor + 3 <= floors.Count; floor++)
                Assert.Contains(true, floors.GetRange(floor, 3)); // never three floors running without one
            Assert.Contains(false, floors);                     // still not every floor
        }
    }

    [Fact]
    public void The_promise_only_fills_in_where_luck_ran_dry()
    {
        var rolled = Plan(chance: 0.4).Build(new Random(9));
        var promised = Plan(chance: 0.4, every: 3).Build(new Random(9));

        // Every floor the dice gave one still has the same one; the promise only adds to the floors that had none.
        foreach (var floor in Enumerable.Range(1, 40).Where(f => rolled.MinigameOn(f) != null))
            Assert.Equal(rolled.MinigameOn(floor), promised.MinigameOn(floor));
    }

    [Fact]
    public void A_floor_has_at_most_one_and_never_right_before_its_boss()
    {
        var map = Plan(chance: 1).Build(new Random(3));

        for (var floor = 1; floor <= 60; floor++) // three loops of the 20 floors
        {
            var minigame = map.MinigameOn(floor);
            Assert.NotNull(minigame);
            var monsterRooms = map.RoomsOn(floor) - 1; // the boss's room is last
            Assert.InRange(minigame.Value.AfterRoom, 1, monsterRooms - 1);
            Assert.Equal(minigame.Value.Kind, map.MinigameAfter(floor, minigame.Value.AfterRoom));
        }
    }

    [Fact]
    public void Not_every_floor_has_one_and_about_the_chance_do()
    {
        var map = Plan(chance: 0.4).Build(new Random(3));

        var share = Enumerable.Range(1, 400).Count(floor => map.MinigameOn(floor) != null) / 400.0;

        Assert.InRange(share, 0.3, 0.5);
        Assert.All(Enumerable.Range(1, 40), floor => Assert.Null(Plan(chance: 0).Build(new Random(3)).MinigameOn(floor)));
    }

    [Fact]
    public void Each_loop_rolls_its_floors_afresh_so_a_long_run_keeps_finding_them()
    {
        // A one-floor dungeon: every floor reached is the same floor's rooms, looped. The mini games aren't.
        var plan = new DungeonPlan(
            [new DepthDef("d", "Depth", [new FloorDef(RoomCount: [3, 3], RoomHp: 20, Pool: ["rat"], Boss: "boss")])],
            [new MonsterDef("rat", "Rat", "rat", 20, 5, 10), new MonsterDef("boss", "Boss", "goblin-boss", 100, 5, 10, IsBoss: true)],
            minigameChance: 0.4);

        var withOne = Enumerable.Range(1, 20).Select(floor => plan.Build(new Random(1)).MinigameOn(floor) != null).ToList();

        Assert.Contains(true, withOne);
        Assert.Contains(false, withOne);
    }

    [Fact]
    public void Every_party_in_a_match_meets_the_same_ones()
    {
        var map = Plan(chance: 0.4).Build(new Random(5));

        var first = Enumerable.Range(1, 30).Select(map.MinigameOn).ToList();
        var again = Enumerable.Range(1, 30).Reverse().Select(map.MinigameOn).Reverse().ToList(); // asked in another order

        Assert.Equal(first, again);
    }
}
