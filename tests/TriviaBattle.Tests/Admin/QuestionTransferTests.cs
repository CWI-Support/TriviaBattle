using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Admin;
using TriviaBattle.Server.Data;

namespace TriviaBattle.Tests.Admin;

public class QuestionTransferTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TriviaDbContext _db;

    public QuestionTransferTests()
    {
        _connection.Open();
        _db = new TriviaDbContext(new DbContextOptionsBuilder<TriviaDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private const string GoodCsv =
        """
        Category,Difficulty,Question,A,B,C,D,Correct
        Space,Easy,"Which planet has rings, famously?",Mars,Saturn,Venus,Mercury,B
        Space,Hard,What is the closest star to the Sun?,Sirius,Vega,Proxima Centauri,Polaris,C
        """;

    [Fact]
    public async Task Csv_import_creates_categories_and_questions()
    {
        var rows = QuestionTransfer.ParseCsv(Stream(GoodCsv));
        var result = await new QuestionTransfer(_db).ImportAsync(rows);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Added);
        var saturn = await _db.Questions.SingleAsync(q => q.Text.Contains("rings"));
        Assert.Equal("Which planet has rings, famously?", saturn.Text); // quoted comma survived
        Assert.Equal(1, saturn.CorrectIndex);
        Assert.True(saturn.IsActive); // no Active column = active
    }

    [Fact]
    public async Task Import_is_all_or_nothing_and_reports_line_numbers()
    {
        var csv = GoodCsv + "\nSpace,Impossible,Bad row,a,b,c,d,E";

        var result = await new QuestionTransfer(_db).ImportAsync(QuestionTransfer.ParseCsv(Stream(csv)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.StartsWith("Line 4") && p.Contains("difficulty"));
        Assert.Contains(result.Problems, p => p.StartsWith("Line 4") && p.Contains("correct answer"));
        Assert.Equal(0, await _db.Questions.CountAsync()); // the good rows weren't added either
    }

    [Fact]
    public async Task Reimporting_the_same_file_skips_existing_questions()
    {
        var transfer = new QuestionTransfer(_db);
        await transfer.ImportAsync(QuestionTransfer.ParseCsv(Stream(GoodCsv)));

        var again = await transfer.ImportAsync(QuestionTransfer.ParseCsv(Stream(GoodCsv)));

        Assert.Equal(0, again.Added);
        Assert.Equal(2, again.Skipped);
    }

    [Fact]
    public async Task Csv_export_can_be_imported_back()
    {
        var transfer = new QuestionTransfer(_db);
        await transfer.ImportAsync(QuestionTransfer.ParseCsv(Stream(GoodCsv)));
        (await _db.Questions.FirstAsync()).IsActive = false;
        await _db.SaveChangesAsync();

        var exported = await transfer.ExportCsvAsync();
        var rows = QuestionTransfer.ParseCsv(new MemoryStream(exported));

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => !r.Active);
        Assert.All(rows, r => Assert.Equal(4, r.Answers.Count));
    }

    [Fact]
    public async Task Json_export_round_trips_through_the_seed_format()
    {
        var transfer = new QuestionTransfer(_db);
        await transfer.ImportAsync(QuestionTransfer.ParseCsv(Stream(GoodCsv)));

        var rows = QuestionTransfer.ParseJson(await transfer.ExportJsonAsync());

        Assert.Equal(2, rows.Count);
        Assert.Equal("C", rows.Single(r => r.Text.Contains("closest star")).Correct);
    }

    [Fact]
    public async Task Shipped_seed_questions_all_import_cleanly()
    {
        var seedFolder = Path.Combine(RepoRoot(), "src", "TriviaBattle.Server", "Data", "Seed");
        var rows = Directory.GetFiles(seedFolder, "*.json")
            .SelectMany(file => QuestionTransfer.ParseJson(File.ReadAllText(file)))
            .ToList();

        var result = await new QuestionTransfer(_db).ImportAsync(rows);

        Assert.True(result.Succeeded, string.Join("\n", result.Problems));
        Assert.Equal(0, result.Skipped); // no duplicate questions within a category

        // Every category has enough of each difficulty for a 10-question match without borrowing.
        foreach (var category in rows.GroupBy(r => r.Category))
            foreach (var difficulty in new[] { "Easy", "Medium", "Hard" })
                Assert.True(category.Count(r => r.Difficulty == difficulty) >= 10, $"{category.Key} has fewer than 10 {difficulty} questions");
    }

    [Theory]
    [InlineData("", "Question text is empty")]
    [InlineData("Ok?", "Two answers are the same", "a", "A", "c", "d")]
    public void Rules_catch_common_mistakes(string text, string expected, params string[] answers)
    {
        var problems = QuestionRules.Validate(text, answers.Length == 4 ? answers : ["a", "b", "c", "d"], 0, null);

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
