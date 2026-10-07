using TriviaBattle.Core.Input;
using TriviaBattle.Core.Output;
using TriviaBattle.Hardware.Input;
using TriviaBattle.Hardware.Output;
using TriviaBattle.Hardware.Plc;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Hubs;

namespace TriviaBattle.Server.Configuration;

/// <summary>
/// Chooses the button inputs and room outputs. This is the one place that decides
/// "keyboard + console" (development) versus "PLC" (the venue):
///
///   Inputs  (IButtonInput): keyboard always (it only works when dev tools are on) + PLC if Plc:Enabled
///   Outputs (IRoomOutput):  PLC if Plc:Enabled, otherwise the console log
/// </summary>
public static class HardwareServices
{
    public static void AddRoomHardware(this IServiceCollection services, IConfiguration config, IHostEnvironment env, ButtonMap buttonMap)
    {
        var devToolsEnabled = config.GetValue("DevTools:Enabled", env.IsDevelopment());
        services.AddSingleton(new DevToolsSettings(devToolsEnabled));

        // Keyboard stand-in for the buttons (config/keyboard.json).
        services.AddSingleton(sp => new KeyboardButtonInput(HardwareConfig.LoadKeyboardMap(config), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IButtonInput>(sp => sp.GetRequiredService<KeyboardButtonInput>());

        // The PLC (config/plc.json).
        var plc = config.GetSection("Plc").Get<PlcSettings>() ?? new PlcSettings();
        services.AddSingleton(plc);
        services.AddSingleton<PlcStatus>();

        if (plc.Enabled)
        {
            var problems = plc.Validate(buttonMap.Assignments.Select(a => a.ButtonId));
            if (problems.Count > 0)
                throw new InvalidOperationException("config/plc.json: " + string.Join(" ", problems));

            services.AddSingleton<IPlcRegisters, ModbusPlcRegisters>();
            services.AddSingleton<PlcEventReader>();
            services.AddSingleton<PlcModbusButtonInput>();
            services.AddSingleton<IButtonInput>(sp => sp.GetRequiredService<PlcModbusButtonInput>());
            services.AddSingleton<PlcModbusRoomOutput>();
            services.AddSingleton<IRoomOutput>(sp => sp.GetRequiredService<PlcModbusRoomOutput>());
            services.AddHostedService<PlcPollingService>();
        }
        else
        {
            services.AddSingleton<IRoomOutput, ConsoleRoomOutput>();
        }

        services.AddSingleton<ButtonRouter>();
        services.AddSingleton<IMatchBroadcaster, RoomOutputCoordinator>();
    }
}
