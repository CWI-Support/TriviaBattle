namespace TriviaBattle.Hardware.Plc;

/// <summary>PLC connection health, shown on the admin page (GET /api/hardware/status).</summary>
public sealed class PlcStatus
{
    private readonly object _lock = new();

    public bool IsConnected { get; private set; }
    public DateTimeOffset? ConnectedSince { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastErrorAt { get; private set; }
    public long PressesReceived { get; private set; }
    public DateTimeOffset? LastPressAt { get; private set; }

    public void Connected(DateTimeOffset now)
    {
        lock (_lock)
        {
            IsConnected = true;
            ConnectedSince = now;
        }
    }

    public void Failed(string error, DateTimeOffset now)
    {
        lock (_lock)
        {
            IsConnected = false;
            ConnectedSince = null;
            LastError = error;
            LastErrorAt = now;
        }
    }

    public void PressReceived(DateTimeOffset now)
    {
        lock (_lock)
        {
            PressesReceived++;
            LastPressAt = now;
        }
    }
}
