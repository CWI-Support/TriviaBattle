using Microsoft.Extensions.Logging;
using TriviaBattle.Core.Input;

namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// The press-reading half of the PLC protocol (docs/PLC-PROTOCOL.md):
/// <list type="number">
///   <item>Read the event counter and the PLC clock (one read, so they're from the same PLC scan).</item>
///   <item>If the counter moved, read the ring buffer and pick out the new slots, oldest first.</item>
///   <item>Turn each slot's button code into a button id and its timestamp into server time.</item>
///   <item>Write the counter back to the Ack register so the PLC can reuse those slots.</item>
/// </list>
/// Press order is the order the PLC latched them in, so fairness doesn't depend on how fast we poll.
/// </summary>
public sealed class PlcEventReader(PlcSettings settings, TimeProvider clock, ILogger<PlcEventReader> logger)
{
    private readonly Dictionary<int, int> _buttonIdByCode = settings.ButtonCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private ushort? _lastCounter; // null = haven't read since (re)connecting
    private long _sequence;       // our own never-wrapping press order

    /// <summary>Call after every (re)connect. Presses latched while we were disconnected are skipped.</summary>
    public void Reset() => _lastCounter = null;

    public IReadOnlyList<ButtonEvent> Poll(IPlcRegisters plc)
    {
        var map = settings.Registers;
        var header = plc.Read(map.EventCounter, 2);
        var now = clock.GetUtcNow();
        var counter = header[0];
        var plcClock = header[1];

        if (_lastCounter == null)
        {
            // First look after connecting: start from here, and tell the PLC everything so far is handled.
            _lastCounter = counter;
            plc.Write(map.Ack, counter);
            return [];
        }

        var newPresses = (ushort)(counter - _lastCounter.Value); // ushort maths handles 65535 -> 0
        if (newPresses == 0)
            return [];

        if (newPresses > map.EventSlots)
        {
            // Shouldn't happen if the PLC follows the protocol (it must not overwrite un-acked slots).
            logger.LogWarning("PLC reported {Count} new presses but the buffer only holds {Slots}; the oldest were lost", newPresses, map.EventSlots);
            newPresses = (ushort)map.EventSlots;
        }

        var ring = plc.Read(map.EventBufferStart, map.EventSlots * 2);
        var events = new List<ButtonEvent>();

        for (var i = newPresses - 1; i >= 0; i--) // oldest first
        {
            var pressNumber = (ushort)(counter - i);
            var slot = (pressNumber - 1 + 65536) % map.EventSlots;
            var code = ring[slot * 2];
            var timestamp = ring[slot * 2 + 1];

            if (!_buttonIdByCode.TryGetValue(code, out var buttonId))
            {
                logger.LogWarning("PLC sent unknown button code {Code}; add it to Plc:ButtonCodes", code);
                continue;
            }

            // How long ago the press happened, by the PLC's clock (ushort maths handles wrapping).
            var ticksAgo = (ushort)(plcClock - timestamp);
            var pressedAt = now - TimeSpan.FromMilliseconds(ticksAgo * settings.ClockTickMs);

            events.Add(new ButtonEvent(buttonId, ++_sequence, pressedAt));
        }

        plc.Write(map.Ack, counter);
        _lastCounter = counter;
        return events;
    }
}
