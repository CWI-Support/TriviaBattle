using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Matches;

/// <summary>
/// Everything chosen at the kiosk before a match starts.
/// A match is just a set of stations (seats). It doesn't belong to a "room";
/// the rooms involved are whatever rooms the seated stations are in.
/// </summary>
public sealed record MatchSetup(
    GameModeKind Mode,
    int CategoryId,
    Difficulty Difficulty,
    IReadOnlyList<SeatSetup> Seats,
    IReadOnlyList<TeamSetup> Teams);

/// <summary>A player sitting at a station.</summary>
/// <param name="PlayerProfileId">Database id of the RFID-registered player, or null for a guest.</param>
public sealed record SeatSetup(string StationId, string PlayerName, int? PlayerProfileId = null);

/// <summary>A team and the stations on it. Teams are chosen at the kiosk, never hardcoded.</summary>
public sealed record TeamSetup(string TeamId, string Name, IReadOnlyList<string> StationIds);
