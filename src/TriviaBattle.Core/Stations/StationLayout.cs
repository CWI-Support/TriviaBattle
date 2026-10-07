namespace TriviaBattle.Core.Stations;

/// <summary>
/// Every room and station the server knows about, loaded from config/stations.json.
/// Nothing in the game assumes a particular number of stations; it always asks this class.
/// </summary>
public sealed class StationLayout
{
    private readonly Dictionary<string, Station> _stationsById;
    private readonly Dictionary<string, string> _roomNames;

    /// <param name="stations">All stations in all rooms.</param>
    /// <param name="roomNames">Display name per room id (optional; defaults to the id).</param>
    public StationLayout(IEnumerable<Station> stations, IReadOnlyDictionary<string, string>? roomNames = null)
    {
        Stations = stations.OrderBy(s => s.RoomId).ThenBy(s => s.Number).ToList();

        var duplicate = Stations.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Station id '{duplicate.Key}' is defined more than once in stations.json.");

        _stationsById = Stations.ToDictionary(s => s.Id);
        _roomNames = roomNames?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();
    }

    public IReadOnlyList<Station> Stations { get; }

    public IEnumerable<string> RoomIds => Stations.Select(s => s.RoomId).Distinct();

    public string RoomName(string roomId) => _roomNames.GetValueOrDefault(roomId, roomId);

    public Station? Find(string stationId) => _stationsById.GetValueOrDefault(stationId);

    public Station? Find(string roomId, int number) =>
        Stations.FirstOrDefault(s => s.RoomId == roomId && s.Number == number);

    public IReadOnlyList<Station> InRoom(string roomId) => Stations.Where(s => s.RoomId == roomId).ToList();
}
