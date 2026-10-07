namespace TriviaBattle.Server.Hubs;

/// <summary>SignalR group names. Each screen joins exactly one group, based on its URL.</summary>
public static class ScreenGroups
{
    public static string MainScreen(string roomId) => $"room:{roomId}:main";
    public static string Kiosk(string roomId) => $"room:{roomId}:kiosk";
    public static string PlayerScreen(string stationId) => $"station:{stationId}";
}
