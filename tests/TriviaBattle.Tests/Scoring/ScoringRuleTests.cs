using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Scoring;

namespace TriviaBattle.Tests.Scoring;

public class ScoringRuleTests
{
    // ---------------- Time remaining ----------------

    private static MatchSettings TimeRemaining() => new()
    {
        AnswerSeconds = 10,
        EndQuestionWhenAllAnswered = false,
        Scoring = new ScoringSettings { Rule = ScoringRuleKind.TimeRemaining, MaxPoints = 1000, MinCorrectPoints = 500 },
    };

    [Fact]
    public void TimeRemaining_instant_answer_gets_max_points()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, TimeRemaining());
        match.SkipToQuestionOpen();

        match.Press("A1", 0);
        match.FinishPhase();

        Assert.Equal(1000, match.ScoreOf("A1"));
    }

    [Fact]
    public void TimeRemaining_halfway_answer_gets_halfway_points()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, TimeRemaining());
        match.SkipToQuestionOpen();

        match.Wait(5);
        match.Press("A1", 0);
        match.FinishPhase();

        Assert.Equal(750, match.ScoreOf("A1"));
    }

    [Fact]
    public void TimeRemaining_wrong_answer_gets_nothing()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, TimeRemaining());
        match.SkipToQuestionOpen();

        match.Press("A1", 3);
        match.FinishPhase();

        Assert.Equal(0, match.ScoreOf("A1"));
    }

    // ---------------- First correct buzz ----------------

    private static MatchSettings FirstCorrect() => new()
    {
        Scoring = new ScoringSettings { Rule = ScoringRuleKind.FirstCorrectBuzz, FirstCorrectPoints = 1000, OtherCorrectPoints = 0 },
    };

    [Fact]
    public void FirstCorrectBuzz_only_the_first_correct_answer_scores()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, FirstCorrect());
        match.SkipToQuestionOpen();

        match.Press("A2", 1); // first press, but wrong
        match.Press("A3", 0); // first CORRECT press
        match.Press("A1", 0); // correct, but later
        match.FinishPhase();

        Assert.Equal(1000, match.ScoreOf("A3"));
        Assert.Equal(0, match.ScoreOf("A1"));
        Assert.Equal(0, match.ScoreOf("A2"));
    }

    [Fact]
    public void FirstCorrectBuzz_later_correct_answers_still_count_as_correct()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, FirstCorrect());
        match.SkipToQuestionOpen();

        match.Press("A3", 0);
        match.Press("A1", 0);
        match.FinishPhase();

        Assert.Equal(1, match.Engine.Roster.FindPlayer("A1")!.Tally.CorrectAnswers);
    }

    // ---------------- Tie-break ----------------

    [Fact]
    public void Standings_break_score_ties_by_speed()
    {
        var settings = new MatchSettings
        {
            EndQuestionWhenAllAnswered = false,
            Scoring = new ScoringSettings { Rule = ScoringRuleKind.FirstCorrectBuzz, FirstCorrectPoints = 100, OtherCorrectPoints = 100 },
        };
        var match = new TestMatch(GameModeKind.FreeForAll, settings);
        match.SkipToQuestionOpen();

        match.Wait(1);
        match.Press("A2", 0); // answered after 1s
        match.Wait(3);
        match.Press("A1", 0); // same points, but answered after 4s
        match.FinishPhase();

        var standings = match.Engine.GetStandings();
        Assert.Equal("A2", standings[0].Id);
        Assert.Equal(1, standings[0].Rank);
        Assert.Equal(2, standings[1].Rank);
    }
}
