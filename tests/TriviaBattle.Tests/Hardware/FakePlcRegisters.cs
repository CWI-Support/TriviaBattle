using TriviaBattle.Hardware.Plc;

namespace TriviaBattle.Tests.Hardware;

/// <summary>An in-memory PLC: 1000 holding registers, plus a log of every write.</summary>
public sealed class FakePlcRegisters : IPlcRegisters
{
    public ushort[] Registers { get; } = new ushort[1000];
    public List<(int Address, ushort Value)> Writes { get; } = new();

    public bool IsConnected { get; private set; } = true;
    public void Connect() => IsConnected = true;
    public void Disconnect() => IsConnected = false;

    public ushort[] Read(int startAddress, int count) => Registers[startAddress..(startAddress + count)];

    public void Write(int address, ushort value)
    {
        Registers[address] = value;
        Writes.Add((address, value));
    }

    /// <summary>Does what the PLC program does when a button is pressed: latch it into the ring and bump the counter.</summary>
    public void LatchPress(PlcSettings settings, int buttonCode, ushort timestamp)
    {
        var map = settings.Registers;
        var counter = (ushort)(Registers[map.EventCounter] + 1);
        var slot = (counter - 1 + 65536) % map.EventSlots;

        Registers[map.EventBufferStart + slot * 2] = (ushort)buttonCode;
        Registers[map.EventBufferStart + slot * 2 + 1] = timestamp;
        Registers[map.EventCounter] = counter;
    }

    public void SetPlcClock(PlcSettings settings, ushort ticks) => Registers[settings.Registers.EventCounter + 1] = ticks;
}
