using System.Buffers.Binary;
using System.Net;
using FluentModbus;
using Microsoft.Extensions.Logging.Abstractions;
using TriviaBattle.Hardware.Plc;

namespace TriviaBattle.Tests.Hardware;

/// <summary>
/// End-to-end over real Modbus TCP: a FluentModbus server on localhost plays the PLC,
/// and our ModbusPlcRegisters + PlcEventReader talk to it.
/// </summary>
public class ModbusRoundTripTests : IDisposable
{
    private const byte UnitId = 1;
    private readonly ModbusTcpServer _fakePlc = new();
    private readonly PlcSettings _settings;

    public ModbusRoundTripTests()
    {
        var port = 15000 + Random.Shared.Next(1000);
        _fakePlc.AddUnit(UnitId);
        _fakePlc.Start(new IPEndPoint(IPAddress.Loopback, port));

        _settings = new PlcSettings
        {
            Host = "127.0.0.1",
            Port = port,
            UnitId = UnitId,
            Registers = new PlcRegisterMap { EventCounter = 0, EventBufferStart = 10, EventSlots = 32, Ack = 100 },
            ButtonCodes = new() { [12] = 12 },
        };
    }

    public void Dispose() => _fakePlc.Stop();

    [Fact]
    public void Press_latched_in_the_plc_arrives_as_a_button_event_and_is_acked()
    {
        var plc = new ModbusPlcRegisters(_settings);
        plc.Connect();
        var reader = new PlcEventReader(_settings, TimeProvider.System, NullLogger<PlcEventReader>.Instance);
        reader.Poll(plc); // first look

        // The "PLC" latches a press of button code 12 as press #1, in slot 0.
        SetRegister(10, 12);
        SetRegister(11, 500);
        SetRegister(1, 500);
        SetRegister(0, 1);

        var press = Assert.Single(reader.Poll(plc));
        Assert.Equal(12, press.ButtonId);

        Thread.Sleep(50); // let the server process the ack write
        Assert.Equal(1, GetRegister(100));
        plc.Disconnect();
    }

    // FluentModbus's server keeps registers in network (big-endian) byte order.
    private void SetRegister(int address, ushort value)
    {
        lock (_fakePlc.Lock)
            _fakePlc.GetHoldingRegisters(UnitId)[address] = (short)BinaryPrimitives.ReverseEndianness(value);
    }

    private ushort GetRegister(int address)
    {
        lock (_fakePlc.Lock)
            return BinaryPrimitives.ReverseEndianness((ushort)_fakePlc.GetHoldingRegisters(UnitId)[address]);
    }
}
