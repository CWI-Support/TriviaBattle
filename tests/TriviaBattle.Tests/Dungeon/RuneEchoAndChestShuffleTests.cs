using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The rune echo (a "Simon says": the runes light up, press them back in order, one longer each round: 3 to 7 runes) and the chest
// shuffle (a coin in one of five chests, shuffled; pick it, the action button being the fifth). The intro is 3.5 s.
public class RuneEchoAndChestShuffleTests
{
    private static TestMatch Match(MinigameKind kind)
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: kind, MinigameAfterRoom: 1)])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)]);
        var settings = new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1 };
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat: the mini game starts
        match.Wait(3.5);                       // past the intro
        return match;
    }

    private static T GameOf<T>(TestMatch match) where T : Minigame => (T)match.AdventurerOf("A1").Minigame!;

    // ---------------------------------------------------------------- Rune echo

    /// <summary>Waits for the runes to be shown, then presses them all back (wrongAt: press a wrong one there instead).</summary>
    private static List<MinigamePress> EchoTheRound(TestMatch match, int wrongAt = -1)
    {
        var echo = GameOf<RuneEcho>(match);
        match.Wait(Math.Max(0, (echo.InputFrom - match.Now).TotalSeconds));
        var results = new List<MinigamePress>();
        foreach (var (rune, i) in echo.Sequence.Select((r, i) => (r, i)))
        {
            var press = i == wrongAt ? (rune + 1) % 4 : rune;
            results.Add(match.Press("A1", press, afterSeconds: 0.3).OfType<MinigamePlayed>().Single().Result);
            if (i == wrongAt)
                break;
        }
        return results;
    }

    [Fact]
    public void Rune_echo_rounds_get_one_rune_longer_and_the_last_one_wins_it()
    {
        var match = Match(MinigameKind.RuneEcho);
        Assert.Equal(3, GameOf<RuneEcho>(match).Length);

        EchoTheRound(match);
        Assert.Equal((2, 4), (GameOf<RuneEcho>(match).Round, GameOf<RuneEcho>(match).Length));
        EchoTheRound(match);
        EchoTheRound(match);
        EchoTheRound(match);
        var last = EchoTheRound(match); // the fifth: 7 runes

        Assert.Equal(MinigamePress.Won, last[^1]);
        Assert.True(GameOf<RuneEcho>(match).Won);
    }

    [Fact]
    public void Presses_while_the_runes_are_showing_do_nothing()
    {
        var match = Match(MinigameKind.RuneEcho);

        Assert.Empty(match.Press("A1", GameOf<RuneEcho>(match).Expected, afterSeconds: 0.9)); // the first rune is lit
    }

    [Fact]
    public void A_wrong_rune_shows_the_round_again()
    {
        var match = Match(MinigameKind.RuneEcho);

        var results = EchoTheRound(match, wrongAt: 1);

        Assert.Equal([MinigamePress.Hit, MinigamePress.Miss], results);
        var echo = GameOf<RuneEcho>(match);
        Assert.Equal((1, 0), (echo.Round, echo.Entered));
        Assert.Equal(1.2, (echo.ShowFrom - match.Now).TotalSeconds, precision: 3); // its runes again, after a pause
        Assert.False(echo.FinishedAt.HasValue);
    }

    [Fact]
    public void A_rune_never_lights_twice_running()
    {
        var match = Match(MinigameKind.RuneEcho);
        for (var round = 0; round < 3; round++)
            EchoTheRound(match);

        var sequence = GameOf<RuneEcho>(match).Sequence;

        Assert.All(sequence.Zip(sequence.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    // ---------------------------------------------------------------- Chest shuffle

    /// <summary>Waits for the chests to stop, then picks the place the coin's in (or, wrong, the next place along).</summary>
    private static IReadOnlyList<MatchEvent> Pick(TestMatch match, bool right = true)
    {
        var shuffle = GameOf<ChestShuffle>(match);
        match.Wait(Math.Max(0, (shuffle.PickFrom - match.Now).TotalSeconds));
        var coinAt = shuffle.FinalPlaces.ToList().IndexOf(shuffle.CoinChest);
        return match.Press("A1", right ? coinAt : (coinAt + 1) % ChestShuffle.Chests, afterSeconds: 0.2);
    }

    [Fact]
    public void Finding_the_coin_every_round_wins_it_each_round_shuffling_more_and_faster()
    {
        var match = Match(MinigameKind.ChestShuffle);
        var shuffle = GameOf<ChestShuffle>(match);
        Assert.Equal((5, 0.4), (shuffle.Swaps.Count, shuffle.SwapSeconds));

        Assert.Equal(MinigamePress.Hit, Pick(match).OfType<MinigamePlayed>().Single().Result);
        Assert.Equal((2, 7, 0.32), (shuffle.Round, shuffle.Swaps.Count, shuffle.SwapSeconds));
        Pick(match);

        Assert.Equal(MinigamePress.Won, Pick(match).OfType<MinigamePlayed>().Single().Result);
    }

    [Fact]
    public void The_wrong_chest_loses_it_there_and_then()
    {
        var match = Match(MinigameKind.ChestShuffle);

        var result = Pick(match, right: false).OfType<MinigamePlayed>().Single().Result;

        Assert.Equal(MinigamePress.Lost, result);
        Assert.False(GameOf<ChestShuffle>(match).Won);
        Assert.NotNull(GameOf<ChestShuffle>(match).FinishedAt);
    }

    [Fact]
    public void The_action_button_picks_the_fifth_chest_and_doesn_t_use_a_power_up()
    {
        var match = Match(MinigameKind.ChestShuffle);
        match.Engine.GivePowerUp("A1", PowerUp.Shield);
        var shuffle = GameOf<ChestShuffle>(match);
        match.Wait((shuffle.PickFrom - match.Now).TotalSeconds);

        var played = match.Press("A1", ButtonPress.Action, afterSeconds: 0.2).OfType<MinigamePlayed>().Single();

        Assert.Equal(4, shuffle.LastPick);
        Assert.Equal(shuffle.FinalPlaces[4] == shuffle.CoinChest ? MinigamePress.Hit : MinigamePress.Lost, played.Result);
        Assert.Equal(PowerUp.Shield, match.AdventurerOf("A1").Held); // still held
    }

    [Fact]
    public void Picks_while_the_chests_are_moving_do_nothing()
    {
        var match = Match(MinigameKind.ChestShuffle);

        Assert.Empty(match.Press("A1", 0, afterSeconds: 2)); // the lids have only just closed
    }

    [Fact]
    public void Coin_swap_share_makes_swaps_move_the_coin_s_chest()
    {
        var settings = new MinigameSettings();
        settings.ChestShuffle.CoinSwapShare = 1; // every swap
        var shuffle = new ChestShuffle(DateTimeOffset.UnixEpoch, settings, new Random(3));

        var coinAt = shuffle.CoinChest;
        foreach (var (a, b) in shuffle.Swaps)
        {
            Assert.True(coinAt == a || coinAt == b, "every swap should move the coin's chest");
            coinAt = coinAt == a ? b : a;
        }
        Assert.Equal(coinAt, shuffle.FinalPlaces.ToList().IndexOf(shuffle.CoinChest));
    }

    [Fact]
    public void The_screen_gets_the_swaps_to_move_the_chests_by()
    {
        var match = Match(MinigameKind.ChestShuffle);

        var view = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.ChestShuffle!;

        Assert.Equal(5, view.Swaps.Count);
        Assert.All(view.Swaps, s => Assert.True(s[0] < s[1] && s[1] < 5));
        Assert.Equal(view.RoundStartsAtMs + 1600 + 5 * 400, view.PickFromMs);
    }

    [Fact]
    public void Every_kind_is_as_likely_as_the_others()
    {
        var random = new Random(7);

        var counts = Enumerable.Range(0, 3000).Select(_ => MinigameCatalog.Pick(random)).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, n => Assert.InRange(n, 850, 1150));
    }
}
