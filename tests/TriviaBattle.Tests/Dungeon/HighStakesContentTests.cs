using System.Text.Json;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Dungeon;

// High Stakes' own question bank: config/high-stakes.json, loaded at startup (HighStakesContent).
public class HighStakesContentTests
{
    [Fact]
    public void The_shipped_bank_loads_with_its_answers_right_first()
    {
        var bank = HighStakesContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server"));

        Assert.True(bank.All.Count >= 30, $"only {bank.All.Count} questions");
        Assert.All(bank.All, q => Assert.Equal(0, q.CorrectIndex)); // shuffled when one's asked
        Assert.All(bank.All, q => Assert.True(q.Id < 0)); // never mistaken for a database question
        Assert.Equal(bank.All.Count, bank.All.Select(q => q.Text).Distinct().Count());
    }

    [Fact]
    public void The_shipped_bank_s_categories_are_the_seeded_ones_so_matches_ask_their_own()
    {
        var root = RepoRoot();
        var bank = HighStakesContent.Load(Path.Combine(root, "src", "TriviaBattle.Server"));
        var seeded = Directory.GetFiles(Path.Combine(root, "src", "TriviaBattle.Server", "Data", "Seed"), "*.json")
            .SelectMany(file => JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("categories").EnumerateArray())
            .Select(category => category.GetProperty("name").GetString())
            .ToHashSet();

        Assert.All(bank.All, q => Assert.Contains(q.Category, seeded));
    }

    [Fact]
    public void A_bank_question_that_breaks_the_editor_s_rules_stops_the_server()
    {
        var dir = Directory.CreateTempSubdirectory("high-stakes");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "config"));
            File.WriteAllText(Path.Combine(dir.FullName, HighStakesContent.FilePath),
                """{ "Questions": [ { "Category": "Music", "Text": "Who?", "Right": "Me", "Wrong": ["Me", "You"] } ] }""");

            var error = Assert.Throws<InvalidOperationException>(() => HighStakesContent.Load(dir.FullName));

            Assert.Contains("question 1", error.Message);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void No_bank_file_means_no_bank()
    {
        var dir = Directory.CreateTempSubdirectory("high-stakes");
        try
        {
            Assert.Empty(HighStakesContent.Load(dir.FullName).All);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the repo root.");
    }
}
