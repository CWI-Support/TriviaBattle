namespace TriviaBattle.Server.Game;

/// <summary>
/// The game clock: ticks every running match, and the arcade lobby, 20 times a second so phases advance
/// on time. Answer timing doesn't depend on this rate; presses carry their own timestamps.
/// </summary>
public sealed class MatchTicker(MatchHost host, LobbyHost lobby, ILogger<MatchTicker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                host.Tick();
                lobby.Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Match tick failed");
            }
        }
    }
}
