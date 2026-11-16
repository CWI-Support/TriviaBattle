using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// A race (each player a side) or a team battle (each team a side) runs a clock per side (PartyClock): each side's kills
// add to its own, its mini games hold only its own, and a side whose time runs out is done (no continue) while the
// others play on; Playing ends when the last side's done. Ann (A1) and Bob (A2) race here. Right answers do 20, a rat
// has 20 HP (one hit), and a kill earns 10 s.
public class PartyClockTests
{
    private static MatchSettings Settings() => new()
    {
        Format = GameFormat.Dungeon,
        Dungeon = { ClockStartSeconds = 60, SecondsPerKill = 10, SecondsPerBoss = 30, ContinueSeconds = 60, ContinueCountdownSeconds = 10 },
    };

    private static readonly DungeonMap Rats = new(
        [new DepthDef("d", "Depth", [new FloorDef(["rat", "rat", "rat", "rat"])])],
        [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 30)]);

    private static TestMatch Race(DungeonMap? map = null)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1", "A2"], dungeon: map ?? Rats);
        match.StartPlaying();
        return match;
    }

    private static PartyClock ClockOf(TestMatch match, string stationId) => match.Engine.ClockOf(match.StreamOf(stationId).OwnerId)!;

    private static int Right(TestMatch match, string stationId) => match.StreamOf(stationId).CurrentQuestion.CorrectIndex;

    [Fact]
    public void Each_side_gets_its_own_clock_and_only_its_own_kills_add_to_it()
    {
        var match = Race();
        var start = ClockOf(match, "A1").EndsAt;
        Assert.Equal(start, ClockOf(match, "A2").EndsAt);

        match.Press("A1", Right(match, "A1"), afterSeconds: 4); // Ann kills a rat: +10 s

        Assert.Equal(start + TimeSpan.FromSeconds(10), ClockOf(match, "A1").EndsAt);
        Assert.Equal(start, ClockOf(match, "A2").EndsAt); // Bob's untouched
    }

    [Fact]
    public void A_mini_game_holds_only_that_side_s_clock()
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: MinigameKind.RuneLock, MinigameAfterRoom: 1)])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 30)]);
        var match = Race(map);
        match.Press("A1", Right(match, "A1"), afterSeconds: 4); // Ann empties the room: her mini game
        Assert.NotNull(match.AdventurerOf("A1").Minigame);
        var ann = ClockOf(match, "A1").EndsAt;
        var bob = ClockOf(match, "A2").EndsAt;

        match.Wait(5);

        Assert.Equal(ann + TimeSpan.FromSeconds(5), ClockOf(match, "A1").EndsAt); // held still: pushed back as time goes
        Assert.NotNull(ClockOf(match, "A1").StoppedLeft);
        Assert.Equal(bob, ClockOf(match, "A2").EndsAt); // Bob's runs on
        Assert.Null(ClockOf(match, "A2").StoppedLeft);
    }

    [Fact]
    public void A_side_out_of_time_is_done_while_the_others_play_on_and_the_last_one_ends_it()
    {
        var match = Race();
        match.Press("A1", Right(match, "A1"), afterSeconds: 4); // Ann: +10 s, so Bob runs out first

        var events = match.Wait((ClockOf(match, "A2").EndsAt - match.Now).TotalSeconds);

        Assert.Contains(events, e => e is SideFinished);
        Assert.True(ClockOf(match, "A2").IsDone);
        Assert.Equal(MatchPhase.Playing, match.Engine.Phase); // Ann plays on
        Assert.Null(match.AdventurerOf("A2").AttackAt); // Bob's monster stops
        Assert.Empty(match.Press("A2", Right(match, "A2"), afterSeconds: 1)); // and his buttons are dead
        Assert.NotEmpty(match.Press("A1", Right(match, "A1"), afterSeconds: 1));

        match.Wait((ClockOf(match, "A1").EndsAt - match.Now).TotalSeconds);

        Assert.True(ClockOf(match, "A1").IsDone);
        Assert.Equal(MatchPhase.Results, match.Engine.Phase); // no continue in a race
    }

    [Fact]
    public void Each_station_is_sent_its_own_side_s_clock_and_the_room_the_last_one()
    {
        var match = Race();
        match.Press("A1", Right(match, "A1"), afterSeconds: 4); // Ann: +10 s

        var ann = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1"));
        var bob = SnapshotBuilder.Build(match.Engine, Viewer.Player("A2"));
        var room = SnapshotBuilder.Build(match.Engine, Viewer.Main);

        Assert.Equal(ClockOf(match, "A1").EndsAt.ToUnixTimeMilliseconds(), ann.PhaseEndsAtMs);
        Assert.Equal(ClockOf(match, "A2").EndsAt.ToUnixTimeMilliseconds(), bob.PhaseEndsAtMs);
        Assert.Equal(ann.PhaseEndsAtMs, room.PhaseEndsAtMs); // the room's runs to the last side's end
        Assert.Equal(bob.PhaseEndsAtMs, bob.You!.Delve!.ClockEndsAtMs); // and the lanes get each side's

        match.Wait((ClockOf(match, "A2").EndsAt - match.Now).TotalSeconds);

        Assert.True(SnapshotBuilder.Build(match.Engine, Viewer.Player("A2")).You!.Delve!.TimeUp);
        Assert.False(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!.TimeUp);
    }

    [Fact]
    public void A_battle_runs_a_clock_per_team()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, Settings(), dungeon: Rats);
        match.StartPlaying();

        Assert.Equal(2, match.Engine.PartyClocks.Count); // red (A1, A3) and blue (A2, A4)
        Assert.Same(ClockOf(match, "A1"), ClockOf(match, "A3"));
        Assert.NotSame(ClockOf(match, "A1"), ClockOf(match, "A2"));
    }

    [Fact]
    public void A_solo_game_keeps_the_match_clock()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1"], dungeon: Rats);
        match.StartPlaying();

        Assert.Empty(match.Engine.PartyClocks);
        Assert.Null(match.Engine.ClockOf(match.StreamOf("A1").OwnerId));
    }
}
