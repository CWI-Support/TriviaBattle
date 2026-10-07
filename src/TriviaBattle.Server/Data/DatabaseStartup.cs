using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Admin;

namespace TriviaBattle.Server.Data;

/// <summary>Runs once when the server starts: brings the database schema up to date and seeds starter questions.</summary>
public static class DatabaseStartup
{
    public static async Task InitializeAsync(IServiceProvider services, string contentRoot)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TriviaDbContext>>();

        await db.Database.MigrateAsync();

        // WAL lets screens read (leaderboards, admin) while a match result is being written.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        if (!await db.Categories.AnyAsync())
        {
            // Data/Seed has one .json file per category, in the same format as an admin JSON export,
            // so they go through the same importer. Only used for a brand-new (empty) database;
            // after that, manage questions on the admin page (or import these files there).
            var seedFolder = Path.Combine(contentRoot, "Data", "Seed");
            var rows = new List<QuestionRow>();
            foreach (var file in Directory.GetFiles(seedFolder, "*.json").Order())
                rows.AddRange(QuestionTransfer.ParseJson(await File.ReadAllTextAsync(file)));

            var result = await new QuestionTransfer(db).ImportAsync(rows);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Seed questions in {seedFolder} have problems: {string.Join(" ", result.Problems)}");

            logger.LogInformation("Seeded {Count} starter questions from {Folder}", result.Added, seedFolder);
        }
    }
}
