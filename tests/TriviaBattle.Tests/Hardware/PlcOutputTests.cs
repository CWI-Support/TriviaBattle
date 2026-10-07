using Microsoft.Extensions.Configuration;
using TriviaBattle.Core.Output;
using TriviaBattle.Hardware.Plc;

namespace TriviaBattle.Tests.Hardware;

public class PlcOutputTests
{
    private static PlcSettings Settings() => new()
    {
        LedRegisters = new() { [10] = 200, [11] = 201 },
        LightingCueRegisters = new() { ["A"] = 220 },
    };

    [Fact]
    public void First_flush_puts_the_room_in_a_known_dark_idle_state()
    {
        var plc = new FakePlcRegisters();
        new PlcModbusRoomOutput(Settings()).FlushTo(plc);

        Assert.Equal(3, plc.Writes.Count);
        Assert.All(plc.Writes, w => Assert.Equal(0, w.Value));
    }

    [Fact]
    public void Only_changed_registers_are_written()
    {
        var plc = new FakePlcRegisters();
        var output = new PlcModbusRoomOutput(Settings());
        output.FlushTo(plc);
        plc.Writes.Clear();

        output.SetButtonLeds(new Dictionary<int, LedState> { [10] = LedState.Blink, [11] = LedState.Off });
        output.SetLightingCue("A", LightingCue.Reveal);
        output.FlushTo(plc);

        Assert.Equal([(200, (ushort)2), (220, (ushort)3)], plc.Writes.OrderBy(w => w.Address));
    }

    [Fact]
    public void After_a_reconnect_everything_is_resent()
    {
        var plc = new FakePlcRegisters();
        var output = new PlcModbusRoomOutput(Settings());
        output.FlushTo(plc);
        plc.Writes.Clear();

        output.ForgetWrittenState();
        output.FlushTo(plc);

        Assert.Equal(3, plc.Writes.Count);
    }

    [Fact]
    public void Shipped_plc_json_binds_and_covers_every_button()
    {
        var configFile = Path.Combine(FindRepoRoot(), "src", "TriviaBattle.Server", "config", "plc.json");
        var settings = new ConfigurationBuilder().AddJsonFile(configFile).Build().GetSection("Plc").Get<PlcSettings>()!;

        var problems = settings.Validate(Enumerable.Range(10, 16));

        Assert.Empty(problems);
        Assert.Equal(200, settings.LedRegisters[10]);
        Assert.Equal(25, settings.ButtonCodes[25]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(512)]
    public void Event_slots_must_be_a_power_of_two(int slots)
    {
        var settings = new PlcSettings { Registers = new PlcRegisterMap { EventSlots = slots } };

        Assert.Contains(settings.Validate([]), p => p.Contains("EventSlots"));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
