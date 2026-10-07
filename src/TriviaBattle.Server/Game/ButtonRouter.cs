using TriviaBattle.Core.Input;

namespace TriviaBattle.Server.Game;

/// <summary>
/// The boundary between hardware and game: listens to every <see cref="IButtonInput"/>,
/// translates raw button ids (10-25) to station + answer with the <see cref="ButtonMap"/>,
/// and hands them to the <see cref="MatchHost"/>. This is the only place raw button ids are converted.
/// </summary>
public sealed class ButtonRouter(
    IEnumerable<IButtonInput> inputs,
    ButtonMap buttonMap,
    MatchHost host,
    ILogger<ButtonRouter> logger)
{
    /// <summary>Subscribes to all inputs. Called once at startup from Program.cs.</summary>
    public void Start()
    {
        foreach (var input in inputs)
        {
            input.ButtonPressed += Route;
            logger.LogInformation("Listening for button presses from {Input}", input.GetType().Name);
        }
    }

    private void Route(ButtonEvent e)
    {
        var press = buttonMap.Translate(e);
        if (press == null)
        {
            logger.LogWarning("Ignoring press from unmapped button id {ButtonId}", e.ButtonId);
            return;
        }

        host.HandlePress(press);
    }
}
