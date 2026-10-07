using TriviaBattle.Core.Input;

namespace TriviaBattle.Hardware.Input;

/// <summary>
/// Stand-in for the real buttons during development and demos. Keys pressed on the
/// /dev/buttons page (or on any screen opened with ?devkeys=1) are sent to the server and
/// turned into button presses here, using the key map in config/keyboard.json.
///
/// Press order (Sequence) is simply the order presses reach the server, which is fine for
/// testing. The PLC provides its own hardware press order instead.
/// </summary>
public sealed class KeyboardButtonInput(IReadOnlyDictionary<string, int> keyToButtonId, TimeProvider clock) : IButtonInput
{
    private long _sequence;

    public event Action<ButtonEvent>? ButtonPressed;

    /// <summary>Key name -> button id, e.g. "q" -> 14.</summary>
    public IReadOnlyDictionary<string, int> KeyMap { get; } =
        keyToButtonId.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value);

    /// <summary>Presses the button mapped to a key. Returns false if the key isn't mapped.</summary>
    public bool PressKey(string key)
    {
        if (!KeyMap.TryGetValue(key.ToLowerInvariant(), out var buttonId))
            return false;

        PressButton(buttonId);
        return true;
    }

    public void PressButton(int buttonId)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        ButtonPressed?.Invoke(new ButtonEvent(buttonId, sequence, clock.GetUtcNow()));
    }
}
