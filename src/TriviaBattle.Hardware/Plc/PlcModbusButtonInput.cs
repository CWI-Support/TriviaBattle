using TriviaBattle.Core.Input;

namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// The real answer buttons. PlcPollingService reads presses from the PLC (via PlcEventReader)
/// and hands them here; from there they flow through ButtonRouter exactly like keyboard presses.
/// </summary>
public sealed class PlcModbusButtonInput : IButtonInput
{
    public event Action<ButtonEvent>? ButtonPressed;

    internal void Raise(ButtonEvent e) => ButtonPressed?.Invoke(e);
}
