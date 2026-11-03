using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Dungeon;

/// <summary>
/// Hero photos. By default everyone's station takes one in the intro (a second to see yourself, then a 3-second
/// countdown) unless they press the action button to say no; a venue can ask first instead, when the action
/// button says yes. The server keeps the picture until the match ends. The intro is 7 seconds.
/// </summary>
public class HeroPhotoTests
{
    private static TestMatch Match(bool byDefault = true, Action<MatchSettings>? configure = null)
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon };
        settings.Dungeon.PhotoByDefault = byDefault;
        configure?.Invoke(settings);
        return new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1", "A2"]);
    }

    private static IReadOnlyList<MatchEvent> PressAction(TestMatch match, string stationId, double afterSeconds = 0.5) =>
        match.Press(stationId, ButtonPress.Action, afterSeconds);

    private static YouView You(TestMatch match, string stationId) =>
        SnapshotBuilder.Build(match.Engine, Viewer.Player(stationId)).You!;

    private static Player PlayerAt(TestMatch match, string stationId) => match.Engine.Roster.FindPlayer(stationId)!;

    [Fact]
    public void By_default_everyone_s_photo_is_coming_from_the_start_of_the_intro()
    {
        var match = Match();

        var takeAt = TestMatch.Start + TimeSpan.FromSeconds(1 + 3);
        Assert.Equal(takeAt, PlayerAt(match, "A1").PhotoAt);
        Assert.Equal(takeAt.ToUnixTimeMilliseconds(), You(match, "A2").PhotoAtMs);
        Assert.Null(You(match, "A1").PhotoOfferUntilMs); // nothing to ask
    }

    [Fact]
    public void The_action_button_in_the_intro_says_no_and_stays_no()
    {
        var match = Match();

        Assert.Equal(new PhotoDeclined("A1"), Assert.Single(PressAction(match, "A1")));
        Assert.Null(You(match, "A1").PhotoAtMs);
        Assert.True(PlayerAt(match, "A1").PhotoDeclined);
        Assert.Empty(match.Engine.PhotoTaken("A1"));  // a photo sent anyway is refused
        Assert.Empty(PressAction(match, "A1"));        // pressing again doesn't bring it back
        Assert.NotNull(You(match, "A2").PhotoAtMs);    // the other player's is still coming
    }

    [Fact]
    public void Saying_no_after_the_photo_arrived_takes_it_off()
    {
        var match = Match();
        match.Wait(4.5);
        match.Engine.PhotoTaken("A1");
        Assert.Equal(1, PlayerAt(match, "A1").Photo);

        Assert.Single(PressAction(match, "A1"));

        Assert.Equal(0, PlayerAt(match, "A1").Photo); // screens go back to the knight's own face
    }

    [Fact]
    public void The_countdown_is_heard_only_once_the_camera_is_on_and_stops_on_no()
    {
        var match = Match();
        IReadOnlyList<PhotoCountdown> Wait(double seconds) => match.Wait(seconds).OfType<PhotoCountdown>().ToList();

        // The countdown's seconds fall at 1, 2 and 3 s into the intro, the shutter at 4 s.
        Assert.Empty(Wait(1.5));                        // no camera reported at A1 yet: quiet (its 3 is missed)
        Assert.True(match.Engine.PhotoCameraOn("A1"));
        Assert.Equal([new PhotoCountdown("A1", 2)], Wait(1));
        Assert.Equal([new PhotoCountdown("A1", 1), new PhotoCountdown("A1", 0)], Wait(1.5));

        var other = Match();
        other.Engine.PhotoCameraOn("A2");
        other.Press("A2", ButtonPress.Action, 0);       // said no before anything played
        Assert.Empty(other.Wait(5).OfType<PhotoCountdown>());
    }

    [Fact]
    public void Asked_first_the_action_button_says_yes_while_the_countdown_still_fits()
    {
        var match = Match(byDefault: false);
        var offerEnds = match.Engine.PhaseEndsAt - TimeSpan.FromSeconds(3);
        Assert.Null(You(match, "A1").PhotoAtMs);
        Assert.Equal(offerEnds.ToUnixTimeMilliseconds(), You(match, "A1").PhotoOfferUntilMs);

        Assert.Equal(new PhotoAsked("A1"), Assert.Single(PressAction(match, "A1", 1)));
        Assert.Equal(match.Now + TimeSpan.FromSeconds(3), PlayerAt(match, "A1").PhotoAt);
        Assert.Null(You(match, "A1").PhotoOfferUntilMs);

        Assert.Empty(PressAction(match, "A2", 4.5));     // 5.5 s in: the countdown wouldn't fit any more
        Assert.Single(PressAction(match, "A1", 0));      // pressed again: no after all
        Assert.Null(PlayerAt(match, "A1").PhotoAt);
    }

    [Fact]
    public void There_are_no_photos_in_Classic_or_with_photos_switched_off()
    {
        var classic = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Classic }, stations: ["A1"]);
        var off = Match(configure: s => s.Dungeon.HeroPhotos = false);

        foreach (var match in new[] { classic, off })
        {
            Assert.Null(You(match, "A1").PhotoAtMs);
            Assert.Null(You(match, "A1").PhotoOfferUntilMs);
            Assert.Empty(PressAction(match, "A1"));
        }
    }

    [Fact]
    public void Once_playing_the_action_button_is_for_power_ups_again()
    {
        var match = Match();
        match.StartPlaying();

        Assert.DoesNotContain(PressAction(match, "A1"), e => e is PhotoDeclined);
        Assert.NotNull(You(match, "A1").PhotoAtMs);
    }

    [Fact]
    public void A_photo_only_counts_while_one_is_coming_and_only_once()
    {
        var match = Match(byDefault: false);
        Assert.Empty(match.Engine.PhotoTaken("A1")); // never taken unasked
        Assert.False(match.Engine.PhotoCameraOn("A1"));

        PressAction(match, "A1");
        Assert.Equal(new PhotoTaken("A1"), Assert.Single(match.Engine.PhotoTaken("A1")));
        Assert.Empty(match.Engine.PhotoTaken("A1"));

        var players = SnapshotBuilder.Build(match.Engine, Viewer.Main).Players;
        Assert.Equal(1, players.Single(p => p.StationId == "A1").Photo);
        Assert.Equal(0, players.Single(p => p.StationId == "A2").Photo);
    }

    [Fact]
    public void A_photo_settled_in_the_lobby_stays_settled_in_the_intro()
    {
        var settings = new MatchSettings { Format = GameFormat.Dungeon };
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1", "A2", "A3"],
            lobbyPhotos: new Dictionary<string, bool> { ["A1"] = true, ["A2"] = false });

        Assert.Equal((1, (DateTimeOffset?)null), (PlayerAt(match, "A1").Photo, PlayerAt(match, "A1").PhotoAt)); // brought one
        Assert.True(PlayerAt(match, "A2").PhotoDeclined);                                                      // chose none
        Assert.NotNull(PlayerAt(match, "A3").PhotoAt);                                                         // a kiosk player's is still coming

        Assert.Empty(PressAction(match, "A1"));        // a "ready" pressed twice doesn't take it off
        Assert.Equal(1, PlayerAt(match, "A1").Photo);
        Assert.Empty(match.Engine.PhotoTaken("A2"));   // and none is taken for someone who said no
    }

    [Fact]
    public void A_lobby_photo_counts_only_in_a_game_with_photos_and_isn_t_offered_again()
    {
        var classic = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Classic }, stations: ["A1"],
            lobbyPhotos: new Dictionary<string, bool> { ["A1"] = true });
        Assert.Equal(0, PlayerAt(classic, "A1").Photo); // no faces in Classic, wherever they came from

        var askFirst = Match(byDefault: false);
        var brought = new TestMatch(GameModeKind.FreeForAll, askFirst.Settings, stations: ["A1", "A2"],
            lobbyPhotos: new Dictionary<string, bool> { ["A1"] = true });
        Assert.Null(You(brought, "A1").PhotoOfferUntilMs);    // they have one: nothing to ask
        Assert.NotNull(You(brought, "A2").PhotoOfferUntilMs); // a kiosk player is still asked
    }

    [Fact]
    public void The_lobby_keeps_a_photo_by_station_until_its_game_takes_it()
    {
        var store = new PhotoStore();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];
        store.KeepLobby("A1", new HeroPicture(png, png));

        Assert.NotNull(store.FindLobby("A1"));
        Assert.NotNull(store.TakeLobby("A1"));
        Assert.Null(store.FindLobby("A1"));            // moved into the match: the lobby has it no more
        Assert.Null(store.TakeLobby("A1"));
    }

    [Fact]
    public void The_store_forgets_a_photo_when_its_player_says_no_and_all_of_a_match_s_when_it_ends()
    {
        var match = Match();
        var store = new PhotoStore();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];
        store.Keep("test-match", "A1", new HeroPicture(png, png));
        store.Keep("test-match", "A2", new HeroPicture(png, png));
        store.Keep("other-match", "A1", new HeroPicture(png, png));

        store.MatchChanged(match.Engine, PressAction(match, "A1"), []);
        Assert.Null(store.Find("test-match", "A1"));
        Assert.NotNull(store.Find("test-match", "A2"));

        store.MatchChanged(match.Engine, match.Engine.Abort(match.Now), []);
        Assert.Null(store.Find("test-match", "A2"));
        Assert.NotNull(store.Find("other-match", "A1"));
        Assert.True(PhotoStore.IsPng(png));
        Assert.False(PhotoStore.IsPng("GIF89a..."u8.ToArray()));
    }
}
