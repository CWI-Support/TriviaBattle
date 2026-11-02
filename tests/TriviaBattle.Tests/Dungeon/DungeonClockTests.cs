using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// A solo game runs on an arcade clock (kills add time, up to a hard cap); groups play a fixed round.
// Uses TestMatch.Dungeon: floor 1 = two rats (40 HP), floor 2 = a boss (60 HP). Slow presses (4 seconds)
// do 20 damage, 30 from the third in a row (combo), so a rat dies every two presses.
public class DungeonClockTests
{
    private static MatchSettings Settings(double soloMaxSeconds = 1000) => new()
    {
        Format = GameFormat.Dungeon,
        MatchSeconds = 120,
        Dungeon = { SoloStartSeconds = 60, SoloSecondsPerKill = 10, SoloSecondsPerBoss = 30, SoloMaxSeconds = soloMaxSeconds },
    };

    private static TestMatch Solo(MatchSettings settings)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"]);
        match.StartPlaying();
        return match;
    }

    private static TimeSpan ClockLength(TestMatch match) => match.Engine.PhaseEndsAt - match.Engine.PhaseStartedAt;

    [Fact]
    public void A_solo_game_starts_on_the_solo_clock()
    {
        var match = Solo(Settings());

        Assert.Equal(TimeSpan.FromSeconds(60), ClockLength(match));
        var snapshot = SnapshotBuilder.Build(match.Engine, Viewer.Main);
        Assert.True(snapshot.SoloClock);
        Assert.Equal(60, snapshot.MatchSeconds);
    }

    [Fact]
    public void Killing_a_monster_adds_time_to_a_solo_game()
    {
        var match = Solo(Settings());

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal(TimeSpan.FromSeconds(60), ClockLength(match)); // a hit alone earns nothing

        match.Press("A1", 0, afterSeconds: 4); // rat dead

        Assert.Equal(TimeSpan.FromSeconds(70), ClockLength(match));
    }

    [Fact]
    public void A_boss_kill_adds_the_boss_time()
    {
        var match = Solo(Settings());

        for (var i = 0; i < 6; i++) // two rats (+10 each), then the boss (+30)
            match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(3, match.DelveOf("A1").Kills);
        Assert.Equal(TimeSpan.FromSeconds(110), ClockLength(match));
    }

    [Fact]
    public void The_solo_clock_never_runs_past_the_hard_cap()
    {
        var match = Solo(Settings(soloMaxSeconds: 65));

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // rat dead: would be 70

        Assert.Equal(TimeSpan.FromSeconds(65), ClockLength(match));
    }

    [Fact]
    public void The_solo_starting_time_is_cut_to_the_hard_cap()
    {
        var match = Solo(Settings(soloMaxSeconds: 45));

        Assert.Equal(TimeSpan.FromSeconds(45), ClockLength(match));
    }

    [Fact]
    public void A_group_game_plays_a_fixed_round()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1", "A2"]);
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // rat dead

        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.Equal(TimeSpan.FromSeconds(120), ClockLength(match));
        Assert.False(SnapshotBuilder.Build(match.Engine, Viewer.Main).SoloClock);
    }

    [Fact]
    public void A_solo_Classic_game_plays_a_fixed_round()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { MatchSeconds = 120 }, stations: ["A1"]);
        match.StartPlaying();

        Assert.False(match.Engine.HasSoloClock);
        Assert.Equal(TimeSpan.FromSeconds(120), ClockLength(match));
    }

    // ---------------------------------------------------------------- Continue? (solo only)

    [Fact]
    public void When_the_solo_clock_runs_out_it_waits_for_a_continue()
    {
        var match = Solo(Settings());

        var events = match.FinishPhase();

        Assert.Equal(MatchPhase.Continue, match.Engine.Phase);
        Assert.Equal(MatchPhase.Continue, Assert.Single(events.OfType<PhaseChanged>()).Phase);
        Assert.Equal(TimeSpan.FromSeconds(10), ClockLength(match)); // SoloContinueCountdownSeconds
        Assert.Equal(60, SnapshotBuilder.Build(match.Engine, Viewer.Main).ContinueSeconds);
    }

    [Fact]
    public void Without_a_continue_the_countdown_runs_out_into_the_results()
    {
        var match = Solo(Settings());
        match.FinishPhase();

        match.FinishPhase();

        Assert.Equal(MatchPhase.Results, match.Engine.Phase);
    }

    [Fact]
    public void A_continue_puts_the_player_back_where_they_were_with_more_time()
    {
        var match = Solo(Settings());
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // first rat dead
        match.Press("A1", 1, afterSeconds: 4); // and a hit taken: 90 HP
        match.FinishPhase();

        var events = match.Engine.Continue(match.Now);

        Assert.Equal(MatchPhase.Playing, Assert.Single(events.OfType<PhaseChanged>()).Phase);
        Assert.Equal(TimeSpan.FromSeconds(60), ClockLength(match)); // SoloContinueSeconds
        Assert.Equal(1, match.Engine.ContinuesUsed);
        Assert.Equal(2, match.DelveOf("A1").Encounter.Room);
        Assert.Equal(90, match.AdventurerOf("A1").Hp);
        Assert.Equal(match.Now, match.StreamOf("A1").CurrentShownAt);
        Assert.Equal(match.Now.AddSeconds(10), match.AdventurerOf("A1").AttackAt);

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal(20, match.DelveOf("A1").MonsterHp); // the buttons work again
    }

    [Fact]
    public void Continues_count_toward_the_hard_cap()
    {
        var match = Solo(Settings(soloMaxSeconds: 130)); // 60 to start, continues of 60
        match.FinishPhase();                              // 60 played
        match.Engine.Continue(match.Now);                 // 60 + 60 fits under 130

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);            // a kill: +10, the cap allows 70 in this stretch
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);            // another: still 70
        Assert.Equal(TimeSpan.FromSeconds(70), ClockLength(match));

        match.FinishPhase();                              // 130 played: no whole continue fits any more

        Assert.Equal(MatchPhase.Results, match.Engine.Phase);
    }

    [Fact]
    public void No_continues_when_they_are_turned_off()
    {
        var settings = Settings();
        settings.Dungeon.SoloContinueSeconds = 0;
        var match = Solo(settings);

        match.FinishPhase();

        Assert.Equal(MatchPhase.Results, match.Engine.Phase);
    }

    [Fact]
    public void A_group_game_never_waits_for_a_continue()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1", "A2"]);
        match.StartPlaying();

        match.FinishPhase();

        Assert.Equal(MatchPhase.Results, match.Engine.Phase);
    }

    [Fact]
    public void A_continue_while_playing_does_nothing()
    {
        var match = Solo(Settings());

        Assert.Empty(match.Engine.Continue(match.Now));
        Assert.Equal(0, match.Engine.ContinuesUsed);
    }

    [Fact]
    public void A_match_copy_has_its_own_dungeon_settings()
    {
        var configured = Settings();

        var copy = configured.Copy();
        copy.Dungeon.SoloStartSeconds = 5;

        Assert.Equal(60, configured.Dungeon.SoloStartSeconds);
    }
}
