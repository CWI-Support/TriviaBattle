using Microsoft.Extensions.Logging.Abstractions;
using TriviaBattle.Hardware.Plc;

namespace TriviaBattle.Tests.Hardware;

public class PlcEventReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PlcSettings _settings = new()
    {
        ClockTickMs = 1,
        Registers = new PlcRegisterMap { EventCounter = 0, EventBufferStart = 10, EventSlots = 8, Ack = 100, Heartbeat = 101 },
        // PLC codes deliberately differ from button ids, to prove the translation happens.
        ButtonCodes = new() { [10] = 1, [11] = 2, [14] = 5 },
    };

    private readonly FakePlcRegisters _plc = new();
    private readonly PlcEventReader _reader;

    public PlcEventReaderTests()
    {
        _reader = new PlcEventReader(_settings, new FixedClock(Now), NullLogger<PlcEventReader>.Instance);
    }

    [Fact]
    public void First_poll_after_connecting_skips_old_presses_and_acks_them()
    {
        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 0); // pressed while we weren't listening

        var events = _reader.Poll(_plc);

        Assert.Empty(events);
        Assert.Equal(1, _plc.Registers[_settings.Registers.Ack]);
    }

    [Fact]
    public void New_presses_come_out_in_press_order_as_button_ids()
    {
        _reader.Poll(_plc); // connect
        _plc.LatchPress(_settings, buttonCode: 5, timestamp: 100);
        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 101);

        var events = _reader.Poll(_plc);

        Assert.Equal([14, 10], events.Select(e => e.ButtonId));
        Assert.True(events[0].Sequence < events[1].Sequence);
        Assert.Equal(2, _plc.Registers[_settings.Registers.Ack]);
    }

    [Fact]
    public void Press_time_is_worked_out_from_the_plc_clock()
    {
        _reader.Poll(_plc);
        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 1000);
        _plc.SetPlcClock(_settings, 1250); // the PLC says the press was 250 ticks ago

        var press = Assert.Single(_reader.Poll(_plc));

        Assert.Equal(Now.AddMilliseconds(-250), press.PressedAt);
    }

    [Fact]
    public void Plc_clock_wrapping_past_65535_is_handled()
    {
        _reader.Poll(_plc);
        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 65530);
        _plc.SetPlcClock(_settings, 4); // wrapped: 10 ticks later

        var press = Assert.Single(_reader.Poll(_plc));

        Assert.Equal(Now.AddMilliseconds(-10), press.PressedAt);
    }

    [Fact]
    public void Event_counter_wrapping_past_65535_is_handled()
    {
        _plc.Registers[_settings.Registers.EventCounter] = 65534;
        _reader.Poll(_plc); // starts at 65534

        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 0); // press 65535
        _plc.LatchPress(_settings, buttonCode: 2, timestamp: 0); // press 0 (wrapped)
        _plc.LatchPress(_settings, buttonCode: 5, timestamp: 0); // press 1

        var events = _reader.Poll(_plc);

        Assert.Equal([10, 11, 14], events.Select(e => e.ButtonId));
        Assert.Equal(1, _plc.Registers[_settings.Registers.Ack]);
    }

    [Fact]
    public void Unknown_button_codes_are_skipped()
    {
        _reader.Poll(_plc);
        _plc.LatchPress(_settings, buttonCode: 99, timestamp: 0);
        _plc.LatchPress(_settings, buttonCode: 1, timestamp: 0);

        Assert.Equal([10], _reader.Poll(_plc).Select(e => e.ButtonId));
    }

    [Fact]
    public void Nothing_new_means_no_reads_of_the_buffer_and_no_ack()
    {
        _reader.Poll(_plc);
        _plc.Writes.Clear();

        Assert.Empty(_reader.Poll(_plc));
        Assert.Empty(_plc.Writes);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
