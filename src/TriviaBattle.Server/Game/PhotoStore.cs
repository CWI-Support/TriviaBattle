using System.Collections.Concurrent;
using TriviaBattle.Core.Matches;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Hero photos: a player's face on their hero, taken by their station's camera after they said yes
/// (see <see cref="MatchEngine.PhotoTaken"/>). Kept in memory only, never written to disk or the
/// database, and forgotten as soon as the match ends: a face never outlives the game it was taken for.
/// It's a broadcaster only to hear about matches ending.
/// </summary>
public sealed class PhotoStore : IMatchBroadcaster
{
    /// <summary>The biggest picture accepted. A station sends a small pixel portrait (a few KB).</summary>
    public const int MaxBytes = 64 * 1024;

    private readonly ConcurrentDictionary<(string MatchId, string StationId), byte[]> _photos = new();

    public void Keep(string matchId, string stationId, byte[] png) => _photos[(matchId, stationId)] = png;

    public byte[]? Find(string matchId, string stationId) => _photos.GetValueOrDefault((matchId, stationId));

    public void Forget(string matchId, string stationId) => _photos.TryRemove((matchId, stationId), out _);

    public void MatchChanged(MatchEngine match, IReadOnlyList<MatchEvent> events, IReadOnlyList<MatchEngine> runningMatches)
    {
        if (!events.Any(e => e is MatchFinished))
            return;
        foreach (var player in match.Roster.Players)
            Forget(match.MatchId, player.StationId);
    }

    /// <summary>True if these bytes start like a PNG file (stations always send PNG).</summary>
    public static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
}
