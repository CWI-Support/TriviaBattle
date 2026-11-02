using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Server.Game;

/// <summary>The game rules the operator can change on the admin page (Operator tab).</summary>
/// <param name="MatchSeconds">The round length: every group game, and every Classic game.</param>
/// <param name="Format">Classic quiz or Dungeon Quiz Quest.</param>
/// <param name="SoloStartSeconds">Solo Dungeon games' arcade clock (see DungeonSettings): starting time,</param>
/// <param name="SoloSecondsPerKill">time added per monster slain,</param>
/// <param name="SoloSecondsPerBoss">time added per boss slain,</param>
/// <param name="SoloMaxSeconds">the most a solo game can last (continues included),</param>
/// <param name="SoloContinueSeconds">the time a continue adds (0 = no continues),</param>
/// <param name="SoloContinueCountdownSeconds">and how long "Continue?" waits for a swipe.</param>
public sealed record GameRules(
    double MatchSeconds,
    WrongAnswerPenalty WrongAnswerPenalty,
    double LockoutSeconds,
    int WrongAnswerPoints,
    GameFormat Format,
    double SoloStartSeconds,
    double SoloSecondsPerKill,
    double SoloSecondsPerBoss,
    double SoloMaxSeconds,
    double SoloContinueSeconds,
    double SoloContinueCountdownSeconds);

/// <summary>
/// Game rules set on the admin page, saved in the database (AppSettings table). Anything not
/// saved there yet comes from "Game" in appsettings.json. A change applies from the next match;
/// a running match keeps the rules it started with.
/// </summary>
public class GameRulesService(IDbContextFactory<TriviaDbContext> dbFactory, IOptionsMonitor<MatchSettings> configured)
{
    private const string KeyPrefix = "Game:";

    /// <summary>The settings for a new match: appsettings.json with the admin page's rules on top.</summary>
    public async Task<MatchSettings> SettingsForNewMatchAsync()
    {
        var rules = await GetAsync();
        var settings = configured.CurrentValue.Copy();
        settings.MatchSeconds = rules.MatchSeconds;
        settings.WrongAnswerPenalty = rules.WrongAnswerPenalty;
        settings.LockoutSeconds = rules.LockoutSeconds;
        settings.WrongAnswerPoints = rules.WrongAnswerPoints;
        settings.Format = rules.Format;
        settings.Dungeon.SoloStartSeconds = rules.SoloStartSeconds;
        settings.Dungeon.SoloSecondsPerKill = rules.SoloSecondsPerKill;
        settings.Dungeon.SoloSecondsPerBoss = rules.SoloSecondsPerBoss;
        settings.Dungeon.SoloMaxSeconds = rules.SoloMaxSeconds;
        settings.Dungeon.SoloContinueSeconds = rules.SoloContinueSeconds;
        settings.Dungeon.SoloContinueCountdownSeconds = rules.SoloContinueCountdownSeconds;
        return settings;
    }

    public async Task<GameRules> GetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saved = await db.AppSettings
            .Where(s => s.Key.StartsWith(KeyPrefix))
            .ToDictionaryAsync(s => s.Key[KeyPrefix.Length..], s => s.Value);

        var defaults = configured.CurrentValue;
        string? Saved(string name) => saved.GetValueOrDefault(name);
        double SavedSeconds(string name, double fallback) =>
            double.TryParse(Saved(name), CultureInfo.InvariantCulture, out var value) ? value : fallback;

        return new GameRules(
            SavedSeconds(nameof(GameRules.MatchSeconds), defaults.MatchSeconds),
            Enum.TryParse<WrongAnswerPenalty>(Saved(nameof(GameRules.WrongAnswerPenalty)), out var penalty) ? penalty : defaults.WrongAnswerPenalty,
            SavedSeconds(nameof(GameRules.LockoutSeconds), defaults.LockoutSeconds),
            int.TryParse(Saved(nameof(GameRules.WrongAnswerPoints)), CultureInfo.InvariantCulture, out var points) ? points : defaults.WrongAnswerPoints,
            Enum.TryParse<GameFormat>(Saved(nameof(GameRules.Format)), out var format) ? format : defaults.Format,
            SavedSeconds(nameof(GameRules.SoloStartSeconds), defaults.Dungeon.SoloStartSeconds),
            SavedSeconds(nameof(GameRules.SoloSecondsPerKill), defaults.Dungeon.SoloSecondsPerKill),
            SavedSeconds(nameof(GameRules.SoloSecondsPerBoss), defaults.Dungeon.SoloSecondsPerBoss),
            SavedSeconds(nameof(GameRules.SoloMaxSeconds), defaults.Dungeon.SoloMaxSeconds),
            SavedSeconds(nameof(GameRules.SoloContinueSeconds), defaults.Dungeon.SoloContinueSeconds),
            SavedSeconds(nameof(GameRules.SoloContinueCountdownSeconds), defaults.Dungeon.SoloContinueCountdownSeconds));
    }

    /// <summary>Plain-English problems with these rules; empty if they're fine.</summary>
    public static IReadOnlyList<string> Validate(GameRules rules)
    {
        var problems = new List<string>();
        if (rules.MatchSeconds is < 15 or > 600)
            problems.Add("Round length must be between 15 and 600 seconds.");
        if (rules.LockoutSeconds is < 0.5 or > 10)
            problems.Add("Lockout must be between 0.5 and 10 seconds.");
        if (rules.WrongAnswerPoints is < 0 or > 1000)
            problems.Add("Points lost must be between 0 and 1000.");
        if (!Enum.IsDefined(rules.WrongAnswerPenalty))
            problems.Add("Unknown wrong-answer penalty.");
        if (!Enum.IsDefined(rules.Format))
            problems.Add("Unknown game format.");
        if (rules.SoloStartSeconds is < 15 or > 600)
            problems.Add("Solo starting time must be between 15 and 600 seconds.");
        if (rules.SoloSecondsPerKill is < 0 or > 60)
            problems.Add("Time per monster must be between 0 and 60 seconds.");
        if (rules.SoloSecondsPerBoss is < 0 or > 120)
            problems.Add("Time per boss must be between 0 and 120 seconds.");
        if (rules.SoloMaxSeconds is < 30 or > 1800)
            problems.Add("The longest a solo game can last must be between 30 and 1800 seconds.");
        else if (rules.SoloMaxSeconds < rules.SoloStartSeconds)
            problems.Add("The longest a solo game can last can't be shorter than its starting time.");
        if (rules.SoloContinueSeconds is < 0 or > 300)
            problems.Add("Time per continue must be between 0 (no continues) and 300 seconds.");
        if (rules.SoloContinueCountdownSeconds is < 3 or > 30)
            problems.Add("The continue countdown must be between 3 and 30 seconds.");
        return problems;
    }

    public async Task SaveAsync(GameRules rules)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        await SetAsync(db, nameof(GameRules.MatchSeconds), rules.MatchSeconds.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.WrongAnswerPenalty), rules.WrongAnswerPenalty.ToString());
        await SetAsync(db, nameof(GameRules.LockoutSeconds), rules.LockoutSeconds.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.WrongAnswerPoints), rules.WrongAnswerPoints.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.Format), rules.Format.ToString());
        await SetAsync(db, nameof(GameRules.SoloStartSeconds), rules.SoloStartSeconds.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.SoloSecondsPerKill), rules.SoloSecondsPerKill.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.SoloSecondsPerBoss), rules.SoloSecondsPerBoss.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.SoloMaxSeconds), rules.SoloMaxSeconds.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.SoloContinueSeconds), rules.SoloContinueSeconds.ToString(CultureInfo.InvariantCulture));
        await SetAsync(db, nameof(GameRules.SoloContinueCountdownSeconds), rules.SoloContinueCountdownSeconds.ToString(CultureInfo.InvariantCulture));
        await db.SaveChangesAsync();
    }

    private static async Task SetAsync(TriviaDbContext db, string name, string value)
    {
        var setting = await db.AppSettings.FindAsync(KeyPrefix + name);
        if (setting == null)
            db.AppSettings.Add(new AppSettingEntity { Key = KeyPrefix + name, Value = value });
        else
            setting.Value = value;
    }
}
