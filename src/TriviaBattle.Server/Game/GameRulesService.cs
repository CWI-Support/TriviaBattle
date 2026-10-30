using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Server.Game;

/// <summary>The game rules the operator can change on the admin page (Operator tab).</summary>
/// <param name="Format">Classic quiz or Dungeon Quiz Quest.</param>
public sealed record GameRules(
    double MatchSeconds,
    WrongAnswerPenalty WrongAnswerPenalty,
    double LockoutSeconds,
    int WrongAnswerPoints,
    GameFormat Format);

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

        return new GameRules(
            double.TryParse(Saved(nameof(GameRules.MatchSeconds)), CultureInfo.InvariantCulture, out var seconds) ? seconds : defaults.MatchSeconds,
            Enum.TryParse<WrongAnswerPenalty>(Saved(nameof(GameRules.WrongAnswerPenalty)), out var penalty) ? penalty : defaults.WrongAnswerPenalty,
            double.TryParse(Saved(nameof(GameRules.LockoutSeconds)), CultureInfo.InvariantCulture, out var lockout) ? lockout : defaults.LockoutSeconds,
            int.TryParse(Saved(nameof(GameRules.WrongAnswerPoints)), CultureInfo.InvariantCulture, out var points) ? points : defaults.WrongAnswerPoints,
            Enum.TryParse<GameFormat>(Saved(nameof(GameRules.Format)), out var format) ? format : defaults.Format);
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
