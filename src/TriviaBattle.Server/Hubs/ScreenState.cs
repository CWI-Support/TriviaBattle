using TriviaBattle.Core.Arcade;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Server.Hubs;

// Messages the server pushes to screens over SignalR (as camelCase JSON).
//
//   "State"  -> ScreenState  : everything a screen needs to draw itself. Sent on join and on every change.
//   "Event"  -> ScreenEvent  : a one-off moment to animate (answer locked, reveal...). Optional to handle.
//   "Theme"  -> string       : the admin switched themes; reload the look and text.

public sealed record StationInfo(string Id, int Number, string Label, string Color)
{
    public static StationInfo From(Station s) => new(s.Id, s.Number, s.Label, s.Color);
}

public sealed record RoomInfo(string Id, string Name, IReadOnlyList<StationInfo> Stations);

/// <param name="Room">The room this screen is in, with its stations (labels, colors).</param>
/// <param name="Station">For player screens: which station this is. Null for other screens.</param>
/// <param name="Match">
/// Player screens: the match at this station, or null. Room screens (main, kiosk): the first of
/// <paramref name="Matches"/>, kept for simple screens that only show one game.
/// </param>
/// <param name="Matches">
/// Room screens: every match running in the room, in station order (several groups can play at
/// once). Player screens: just their own match, or empty. Empty = nobody is playing.
/// </param>
/// <param name="Cue">Media to play in sync (e.g. the intro video), or null.</param>
/// <param name="Theme">The active theme folder name (wwwroot/themes/...). Also pushed on its own as "Theme" when it changes.</param>
/// <param name="Lobby">The room's arcade lobby: where every station not in a game is (picking a hero, a game), and any party forming.</param>
public sealed record ScreenState(RoomInfo Room, StationInfo? Station, MatchSnapshot? Match, IReadOnlyList<MatchSnapshot> Matches, MediaCue? Cue, string Theme, LobbyView Lobby);

/// <summary>
/// "Play this media at exactly this server time." Every screen converts StartAtMs to its own
/// clock (see wwwroot/js/synced-video.js), so playback lines up across screens and machines.
/// </summary>
/// <param name="Id">Stays the same for the same moment, so a screen doesn't restart a video it's already playing.</param>
public sealed record MediaCue(string Id, string Url, long StartAtMs);

/// <summary>
/// A one-off moment for animations. Deliberately carries no secrets: for example
/// AnswerGiven says WHO answered, never WHICH answer (that's in the State, if the screen may see it).
/// </summary>
/// <param name="Type">"PhaseChanged", "AnswerGiven", "LockoutEnded", "MatchFinished" or "LeaderboardPlacement".</param>
/// <param name="MatchId">Which match it happened in (a room can have several at once).</param>
/// <param name="Placement">Only for "LeaderboardPlacement": who made which board, at what rank.</param>
public sealed record ScreenEvent(
    string Type,
    string MatchId,
    string? Phase = null,
    string? OwnerId = null,
    string? StationId = null,
    LeaderboardPlacement? Placement = null);
