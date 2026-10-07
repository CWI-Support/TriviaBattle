using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Questions;
using TriviaBattle.Hardware.Input;
using TriviaBattle.Server.Configuration;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Display;
using TriviaBattle.Server.Game;
using TriviaBattle.Server.Hubs;
using TriviaBattle.Server.Leaderboards;
using TriviaBattle.Server.Middleware;
using TriviaBattle.Server.Services;

// Trivia Battle game server.
// This file only wires things together; each piece lives in its own folder (see README.md).

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/triviabattle-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ---- Configuration files (all hand-editable, see README "Configuration") ----
    builder.Configuration
        .AddJsonFile("config/stations.json", optional: false, reloadOnChange: false)
        .AddJsonFile("config/buttons.json", optional: false, reloadOnChange: false)
        .AddJsonFile("config/keyboard.json", optional: true, reloadOnChange: false)
        .AddJsonFile("config/plc.json", optional: true, reloadOnChange: false)
        // Re-added so environment variables and command-line arguments still override the files above
        // (e.g. Plc__Enabled=true), the same way they override appsettings.json.
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    var stationLayout = HardwareConfig.LoadStationLayout(builder.Configuration);
    var buttonMap = HardwareConfig.LoadButtonMap(builder.Configuration, stationLayout);
    builder.Services.AddSingleton(stationLayout);
    builder.Services.AddSingleton(buttonMap);
    builder.Services.Configure<MatchSettings>(builder.Configuration.GetSection("Game"));

    // ---- Web ----
    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // ---- Database. The factory is for long-lived services; controllers can take TriviaDbContext directly. ----
    builder.Services.AddDbContextFactory<TriviaDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("TriviaBattle")));
    builder.Services.AddSingleton<IQuestionSource, SqliteQuestionSource>();
    builder.Services.AddSingleton<MatchHistory>();
    builder.Services.AddSingleton<LeaderboardService>();
    builder.Services.Configure<LeaderboardSettings>(builder.Configuration.GetSection("Leaderboards"));

    // ---- Game ----
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<MatchHost>();
    builder.Services.AddHostedService<MatchTicker>();
    builder.Services.AddSingleton<IMatchBroadcaster, LoggingMatchBroadcaster>();
    builder.Services.AddSingleton<IMatchBroadcaster, SignalRMatchBroadcaster>();
    builder.Services.AddSingleton<ScreenStateFactory>();
    builder.Services.AddSingleton<ThemeService>();
    builder.Services.Configure<MediaSettings>(builder.Configuration.GetSection("Media"));

    // ---- Buttons, LEDs and lighting: keyboard/console in dev, the PLC in the venue (see HardwareServices) ----
    builder.Services.AddRoomHardware(builder.Configuration, builder.Environment, buttonMap);

    builder.Services.AddSingleton<IWordFilterService, FileWordFilterService>();

    var app = builder.Build();

    await DatabaseStartup.InitializeAsync(app.Services, app.Environment.ContentRootPath);

    Log.Information("Loaded {Stations} stations and {Buttons} buttons",
        stationLayout.Stations.Count, buttonMap.Assignments.Count);
    if (string.IsNullOrEmpty(app.Configuration["AdminApiKey"]))
        Log.Warning("AdminApiKey is empty: admin endpoints are OPEN to anyone on the network.");

    app.Services.GetRequiredService<ButtonRouter>().Start();

    app.UseGlobalExceptionHandler();
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        // Always check for a newer file, so screens pick up edits on refresh.
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
    });
    app.MapControllers();
    app.MapHub<GameHub>("/gamehub");

    // Screen URLs. A screen works out its role from its own URL (see wwwroot/js/screen-connection.js).
    // Optional ?room=A picks the room; the default is the first room in stations.json.
    app.MapFallbackToFile("/display/main", "screens/main.html");
    app.MapFallbackToFile("/display/player/{number:int}", "screens/player.html");
    app.MapFallbackToFile("/display/leaderboard", "screens/leaderboard.html");
    app.MapFallbackToFile("/kiosk", "screens/kiosk.html");
    app.MapFallbackToFile("/dev/buttons", "screens/dev-buttons.html");
    app.MapFallbackToFile("/admin", "screens/admin.html");

    var port = app.Configuration.GetValue("Port", 5000);
    Log.Information("Trivia Battle server listening on port {Port}", port);
    app.Run($"http://0.0.0.0:{port}");
}
catch (Exception ex) when (ex is not HostAbortedException) // EF migration tooling stops the host on purpose
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
