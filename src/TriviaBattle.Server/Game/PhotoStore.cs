using System.Collections.Concurrent;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Server.Game;

/// <summary>
/// One player's hero photo, as their station made it (both PNGs, with see-through backgrounds).
/// </summary>
/// <param name="Face">Just their face, cut out and laid out the same way as every face (js/face-cutout.js): the station's 3D knight wears it.</param>
/// <param name="Portrait">The knight's head wearing it, for the screens that draw in 2D (the plate, the main screen).</param>
public sealed record HeroPicture(byte[] Face, byte[] Portrait);

/// <summary>
/// Hero photos: a player's face on their hero, taken by their station's camera, in the lobby after choosing
/// a hero (kept by station until their game starts, then moved into it) or in a kiosk game's intro (see
/// <see cref="MatchEngine.PhotoTaken"/>). Kept in memory only, never written to disk or the database, and
/// forgotten as soon as the player drops it or leaves, or their match ends: a face never outlives the game
/// it was taken for. It's a broadcaster only to hear about matches.
/// </summary>
public sealed class PhotoStore : IMatchBroadcaster
{
    /// <summary>The most a station may send for one photo (both pictures; they're usually well under 100 KB each).</summary>
    public const int MaxBytes = 512 * 1024;

    private readonly ConcurrentDictionary<(string MatchId, string StationId), HeroPicture> _photos = new();
    private readonly ConcurrentDictionary<string, HeroPicture> _lobby = new();

    public void Keep(string matchId, string stationId, HeroPicture picture) => _photos[(matchId, stationId)] = picture;

    public HeroPicture? Find(string matchId, string stationId) => _photos.GetValueOrDefault((matchId, stationId));

    public void Forget(string matchId, string stationId) => _photos.TryRemove((matchId, stationId), out _);

    /// <summary>A photo taken in the lobby, by station: whoever is choosing there now.</summary>
    public void KeepLobby(string stationId, HeroPicture picture) => _lobby[stationId] = picture;

    public HeroPicture? FindLobby(string stationId) => _lobby.GetValueOrDefault(stationId);

    public void ForgetLobby(string stationId) => _lobby.TryRemove(stationId, out _);

    /// <summary>Takes a lobby photo out (its game is starting: it moves into the match).</summary>
    public HeroPicture? TakeLobby(string stationId) => _lobby.TryRemove(stationId, out var picture) ? picture : null;

    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        foreach (var declined in events.OfType<PhotoDeclined>())
            Forget(match.MatchId, declined.StationId);

        if (!events.Any(e => e is MatchFinished))
            return;
        foreach (var player in match.Roster.Players)
            Forget(match.MatchId, player.StationId);
    }

    /// <summary>True if these bytes start like a PNG file (stations always send PNG).</summary>
    public static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
}
