using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Battle beats: after each answer the next question waits while the screen shows the fight.
// Defaults: a hit 1.5 s, the monster's attack 1.7 s, a kill 2 s, one that empties the room 3 s (the walk on), a floor cleared 3 s.
// Uses TestMatch.Dungeon: floor 1 = two rats (40 HP), floor 2 = a boss (60 HP). Slow presses (4 s) do 20.
public class DungeonBeatTests
{
    private static TestMatch Solo()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], battleBeats: true);
        match.StartPlaying();
        return match;
    }

    private static BeatView? BeatOf(TestMatch match) => SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Beat;

    [Fact]
    public void After_a_hit_the_next_question_waits_for_the_beat()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(match.Now.AddSeconds(1.5), match.StreamOf("A1").CurrentShownAt);
        var beat = BeatOf(match)!;
        Assert.Equal("hit", beat.Kind);
        Assert.Equal(match.Now.ToUnixTimeMilliseconds(), beat.StartedAtMs);
        Assert.Equal(match.Now.AddSeconds(1.5).ToUnixTimeMilliseconds(), beat.EndsAtMs);
    }

    [Fact]
    public void Presses_during_the_beat_are_ignored()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);

        var events = match.Press("A1", 0, afterSeconds: 0.5); // the fight is still on screen

        Assert.Empty(events);
        Assert.Equal(20, match.DelveOf("A1").MonsterHp);
    }

    [Fact]
    public void The_hourglass_waits_until_the_next_question_appears()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(match.Now.AddSeconds(1.5 + 10), match.AdventurerOf("A1").AttackAt);
    }

    [Fact]
    public void Kills_and_floors_get_longer_beats()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4); // 20 (4 s to answer)
        match.Press("A1", 0, afterSeconds: 4); // a crit (2.5 s: the beat took 1.5): first rat dead, its room empty

        Assert.Equal("kill", BeatOf(match)!.Kind);
        Assert.True(match.DelveOf("A1").LastHit!.RoomCleared);
        Assert.Equal(match.Now.AddSeconds(3), match.StreamOf("A1").CurrentShownAt); // the walk to the next room

        match.Press("A1", 0, afterSeconds: 4); // a crit (1 s: the walk took 3): 30 of the second rat's 40
        match.Press("A1", 0, afterSeconds: 4); // a crit: second rat dead, floor 1 cleared, the boss steps up

        Assert.Equal("boss", BeatOf(match)!.Kind);
        Assert.Equal(match.Now.AddSeconds(5), match.StreamOf("A1").CurrentShownAt); // its entrance: the curtain, then it leaps in
    }

    [Fact]
    public void A_kill_with_more_monsters_waiting_in_the_room_is_a_plain_kill_beat()
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat", "rat"], ["rat"]])])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)]);
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], battleBeats: true, dungeon: map);
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 4); // the first rat of two

        Assert.False(match.DelveOf("A1").LastHit!.RoomCleared);
        Assert.Equal(match.Now.AddSeconds(2), match.StreamOf("A1").CurrentShownAt); // no walk: the next one steps up
    }

    [Fact]
    public void A_wrong_answer_shows_the_monster_attacking()
    {
        var match = Solo();

        match.Press("A1", 1, afterSeconds: 4);

        Assert.Equal("hurt", BeatOf(match)!.Kind);
        Assert.Equal(match.Now + TimeSpan.FromSeconds(1.7), match.StreamOf("A1").CurrentShownAt);
        Assert.Equal(90, match.DelveOf("A1").LastTaken!.HpLeft);
    }

    [Fact]
    public void Once_the_next_question_shows_there_is_no_beat()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);

        Assert.NotNull(BeatOf(match));
        match.Press("A1", 0, afterSeconds: 2); // the next question has been up for half a second: this answers it

        Assert.Equal(3, match.StreamOf("A1").CurrentNumber);
    }

    [Fact]
    public void Classic_format_has_no_beats()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, stations: ["A1"], battleBeats: true);
        match.StartPlaying();

        match.Press("A1", 0);

        Assert.Equal(match.Now, match.StreamOf("A1").CurrentShownAt);
        Assert.Null(BeatOf(match));
    }
}
