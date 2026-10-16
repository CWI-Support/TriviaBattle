using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace TriviaBattle.Server.Diagnostics;

/// <summary>
/// Keeps the last few hundred log lines in memory for the admin "Live log" tab (GET /api/admin/live-log).
/// It's a Serilog sink, so everything that goes to the log file shows up here too, including errors the
/// screens report (see GameHub.ReportError).
/// </summary>
public sealed class LiveLog : ILogEventSink
{
    public const int Capacity = 500;

    // {Message:lj} = values without the quote marks Serilog puts around strings by default.
    private static readonly MessageTemplateTextFormatter MessageOnly = new("{Message:lj}");

    private readonly Queue<LiveLogEntry> _entries = new();
    private readonly object _gate = new();
    private long _lastId;

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _entries.Enqueue(new LiveLogEntry(
                ++_lastId,
                logEvent.Timestamp,
                logEvent.Level.ToString(),
                Source(logEvent),
                Render(logEvent),
                logEvent.Exception?.ToString()));

            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }

    /// <summary>The id of the newest line (0 = nothing logged yet). Starts again from 0 when the server restarts.</summary>
    public long LastId
    {
        get { lock (_gate) return _lastId; }
    }

    /// <summary>Lines newer than <paramref name="afterId"/>, oldest first (0 = every line still kept).</summary>
    public IReadOnlyList<LiveLogEntry> After(long afterId)
    {
        lock (_gate) return _entries.Where(e => e.Id > afterId).ToList();
    }

    private static string Render(LogEvent logEvent)
    {
        var text = new StringWriter();
        MessageOnly.Format(logEvent, text);
        return text.ToString();
    }

    // "TriviaBattle.Server.Game.MatchHost" -> "MatchHost"
    private static string Source(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string name }
            ? name[(name.LastIndexOf('.') + 1)..]
            : "";
}

public sealed record LiveLogEntry(long Id, DateTimeOffset Time, string Level, string Source, string Message, string? Exception);
