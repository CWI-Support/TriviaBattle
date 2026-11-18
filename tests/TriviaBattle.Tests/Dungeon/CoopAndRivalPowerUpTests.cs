using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The power-ups between players: Phone a Friend (co-op: a teammate answers your question with you), and the ones aimed
// at the leading rival (a race, a team battle): Fog Bomb (their answers blur) and Tiny Hourglass (their hourglass runs out
// twice as fast for a question). A Shield soaks a rival's. Teams here: red A1 + A3, blue A2 + A4 (2v2 Combined: each
// answers for themselves, each team shares a delve, so a team is a party). Presses wait a second (past the grace).
public class CoopAndRivalPowerUpTests
{
    private static TestMatch Battle()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 });
        match.StartPlaying();
        return match;
    }

    private static int Right(TestMatch match, string stationId) => match.StreamOf(stationId).CurrentQuestion.CorrectIndex;

    private static IReadOnlyList<MatchEvent> UsePowerUp(TestMatch match, string stationId, PowerUp powerUp)
    {
        match.Engine.GivePowerUp(stationId, powerUp);
        return match.Press(stationId, ButtonPress.Action, afterSeconds: 1);
    }

    // ---------------------------------------------------------------- Which can drop

    [Fact]
    public void Each_drops_only_where_it_makes_sense()
    {
        var solo = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1"]);
        solo.StartPlaying();
        var race = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1", "A2"]);
        race.StartPlaying();
        var battle = Battle();

        var soloFinds = solo.Engine.Dungeon!.PowerUpsFor(solo.StreamOf("A1").OwnerId);
        var raceFinds = race.Engine.Dungeon!.PowerUpsFor(race.StreamOf("A1").OwnerId);
        var battleFinds = battle.Engine.Dungeon!.PowerUpsFor(battle.StreamOf("A1").OwnerId);

        Assert.Contains(PowerUp.TimeCrystal, soloFinds); // the clock is theirs alone
        Assert.DoesNotContain(PowerUp.PhoneAFriend, soloFinds);
        Assert.DoesNotContain(PowerUp.FogBomb, soloFinds);
        Assert.DoesNotContain(PowerUp.TimeCrystal, raceFinds); // a shared clock
        Assert.DoesNotContain(PowerUp.PhoneAFriend, raceFinds); // racing alone
        Assert.Contains(PowerUp.FogBomb, raceFinds);
        Assert.Contains(PowerUp.TinyHourglass, raceFinds);
        Assert.Contains(PowerUp.PhoneAFriend, battleFinds); // a teammate
        Assert.Contains(PowerUp.FogBomb, battleFinds); // and the other team
        Assert.All([soloFinds, raceFinds, battleFinds], finds => Assert.Contains(PowerUp.Potion, finds));
    }

    // ---------------------------------------------------------------- Rivals

    [Fact]
    public void A_fog_bomb_blurs_the_leading_rival_s_answers_for_three_seconds()
    {
        var match = Battle();
        match.Press("A4", Right(match, "A4"), afterSeconds: 1); // blue's leader: A4 has points, A2 none

        var use = Assert.Single(UsePowerUp(match, "A1", PowerUp.FogBomb).OfType<PowerUpUsed>()).Use;

        Assert.Equal("A4", use.TargetOwnerId);
        var rival = match.AdventurerOf("A4");
        Assert.True(rival.FogFrom >= match.Now - TimeSpan.FromSeconds(1)); // now, or when their next question is up
        Assert.Equal(rival.FogFrom + TimeSpan.FromSeconds(3), rival.FogUntil);
        var view = SnapshotBuilder.Build(match.Engine, Viewer.Player("A4")).You!.Delve!;
        Assert.Equal(rival.FogUntil!.Value.ToUnixTimeMilliseconds(), view.FogUntilMs);
        Assert.Equal((PowerUp.FogBomb, "Ann", false), (view.LastSabotage!.PowerUp, view.LastSabotage.FromName, view.LastSabotage.Blocked));
        Assert.Null(match.AdventurerOf("A2").FogUntil); // only the leader
        Assert.Null(match.AdventurerOf("A3").FogUntil); // never a teammate
    }

    [Fact]
    public void A_tiny_hourglass_halves_what_the_rival_has_left()
    {
        var match = Battle();
        match.Press("A2", Right(match, "A2"), afterSeconds: 1); // blue's leader
        var rival = match.AdventurerOf("A2");
        match.Wait(1);
        var left = rival.AttackAt!.Value - (match.Now + TimeSpan.FromSeconds(1));

        UsePowerUp(match, "A1", PowerUp.TinyHourglass);

        Assert.Equal(match.Now + left / 2, rival.AttackAt);
    }

    [Fact]
    public void A_tiny_hourglass_that_comes_while_the_hourglass_is_stopped_halves_the_next()
    {
        var adventurer = new Adventurer("A1", "red", 100, HeroClass.Ranger, lives: 3);

        adventurer.HalveHourglass(TestMatch.Start); // no hourglass running
        adventurer.StartHourglass(TestMatch.Start, 8);
        adventurer.StartHourglass(TestMatch.Start, 8);

        Assert.Equal(TestMatch.Start + TimeSpan.FromSeconds(8), adventurer.AttackAt); // only the first was halved
        var fresh = new Adventurer("A2", "red", 100, HeroClass.Ranger, lives: 3);
        fresh.HalveHourglass(TestMatch.Start);
        fresh.StartHourglass(TestMatch.Start, 8);
        Assert.Equal(TestMatch.Start + TimeSpan.FromSeconds(4), fresh.AttackAt);
    }

    [Fact]
    public void A_shield_soaks_a_rival_s_power_up()
    {
        var match = Battle();
        match.Press("A2", Right(match, "A2"), afterSeconds: 1);
        UsePowerUp(match, "A2", PowerUp.Shield);
        var attackAt = match.AdventurerOf("A2").AttackAt;

        var use = Assert.Single(UsePowerUp(match, "A1", PowerUp.TinyHourglass).OfType<PowerUpUsed>()).Use;

        Assert.True(use.Blocked);
        var rival = match.AdventurerOf("A2");
        Assert.Equal(attackAt, rival.AttackAt); // untouched
        Assert.False(rival.Shielded); // and the Shield's spent
        Assert.True(rival.LastSabotage!.Blocked);
    }

    [Fact]
    public void With_no_rival_to_aim_at_they_keep_it()
    {
        var solo = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1"]);
        solo.StartPlaying();

        Assert.Empty(UsePowerUp(solo, "A1", PowerUp.FogBomb));
        Assert.Equal(PowerUp.FogBomb, solo.AdventurerOf("A1").Held);
    }

    // ---------------------------------------------------------------- Phone a Friend

    [Fact]
    public void Phone_a_friend_puts_your_question_on_a_teammate_s_screen_and_holds_theirs()
    {
        var match = Battle();

        var use = Assert.Single(UsePowerUp(match, "A1", PowerUp.PhoneAFriend).OfType<PowerUpUsed>()).Use;

        Assert.Equal("A3", use.TargetOwnerId); // the teammate, not a rival
        var friendView = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3")).You!;
        Assert.True(friendView.Helping!.Phoned);
        Assert.Equal("Ann", friendView.Helping.Name);
        Assert.Equal(match.StreamOf("A1").CurrentQuestion.Text, friendView.Helping.Question.Text);
        Assert.Null(match.AdventurerOf("A3").AttackAt); // their own question waits
        Assert.Equal("Cat", SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.CallingName);
    }

    [Fact]
    public void The_friend_s_answer_is_the_caller_s_and_ends_the_call()
    {
        var match = Battle();
        UsePowerUp(match, "A1", PowerUp.PhoneAFriend);
        var callerQuestion = match.StreamOf("A1").CurrentNumber;
        var friendQuestion = match.StreamOf("A3").CurrentNumber;

        var hit = Assert.Single(match.Press("A3", Right(match, "A1"), afterSeconds: 1).OfType<MonsterHit>());

        Assert.Equal("A1", hit.OwnerId); // the hit is the caller's
        Assert.Equal(callerQuestion + 1, match.StreamOf("A1").CurrentNumber);
        Assert.Equal(friendQuestion, match.StreamOf("A3").CurrentNumber); // the friend's own is still to answer
        Assert.True(match.Engine.Roster.FindPlayer("A1")!.Tally.Score > 0);
        Assert.Equal(0, match.Engine.Roster.FindPlayer("A3")!.Tally.Score);
        Assert.Null(match.AdventurerOf("A1").CalledFriend);
        Assert.NotNull(match.AdventurerOf("A3").AttackAt); // their own question goes on, its hourglass running again
        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A3")).You!.Helping);
    }

    [Fact]
    public void The_caller_can_still_answer_it_themselves()
    {
        var match = Battle();
        UsePowerUp(match, "A1", PowerUp.PhoneAFriend);

        match.Press("A1", Right(match, "A1"), afterSeconds: 1);

        Assert.Null(match.AdventurerOf("A3").AnsweringFor);
        Assert.NotNull(match.AdventurerOf("A3").AttackAt);
    }

    [Fact]
    public void When_the_call_ends_the_friend_s_own_question_shows_again_from_then()
    {
        var match = Battle();
        UsePowerUp(match, "A1", PowerUp.PhoneAFriend);
        var friendQuestion = match.StreamOf("A3").CurrentNumber;

        // The caller answers first; the friend's press for the same question lands a moment later.
        match.Press("A1", Right(match, "A1"), afterSeconds: 1);
        var hungUpAt = match.Now;
        var late = match.Press("A3", 1, afterSeconds: 0.05);

        Assert.Equal(hungUpAt, match.StreamOf("A3").CurrentShownAt); // shown again from the hang-up...
        Assert.Empty(late.OfType<AnswerGiven>()); // ...so the late press (inside the new-question grace) answers nothing
        Assert.Equal(friendQuestion, match.StreamOf("A3").CurrentNumber);
    }

    [Fact]
    public void The_call_ends_when_the_caller_s_hourglass_runs_out()
    {
        var match = Battle();
        UsePowerUp(match, "A1", PowerUp.PhoneAFriend);

        match.Wait((match.AdventurerOf("A1").AttackAt!.Value - match.Now).TotalSeconds + 0.1);

        Assert.Null(match.AdventurerOf("A3").AnsweringFor);
        Assert.NotNull(match.AdventurerOf("A3").AttackAt);
    }

    [Fact]
    public void With_no_teammate_free_they_keep_it()
    {
        var match = Battle();
        UsePowerUp(match, "A3", PowerUp.PhoneAFriend); // A3 is on the phone to A1...

        Assert.Empty(UsePowerUp(match, "A1", PowerUp.PhoneAFriend)); // ...so A1 has nobody free to call

        Assert.Equal(PowerUp.PhoneAFriend, match.AdventurerOf("A1").Held);
    }
}
