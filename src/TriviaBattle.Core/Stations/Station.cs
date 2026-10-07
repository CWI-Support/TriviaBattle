namespace TriviaBattle.Core.Stations;

/// <summary>
/// One player position in a room: a player screen plus four answer buttons.
/// Stations are defined in config/stations.json. Their Ids (e.g. "A1") are unique
/// across all rooms, so a future room-vs-room match can mix stations from two rooms.
/// </summary>
/// <param name="Id">Globally unique station id, e.g. "A1".</param>
/// <param name="RoomId">The room the station physically sits in, e.g. "A".</param>
/// <param name="Number">Position within the room, 1-based. Used in URLs: /display/player/2.</param>
/// <param name="Label">What the room signage calls it, e.g. "Player 1".</param>
/// <param name="Color">The station's accent color (matches its LED strip), as a CSS color.</param>
public sealed record Station(string Id, string RoomId, int Number, string Label, string Color);
