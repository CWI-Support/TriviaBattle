namespace TriviaBattle.Core.Arcade;

// What screens receive about a room's lobby (as camelCase JSON, in ScreenState.Lobby). Nothing in it is
// secret, so every screen in the room gets the same view; a station screen finds its own seat by its id.

/// <param name="Heroes">Every hero, in the order the stations flip through them.</param>
/// <param name="Categories">The categories on offer, in the order the stations flip through them (after "everything").</param>
/// <param name="Seats">Every station of the room, in station order (a station in a game shows as Idle here).</param>
/// <param name="Forming">The party or race that's forming, or null.</param>
public sealed record LobbyView(int Version, IReadOnlyList<string> Heroes, IReadOnlyList<string> Categories, IReadOnlyList<LobbySeatView> Seats, LobbyGroupView? Forming)
{
    public static LobbyView From(Lobby lobby, IEnumerable<string> stationOrder) => new(
        lobby.Version,
        Arcade.Heroes.All,
        lobby.Categories.Select(c => c.Name).ToList(),
        stationOrder.Select(lobby.Seat).OfType<LobbySeat>()
            .Select(s => new LobbySeatView(
                s.StationId, s.Step, s.Name, s.Hero, s.Ready,
                s.Step == LobbyStep.Game ? lobby.ChoicesFor(s) : [],
                s.Step == LobbyStep.Category ? s.PendingGame : null,
                s.Step == LobbyStep.Category ? s.CategoryIndex : 0))
            .ToList(),
        lobby.Forming == null ? null : new LobbyGroupView(
            lobby.Forming.Id,
            lobby.Forming.Kind,
            lobby.Forming.Host.Name,
            lobby.Forming.Category?.Name,
            lobby.Forming.IsBattle,
            lobby.Forming.Members.Select(m => new LobbyMemberView(m.StationId, m.Name, m.Hero, m.Ready, m.Side)).ToList(),
            lobby.Forming.StartsAt.ToUnixTimeMilliseconds()));
}

/// <param name="Choices">On the Game step: what A, B, C... do, in order (D goes back to the heroes).</param>
/// <param name="ForGame">On the Category step: the game the category is for (Solo, Party or Race).</param>
/// <param name="CategoryIndex">On the Category step: 0 for everything mixed, otherwise 1 + its place in <see cref="LobbyView.Categories"/>.</param>
public sealed record LobbySeatView(string StationId, LobbyStep Step, string Name, string Hero, bool Ready, IReadOnlyList<GameChoice> Choices, GameChoice? ForGame, int CategoryIndex);

/// <param name="Kind">Party or Race.</param>
/// <param name="Category">The questions' category its host chose; null for everything mixed.</param>
/// <param name="IsBattle">A party someone challenged: team against team.</param>
/// <param name="StartsAtMs">When it starts if not everyone is ready before (server clock).</param>
public sealed record LobbyGroupView(string Id, GameChoice Kind, string HostName, string? Category, bool IsBattle, IReadOnlyList<LobbyMemberView> Members, long StartsAtMs);

/// <param name="Side">In a party: 0 on the host's side, 1 with the challengers.</param>
public sealed record LobbyMemberView(string StationId, string Name, string Hero, bool Ready, int Side);
