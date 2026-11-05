using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// Floor rewards (see Reward): answer 80% of a floor's questions right and, once its boss dies, pick one of three
// rewards with the answer buttons. Easy questions do 20; slow presses (4 seconds) don't crit.
public class DungeonRewardTests
{
    /// <summary>Floor 1: a rat (20 HP: one easy hit, hits for 20), then a boss (40 HP: two hits). Floor 2: a rat.</summary>
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["boss"]]), new FloorDef(["rat"])])],
        [
            new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 20, AttackSeconds: 10),
            new MonsterDef("boss", "Boss", "goblin-boss", Hp: 40, Damage: 20, AttackSeconds: 10, IsBoss: true),
        ]);

    private static TestMatch Match(GameModeKind mode = GameModeKind.FreeForAll, int hp = 100, string? hero = null)
    {
        var settings = new MatchSettings
        {
            Format = GameFormat.Dungeon,
            LockoutSeconds = 1,
            Dungeon = { AdventurerHp = hp, PartyHpPerMember = 0, BossChargeSteps = 0 },
        };
        var match = new TestMatch(mode, settings, stations: mode == GameModeKind.FreeForAll ? ["A1"] : null, dungeon: Map, rewards: true,
            heroes: hero != null ? new Dictionary<string, string> { ["A1"] = hero } : null);
        match.StartPlaying();
        return match;
    }

    /// <summary>The rat, then the boss's two hits: floor 1 cleared with every answer right.</summary>
    private static IReadOnlyList<MatchEvent> ClearFloorOne(TestMatch match)
    {
        match.Press("A1", 0, afterSeconds: 4);
        match.Press("A1", 0, afterSeconds: 4);
        return match.Press("A1", 0, afterSeconds: 4);
    }

    /// <summary>A card that isn't Time, so the clock only changes by the time spent choosing.</summary>
    private static int NotTime(RewardOffer offer) => offer.Choices.ToList().FindIndex(r => r != Reward.Time);

    [Fact]
    public void A_floor_answered_well_earns_a_choice_of_rewards_when_its_boss_dies()
    {
        var match = Match();

        var events = ClearFloorOne(match);

        var offer = Assert.Single(events.OfType<RewardOffered>()).Offer;
        Assert.Equal(1, offer.Floor);
        Assert.Equal(3, offer.Choices.Distinct().Count());
        var adventurer = match.AdventurerOf("A1");
        Assert.True(adventurer.IsChoosing);
        Assert.Equal(new FloorTrial(1, 3, 3, Earned: true), adventurer.LastTrial);
        Assert.Equal(new FloorTrial(2, 0, 0), adventurer.Trial); // a fresh count for floor 2
    }

    [Fact]
    public void Too_many_misses_on_the_floor_and_there_is_no_reward()
    {
        var match = Match();
        match.Press("A1", 1, afterSeconds: 4); // wrong: 3 of 4 right is 75%

        var events = ClearFloorOne(match);

        Assert.Empty(events.OfType<RewardOffered>());
        Assert.False(match.AdventurerOf("A1").IsChoosing);
        Assert.Equal(new FloorTrial(1, 3, 4, Earned: false), match.AdventurerOf("A1").LastTrial);
    }

    [Fact]
    public void An_answer_button_picks_that_card_then_the_question_comes_back_after_a_beat()
    {
        var match = Match();
        var offer = ClearFloorOne(match).OfType<RewardOffered>().Single().Offer;

        var chosen = Assert.Single(match.Press("A1", 1, afterSeconds: 1).OfType<RewardChosen>()).Taken;

        Assert.Equal(offer.Choices[1], chosen.Reward);
        Assert.True(chosen.Picked);
        var adventurer = match.AdventurerOf("A1");
        Assert.False(adventurer.IsChoosing);
        Assert.Equal([offer.Choices[1]], adventurer.Rewards.Select(r => r.Reward));
        Assert.Equal(match.Now.AddSeconds(1.5), match.StreamOf("A1").CurrentShownAt);
        Assert.Equal("reward", SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Beat!.Kind);
    }

    [Fact]
    public void Presses_too_soon_or_on_a_button_without_a_card_pick_nothing()
    {
        var match = Match();
        ClearFloorOne(match);

        Assert.Empty(match.Press("A1", 0, afterSeconds: 0.5)); // the cards have only just come up
        Assert.Empty(match.Press("A1", 3, afterSeconds: 1));   // D: there are three cards
        Assert.True(match.AdventurerOf("A1").IsChoosing);
    }

    [Fact]
    public void The_monster_waits_while_they_choose_and_time_running_out_picks_one_for_them()
    {
        var match = Match();
        ClearFloorOne(match);

        var waiting = match.Wait(11.5); // the next floor's rat would have attacked after 10
        Assert.Empty(waiting.OfType<AdventurerHit>());
        Assert.Null(match.AdventurerOf("A1").AttackAt);

        var chosen = Assert.Single(match.Wait(0.5).OfType<RewardChosen>()).Taken;
        Assert.False(chosen.Picked);
        Assert.NotNull(match.AdventurerOf("A1").AttackAt); // the question's back: the hourglass runs again
    }

    [Fact]
    public void The_clock_stops_while_they_choose()
    {
        var match = Match();
        var offer = ClearFloorOne(match).OfType<RewardOffered>().Single().Offer;
        var endsAt = match.Engine.PhaseEndsAt;

        match.Press("A1", NotTime(offer), afterSeconds: 5);

        Assert.Equal(endsAt.AddSeconds(5), match.Engine.PhaseEndsAt);
    }

    [Fact]
    public void A_player_choosing_sees_the_cards_and_no_question()
    {
        var match = Match();
        ClearFloorOne(match);

        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;

        Assert.Null(you.Question);
        Assert.Equal(3, you.Reward!.Choices.Count);
        Assert.Equal((3, 3), (you.Reward.Right, you.Reward.Answered));
        Assert.Equal(you.Reward.OpensAtMs + 1000, you.Reward.PickFromMs);
        Assert.Equal(0.8, you.Delve!.RewardAccuracy);
    }

    [Fact]
    public void In_a_party_only_those_who_answered_well_choose_and_the_others_play_on()
    {
        var match = Match(GameModeKind.TeamCombinedScore); // Red = A1 + A3, sharing one rat, then one boss
        match.Press("A3", 1, afterSeconds: 1);           // Cat misses: 0 of 1 on floor 1

        var events = ClearFloorOne(match);

        Assert.Equal(["A1"], events.OfType<RewardOffered>().Select(e => e.OwnerId));
        Assert.False(match.AdventurerOf("A3").IsChoosing);
        Assert.Contains(match.Press("A3", 0, afterSeconds: 1), e => e is MonsterHit); // she fights on
    }

    [Fact]
    public void The_clock_stays_stopped_until_everyone_in_the_party_has_picked()
    {
        var match = Match(GameModeKind.TeamCombinedScore);
        match.Press("A3", 0, afterSeconds: 1); // the rat
        match.Press("A1", 0, afterSeconds: 5); // slow enough for no party combo (it would kill the boss at once)
        var offers = match.Press("A1", 0, afterSeconds: 1).OfType<RewardOffered>().ToDictionary(e => e.OwnerId, e => e.Offer);
        Assert.Equal(["A1", "A3"], offers.Keys.Order());
        var endsAt = match.Engine.PhaseEndsAt;

        match.Press("A1", NotTime(offers["A1"]), afterSeconds: 2);
        Assert.Equal(endsAt.AddSeconds(2), match.Engine.PhaseEndsAt);
        match.Wait(1);
        Assert.Equal(endsAt.AddSeconds(3), match.Engine.PhaseEndsAt); // Cat is still choosing: still stopped
        match.Press("A3", NotTime(offers["A3"]), afterSeconds: 2);
        match.Wait(4);

        Assert.Equal(endsAt.AddSeconds(5), match.Engine.PhaseEndsAt); // and running again since she picked
    }

    // ---------------------------------------------------------------- What each reward does

    private static void Give(TestMatch match, Reward reward) => match.Engine.GiveReward("A1", reward, match.Now);

    [Fact]
    public void Might_hits_harder()
    {
        var match = Match();
        Give(match, Reward.Might);

        match.Press("A1", 0, afterSeconds: 4);

        Assert.Equal(24, match.AdventurerOf("A1").LastDealt!.Damage); // 20, 20% more
    }

    [Fact]
    public void Armor_takes_less_and_stacks()
    {
        var match = Match();
        Give(match, Reward.Armor);
        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(17, match.AdventurerOf("A1").LastTaken!.Damage); // 20, 15% less

        Give(match, Reward.Armor);
        match.Press("A1", 1, afterSeconds: 4);
        Assert.Equal(14, match.AdventurerOf("A1").LastTaken!.Damage); // 20 x 0.85 x 0.85 = 14.45
    }

    [Fact]
    public void Focus_gives_longer_to_be_fast()
    {
        var match = Match();
        Give(match, Reward.Focus);

        match.Press("A1", 0, afterSeconds: 3.5); // over the normal 3 seconds

        Assert.True(match.AdventurerOf("A1").LastDealt!.Crit);
    }

    [Fact]
    public void Vitality_raises_max_hp_and_heals_to_full()
    {
        var match = Match();
        match.Press("A1", 1, afterSeconds: 4); // 80 of 100

        Give(match, Reward.Vitality);

        Assert.Equal((125, 125), (match.AdventurerOf("A1").Hp, match.AdventurerOf("A1").MaxHp));
    }

    [Fact]
    public void Time_goes_on_the_clock()
    {
        var match = Match();
        var endsAt = match.Engine.PhaseEndsAt;

        Give(match, Reward.Time);

        Assert.Equal(endsAt.AddSeconds(20), match.Engine.PhaseEndsAt);
    }

    [Fact]
    public void A_last_stand_turns_the_blow_that_would_down_them_into_one_hp_once()
    {
        var match = Match(hp: 30);
        Give(match, Reward.LastStand);
        match.Press("A1", 1, afterSeconds: 4); // 10 left

        match.Press("A1", 1, afterSeconds: 1.5); // would down them
        var taken = match.AdventurerOf("A1").LastTaken!;
        Assert.True(taken.LastStand);
        Assert.Equal((1, false), (taken.HpLeft, taken.Downed));

        match.Press("A1", 1, afterSeconds: 1.5);
        Assert.True(match.AdventurerOf("A1").IsDowned); // it was only good once
    }

    [Fact]
    public void Mastery_makes_a_rogue_s_crits_hit_harder()
    {
        var match = Match(hero: "rogue");
        Give(match, Reward.Mastery);

        match.Press("A1", 0, afterSeconds: 1);

        Assert.Equal(60, match.AdventurerOf("A1").LastDealt!.Damage); // 20 x (2.5 + 0.5)
    }

    [Theory]
    [InlineData("rogue", 3.0)]   // the crit multiplier it becomes
    [InlineData("tank", 15.0)]   // percent less damage
    [InlineData("healer", 25.0)] // HP a heal does
    [InlineData("wizard", 75.0)] // splash percent
    [InlineData("pugilist", 20.0)] // percent per answer in a row
    [InlineData("warlock", 70.0)]  // reveal percent
    [InlineData("ranger", 2.5)]    // seconds early
    public void A_mastery_card_shows_what_the_class_s_power_becomes(string hero, double amount)
    {
        var match = Match(hero: hero);

        Assert.Equal(amount, match.Engine.Dungeon!.RewardAmount(match.AdventurerOf("A1"), Reward.Mastery));
    }
}
