using Microsoft.Extensions.Logging;
using TriviaBattle.Core.Output;

namespace TriviaBattle.Hardware.Output;

/// <summary>
/// Dev stand-in for the PLC outputs: logs what the button LEDs and room lights would do.
/// Only logs when something actually changes.
/// </summary>
public sealed class ConsoleRoomOutput(ILogger<ConsoleRoomOutput> logger) : IRoomOutput
{
    private readonly object _lock = new();
    private readonly SortedDictionary<int, LedState> _leds = new();
    private readonly Dictionary<string, LightingCue> _cues = new();

    public void SetButtonLeds(IReadOnlyDictionary<int, LedState> ledsByButtonId)
    {
        lock (_lock)
        {
            var changed = ledsByButtonId.Any(kv => !_leds.TryGetValue(kv.Key, out var current) || current != kv.Value);
            if (!changed) return;

            foreach (var (buttonId, state) in ledsByButtonId)
                _leds[buttonId] = state;

            // e.g. "LEDs: 10:on 11:off 12:off 13:off 14:blink ..."
            logger.LogInformation("LEDs: {Leds}",
                string.Join(' ', _leds.Select(kv => $"{kv.Key}:{kv.Value.ToString().ToLowerInvariant()}")));
        }
    }

    public void SetLightingCue(string roomId, LightingCue cue)
    {
        lock (_lock)
        {
            if (_cues.TryGetValue(roomId, out var current) && current == cue) return;
            _cues[roomId] = cue;
            logger.LogInformation("Room {Room} lighting: {Cue}", roomId, cue);
        }
    }
}
