using Microsoft.Extensions.Options;
using TriviaBattle.Core.Arcade;
using TriviaBattle.Core.Stations;

namespace TriviaBattle.Server.Game;

/// <summary>
/// The arcade lobby of every room (<see cref="Lobby"/>), behind one lock. <see cref="LobbyHost"/> drives them
/// (presses, card swipes, the clock); the screens' state reads them (<see cref="Hubs.ScreenStateFactory"/>).
/// It depends on nothing else, so both can use it.
/// </summary>
public sealed class Lobbies
{
    private readonly object _lock = new();
    private readonly StationLayout _stations;
    private readonly Dictionary<string, Lobby> _byRoom;

    public Lobbies(StationLayout stations, IOptions<LobbySettings> settings)
    {
        _stations = stations;
        _byRoom = stations.RoomIds.ToDictionary(
            roomId => roomId,
            roomId => new Lobby(stations.InRoom(roomId).Select(s => s.Id), settings.Value, new Random()));
    }

    public IEnumerable<string> RoomIds => _byRoom.Keys;

    /// <summary>Runs <paramref name="work"/> on a room's lobby, under the lock (keep it quick).</summary>
    public T Use<T>(string roomId, Func<Lobby, T> work)
    {
        lock (_lock)
            return work(_byRoom[roomId]);
    }

    /// <summary>The room's lobby as its screens see it.</summary>
    public LobbyView View(string roomId) =>
        Use(roomId, lobby => LobbyView.From(lobby, _stations.InRoom(roomId).Select(s => s.Id)));
}
