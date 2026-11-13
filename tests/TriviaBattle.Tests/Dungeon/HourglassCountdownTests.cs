using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The hourglass's last seconds (DungeonSettings.HourglassCountdownSeconds, 3): the room hears each one counted down,
// and the station winds up for the attack from DelveView.CountdownFromMs.
public class HourglassCountdownTests
{
    private static TestMatch Solo()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 }, stations: ["A1"]);
        match.StartPlaying();
        return match;
    }

    /// <summary>Waits until this long before the monster attacks, and returns the countdown ticks heard on the way.</summary>
    private static List<int> WaitUntilLeft(TestMatch match, double secondsLeft)
    {
        var attackAt = match.AdventurerOf("A1").AttackAt!.Value;
        return match.Wait((attackAt - match.Now).TotalSeconds - secondsLeft).OfType<HourglassCountdown>().Select(c => c.SecondsLeft).ToList();
    }

    [Fact]
    public void The_last_three_seconds_are_counted_down_once_each()
    {
        var match = Solo();

        Assert.Empty(WaitUntilLeft(match, 3.5));
        Assert.Equal([3], WaitUntilLeft(match, 2.9));
        Assert.Empty(WaitUntilLeft(match, 2.5)); // still the third second: heard once
        Assert.Equal([2], WaitUntilLeft(match, 1.9));
        Assert.Equal([1], WaitUntilLeft(match, 0.5));
    }

    [Fact]
    public void Answering_stops_it_and_the_next_hourglass_counts_down_afresh()
    {
        var match = Solo();
        Assert.Equal([3], WaitUntilLeft(match, 2.5));

        match.Press("A1", 0, afterSeconds: 0.1); // a fresh hourglass for the next question

        Assert.Empty(WaitUntilLeft(match, 4));
        Assert.Equal([3], WaitUntilLeft(match, 2.5));
    }

    [Fact]
    public void The_station_gets_when_the_last_seconds_start()
    {
        var match = Solo();

        var delve = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!;

        Assert.Equal(delve.AttackAtMs - 3000, delve.CountdownFromMs);
    }
}
