using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TriviaBattle.Hardware.Plc;

/// <summary>
/// The one loop that talks to the PLC. Every cycle it:
/// <list type="number">
///   <item>connects if needed,</item>
///   <item>reads new button presses and passes them to the game,</item>
///   <item>writes any LED / lighting changes,</item>
///   <item>bumps the heartbeat register once a second.</item>
/// </list>
/// Because only this loop touches the Modbus connection, there are no threading issues with it.
/// If anything fails, it disconnects, waits, and tries again forever.
/// </summary>
public sealed class PlcPollingService(
    PlcSettings settings,
    IPlcRegisters plc,
    PlcEventReader reader,
    PlcModbusButtonInput input,
    PlcModbusRoomOutput output,
    PlcStatus status,
    TimeProvider clock,
    ILogger<PlcPollingService> logger) : BackgroundService
{
    private ushort _heartbeat;
    private DateTimeOffset _nextHeartbeatAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PLC: connecting to {Host}:{Port} (unit {Unit})", settings.Host, settings.Port, settings.UnitId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!plc.IsConnected)
                    ConnectFresh();

                foreach (var press in reader.Poll(plc))
                {
                    status.PressReceived(clock.GetUtcNow());
                    input.Raise(press);
                }

                output.FlushTo(plc);
                SendHeartbeatIfDue();

                await Task.Delay(settings.PollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (status.IsConnected || status.LastError == null)
                    logger.LogWarning("PLC: connection problem ({Error}); retrying every {Delay} ms", ex.Message, settings.ReconnectDelayMs);

                status.Failed(ex.Message, clock.GetUtcNow());
                SafeDisconnect();

                try { await Task.Delay(settings.ReconnectDelayMs, stoppingToken); }
                catch (OperationCanceledException) { break; } // server shutting down
            }
        }

        SafeDisconnect();
    }

    private void ConnectFresh()
    {
        plc.Connect();
        reader.Reset();
        output.ForgetWrittenState(); // resend all LEDs/lighting: the PLC may have restarted
        status.Connected(clock.GetUtcNow());
        logger.LogInformation("PLC: connected");
    }

    private void SendHeartbeatIfDue()
    {
        var now = clock.GetUtcNow();
        if (now < _nextHeartbeatAt) return;

        plc.Write(settings.Registers.Heartbeat, ++_heartbeat);
        _nextHeartbeatAt = now.AddSeconds(1);
    }

    private void SafeDisconnect()
    {
        try { plc.Disconnect(); }
        catch { /* already gone */ }
    }
}
