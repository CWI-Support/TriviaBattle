namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// PLC connection and register map, bound from the "Plc" section of config/plc.json.
///
/// The PLC is an AutomationDirect Productivity series talking Modbus TCP. All register numbers
/// are 0-based holding-register offsets (Modbus 400001 = 0) and every value is a 16-bit register.
///
/// THE REGISTER NUMBERS ARE PLACEHOLDERS until the real register map is finalised.
/// Changing them only means editing config/plc.json; no code changes.
/// See docs/PLC-PROTOCOL.md for how the PLC side must behave.
/// </summary>
public sealed class PlcSettings
{
    /// <summary>Off = no PLC: keyboard buttons and console "lights" only.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "192.168.1.10";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;

    /// <summary>How often to check for new presses. Fairness comes from the PLC's own press order, not from this.</summary>
    public int PollIntervalMs { get; set; } = 10;

    /// <summary>Wait this long before reconnecting after the connection drops.</summary>
    public int ReconnectDelayMs { get; set; } = 2000;

    /// <summary>Connect/read/write timeout.</summary>
    public int TimeoutMs { get; set; } = 1000;

    /// <summary>How many milliseconds one tick of the PLC clock (and event timestamps) represents.</summary>
    public double ClockTickMs { get; set; } = 1;

    public PlcRegisterMap Registers { get; set; } = new();

    /// <summary>Button id (from buttons.json) -> the code the PLC writes into an event slot for that button.</summary>
    public Dictionary<int, int> ButtonCodes { get; set; } = new();

    /// <summary>Button id -> holding register for that button's LED (0 off, 1 on, 2 blink).</summary>
    public Dictionary<int, int> LedRegisters { get; set; } = new();

    /// <summary>Room id -> holding register for that room's lighting cue (see LightingCue for the values).</summary>
    public Dictionary<string, int> LightingCueRegisters { get; set; } = new();

    /// <summary>Catches config mistakes at startup instead of mid-game. Returns plain-English problems.</summary>
    public List<string> Validate(IEnumerable<int> knownButtonIds)
    {
        var problems = new List<string>();
        var slots = Registers.EventSlots;

        if (slots <= 0 || slots > 256 || (slots & (slots - 1)) != 0)
            problems.Add($"Plc:Registers:EventSlots must be a power of two up to 256 (got {slots}).");

        foreach (var buttonId in knownButtonIds)
        {
            if (!ButtonCodes.ContainsKey(buttonId)) problems.Add($"Plc:ButtonCodes has no code for button {buttonId}.");
            if (!LedRegisters.ContainsKey(buttonId)) problems.Add($"Plc:LedRegisters has no register for button {buttonId}.");
        }

        var duplicateCode = ButtonCodes.GroupBy(kv => kv.Value).FirstOrDefault(g => g.Count() > 1);
        if (duplicateCode != null)
            problems.Add($"Plc:ButtonCodes uses code {duplicateCode.Key} for more than one button.");

        return problems;
    }
}

public sealed class PlcRegisterMap
{
    /// <summary>
    /// PLC -> server. Two registers in a row:
    /// [EventCounter]     how many presses the PLC has latched in total (wraps 65535 -> 0)
    /// [EventCounter + 1] the PLC clock: a free-running tick counter (wraps), same units as event timestamps
    /// </summary>
    public int EventCounter { get; set; } = 0;

    /// <summary>
    /// PLC -> server. Ring buffer of latched presses, 2 registers per slot: [button code, timestamp].
    /// Press number N (the EventCounter value right after latching it) goes in slot (N - 1) mod EventSlots.
    /// </summary>
    public int EventBufferStart { get; set; } = 10;

    /// <summary>Number of ring slots. Must be a power of two (so the slot numbering survives the counter wrapping).</summary>
    public int EventSlots { get; set; } = 32;

    /// <summary>Server -> PLC: the EventCounter value the server has processed up to.</summary>
    public int Ack { get; set; } = 100;

    /// <summary>Server -> PLC: goes up by one every second while the server is healthy.</summary>
    public int Heartbeat { get; set; } = 101;
}
