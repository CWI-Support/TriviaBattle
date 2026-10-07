// PLC simulator: stands in for the venue PLC so the whole PLC path (buttons in, LEDs and
// lighting out) can be tested without hardware.
//
//   1. dotnet run --project tools/PlcSimulator              (listens on port 5020)
//   2. Start the server with the PLC switched on, pointed at the simulator:
//        $env:Plc__Enabled="true"; $env:Plc__Host="127.0.0.1"; $env:Plc__Port="5020"; dotnet run
//   3. Press 1234 / QWER / ASDF / ZXCV here to press the buttons (same layout as config/keyboard.json).
//      The screen shows every LED and the lighting cue as the server sets them. Esc quits.
//
// Options: --port <n>   --config <folder with plc.json and keyboard.json>
// With input redirected (scripts/tests), it reads lines like "press 12" instead of keys.

using Microsoft.Extensions.Configuration;
using PlcSimulator;
using TriviaBattle.Core.Output;
using TriviaBattle.Hardware.Plc;

var port = int.Parse(Option("--port") ?? "5020");
var configFolder = Option("--config") ?? FindServerConfigFolder();

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(configFolder, "plc.json"))
    .AddJsonFile(Path.Combine(configFolder, "keyboard.json"), optional: true)
    .Build();
var settings = config.GetSection("Plc").Get<PlcSettings>()!;
var keyToButton = config.GetSection("KeyboardInput:Keys").Get<Dictionary<string, int>>() ?? new();

using var plc = new SimulatedPlc(settings, port);
Console.WriteLine($"Simulated PLC listening on port {port} (unit {settings.UnitId}), config from {configFolder}");

if (Console.IsInputRedirected)
    RunScripted();
else
    RunInteractive();

// ---------------------------------------------------------------------------------------------

void RunInteractive()
{
    Console.Clear();
    while (true)
    {
        plc.Scan();

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape) return;
            if (keyToButton.TryGetValue(key.KeyChar.ToString().ToLowerInvariant(), out var buttonId))
                plc.Press(buttonId);
        }

        Draw();
        Thread.Sleep(10);
    }
}

void RunScripted()
{
    using var scanner = new Timer(_ => plc.Scan(), null, 0, 10);
    string? line;
    while ((line = Console.ReadLine()) != null)
    {
        var parts = line.Trim().Split(' ');
        if (parts is ["press", var id] && int.TryParse(id, out var buttonId))
            Console.WriteLine(plc.Press(buttonId) ? $"pressed {buttonId}" : $"buffer full, dropped {buttonId}");
        else if (parts is ["leds"])
            Console.WriteLine(string.Join(' ', settings.LedRegisters.Keys.Order().Select(b => $"{b}:{plc.Led(b)}")));
        else if (parts is ["wait", var ms])
            Thread.Sleep(int.Parse(ms));
    }
}

void Draw()
{
    Console.SetCursorPosition(0, 2);
    Console.WriteLine($"Server connected: {(plc.ConnectedClients > 0 ? "yes" : "no ")}   heartbeat {plc.Heartbeat,5}   " +
                      $"presses {plc.EventCounter,5}   acked {plc.Ack,5}   clock {plc.ClockTicks,5}   ");
    foreach (var (room, _) in settings.LightingCueRegisters)
        Console.WriteLine($"Room {room} lighting: {plc.Cue(room),-10}");
    Console.WriteLine();

    // One line per station: its key row, then its four LEDs.
    var buttons = settings.LedRegisters.Keys.Order().ToList();
    var keysByButton = keyToButton.ToDictionary(kv => kv.Value, kv => kv.Key.ToUpperInvariant());
    for (var i = 0; i < buttons.Count; i += 4)
    {
        var row = buttons.Skip(i).Take(4).ToList();
        var keys = string.Join("", row.Select(b => keysByButton.GetValueOrDefault(b, "?")));
        var leds = string.Join("  ", row.Select((b, n) => $"{"ABCD"[n]}:{LedSymbol(plc.Led(b))}"));
        Console.WriteLine($"Buttons {row[0]}-{row[^1]}  keys {keys}   {leds}   ");
    }
    Console.WriteLine();
    Console.WriteLine("Esc to quit.");
}

static string LedSymbol(LedState state) => state switch
{
    LedState.On => "ON   ",
    LedState.Blink => DateTime.Now.Millisecond < 500 ? "BLINK" : "     ",
    _ => "off  ",
};

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string FindServerConfigFolder()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
        dir = dir.Parent;
    return dir == null
        ? throw new InvalidOperationException("Run from inside the repo, or pass --config <folder>.")
        : Path.Combine(dir.FullName, "src", "TriviaBattle.Server", "config");
}
