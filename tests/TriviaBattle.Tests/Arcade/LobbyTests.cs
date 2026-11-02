using TriviaBattle.Core.Arcade;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Tests.Arcade;

/// <summary>
/// The arcade lobby: walk up to a station, press any button, pick a hero (A/B flip, ACTION picks), then a
/// game and its category (A/B flip, ACTION picks; the first is everything mixed): solo at once, or a party
/// or race the other stations can join. Joins for 20 s, late joiners get 6 s.
/// </summary>
public class LobbyTests
{
    private const int A = 0, B = 1, C = 2, D = 3, Action = ButtonPress.Action;
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    private static readonly StationLayout Layout = new(
    [
        new Station("A1", "A", 1, "Player 1", "green"),
        new Station("A2", "A", 2, "Player 2", "red"),
        new Station("A3", "A", 3, "Player 3", "blue"),
    ]);

    private DateTimeOffset _now = Start;
    private readonly Lobby _lobby = new(["A1", "A2", "A3"], new LobbySettings(), new Random(1));

    public LobbyTests() => _lobby.SetCategories([new LobbyCategory(7, "Movies"), new LobbyCategory(9, "Music")]);

    private LobbyResult Press(string station, params int[] buttons)
    {
        var result = LobbyResult.Nothing;
        foreach (var button in buttons)
        {
            _now += TimeSpan.FromSeconds(1);
            result = _lobby.Press(station, button, _now);
        }
        return result;
    }

    private LobbySeat Seat(string station) => _lobby.Seat(station)!;

    [Fact]
    public void Any_button_wakes_a_station_with_a_made_up_name_then_A_and_B_flip_the_heroes()
    {
        Press("A1", C);
        Assert.Equal(LobbyStep.Hero, Seat("A1").Step);
        Assert.False(string.IsNullOrEmpty(Seat("A1").Name));
        Assert.True(Seat("A1").Name.Length <= MatchSetupValidator.MaxNameLength);
        Assert.Equal("knight", Seat("A1").Hero);

        Press("A1", B, B);
        Assert.Equal("mage", Seat("A1").Hero);
        Press("A1", A, A, A);
        Assert.Equal("ranger", Seat("A1").Hero); // round the back

        Press("A1", Action);
        Assert.Equal(LobbyStep.Game, Seat("A1").Step);
        Assert.Equal([GameChoice.Solo, GameChoice.Party, GameChoice.Race], _lobby.ChoicesFor(Seat("A1")));
    }

    [Fact]
    public void Solo_starts_straight_away_with_the_hero_picked()
    {
        var result = Press("A1", A, B, Action, A, Action);   // ..., Solo, everything mixed

        var start = Assert.Single(result.Starts);
        Assert.Equal(GameChoice.Solo, start.Game);
        var player = Assert.Single(start.Players);
        Assert.Equal(("A1", "barbarian"), (player.StationId, player.Hero));
        Assert.Equal(LobbyStep.Idle, Seat("A1").Step); // the match takes over the screen

        var setup = start.ToSetup([1], Difficulty.Easy);
        Assert.Equal(GameModeKind.FreeForAll, setup.Mode);
        Assert.Empty(MatchSetupValidator.Validate(setup, Layout));
    }

    [Fact]
    public void A_party_forms_others_join_it_and_it_starts_when_everyone_is_ready()
    {
        Press("A1", A, Action, B, Action);               // A1 starts a party
        Assert.Equal(LobbyStep.Waiting, Seat("A1").Step);
        Assert.NotNull(_lobby.Forming);

        Press("A2", A, Action);                          // A2 sees joining first
        Assert.Equal([GameChoice.Join, GameChoice.Challenge, GameChoice.Solo], _lobby.ChoicesFor(Seat("A2")));
        Press("A2", A);
        Assert.Equal(2, _lobby.Forming!.Members.Count);

        Assert.Empty(Press("A1", Action).Starts);         // one ready isn't enough
        var start = Assert.Single(Press("A2", Action).Starts);

        Assert.Equal(GameChoice.Party, start.Game);
        Assert.Equal(["A1", "A2"], start.Players.Select(p => p.StationId));
        Assert.Null(_lobby.Forming);

        var setup = start.ToSetup([1], Difficulty.Easy);
        Assert.Equal(GameModeKind.TeamCombinedScore, setup.Mode);
        Assert.Equal(["A1", "A2"], Assert.Single(setup.Teams).StationIds);
        Assert.Empty(MatchSetupValidator.Validate(setup, Layout));
    }

    [Fact]
    public void A_race_starts_when_its_timer_runs_out_and_a_late_joiner_gets_a_few_seconds()
    {
        Press("A1", A, Action, C, Action);               // a race, starting 20 s from now
        var startsAt = _lobby.Forming!.StartsAt;

        _now = startsAt - TimeSpan.FromSeconds(3);
        Assert.Empty(_lobby.Tick(_now).Starts);
        _lobby.Press("A2", A, _now);
        _lobby.Press("A2", Action, _now);
        _lobby.Press("A2", A, _now);                     // join with 3 s left: now 6 s
        Assert.Equal(_now + TimeSpan.FromSeconds(6), _lobby.Forming.StartsAt);

        Assert.Empty(_lobby.Tick(startsAt).Starts);
        var start = Assert.Single(_lobby.Tick(_now + TimeSpan.FromSeconds(6)).Starts);
        Assert.Equal(GameChoice.Race, start.Game);
        Assert.Equal(GameModeKind.FreeForAll, start.ToSetup([1], Difficulty.Easy).Mode);
    }

    [Fact]
    public void Challenging_a_party_makes_it_a_battle_one_team_a_side()
    {
        Press("A1", A, Action, B, Action);               // A1 starts a party
        Press("A2", A, Action, B);                       // A2 challenges it
        Press("A3", A, Action, A);                       // A3 joins A1's side

        var start = Assert.Single(_lobby.Tick(_lobby.Forming!.StartsAt).Starts);

        Assert.Equal(GameChoice.Battle, start.Game);
        var setup = start.ToSetup([1], Difficulty.Easy);
        Assert.Equal(GameModeKind.TeamCombinedScore, setup.Mode);
        Assert.Equal([["A1", "A3"], ["A2"]], setup.Teams.Select(t => t.StationIds));
        Assert.Empty(MatchSetupValidator.Validate(setup, Layout));
    }

    [Fact]
    public void A_race_can_only_be_joined_not_challenged()
    {
        Press("A1", A, Action, C, Action);
        Press("A2", A, Action);

        Assert.Equal([GameChoice.Join, GameChoice.Solo], _lobby.ChoicesFor(Seat("A2")));
    }

    [Fact]
    public void A_party_nobody_joins_starts_as_a_solo_game()
    {
        Press("A1", A, Action, B, Action);

        var start = Assert.Single(_lobby.Tick(_lobby.Forming!.StartsAt).Starts);

        Assert.Equal(GameChoice.Solo, start.Game);
    }

    [Fact]
    public void D_goes_back_a_step_and_leaving_the_last_one_out_closes_the_party()
    {
        Press("A1", A, Action, D);
        Assert.Equal(LobbyStep.Hero, Seat("A1").Step);

        Press("A1", Action, B, Action, D);               // in a party, then out again
        Assert.Equal(LobbyStep.Game, Seat("A1").Step);
        Assert.Null(_lobby.Forming);
    }

    [Fact]
    public void A_station_left_choosing_goes_back_to_idle()
    {
        Press("A1", A);

        _lobby.Tick(_now + TimeSpan.FromSeconds(31));

        Assert.Equal(LobbyStep.Idle, Seat("A1").Step);
    }

    [Fact]
    public void A_card_says_who_is_playing()
    {
        _lobby.Swipe("A1", "Brett", 42, _now);
        Assert.Equal((LobbyStep.Hero, "Brett", (int?)42), (Seat("A1").Step, Seat("A1").Name, Seat("A1").PlayerProfileId));

        var start = Assert.Single(Press("A1", Action, A, Action).Starts);
        Assert.Equal(42, start.Players[0].PlayerProfileId);
    }

    [Fact]
    public void The_view_lists_the_seats_in_station_order_and_the_party_forming()
    {
        Press("A2", A, Action, B, B, B, Action);        // a party (B) of Music (everything, Movies, Music)
        Press("A3", A);

        var view = LobbyView.From(_lobby, Layout.InRoom("A").Select(s => s.Id));

        Assert.Equal(["A1", "A2", "A3"], view.Seats.Select(s => s.StationId));
        Assert.Equal([LobbyStep.Idle, LobbyStep.Waiting, LobbyStep.Hero], view.Seats.Select(s => s.Step));
        Assert.Equal(GameChoice.Party, view.Forming!.Kind);
        Assert.Equal(Seat("A2").Name, view.Forming.HostName);
        Assert.Equal("Music", view.Forming.Category);
        Assert.Equal(["Movies", "Music"], view.Categories);
    }

    [Fact]
    public void A_and_B_flip_the_categories_round_from_everything_and_D_goes_back()
    {
        Press("A1", A, Action, A);                       // Solo: now the category
        Assert.Equal(LobbyStep.Category, Seat("A1").Step);
        Assert.Null(_lobby.CategoryOf(Seat("A1")));      // everything mixed comes first

        Press("A1", B);
        Assert.Equal("Movies", _lobby.CategoryOf(Seat("A1"))!.Name);
        Press("A1", A, A);
        Assert.Equal("Music", _lobby.CategoryOf(Seat("A1"))!.Name); // round the back

        Press("A1", D);
        Assert.Equal(LobbyStep.Game, Seat("A1").Step);
    }

    [Fact]
    public void A_chosen_category_is_the_game_s_questions_and_everything_is_every_category()
    {
        var movies = Assert.Single(Press("A1", A, Action, A, B, Action).Starts);
        Assert.Equal([7], movies.ToSetup([7, 9, 11], Difficulty.Easy).CategoryIds);

        var everything = Assert.Single(Press("A2", A, Action, A, Action).Starts);
        Assert.Equal([7, 9, 11], everything.ToSetup([7, 9, 11], Difficulty.Easy).CategoryIds);
    }

    [Fact]
    public void Joiners_skip_the_category_the_party_s_host_chose_it()
    {
        Press("A1", A, Action, B, B, Action);            // a party (B) of Movies
        Press("A2", A, Action, A);                       // join

        var start = Assert.Single(_lobby.Tick(_lobby.Forming!.StartsAt).Starts);
        Assert.Equal(GameChoice.Party, start.Game);
        Assert.Equal("Movies", start.Category!.Name);
    }
}
