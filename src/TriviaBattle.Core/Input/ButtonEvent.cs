namespace TriviaBattle.Core.Input;

/// <summary>
/// A raw button press as reported by the hardware (or the keyboard stand-in).
/// Only the input layer deals in raw button ids; it turns these into a <see cref="ButtonPress"/>
/// using the <see cref="ButtonMap"/> before anything reaches the game.
/// </summary>
/// <param name="ButtonId">The fixed button id from config/buttons.json (10-25 for room A).</param>
/// <param name="Sequence">
/// Press order. Lower = pressed earlier. For the PLC this comes from the PLC's own latch order,
/// so fairness doesn't depend on how often the server polls.
/// </param>
/// <param name="PressedAt">When the press happened, on the server's clock.</param>
public sealed record ButtonEvent(int ButtonId, long Sequence, DateTimeOffset PressedAt);

/// <summary>
/// A button press after translation: which station pressed which answer.
/// This is the only kind of input the game engine understands.
/// </summary>
/// <param name="StationId">Station that owns the button, e.g. "A1".</param>
/// <param name="AnswerIndex">0-3 for answers A-D.</param>
/// <param name="Sequence">Press order, carried over from <see cref="ButtonEvent.Sequence"/>.</param>
/// <param name="PressedAt">When the press happened, on the server's clock.</param>
public sealed record ButtonPress(string StationId, int AnswerIndex, long Sequence, DateTimeOffset PressedAt);
