using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The opening: after the intro's countdown the hero runs into the first room (a beat, "enter", 3.5 s by default) before
// the first question comes up, and the clock waits for it.
public class OpeningTests
{
    private const double Opening = 3.5;

    private static TestMatch Solo(GameModeKind mode = GameModeKind.FreeForAll, IReadOnlyList<string>? stations = null)
    {
        var match = new TestMatch(mode, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: stations ?? ["A1"], opening: true);
        match.StartPlaying();
        return match;
    }

    private static BeatView? BeatOf(TestMatch match, string stationId = "A1") =>
        SnapshotBuilder.Build(match.Engine, Viewer.Player(stationId)).You!.Beat;

    [Fact]
    public void The_first_question_waits_for_the_opening()
    {
        var match = Solo();

        Assert.Equal(match.Now.AddSeconds(Opening), match.StreamOf("A1").CurrentShownAt);
        var beat = BeatOf(match)!;
        Assert.Equal("enter", beat.Kind);
        Assert.Equal(match.Now.ToUnixTimeMilliseconds(), beat.StartedAtMs);
        Assert.Equal(match.Now.AddSeconds(Opening).ToUnixTimeMilliseconds(), beat.EndsAtMs);
        // The monster's hourglass starts with the question, not before.
        Assert.Equal(match.Now.AddSeconds(Opening), match.AdventurerOf("A1").AttackStartedAt);
    }

    [Fact]
    public void Presses_during_the_opening_are_ignored()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: 2);

        Assert.Empty(match.StreamOf("A1").Answers);
        Assert.Equal("enter", BeatOf(match)!.Kind);
    }

    [Fact]
    public void The_clock_waits_for_the_opening()
    {
        var match = Solo();
        var round = TimeSpan.FromSeconds(match.Engine.RoundSeconds);

        match.Wait(1);
        Assert.Equal(round, match.Engine.ClockStoppedAt); // it shows the whole round, standing still
        match.Wait(Opening); // past the end of the opening, in one long tick: only the opening's part is held

        Assert.Null(match.Engine.ClockStoppedAt);
        Assert.Equal(round - TimeSpan.FromSeconds(1), match.Engine.PhaseEndsAt - match.Now);
    }

    [Fact]
    public void Each_side_s_clock_waits_for_it_in_a_race()
    {
        var match = Solo(GameModeKind.FreeForAll, ["A1", "A2"]);
        var round = TimeSpan.FromSeconds(match.Engine.RoundSeconds);

        match.Wait(Opening + 2);

        Assert.Equal(round - TimeSpan.FromSeconds(2), match.Engine.PhaseEndsAt - match.Now);
    }

    [Fact]
    public void The_clock_shows_stopped_from_the_start_and_the_screens_hear_when_it_runs()
    {
        var match = Solo();

        // The first Playing snapshot already shows it standing still at the whole round...
        Assert.Equal(TimeSpan.FromSeconds(match.Engine.RoundSeconds), match.Engine.ClockStoppedAt);

        // ...and when the opening ends the screens are told, though nothing else happens on that tick.
        var events = match.Wait(Opening);
        Assert.Contains(events, e => e is ClockHeld);
        Assert.Null(match.Engine.ClockStoppedAt);
    }

    [Fact]
    public void In_a_race_the_room_clock_counts_the_opening_as_held_not_earned()
    {
        var match = Solo(GameModeKind.FreeForAll, ["A1", "A2"]);
        var roundEnds = match.Engine.PhaseEndsAt;
        Assert.NotNull(match.Engine.ClockStoppedAt); // the room's screens show it stopped too

        match.Wait(Opening + 1);

        // The room's clock ends 3.5 s later, all of it held time: screens don't show it as time earned.
        Assert.Equal(TimeSpan.FromSeconds(Opening), match.Engine.ClockHeldFor);
        Assert.Equal(roundEnds, match.Engine.PhaseEndsAt - match.Engine.ClockHeldFor);
        Assert.Null(match.Engine.ClockStoppedAt);
    }

    [Fact]
    public void After_the_first_answer_the_opening_is_gone()
    {
        var match = Solo();

        match.Press("A1", 0, afterSeconds: Opening + 1);

        Assert.Single(match.StreamOf("A1").Answers);
        Assert.NotEqual("enter", BeatOf(match)?.Kind);
    }
}
