namespace TriviaBattle.Core.Input;

/// <summary>
/// A source of raw button presses. There are two implementations:
/// <list type="bullet">
///   <item>KeyboardButtonInput: dev/demo, keys pressed on the /dev/buttons page.</item>
///   <item>PlcModbusButtonInput: the real buttons, read from the PLC over Modbus TCP.</item>
/// </list>
/// Which one runs is chosen in config ("Input:Provider"). The game never knows the difference.
/// </summary>
public interface IButtonInput
{
    /// <summary>Raised once per press, in press order.</summary>
    event Action<ButtonEvent>? ButtonPressed;
}
