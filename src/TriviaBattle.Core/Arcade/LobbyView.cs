namespace TriviaBattle.Core.Arcade;

// What screens receive about a room's lobby (as camelCase JSON, in ScreenState.Lobby). Nothing in it is
// secret, so every screen in the room gets the same view; a station screen finds its own seat by its id.

/// <param name="Heroes">Every hero, in the order the stations flip through them.</param>
/// <param name="Seats">Every station of the room, in station order (a station in a game shows as Idle here).</param>
/// <param name="Forming">The party or race that's forming, or null.</param>
public sealed record LobbyView(int Version, IReadOnlyList<string> Heroes, IReadOnlyList<LobbySeatView> Seats, LobbyGroupView? Forming)
{
    public static LobbyView From(Lobby lobby, IEnumerable<string> stationOrder) => new(
        lobby.Version,
        Arcade.Heroes.All,
        stationOrder.Select(lobby.Seat).OfType<LobbySeat>()
            .Select(s => new LobbySeatView(s.StationId, s.Step, s.Name, s.Hero, s.Ready, s.Step == LobbyStep.Game ? lobby.ChoicesFor(s) : []))
            .ToList(),
        lobby.Forming == null ? null : new LobbyGroupView(
            lobby.Forming.Id,
            lobby.Forming.Kind,
            lobby.Forming.Host.Name,
            lobby.Forming.Members.Select(m => new LobbyMemberView(m.StationId, m.Name, m.Hero, m.Ready)).ToList(),
            lobby.Forming.StartsAt.ToUnixTimeMilliseconds()));
}

/// <param name="Choices">On the Game step: what A, B, C... do, in order (D goes back to the heroes).</param>
public sealed record LobbySeatView(string StationId, LobbyStep Step, string Name, string Hero, bool Ready, IReadOnlyList<GameChoice> Choices);

/// <param name="Kind">Party or Race.</param>
/// <param name="StartsAtMs">When it starts if not everyone is ready before (server clock).</param>
public sealed record LobbyGroupView(string Id, GameChoice Kind, string HostName, IReadOnlyList<LobbyMemberView> Members, long StartsAtMs);

public sealed record LobbyMemberView(string StationId, string Name, string Hero, bool Ready);
