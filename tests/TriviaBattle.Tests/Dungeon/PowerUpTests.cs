using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Uses TestMatch.Dungeon: floor 1 = two rats (40 HP, hit for 10), floor 2 = a boss (60 HP, hits for 50).
// Easy questions do 20 damage (30 once the streak makes it a combo); presses here wait 4 seconds, so
// no crits. Drops are switched off unless a test is about them, so nothing turns up by chance.
public class PowerUpTests
{
    private static TestMatch Solo(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        settings.Dungeon.PowerUpDropChance = 0;
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.FreeForAll, settings, questionCount: 30, stations: ["A1"]);
        match.StartPlaying();
        return match;
    }

    /// <summary>2v2 Combined: Red is A1 + A3, each their own adventurer with their own HP.</summary>
    private static TestMatch Party(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        settings.Dungeon.PowerUpDropChance = 0;
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.TeamCombinedScore, settings, questionCount: 30);
        match.StartPlaying();
        return match;
    }

    private static IReadOnlyList<MatchEvent> PressAction(TestMatch match, string stationId) =>
        match.Press(stationId, ButtonPress.Action, afterSeconds: 0.5);

    [Fact]
    public void Five_right_answers_in_a_row_earn_a_power_up()
    {
        var match = Solo();

        for (var i = 0; i < 4; i++)
            match.Press("A1", 0, afterSeconds: 4);
        Assert.Null(match.AdventurerOf("A1").Held);

        var events = match.Press("A1", 0, afterSeconds: 4);

        var found = Assert.Single(events.OfType<PowerUpFound>());
        Assert.Equal(found.PowerUp, match.AdventurerOf("A1").Held);
        Assert.Equal(found.PowerUp, match.AdventurerOf("A1").LastDealt!.Found);
    }

    [Fact]
    public void A_boss_always_drops_one()
    {
        var match = Solo(s => s.Dungeon.PowerUpStreak = 0);
        while (!match.DelveOf("A1").Encounter.Monster.IsBoss)
            match.Press("A1", 0, afterSeconds: 4);

        IReadOnlyList<MatchEvent> events = [];
        while (match.DelveOf("A1").Encounter.Monster.IsBoss)
            events = match.Press("A1", 0, afterSeconds: 4);

        Assert.Single(events.OfType<PowerUpFound>());
        Assert.NotNull(match.AdventurerOf("A1").Held);
    }

    [Fact]
    public void They_hold_one_at_a_time()
    {
        var match = Solo();
        match.Engine.GivePowerUp("A1", PowerUp.Potion);

        var events = new List<MatchEvent>();
        for (var i = 0; i < 5; i++)
            events.AddRange(match.Press("A1", 0, afterSeconds: 4));

        Assert.Empty(events.OfType<PowerUpFound>());
        Assert.Equal(PowerUp.Potion, match.AdventurerOf("A1").Held);
    }

    [Fact]
    public void The_action_button_uses_it_and_does_nothing_with_empty_pockets()
    {
        var match = Solo();
        Assert.Empty(PressAction(match, "A1"));

        match.Engine.GivePowerUp("A1", PowerUp.Shield);
        var events = PressAction(match, "A1");

        var used = Assert.Single(events.OfType<PowerUpUsed>());
        Assert.Equal(PowerUp.Shield, used.Use.PowerUp);
        Assert.Null(match.AdventurerOf("A1").Held);
        Assert.True(match.AdventurerOf("A1").Shielded);
        Assert.Equal(1, match.StreamOf("A1").CurrentNumber); // it isn't an answer
    }

    [Fact]
    public void A_fireball_triples_the_next_right_answer_only()
    {
        var match = Solo();
        match.Engine.GivePowerUp("A1", PowerUp.Fireball);
        PressAction(match, "A1");

        match.Press("A1", 0, afterSeconds: 4);
        var fireball = match.AdventurerOf("A1").LastDealt!;
        match.Press("A1", 0, afterSeconds: 4);
        var next = match.AdventurerOf("A1").LastDealt!;

        Assert.True(fireball.Fireball);
        Assert.Equal(60, fireball.Damage);
        Assert.False(next.Fireball);
        Assert.Equal(20, next.Damage);
    }

    [Fact]
    public void A_shield_takes_the_next_hit_and_the_streak_survives()
    {
        var match = Solo();
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);
        match.Engine.GivePowerUp("A1", PowerUp.Shield);
        PressAction(match, "A1");

        match.Press("A1", 1, afterSeconds: 4);

        var adventurer = match.AdventurerOf("A1");
        Assert.True(adventurer.LastTaken!.Blocked);
        Assert.Equal(0, adventurer.LastTaken.Damage);
        Assert.Equal(100, adventurer.Hp);
        Assert.Equal(2, adventurer.Streak);
        Assert.False(adventurer.Shielded);

        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(90, adventurer.Hp); // gone: the next one lands
    }

    [Fact]
    public void A_freeze_holds_the_hourglass_then_it_starts_over()
    {
        var match = Solo();
        match.Engine.GivePowerUp("A1", PowerUp.Freeze);
        PressAction(match, "A1");

        // Frozen for 10 seconds, then the rats' 10-second hourglass: nothing for 19 seconds.
        Assert.Empty(match.Wait(19).OfType<AdventurerHit>());
        Assert.Single(match.Wait(1.5).OfType<AdventurerHit>());
    }

    [Fact]
    public void A_fifty_fifty_hides_two_wrong_answers_and_pressing_them_does_nothing()
    {
        var match = Solo();
        match.Engine.GivePowerUp("A1", PowerUp.FiftyFifty);
        PressAction(match, "A1");

        var hidden = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Question!.Hidden!;
        Assert.Equal(2, hidden.Count);
        Assert.DoesNotContain(0, hidden); // the right answer is never one of them

        Assert.Empty(match.Press("A1", hidden[0], afterSeconds: 4));
        Assert.Single(match.Press("A1", 0, afterSeconds: 1).OfType<MonsterHit>());
        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Question!.Hidden); // only that question
    }

    [Fact]
    public void A_time_crystal_adds_time_to_a_solo_clock()
    {
        var match = Solo();
        var before = match.Engine.PhaseEndsAt;
        match.Engine.GivePowerUp("A1", PowerUp.TimeCrystal);

        PressAction(match, "A1");

        Assert.Equal(before + TimeSpan.FromSeconds(20), match.Engine.PhaseEndsAt);
    }

    [Fact]
    public void A_potion_goes_to_whoever_in_the_party_needs_it_most()
    {
        var match = Party();
        match.Press("A3", 1, afterSeconds: 4); // the rat hits Cat for 10
        match.Press("A3", 1, afterSeconds: 4);
        match.Engine.GivePowerUp("A1", PowerUp.Potion);

        var used = Assert.Single(PressAction(match, "A1").OfType<PowerUpUsed>()).Use;

        Assert.Equal("A3", used.TargetOwnerId);
        Assert.Equal(20, used.Amount); // healed to full, no further
        Assert.Equal(100, match.AdventurerOf("A3").Hp);
    }

    [Fact]
    public void A_potion_gets_a_downed_teammate_straight_back_up()
    {
        var match = Party(s => s.Dungeon.AdventurerHp = 10);
        match.Press("A3", 1, afterSeconds: 4); // downed: Cat waits for the party
        Assert.True(match.AdventurerOf("A3").IsDowned);
        match.Engine.GivePowerUp("A1", PowerUp.Potion);

        var events = PressAction(match, "A1");

        Assert.True(Assert.Single(events.OfType<PowerUpUsed>()).Use.Revived);
        Assert.Contains(events.OfType<AdventurerGotUp>(), e => e.OwnerId == "A3");
        Assert.False(match.AdventurerOf("A3").IsDowned);
        Assert.Null(match.StreamOf("A3").LockedOutUntil); // buttons work again
    }

    [Fact]
    public void Downed_adventurers_cant_use_one()
    {
        var match = Solo(s => s.Dungeon.AdventurerHp = 10);
        match.Press("A1", 1, afterSeconds: 4);
        match.Engine.GivePowerUp("A1", PowerUp.Potion);

        Assert.Empty(PressAction(match, "A1"));
        Assert.Equal(PowerUp.Potion, match.AdventurerOf("A1").Held);
    }
}
