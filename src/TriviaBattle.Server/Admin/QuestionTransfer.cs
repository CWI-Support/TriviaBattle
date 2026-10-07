using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Server.Admin;

/// <summary>One question as it appears in an import/export file.</summary>
/// <param name="Correct">"A", "B", "C" or "D".</param>
/// <param name="Line">Where it came from in the file (for error messages); 0 if not from a file.</param>
public sealed record QuestionRow(
    string Category,
    string Difficulty,
    string Text,
    IReadOnlyList<string> Answers,
    string Correct,
    string? MediaUrl,
    bool Active,
    int Line);

public sealed record ImportResult(int Added, int Skipped, IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>
/// Importing and exporting question packs, as CSV (for spreadsheets) or JSON (same shape as
/// Data/Seed/questions.json). Import is all-or-nothing: if any row has a problem, nothing is
/// added and every problem is reported with its line number, so the file can be fixed and re-imported.
/// Rows whose question already exists in that category are skipped, so re-importing a file is safe.
/// </summary>
public class QuestionTransfer(TriviaDbContext db)
{
    // ------------------------------------------------------------------ Import

    public async Task<ImportResult> ImportAsync(IReadOnlyList<QuestionRow> rows)
    {
        var problems = new List<string>();
        foreach (var row in rows)
        {
            var where = row.Line > 0 ? $"Line {row.Line}" : $"\"{row.Text}\"";
            if (string.IsNullOrWhiteSpace(row.Category))
                problems.Add($"{where}: no category.");
            if (!Enum.TryParse<Difficulty>(row.Difficulty, ignoreCase: true, out _))
                problems.Add($"{where}: difficulty must be Easy, Medium or Hard (got \"{row.Difficulty}\").");

            var correctIndex = TryLetterToIndex(row.Correct);
            if (correctIndex == null)
                problems.Add($"{where}: correct answer must be A, B, C or D (got \"{row.Correct}\").");

            problems.AddRange(QuestionRules.Validate(row.Text, row.Answers, correctIndex ?? 0, row.MediaUrl)
                .Select(p => $"{where}: {p}"));
        }

        if (problems.Count > 0)
            return new ImportResult(0, 0, problems);

        var categories = await db.Categories.Include(c => c.Questions).ToListAsync();
        int added = 0, skipped = 0;

        foreach (var row in rows)
        {
            var categoryName = row.Category.Trim();
            var category = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                category = new CategoryEntity { Name = categoryName };
                categories.Add(category);
                db.Categories.Add(category);
            }

            var text = row.Text.Trim();
            if (category.Questions.Any(q => string.Equals(q.Text, text, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            category.Questions.Add(new QuestionEntity
            {
                Difficulty = Enum.Parse<Difficulty>(row.Difficulty, ignoreCase: true),
                Text = text,
                AnswerA = row.Answers[0].Trim(),
                AnswerB = row.Answers[1].Trim(),
                AnswerC = row.Answers[2].Trim(),
                AnswerD = row.Answers[3].Trim(),
                CorrectIndex = TryLetterToIndex(row.Correct)!.Value,
                MediaUrl = string.IsNullOrWhiteSpace(row.MediaUrl) ? null : row.MediaUrl.Trim(),
                IsActive = row.Active,
            });
            added++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(added, skipped, []);
    }

    // ------------------------------------------------------------------ CSV

    // Column names in the CSV header. MediaUrl and Active are optional.
    private sealed class CsvQuestion
    {
        public string Category { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public string Question { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string C { get; set; } = "";
        public string D { get; set; } = "";
        public string Correct { get; set; } = "";
        public string? MediaUrl { get; set; }
        public string? Active { get; set; }
    }

    private static readonly CsvConfiguration CsvSettings = new(CultureInfo.InvariantCulture)
    {
        PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
        MissingFieldFound = null, // optional columns may be absent
        HeaderValidated = null,
    };

    public static List<QuestionRow> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CsvSettings);

        var rows = new List<QuestionRow>();
        foreach (var r in csv.GetRecords<CsvQuestion>())
        {
            var line = csv.Context.Parser!.RawRow;
            rows.Add(new QuestionRow(r.Category, r.Difficulty, r.Question, [r.A, r.B, r.C, r.D], r.Correct, r.MediaUrl, ParseActive(r.Active), line));
        }
        return rows;
    }

    public async Task<byte[]> ExportCsvAsync()
    {
        var rows = await LoadAllAsync();
        using var writer = new StringWriter();
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows.Select(r => new CsvQuestion
            {
                Category = r.Category,
                Difficulty = r.Difficulty,
                Question = r.Text,
                A = r.Answers[0], B = r.Answers[1], C = r.Answers[2], D = r.Answers[3],
                Correct = r.Correct,
                MediaUrl = r.MediaUrl,
                Active = r.Active ? "yes" : "no",
            }));
        }
        // UTF-8 with a byte-order mark so Excel shows accented characters correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(writer.ToString())).ToArray();
    }

    // ------------------------------------------------------------------ JSON (same shape as Data/Seed/questions.json)

    private sealed record JsonFile(List<JsonCategory> Categories);
    private sealed record JsonCategory(string Name, List<JsonQuestion> Questions);
    private sealed record JsonQuestion(string Difficulty, string Text, List<string> Answers, string Correct, string? MediaUrl = null, bool? Active = null);

    private static readonly JsonSerializerOptions JsonSettings = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<QuestionRow> ParseJson(string json)
    {
        var file = JsonSerializer.Deserialize<JsonFile>(json, JsonSettings)
            ?? throw new InvalidOperationException("The file is empty.");

        return file.Categories
            .SelectMany(c => c.Questions.Select(q =>
                new QuestionRow(c.Name, q.Difficulty, q.Text, q.Answers ?? [], q.Correct, q.MediaUrl, q.Active ?? true, Line: 0)))
            .ToList();
    }

    public async Task<string> ExportJsonAsync()
    {
        var rows = await LoadAllAsync();
        var file = new JsonFile(rows
            .GroupBy(r => r.Category)
            .Select(g => new JsonCategory(g.Key, g.Select(r =>
                new JsonQuestion(r.Difficulty, r.Text, r.Answers.ToList(), r.Correct, r.MediaUrl, r.Active ? null : false)).ToList()))
            .ToList());

        return JsonSerializer.Serialize(file, JsonSettings);
    }

    // ------------------------------------------------------------------ Helpers

    private async Task<List<QuestionRow>> LoadAllAsync()
    {
        var questions = await db.Questions
            .Include(q => q.Category)
            .OrderBy(q => q.Category!.Name).ThenBy(q => q.Difficulty).ThenBy(q => q.Id)
            .ToListAsync();

        return questions.Select(q => new QuestionRow(
            q.Category!.Name,
            q.Difficulty.ToString(),
            q.Text,
            [q.AnswerA, q.AnswerB, q.AnswerC, q.AnswerD],
            ButtonMap.AnswerIndexToLetter(q.CorrectIndex),
            q.MediaUrl,
            q.IsActive,
            Line: 0)).ToList();
    }

    private static int? TryLetterToIndex(string? letter)
    {
        var trimmed = letter?.Trim().ToUpperInvariant();
        return trimmed is { Length: 1 } && trimmed[0] >= 'A' && trimmed[0] <= 'D' ? trimmed[0] - 'A' : null;
    }

    /// <summary>Blank counts as active, so a simple CSV can leave the column out.</summary>
    private static bool ParseActive(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1" or "active";
}
