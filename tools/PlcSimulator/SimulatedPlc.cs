using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using FluentModbus;
using TriviaBattle.Core.Output;
using TriviaBattle.Hardware.Plc;

namespace PlcSimulator;

/// <summary>
/// Behaves like the venue PLC is required to (docs/PLC-PROTOCOL.md), using the same register
/// map as the server (config/plc.json). Written to be read by the PLC programmer as a reference.
/// </summary>
public sealed class SimulatedPlc : IDisposable
{
    private readonly PlcSettings _settings;
    private readonly ModbusTcpServer _server = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public SimulatedPlc(PlcSettings settings, int port)
    {
        _settings = settings;
        _server.AddUnit(settings.UnitId);
        _server.Start(new IPEndPoint(IPAddress.Any, port));
    }

    public void Dispose() => _server.Stop();

    /// <summary>The PLC clock: free-running ticks since power-up, wrapping at 65536. Updated every scan.</summary>
    public ushort ClockTicks => (ushort)(_uptime.Elapsed.TotalMilliseconds / _settings.ClockTickMs);

    /// <summary>Call regularly (the PLC does this every scan).</summary>
    public void Scan() => Set(_settings.Registers.EventCounter + 1, ClockTicks);

    /// <summary>
    /// What the PLC does on a button's rising edge: latch it into the next ring slot, stamped with
    /// the clock, then bump the event counter. If the server hasn't acked enough slots, the press is
    /// dropped rather than overwriting one the server hasn't read yet.
    /// </summary>
    /// <returns>False if the buffer was full.</returns>
    public bool Press(int buttonId)
    {
        var map = _settings.Registers;
        var code = _settings.ButtonCodes[buttonId];

        lock (_server.Lock)
        {
            var counter = Get(map.EventCounter);
            var ack = Get(map.Ack);
            var unacked = (ushort)(counter - ack);
            if (unacked >= map.EventSlots)
                return false;

            var pressNumber = (ushort)(counter + 1);
            var slot = (pressNumber - 1 + 65536) % map.EventSlots;

            Set(map.EventBufferStart + slot * 2, (ushort)code);
            Set(map.EventBufferStart + slot * 2 + 1, ClockTicks);
            Set(map.EventCounter, pressNumber); // last, so the server never sees a half-written slot
            return true;
        }
    }

    public LedState Led(int buttonId) => (LedState)Get(_settings.LedRegisters[buttonId]);

    public LightingCue Cue(string roomId) => (LightingCue)Get(_settings.LightingCueRegisters[roomId]);

    public ushort Heartbeat => Get(_settings.Registers.Heartbeat);
    public ushort EventCounter => Get(_settings.Registers.EventCounter);
    public ushort Ack => Get(_settings.Registers.Ack);
    public int ConnectedClients => _server.ConnectionCount;

    // FluentModbus keeps registers in network (big-endian) byte order, so swap on the way in and out.
    private ushort Get(int address)
    {
        lock (_server.Lock)
            return BinaryPrimitives.ReverseEndianness((ushort)_server.GetHoldingRegisters(_settings.UnitId)[address]);
    }

    private void Set(int address, ushort value)
    {
        lock (_server.Lock)
            _server.GetHoldingRegisters(_settings.UnitId)[address] = (short)BinaryPrimitives.ReverseEndianness(value);
    }
}
