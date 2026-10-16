using System.Collections.Concurrent;

namespace TriviaBattle.Server.Diagnostics;

/// <summary>
/// Which screens are connected right now, for the admin "Live log" tab.
/// GameHub adds a screen when it joins and removes it when its connection drops.
/// </summary>
public sealed class ScreenRegistry
{
    private readonly ConcurrentDictionary<string, ConnectedScreen> _byConnection = new();

    public void Joined(string connectionId, ConnectedScreen screen) => _byConnection[connectionId] = screen;

    /// <summary>Removes the connection and returns what it was (null if it never joined).</summary>
    public ConnectedScreen? Left(string connectionId) =>
        _byConnection.TryRemove(connectionId, out var screen) ? screen : null;

    public ConnectedScreen? Find(string connectionId) => _byConnection.GetValueOrDefault(connectionId);

    public IReadOnlyList<ConnectedScreen> All => _byConnection.Values.ToList();
}

/// <param name="Key">Which screen it is: "main:A", "kiosk:A" or "player:A2".</param>
/// <param name="Name">For people: "Main screen", "Kiosk", "Player 2".</param>
/// <param name="Address">The screen PC's IP address.</param>
public sealed record ConnectedScreen(string Key, string Name, string? Address, DateTimeOffset ConnectedAt);

/// <summary>
/// Changes every time the server starts. Screens remember it, and reload themselves when they reconnect
/// to a server with a different one, so a long-running screen never keeps running old page code after an update.
/// </summary>
public static class ServerInstance
{
    public static string Id { get; } = Guid.NewGuid().ToString("N")[..8];
}
