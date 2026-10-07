using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Tests.Matches;

public class MatchSetupValidatorTests
{
    private static readonly StationLayout Layout = new(
    [
        new Station("A1", "A", 1, "Player 1", "green"),
        new Station("A2", "A", 2, "Player 2", "red"),
        new Station("A3", "A", 3, "Player 3", "blue"),
        new Station("A4", "A", 4, "Player 4", "purple"),
    ]);

    private static MatchSetup Setup(GameModeKind mode, List<SeatSetup> seats, List<TeamSetup>? teams = null) =>
        new(mode, 1, Difficulty.Easy, seats, teams ?? []);

    private static List<SeatSetup> FourSeats() =>
        [new("A1", "Ann"), new("A2", "Bob"), new("A3", "Cat"), new("A4", "Dan")];

    [Fact]
    public void Valid_free_for_all_has_no_problems()
    {
        Assert.Empty(MatchSetupValidator.Validate(Setup(GameModeKind.FreeForAll, FourSeats()), Layout));
    }

    [Fact]
    public void Teams_can_be_any_stations_not_just_1_2_vs_3_4()
    {
        var teams = new List<TeamSetup> { new("t1", "Red", ["A1", "A4"]), new("t2", "Blue", ["A2", "A3"]) };

        Assert.Empty(MatchSetupValidator.Validate(Setup(GameModeKind.TeamSharedAnswer, FourSeats(), teams), Layout));
    }

    [Fact]
    public void Team_mode_without_teams_is_rejected()
    {
        var problems = MatchSetupValidator.Validate(Setup(GameModeKind.TeamCombinedScore, FourSeats()), Layout);

        Assert.Contains(problems, p => p.Contains("two teams"));
    }

    [Fact]
    public void Station_missing_from_teams_is_rejected()
    {
        var teams = new List<TeamSetup> { new("t1", "Red", ["A1", "A2"]), new("t2", "Blue", ["A3"]) };

        var problems = MatchSetupValidator.Validate(Setup(GameModeKind.TeamSharedAnswer, FourSeats(), teams), Layout);

        Assert.Contains(problems, p => p.Contains("A4"));
    }

    [Fact]
    public void Unknown_station_and_blank_name_are_rejected()
    {
        var problems = MatchSetupValidator.Validate(
            Setup(GameModeKind.FreeForAll, [new("Z9", "Ann"), new("A2", " ")]), Layout);

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Same_station_twice_is_rejected()
    {
        var problems = MatchSetupValidator.Validate(
            Setup(GameModeKind.FreeForAll, [new("A1", "Ann"), new("A1", "Bob")]), Layout);

        Assert.Single(problems);
    }
}
