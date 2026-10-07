using System.Net;
using FluentModbus;

namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// Reading and writing the PLC's 16-bit holding registers. The only seam between our PLC
/// logic and the network, so the logic can be tested against a fake.
/// </summary>
public interface IPlcRegisters
{
    bool IsConnected { get; }
    void Connect();
    void Disconnect();
    ushort[] Read(int startAddress, int count);
    void Write(int address, ushort value);
}

/// <summary>The real thing: Modbus TCP via FluentModbus. Not thread-safe; only PlcPollingService uses it.</summary>
public sealed class ModbusPlcRegisters(PlcSettings settings) : IPlcRegisters
{
    private readonly ModbusTcpClient _client = new()
    {
        ConnectTimeout = settings.TimeoutMs,
        ReadTimeout = settings.TimeoutMs,
        WriteTimeout = settings.TimeoutMs,
    };

    public bool IsConnected => _client.IsConnected;

    public void Connect()
    {
        var address = Dns.GetHostAddresses(settings.Host).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        _client.Connect(new IPEndPoint(address, settings.Port), ModbusEndianness.BigEndian);
    }

    public void Disconnect()
    {
        if (_client.IsConnected)
            _client.Disconnect();
    }

    public ushort[] Read(int startAddress, int count) =>
        _client.ReadHoldingRegisters<ushort>(settings.UnitId, startAddress, count).ToArray();

    public void Write(int address, ushort value) =>
        _client.WriteSingleRegister(settings.UnitId, address, value);
}
