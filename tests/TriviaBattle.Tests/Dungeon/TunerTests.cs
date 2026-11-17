using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The tuner: four sinking bars, one per answer button (A-D). A press raises its bar by Step; all four held in their
// target zones together for HoldSeconds wins it; the action button does nothing. The intro is 3.5 s.
public class TunerTests
{
    /// <summary>A match on the tuner, past its intro. sink: every bar's speed (0: they stay put), so the tests know where they are.</summary>
    private static TestMatch Match(double sink = 0, double hold = 0.5)
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: MinigameKind.Tuner, MinigameAfterRoom: 1)])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)]);
        var settings = new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1 };
        settings.Dungeon.Minigames.Tuner.SinkSlowest = sink;
        settings.Dungeon.Minigames.Tuner.SinkFastest = sink;
        settings.Dungeon.Minigames.Tuner.HoldSeconds = hold;
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat: the mini game starts
        match.Wait(3.5);                       // past the intro
        return match;
    }

    private static Tuner TunerOf(TestMatch match) => (Tuner)match.AdventurerOf("A1").Minigame!;

    /// <summary>Presses a bar up until it's in its zone (they don't sink here), a press every 0.05 s.</summary>
    private static List<MatchEvent> TuneIn(TestMatch match, int bar)
    {
        var events = new List<MatchEvent>();
        var tuner = TunerOf(match);
        while (tuner.LevelOf(bar, match.Now) < tuner.BarList[bar].ZoneLow)
            events.AddRange(match.Press("A1", bar, afterSeconds: 0.05));
        return events;
    }

    [Fact]
    public void Four_empty_bars_each_with_a_zone_to_reach()
    {
        var tuner = TunerOf(Match(sink: 0.1));

        Assert.Equal(4, tuner.BarList.Count);
        Assert.All(tuner.BarList, bar =>
        {
            Assert.Equal(0, bar.Level);
            Assert.InRange(bar.ZoneLow, 0.225 - 1e-9, 0.725 + 1e-9); // middles 0.3 to 0.8, 0.15 tall
            Assert.Equal(0.15, bar.ZoneHigh - bar.ZoneLow, 9);
        });
    }

    [Fact]
    public void A_press_raises_its_own_bar_and_it_sinks_from_there()
    {
        var match = Match(sink: 0.1);

        var played = match.Press("A1", 2, afterSeconds: 0.1).OfType<MinigamePlayed>().Single();

        Assert.Equal(MinigamePress.Nudge, played.Result); // no sound of its own
        var tuner = TunerOf(match);
        Assert.Equal(0.1, tuner.LevelOf(2, match.Now), 9);
        Assert.Equal(0, tuner.LevelOf(0, match.Now)); // the others untouched
        Assert.Equal(0.05, tuner.LevelOf(2, match.Now + TimeSpan.FromSeconds(0.5)), 9); // sinking 0.1 a second
        Assert.Equal(0, tuner.LevelOf(2, match.Now + TimeSpan.FromSeconds(5))); // never below empty
    }

    [Fact]
    public void The_action_button_does_nothing()
    {
        var match = Match();

        Assert.Empty(match.Press("A1", ButtonPress.Action, afterSeconds: 0.1).OfType<MinigamePlayed>());
        Assert.Equal(0, TunerOf(match).Presses);
    }

    [Fact]
    public void All_four_held_in_their_zones_wins_it()
    {
        var match = Match(sink: 0, hold: 0.5);
        for (var bar = 0; bar < 4; bar++)
            TuneIn(match, bar);
        var tuner = TunerOf(match);
        Assert.Null(tuner.FinishedAt); // all in, but not held long enough yet

        var events = match.Wait(0.5);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
        Assert.True(tuner.Won);
    }

    [Fact]
    public void A_bar_sinking_into_its_zone_counts()
    {
        // Every bar pushed over the top of its zone; they sink into place together, and that wins it (no press needed).
        var match = Match(sink: 0.1, hold: 0);
        var tuner = TunerOf(match);
        for (var bar = 0; bar < 4; bar++)
            while (tuner.LevelOf(bar, match.Now) <= tuner.BarList[bar].ZoneHigh + 0.05)
                match.Press("A1", bar, afterSeconds: 0.01);
        Assert.Null(tuner.FinishedAt);

        match.Wait(3); // long enough for every one to sink in, not out (zones are 0.2 tall: 2 s at 0.1 a second)

        Assert.True(tuner.Won);
    }

    [Fact]
    public void Out_of_time_loses_it()
    {
        var match = Match();

        match.Wait(41);

        Assert.False(TunerOf(match).Won);
        Assert.NotNull(TunerOf(match).FinishedAt);
    }

    [Fact]
    public void The_screen_gets_each_bar_s_numbers()
    {
        var match = Match(sink: 0.1);
        match.Press("A1", 1, afterSeconds: 0.1);

        var view = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.Tuner!;

        Assert.Equal(4, view.Bars.Count);
        Assert.Equal((0.1, match.Now.ToUnixTimeMilliseconds()), (view.Bars[1].Level, view.Bars[1].AtMs));
        Assert.Equal(TunerOf(match).BarList[1].ZoneLow, view.Bars[1].ZoneLow);
        Assert.Equal(500, view.HoldMs);
    }
}
