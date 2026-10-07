using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Tests.Data;

public class SqliteQuestionSourceTests : IDisposable
{
    // An in-memory SQLite database lives as long as its connection stays open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<TriviaDbContext> _options;

    public SqliteQuestionSourceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<TriviaDbContext>().UseSqlite(_connection).Options;

        using var db = new TriviaDbContext(_options);
        db.Database.EnsureCreated();

        var category = new CategoryEntity { Name = "Test" };
        AddQuestions(category, Difficulty.Easy, 3);
        AddQuestions(category, Difficulty.Hard, 5);
        category.Questions.Add(NewQuestion(Difficulty.Easy, "Retired question", isActive: false));
        db.Categories.Add(category);
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Fills_shortfall_from_other_difficulties()
    {
        var source = new SqliteQuestionSource(new TestDbFactory(_options));

        var picked = await source.PickQuestionsAsync(categoryId: 1, Difficulty.Easy, count: 5);

        Assert.Equal(5, picked.Count);
        Assert.Equal(3, picked.Count(q => q.Difficulty == Difficulty.Easy)); // all the easy ones...
        Assert.Equal(2, picked.Count(q => q.Difficulty == Difficulty.Hard)); // ...topped up with hard
    }

    [Fact]
    public async Task Never_picks_inactive_questions()
    {
        var source = new SqliteQuestionSource(new TestDbFactory(_options));

        var picked = await source.PickQuestionsAsync(categoryId: 1, Difficulty.Easy, count: 100);

        Assert.Equal(8, picked.Count);
        Assert.DoesNotContain(picked, q => q.Text == "Retired question");
    }

    private static void AddQuestions(CategoryEntity category, Difficulty difficulty, int count)
    {
        for (var i = 1; i <= count; i++)
            category.Questions.Add(NewQuestion(difficulty, $"{difficulty} question {i}"));
    }

    private static QuestionEntity NewQuestion(Difficulty difficulty, string text, bool isActive = true) => new()
    {
        Difficulty = difficulty,
        Text = text,
        AnswerA = "a", AnswerB = "b", AnswerC = "c", AnswerD = "d",
        IsActive = isActive,
    };

    private sealed class TestDbFactory(DbContextOptions<TriviaDbContext> options) : IDbContextFactory<TriviaDbContext>
    {
        public TriviaDbContext CreateDbContext() => new(options);
    }
}
