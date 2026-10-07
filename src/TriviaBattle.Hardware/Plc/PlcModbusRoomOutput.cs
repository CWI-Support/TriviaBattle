using TriviaBattle.Core.Output;

namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// Button LEDs and room lighting via the PLC. The game calls SetButtonLeds/SetLightingCue (which
/// only remember the wanted state, so they're instant); PlcPollingService then calls FlushTo,
/// which writes just the registers that changed.
/// </summary>
public sealed class PlcModbusRoomOutput : IRoomOutput
{
    private readonly PlcSettings _settings;
    private readonly object _lock = new();

    // register address -> value we want / value we last wrote successfully
    private readonly Dictionary<int, ushort> _wanted = new();
    private readonly Dictionary<int, ushort> _written = new();

    public PlcModbusRoomOutput(PlcSettings settings)
    {
        _settings = settings;

        // Start dark and idle, so the first flush after connecting puts the room in a known state.
        foreach (var register in settings.LedRegisters.Values)
            _wanted[register] = (ushort)LedState.Off;
        foreach (var register in settings.LightingCueRegisters.Values)
            _wanted[register] = (ushort)LightingCue.Idle;
    }

    public void SetButtonLeds(IReadOnlyDictionary<int, LedState> ledsByButtonId)
    {
        lock (_lock)
        {
            foreach (var (buttonId, state) in ledsByButtonId)
            {
                if (_settings.LedRegisters.TryGetValue(buttonId, out var register))
                    _wanted[register] = (ushort)state;
            }
        }
    }

    public void SetLightingCue(string roomId, LightingCue cue)
    {
        lock (_lock)
        {
            if (_settings.LightingCueRegisters.TryGetValue(roomId, out var register))
                _wanted[register] = (ushort)cue;
        }
    }

    /// <summary>Writes every register whose wanted value differs from what the PLC last got.</summary>
    public void FlushTo(IPlcRegisters plc)
    {
        List<KeyValuePair<int, ushort>> changes;
        lock (_lock)
        {
            changes = _wanted.Where(kv => !_written.TryGetValue(kv.Key, out var written) || written != kv.Value).ToList();
        }

        foreach (var (register, value) in changes)
        {
            plc.Write(register, value);
            lock (_lock) _written[register] = value;
        }
    }

    /// <summary>After a reconnect we can't trust what the PLC has, so resend everything on the next flush.</summary>
    public void ForgetWrittenState()
    {
        lock (_lock) _written.Clear();
    }
}
