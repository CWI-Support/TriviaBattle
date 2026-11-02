using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Dungeon;

/// <summary>
/// Hero photos: in the intro the action button says yes, the station takes it after a countdown, and the
/// server keeps it until the match ends. The intro is 7 seconds and the countdown 3 (the defaults).
/// </summary>
public class HeroPhotoTests
{
    private static TestMatch Match(Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon };
        configure?.Invoke(settings);
        return new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1", "A2"]);
    }

    private static IReadOnlyList<MatchEvent> SayYes(TestMatch match, string stationId, double afterSeconds = 1) =>
        match.Press(stationId, ButtonPress.Action, afterSeconds);

    private static YouView You(TestMatch match, string stationId) =>
        SnapshotBuilder.Build(match.Engine, Viewer.Player(stationId)).You!;

    [Fact]
    public void The_action_button_in_the_intro_asks_for_a_photo_taken_after_the_countdown()
    {
        var match = Match();

        var events = SayYes(match, "A1");

        Assert.Equal(new PhotoAsked("A1"), Assert.Single(events));
        var takeAt = match.Now + TimeSpan.FromSeconds(3);
        Assert.Equal(takeAt, match.Engine.Roster.FindPlayer("A1")!.PhotoAt);
        Assert.Equal(takeAt.ToUnixTimeMilliseconds(), You(match, "A1").PhotoAtMs);
        Assert.Null(You(match, "A1").PhotoOfferUntilMs); // said yes: no more offer

        // The other player is still being offered one, until the countdown would no longer fit in the intro.
        var offerEnds = match.Engine.PhaseEndsAt - TimeSpan.FromSeconds(3);
        Assert.Equal(offerEnds.ToUnixTimeMilliseconds(), You(match, "A2").PhotoOfferUntilMs);
        Assert.Null(You(match, "A2").PhotoAtMs);
    }

    [Fact]
    public void Too_late_in_the_intro_or_twice_is_ignored()
    {
        var match = Match();

        Assert.Single(SayYes(match, "A1"));
        Assert.Empty(SayYes(match, "A1"));       // once per match
        Assert.Empty(SayYes(match, "A2", 4.5));  // 5.5 s in: only 1.5 s of intro left, the countdown wouldn't fit
        Assert.Null(match.Engine.Roster.FindPlayer("A2")!.PhotoAt);
    }

    [Fact]
    public void There_is_no_offer_in_Classic_or_with_photos_switched_off()
    {
        var classic = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Classic }, stations: ["A1"]);
        var off = Match(s => s.Dungeon.HeroPhotos = false);

        foreach (var match in new[] { classic, off })
        {
            Assert.Null(You(match, "A1").PhotoOfferUntilMs);
            Assert.Empty(SayYes(match, "A1"));
        }
    }

    [Fact]
    public void Once_playing_the_action_button_is_for_power_ups_again()
    {
        var match = Match();
        match.StartPlaying();

        Assert.Null(You(match, "A1").PhotoOfferUntilMs);
        Assert.DoesNotContain(SayYes(match, "A1"), e => e is PhotoAsked);
    }

    [Fact]
    public void A_photo_only_counts_from_a_player_who_asked_and_only_once()
    {
        var match = Match();
        Assert.Empty(match.Engine.PhotoTaken("A1")); // never taken unasked

        SayYes(match, "A1");
        Assert.Equal(new PhotoTaken("A1"), Assert.Single(match.Engine.PhotoTaken("A1")));
        Assert.Empty(match.Engine.PhotoTaken("A1"));

        var players = SnapshotBuilder.Build(match.Engine, Viewer.Main).Players;
        Assert.Equal(1, players.Single(p => p.StationId == "A1").Photo);
        Assert.Equal(0, players.Single(p => p.StationId == "A2").Photo);
    }

    [Fact]
    public void The_store_forgets_a_match_s_photos_when_it_ends()
    {
        var match = Match();
        var store = new PhotoStore();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];
        store.Keep("test-match", "A1", new HeroPicture(png, png));
        store.Keep("other-match", "A1", new HeroPicture(png, png));

        store.MatchChanged(match.Engine, match.Engine.Abort(match.Now), []);

        Assert.Null(store.Find("test-match", "A1"));
        Assert.NotNull(store.Find("other-match", "A1"));
        Assert.True(PhotoStore.IsPng(png));
        Assert.False(PhotoStore.IsPng("GIF89a..."u8.ToArray()));
    }
}
