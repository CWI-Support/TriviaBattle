using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The High Stakes room's own question bank (<see cref="HighStakesRoom"/>): brutal questions, harder than the hardest in
/// the question database, kept apart from it so the fight never asks them. The server loads them from
/// config/high-stakes.json at startup (HighStakesContent). Each has a category; a match asks the ones in its own
/// categories when there are any (<see cref="For"/>), and any of them when not.
/// </summary>
public sealed class HighStakesQuestions(IReadOnlyList<Question> questions)
{
    /// <summary>No bank: High Stakes asks a hard question from the player's own stream instead (the tests' default).</summary>
    public static readonly HighStakesQuestions None = new([]);

    public IReadOnlyList<Question> All { get; } = questions;

    /// <summary>
    /// The ones for a match in these categories (the names its questions carry): those in them if there are any,
    /// otherwise all of them (a category with none of its own still gets a brutal question, from another).
    /// </summary>
    public IReadOnlyList<Question> For(IEnumerable<string> categories)
    {
        var wanted = categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = All.Where(q => wanted.Contains(q.Category)).ToList();
        return mine.Count > 0 ? mine : All;
    }
}
