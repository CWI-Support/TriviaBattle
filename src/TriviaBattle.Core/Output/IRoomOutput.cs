namespace TriviaBattle.Core.Output;

/// <summary>What a button's LED should do. The numbers are what the PLC receives.</summary>
public enum LedState
{
    Off = 0,
    On = 1,
    Blink = 2,
}

/// <summary>Room lighting scenes. The numbers are what the PLC receives; the PLC decides what each looks like.</summary>
public enum LightingCue
{
    Idle = 0,
    Intro = 1,
    Question = 2,   // the timed round is running
    Reveal = 3,     // not used since every match became a timed round; kept so the PLC numbering stays put
    Standings = 4,  // not used either, same reason
    Results = 5,
    Boss = 6,       // Dungeon format: someone in the room is fighting a boss (the room turns ominous)
}

/// <summary>
/// Physical outputs in the room: button LEDs and lighting. Implementations:
/// <list type="bullet">
///   <item>ConsoleRoomOutput: dev, logs what the lights would do.</item>
///   <item>PlcModbusRoomOutput: writes to the PLC.</item>
/// </list>
/// LEDs are addressed by the same fixed button ids as the inputs (10-25 for room A).
/// Calls must return quickly (they're made while the game holds its lock); real I/O happens elsewhere.
/// </summary>
public interface IRoomOutput
{
    /// <summary>Sets the listed buttons' LEDs. Buttons not listed keep their current state.</summary>
    void SetButtonLeds(IReadOnlyDictionary<int, LedState> ledsByButtonId);

    void SetLightingCue(string roomId, LightingCue cue);
}
