using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The hot potato: the party's game. A lit bomb goes round the table; whoever holds it presses the runes shown (3) to
// throw it on to the next player, and a wrong one fumbles it. Whoever holds it when the secret fuse runs out takes the
// blast (25% of their max HP, never the last of it); everyone else wins. The intro is 3.5 s. A party here is Ann (A1)
// and Bob (A2), each their own adventurer.
public class HotPotatoTests
{
    private static readonly DungeonMap Map = new(
        [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: MinigameKind.HotPotato, MinigameAfterRoom: 1)])],
        [new MonsterDef("rat", "Cellar Rat", "rat", Hp: 10, Damage: 10, AttackSeconds: 10)]);

    /// <summary>A party of two on the hot potato (after floor 1's first room), past its intro. The fuse: 12 to 20 seconds.</summary>
    private static TestMatch Party(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 };
        settings.Dungeon.Minigames.HotPotato.FuseShortest = 12;
        settings.Dungeon.Minigames.HotPotato.FuseLongest = 20;
        configure?.Invoke(settings);
        var match = new TestMatch(GameModeKind.TeamCombinedScore, settings, stations: ["A1", "A2"],
            teams: [new TeamSetup("red", "Red", ["A1", "A2"])], dungeon: Map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat: the party's mini game starts
        match.Wait(3.5);                       // past the intro
        return match;
    }

    private static HotPotato SeatOf(TestMatch match, string stationId) => (HotPotato)match.AdventurerOf(stationId).Minigame!;

    private static string HolderOf(TestMatch match) => SeatOf(match, "A1").Holding ? "A1" : "A2";

    private static string OtherThan(string stationId) => stationId == "A1" ? "A2" : "A1";

    /// <summary>The holder presses their runes, a second after <paramref name="afterSeconds"/> and a moment apart.</summary>
    private static void Throw(TestMatch match, double afterSeconds = 0.7)
    {
        var holder = HolderOf(match);
        var runes = SeatOf(match, holder).Bomb.Sequence.ToList();
        match.Press(holder, runes[0], afterSeconds);
        foreach (var rune in runes.Skip(1))
            match.Press(holder, rune, afterSeconds: 0.2);
    }

    [Fact]
    public void The_whole_party_plays_one_bomb()
    {
        var match = Party();

        var ann = SeatOf(match, "A1");
        var bob = SeatOf(match, "A2");
        Assert.Same(ann.Bomb, bob.Bomb);
        Assert.Equal(["A1", "A2"], ann.Bomb.Seats);
        Assert.True(ann.Holding ^ bob.Holding); // one of them holds it
        Assert.Equal(3, ann.Bomb.Sequence.Count);
        Assert.All(ann.Bomb.Sequence, rune => Assert.InRange(rune, 0, 3));
        Assert.True(match.Engine.Dungeon!.ClockStopped);
    }

    [Fact]
    public void Pressing_the_runes_throws_it_to_the_next_player()
    {
        var match = Party();
        var holder = HolderOf(match);
        var bomb = SeatOf(match, holder).Bomb;
        var runes = bomb.Sequence.ToList();

        Assert.Empty(match.Press(OtherThan(holder), runes[0], afterSeconds: 0.5)); // not theirs: nothing happens
        Assert.Contains(match.Press(holder, runes[0], afterSeconds: 0.2), e => e is MinigamePlayed { Result: MinigamePress.Nudge });
        Assert.Equal(1, bomb.Entered);
        match.Press(holder, runes[1], afterSeconds: 0.2);
        var thrown = match.Press(holder, runes[2], afterSeconds: 0.2);

        Assert.Contains(thrown, e => e is MinigamePlayed { Result: MinigamePress.Hit });
        Assert.Equal(OtherThan(holder), HolderOf(match));
        Assert.Equal((1, 0), (bomb.Passes, bomb.Entered));
        Assert.Equal(match.Now, bomb.CaughtAt);

        // The catch takes a moment (0.6 s): the new holder's presses count from then.
        Assert.Empty(match.Press(OtherThan(holder), bomb.Sequence[0], afterSeconds: 0.3));
        Assert.NotEmpty(match.Press(OtherThan(holder), bomb.Sequence[0], afterSeconds: 0.4));
    }

    [Fact]
    public void A_wrong_rune_fumbles_it_back_to_the_first_with_the_buttons_dead_a_moment()
    {
        var match = Party();
        var holder = HolderOf(match);
        var bomb = SeatOf(match, holder).Bomb;
        match.Press(holder, bomb.Sequence[0], afterSeconds: 0.5);

        var fumble = match.Press(holder, (bomb.Sequence[1] + 1) % 4, afterSeconds: 0.2);

        Assert.Contains(fumble, e => e is MinigamePlayed { Result: MinigamePress.Miss });
        Assert.Equal((0, 1), (bomb.Entered, bomb.Fumbles));
        Assert.Equal(holder, HolderOf(match)); // still theirs
        Assert.Empty(match.Press(holder, bomb.Sequence[0], afterSeconds: 0.5)); // dead for 0.8 s
        Assert.NotEmpty(match.Press(holder, bomb.Sequence[0], afterSeconds: 0.4));
    }

    [Fact]
    public void It_goes_off_on_whoever_holds_it_when_the_fuse_runs_out_and_everyone_else_wins()
    {
        var match = Party();
        Throw(match);
        var holder = HolderOf(match);
        var safe = OtherThan(holder);
        var maxHp = match.AdventurerOf(holder).MaxHp;
        var fuse = SeatOf(match, holder).EndsAt;
        Assert.InRange((fuse - SeatOf(match, holder).PlaysFrom).TotalSeconds, 12, 20);

        var events = match.Wait((fuse - match.Now).TotalSeconds + 0.05);

        Assert.Contains(events, e => e is MinigameLost { OwnerId: var o } && o == holder);
        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won, OwnerId: var o } && o == safe);
        Assert.False(SeatOf(match, holder).Won);
        Assert.True(SeatOf(match, safe).Won);
        var blasted = match.AdventurerOf(holder);
        Assert.Equal(maxHp - (int)Math.Round(maxHp * 0.25), blasted.Hp);
        Assert.False(blasted.IsDowned);
        Assert.Equal((holder, (int)Math.Round(maxHp * 0.25)), SeatOf(match, holder).Bomb.Blast);
        Assert.Equal(match.AdventurerOf(safe).MaxHp, match.AdventurerOf(safe).Hp);

        // Once the result has shown (3.2 s) both are back in the fight.
        match.Wait(3.3);
        Assert.Null(match.AdventurerOf("A1").Minigame);
        Assert.Null(match.AdventurerOf("A2").Minigame);
        Assert.Equal(1, match.AdventurerOf(safe).MinigamesWon);
        Assert.Equal(0, match.AdventurerOf(holder).MinigamesWon);
    }

    [Fact]
    public void The_blast_never_downs_anyone()
    {
        var match = Party(s => s.Dungeon.Minigames.HotPotato.BlastShare = 5);
        var holder = HolderOf(match);

        match.Wait((SeatOf(match, holder).EndsAt - match.Now).TotalSeconds + 0.05);

        Assert.Equal(1, match.AdventurerOf(holder).Hp);
        Assert.False(match.AdventurerOf(holder).IsDowned);
    }

    [Fact]
    public void Screens_learn_when_it_goes_off_only_once_it_has()
    {
        var match = Party();
        var seat = SeatOf(match, "A1");

        var view = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!;

        Assert.Equal("HotPotato", view.Kind);
        Assert.Equal(seat.PlaysFrom.AddSeconds(20).ToUnixTimeMilliseconds(), view.EndsAtMs); // the longest it could be
        Assert.Equal((12000, 20000), (view.HotPotato!.FuseShortestMs, view.HotPotato.FuseLongestMs));
        Assert.Equal(["Ann", "Bob"], view.HotPotato.Seats.Select(s => s.Name));
        Assert.Equal(0, view.HotPotato.You);
        Assert.Null(view.HotPotato.BlastOwnerId);

        match.Wait((seat.EndsAt - match.Now).TotalSeconds + 0.05);
        var after = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!;

        Assert.Equal(seat.EndsAt.ToUnixTimeMilliseconds(), after.EndsAtMs);
        Assert.Equal(seat.Bomb.HolderOwnerId, after.HotPotato!.BlastOwnerId);
        Assert.True(after.HotPotato.BlastDamage > 0);
    }

    [Fact]
    public void A_player_on_their_own_gets_another_game_instead()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1"], dungeon: Map);
        match.StartPlaying();

        var started = match.Press("A1", 0, afterSeconds: 4);

        var kind = Assert.Single(started.OfType<MinigameStarted>()).Kind;
        Assert.NotEqual(MinigameKind.HotPotato, kind);
        Assert.DoesNotContain(kind, MinigameCatalog.NeverFirst);
        Assert.Equal(kind, match.AdventurerOf("A1").Minigame!.Kind);
    }

    [Fact]
    public void A_party_with_only_one_left_to_play_gets_the_same_stand_in_a_solo_player_would()
    {
        var map = Map;
        Assert.Equal(map.MinigameFor(1, MinigameKind.HotPotato, players: 1), map.MinigameFor(1, MinigameKind.HotPotato, players: 1));
        Assert.Equal(MinigameKind.HotPotato, map.MinigameFor(1, MinigameKind.HotPotato, players: 2));
        Assert.Equal(MinigameKind.RuneLock, map.MinigameFor(1, MinigameKind.RuneLock, players: 1));
    }
}
