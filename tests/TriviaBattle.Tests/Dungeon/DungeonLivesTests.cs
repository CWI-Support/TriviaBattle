using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Lives (DungeonSettings.Lives, 3): every downing costs one, and downed on the last they're out. Out solo, or with the
// whole party out, the game goes to "Continue?"; a swipe brings everyone back with all their lives.
public class DungeonLivesTests
{
    /// <summary>One room: an ogre that downs anyone in one hit (100 damage), with HP to last.</summary>
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [new FloorDef(["ogre"])])],
        [new MonsterDef("ogre", "Ogre", "goblin", Hp: 10_000, Damage: 100, AttackSeconds: 30)]);

    private static TestMatch Match(GameModeKind mode = GameModeKind.FreeForAll, int lives = 3)
    {
        var settings = new MatchSettings
        {
            Format = GameFormat.Dungeon,
            LockoutSeconds = 1,
            Dungeon = { AdventurerHp = 100, PartyHpPerMember = 0, Lives = lives, DownedSeconds = 5, PartyDownedSeconds = 20 },
        };
        var match = new TestMatch(mode, settings, stations: mode == GameModeKind.FreeForAll ? ["A1"] : null, dungeon: Map);
        match.StartPlaying();
        return match;
    }

    /// <summary>A wrong answer (the ogre downs them), then the downed time to get back up.</summary>
    private static void DownedAndUp(TestMatch match, string stationId = "A1")
    {
        match.Press(stationId, 1, afterSeconds: 1.5);
        match.Wait(5);
    }

    [Fact]
    public void Each_downing_costs_a_life_and_the_last_one_puts_them_out()
    {
        var match = Match();

        DownedAndUp(match);
        Assert.Equal((2, false), (match.AdventurerOf("A1").Lives, match.AdventurerOf("A1").IsDowned));
        DownedAndUp(match);
        Assert.Equal(1, match.AdventurerOf("A1").Lives);

        match.Press("A1", 1, afterSeconds: 1.5);

        var adventurer = match.AdventurerOf("A1");
        Assert.True(adventurer.IsOut);
        Assert.True(adventurer.LastTaken!.Out);
        var delve = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Delve!;
        Assert.Equal((0, 3, true), (delve.Lives, delve.MaxLives, delve.Out));
    }

    [Fact]
    public void Out_of_lives_on_their_own_and_the_game_goes_to_continue()
    {
        var match = Match();
        DownedAndUp(match);
        DownedAndUp(match);
        match.Press("A1", 1, afterSeconds: 1.5);

        match.Wait(0.1);

        Assert.Equal(MatchPhase.Continue, match.Engine.Phase); // the clock had plenty left
    }

    [Fact]
    public void A_continue_brings_them_back_with_all_their_lives()
    {
        var match = Match();
        DownedAndUp(match);
        DownedAndUp(match);
        match.Press("A1", 1, afterSeconds: 1.5);
        match.Wait(0.1);

        match.Engine.Continue(match.Now);

        var adventurer = match.AdventurerOf("A1");
        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);
        Assert.Equal((3, false, 100), (adventurer.Lives, adventurer.IsOut, adventurer.Hp));
        Assert.Contains(match.Press("A1", 0, afterSeconds: 1), e => e is MonsterHit); // buttons work again
    }

    [Fact]
    public void In_a_party_the_one_out_sits_out_and_the_rest_fight_on()
    {
        var match = Match(GameModeKind.TeamCombinedScore); // Red = A1 + A3
        for (var i = 0; i < 3; i++)
        {
            match.Press("A1", 1, afterSeconds: 1.5);                 // Ann goes down...
            if (i < 2)
                for (var r = 0; r < 3; r++)
                    match.Press("A3", 0, afterSeconds: 1.5);         // ...and Cat's three right answers revive her
        }
        Assert.True(match.AdventurerOf("A1").IsOut);

        for (var r = 0; r < 3; r++)
            match.Press("A3", 0, afterSeconds: 1.5);                 // no reviving her now
        match.Wait(60);                                              // nor getting up by herself

        Assert.True(match.AdventurerOf("A1").IsOut);
        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);        // Cat (and Blue) play on
        var ann = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3")).You!.Delve!.Party.Single(m => m.OwnerId == "A1");
        Assert.Equal((0, true), (ann.Lives, ann.Out));
    }

    [Fact]
    public void With_no_limit_they_always_get_up()
    {
        var match = Match(lives: 0);

        for (var i = 0; i < 5; i++)
            DownedAndUp(match);

        Assert.False(match.AdventurerOf("A1").IsOut);
        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);
    }
}
