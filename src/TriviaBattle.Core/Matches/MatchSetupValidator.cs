using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Checks a <see cref="MatchSetup"/> before a match is created. Returns plain-English
/// problems the kiosk can show; an empty list means the setup is good.
/// </summary>
public static class MatchSetupValidator
{
    public const int MaxNameLength = 16;

    public static List<string> Validate(MatchSetup setup, StationLayout layout)
    {
        var problems = new List<string>();

        if (setup.Seats.Count == 0)
            problems.Add("At least one player is needed.");

        foreach (var seat in setup.Seats)
        {
            if (layout.Find(seat.StationId) == null)
                problems.Add($"Unknown station '{seat.StationId}'.");
            if (string.IsNullOrWhiteSpace(seat.PlayerName))
                problems.Add($"Station {seat.StationId} needs a player name.");
            else if (seat.PlayerName.Trim().Length > MaxNameLength)
                problems.Add($"'{seat.PlayerName}' is longer than {MaxNameLength} characters.");
        }

        var seatedStations = setup.Seats.Select(s => s.StationId).ToList();
        if (seatedStations.Distinct().Count() != seatedStations.Count)
            problems.Add("The same station was used for two players.");

        if (GameModes.UsesTeams(setup.Mode))
            ValidateTeams(setup, seatedStations, problems);
        else if (setup.Teams.Count > 0)
            problems.Add("Free-for-all matches don't have teams.");

        return problems;
    }

    private static void ValidateTeams(MatchSetup setup, List<string> seatedStations, List<string> problems)
    {
        if (setup.Teams.Count < 2)
        {
            problems.Add("Team modes need at least two teams.");
            return;
        }

        if (setup.Teams.Select(t => t.TeamId).Distinct().Count() != setup.Teams.Count)
            problems.Add("Two teams have the same id.");

        foreach (var team in setup.Teams)
        {
            if (team.StationIds.Count == 0)
                problems.Add($"Team '{team.Name}' has no players.");
        }

        // Every seated station must be on exactly one team.
        var teamStations = setup.Teams.SelectMany(t => t.StationIds).ToList();
        foreach (var station in seatedStations)
        {
            var count = teamStations.Count(s => s == station);
            if (count == 0) problems.Add($"Station {station} isn't on a team.");
            if (count > 1) problems.Add($"Station {station} is on more than one team.");
        }

        foreach (var station in teamStations.Distinct().Except(seatedStations))
            problems.Add($"Team includes station {station}, but nobody is seated there.");
    }
}
