using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Every Dungeon game runs on an arcade clock: kills add time, as much as they earn; when it runs
// out a swipe continues (a new session). There is no cap. Uses TestMatch.Dungeon: floor 1 = two rats (40 HP), floor 2 = a
// boss (60 HP). Slow presses (4 seconds) do 20 damage, so a rat dies every two presses and the boss in three.
public class DungeonClockTests
{
    private static MatchSettings Settings() => new()
    {
        Format = GameFormat.Dungeon,
        MatchSeconds = 120, // Classic's round: a Dungeon game doesn't use it
        Dungeon =
        {
            ClockStartSeconds = 60, SecondsPerKill = 10, SecondsPerBoss = 30,
            ContinueSeconds = 60, ContinueCountdownSeconds = 10, PartyHpPerMember = 0.75,
        },
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
        Assert.True(snapshot.EarnsTime);
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

        for (var i = 0; i < 7; i++) // two rats (+10 each), then the boss (+30)
            match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(3, match.DelveOf("A1").Kills);
        Assert.Equal(TimeSpan.FromSeconds(110), ClockLength(match));
    }

    [Fact]
    public void There_is_no_cap_the_clock_keeps_growing_while_they_keep_killing()
    {
        var match = Solo(Settings());

        for (var i = 0; i < 40; i++) // rats and the boss, round and round the dungeon: far more time than it started with
            match.Press("A1", 0, afterSeconds: 1);

        Assert.True(match.DelveOf("A1").Kills > 10);
        Assert.True(ClockLength(match) > TimeSpan.FromSeconds(150));
        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);
    }

    [Fact]
    public void In_a_race_each_party_s_kills_earn_their_share_of_the_shared_clock()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1", "A2"]);
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4); // rat dead: 10 seconds, shared by two parties

        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.Equal(TimeSpan.FromSeconds(65), ClockLength(match));
        Assert.True(SnapshotBuilder.Build(match.Engine, Viewer.Main).EarnsTime);
    }

    [Fact]
    public void A_party_faces_tougher_monsters_and_earns_less_time_per_kill()
    {
        // 2v2 Combined: two parties of two. Monsters have 175% HP for them; a kill earns 10 x 1.75 / 2, shared by the two parties.
        var match = new TestMatch(GameModeKind.TeamCombinedScore, Settings());
        match.StartPlaying();
        Assert.Equal(70, match.DelveOf("A1").MonsterHp);

        for (var i = 0; i < 3; i++)
            match.Press("A1", 0, afterSeconds: 1); // 30 a crit: dead on the third (only Ann answers, so no party combo)

        Assert.Equal(1, match.DelveOf("A1").Kills);
        Assert.Equal(TimeSpan.FromSeconds(60 + 10 * 1.75 / 2 / 2), ClockLength(match));
    }

    [Fact]
    public void A_solo_Classic_game_plays_a_fixed_round()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { MatchSeconds = 120 }, stations: ["A1"]);
        match.StartPlaying();

        Assert.False(match.Engine.EarnsTime);
        Assert.Equal(TimeSpan.FromSeconds(120), ClockLength(match));
    }

    // ---------------------------------------------------------------- Continue? (a swipe: a second paid session)

    [Fact]
    public void When_the_solo_clock_runs_out_it_waits_for_a_continue()
    {
        var match = Solo(Settings());

        var events = match.FinishPhase();

        Assert.Equal(MatchPhase.Continue, match.Engine.Phase);
        Assert.Equal(MatchPhase.Continue, Assert.Single(events.OfType<PhaseChanged>()).Phase);
        Assert.Equal(TimeSpan.FromSeconds(10), ClockLength(match)); // ContinueCountdownSeconds
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
        Assert.Equal(TimeSpan.FromSeconds(60), ClockLength(match)); // ContinueSeconds
        Assert.Equal(1, match.Engine.ContinuesUsed);
        Assert.Equal(2, match.DelveOf("A1").Encounter.Room);
        Assert.Equal(90, match.AdventurerOf("A1").Hp);
        Assert.Equal(match.Now, match.StreamOf("A1").CurrentShownAt);
        Assert.Equal(match.Now.AddSeconds(10), match.AdventurerOf("A1").AttackAt);

        match.Press("A1", 0, afterSeconds: 4);
        Assert.Equal(20, match.DelveOf("A1").MonsterHp); // the buttons work again
    }

    [Fact]
    public void Each_continue_is_a_fresh_session_that_earns_time_too()
    {
        var match = Solo(Settings()); // 60 to start, continues of 60
        match.FinishPhase();
        match.Engine.Continue(match.Now);                 // a fresh session

        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);            // a kill: +10
        Assert.Equal(TimeSpan.FromSeconds(70), ClockLength(match));

        match.FinishPhase();
        Assert.Equal(MatchPhase.Continue, match.Engine.Phase); // and another can follow, however many came before
        match.Engine.Continue(match.Now);
        Assert.Equal(2, match.Engine.ContinuesUsed);
    }

    [Fact]
    public void No_continues_when_they_are_turned_off()
    {
        var settings = Settings();
        settings.Dungeon.ContinueSeconds = 0;
        var match = Solo(settings);

        match.FinishPhase();

        Assert.Equal(MatchPhase.Results, match.Engine.Phase);
    }

    [Fact]
    public void A_group_game_waits_for_a_continue_too_and_one_swipe_brings_everyone_back()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings(), stations: ["A1", "A2"]);
        match.StartPlaying();
        match.Press("A2", 1, afterSeconds: 4); // Bob is hit: 90 HP

        match.FinishPhase();
        Assert.Equal(MatchPhase.Continue, match.Engine.Phase);

        match.Engine.Continue(match.Now);

        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);
        Assert.Equal(TimeSpan.FromSeconds(60), ClockLength(match));
        Assert.Equal(90, match.AdventurerOf("A2").Hp);
        Assert.NotNull(match.AdventurerOf("A1").AttackAt); // both are fighting again
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
        copy.Dungeon.ClockStartSeconds = 5;

        Assert.Equal(60, configured.Dungeon.ClockStartSeconds);
    }
}
