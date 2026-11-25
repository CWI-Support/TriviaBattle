using System.Text.Json;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Admin;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Loads High Stakes' own question bank from config/high-stakes.json (see <see cref="HighStakesQuestions"/>): brutal
/// questions kept apart from the question database, each { Category, Text, Right, Wrong: [three] }. Read once at startup,
/// checked by the admin editor's rules (<see cref="QuestionRules"/>): a broken file stops the server with the problem
/// spelled out. No file: High Stakes asks a hard question from the player's own stream instead.
/// </summary>
public static class HighStakesContent
{
    public const string FilePath = "config/high-stakes.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static HighStakesQuestions Load(string contentRoot)
    {
        var path = Path.Combine(contentRoot, FilePath);
        if (!File.Exists(path))
            return HighStakesQuestions.None;
        var file = JsonSerializer.Deserialize<BankFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException($"{FilePath} is empty.");

        var questions = new List<Question>();
        var problems = new List<string>();
        foreach (var (entry, i) in (file.Questions ?? []).Select((q, i) => (q, i)))
        {
            // The right answer first; they're shuffled each time one's asked (DungeonRun).
            var answers = new List<string?> { entry.Right };
            answers.AddRange(entry.Wrong ?? []);
            var wrong = QuestionRules.Validate(entry.Text, answers, 0, null);
            if (string.IsNullOrWhiteSpace(entry.Category))
                wrong.Add("It has no Category.");
            if (wrong.Count > 0)
            {
                problems.Add($"question {i + 1} (\"{entry.Text}\"): {string.Join(" ", wrong)}");
                continue;
            }
            questions.Add(new Question(-(i + 1), entry.Category!.Trim(), Difficulty.Hard, entry.Text!.Trim(),
                answers.Select(a => a!.Trim()).ToList(), CorrectIndex: 0));
        }

        if (problems.Count > 0)
            throw new InvalidOperationException($"{FilePath}: {string.Join("; ", problems)}");
        return new HighStakesQuestions(questions);
    }

    private sealed record BankFile(List<BankQuestion>? Questions);

    private sealed record BankQuestion(string? Category, string? Text, string? Right, List<string>? Wrong);
}
