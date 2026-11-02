using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Everything chosen before a match starts: at a player station (the arcade lobby, <see cref="Arcade.Lobby"/>)
/// or at the kiosk.
/// A match is just a set of stations (seats). It doesn't belong to a "room";
/// the rooms involved are whatever rooms the seated stations are in.
/// </summary>
/// <param name="CategoryIds">One or more categories; their questions are mixed together.</param>
public sealed record MatchSetup(
    GameModeKind Mode,
    IReadOnlyList<int> CategoryIds,
    Difficulty Difficulty,
    IReadOnlyList<SeatSetup> Seats,
    IReadOnlyList<TeamSetup> Teams);

/// <summary>A player sitting at a station.</summary>
/// <param name="PlayerProfileId">Database id of the RFID-registered player, or null for a guest.</param>
/// <param name="Hero">The hero they picked (<see cref="Arcade.Heroes"/>), or null for the default.</param>
public sealed record SeatSetup(string StationId, string PlayerName, int? PlayerProfileId = null, string? Hero = null);

/// <summary>A team and the stations on it, never hardcoded. One team on its own is a co-op party (everyone against the dungeon).</summary>
public sealed record TeamSetup(string TeamId, string Name, IReadOnlyList<string> StationIds);
