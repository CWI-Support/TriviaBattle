using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Scoring;

namespace TriviaBattle.Tests.Modes;

/// <summary>
/// Scoring behaviour of each game mode. Uses first-correct-buzz scoring with 100 points for
/// every correct answer so the numbers are easy to follow. In every test question, A is correct.
/// </summary>
public class GameModeTests
{
    private static MatchSettings FlatScoring() => new()
    {
        Scoring = new ScoringSettings { Rule = ScoringRuleKind.FirstCorrectBuzz, FirstCorrectPoints = 100, OtherCorrectPoints = 100 },
    };

    // ---------------- Free-for-all ----------------

    [Fact]
    public void FreeForAll_each_player_scores_their_own_correct_answer()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, FlatScoring());
        match.SkipToQuestionOpen();

        match.Press("A1", 0); // right
        match.Press("A2", 1); // wrong
        match.Press("A3", 0); // right
        match.FinishPhase();  // A4 never answers -> reveal

        Assert.Equal(100, match.ScoreOf("A1"));
        Assert.Equal(0, match.ScoreOf("A2"));
        Assert.Equal(100, match.ScoreOf("A3"));
        Assert.Equal(0, match.ScoreOf("A4"));
    }

    [Fact]
    public void FreeForAll_first_press_is_final()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, FlatScoring());
        match.SkipToQuestionOpen();

        match.Press("A1", 1);              // wrong, locked in
        var events = match.Press("A1", 0); // trying to change to the right answer

        Assert.Empty(events);
        Assert.Equal(1, match.Engine.CurrentRound!.Answers["A1"].AnswerIndex);
    }

    [Fact]
    public void FreeForAll_standings_rank_players()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, FlatScoring());
        match.SkipToQuestionOpen();
        match.Press("A2", 0);
        match.FinishPhase();

        var standings = match.Engine.GetStandings();

        Assert.Equal("A2", standings[0].Id);
        Assert.False(standings[0].IsTeam);
        Assert.Equal(4, standings.Count);
    }

    // ---------------- 2v2 Group (shared answer) ----------------

    [Fact]
    public void Group_first_teammate_press_locks_the_team_answer()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, FlatScoring());
        match.SkipToQuestionOpen();

        var first = match.Press("A1", 2);   // Red team: A1 presses C
        var second = match.Press("A3", 0);  // Red teammate A3 tries A, too late

        Assert.Single(first.OfType<AnswerLocked>());
        Assert.Empty(second);

        var redAnswer = match.Engine.CurrentRound!.Answers["red"];
        Assert.Equal(2, redAnswer.AnswerIndex);
        Assert.Equal("A1", redAnswer.PressedByStationId);
    }

    [Fact]
    public void Group_team_scores_as_a_unit_and_presser_gets_credit()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, FlatScoring());
        match.SkipToQuestionOpen();

        match.Press("A3", 0); // Red: correct, pressed by A3
        match.Press("A2", 1); // Blue: wrong
        // Both teams answered -> question closes early.

        Assert.Equal(MatchPhase.Reveal, match.Engine.Phase);
        Assert.Equal(100, match.TeamScore("red"));
        Assert.Equal(0, match.TeamScore("blue"));
        Assert.Equal(100, match.ScoreOf("A3")); // contribution
        Assert.Equal(0, match.ScoreOf("A1"));
    }

    [Fact]
    public void Group_standings_rank_teams()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, FlatScoring());
        match.SkipToQuestionOpen();
        match.Press("A4", 0); // Blue correct
        match.FinishPhase();

        var standings = match.Engine.GetStandings();

        Assert.Equal(2, standings.Count);
        Assert.Equal("blue", standings[0].Id);
        Assert.True(standings[0].IsTeam);
    }

    // ---------------- 2v2 Combined (individual answers, summed) ----------------

    [Fact]
    public void Combined_everyone_answers_and_team_score_is_the_sum()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, FlatScoring());
        match.SkipToQuestionOpen();

        match.Press("A1", 0); // Red, right
        match.Press("A3", 0); // Red, right
        match.Press("A2", 0); // Blue, right
        match.Press("A4", 3); // Blue, wrong

        Assert.Equal(MatchPhase.Reveal, match.Engine.Phase);
        Assert.Equal(200, match.TeamScore("red"));
        Assert.Equal(100, match.TeamScore("blue"));
        Assert.Equal(100, match.ScoreOf("A1"));
        Assert.Equal(0, match.ScoreOf("A4"));
    }

    [Fact]
    public void Combined_teammates_do_not_lock_each_other_out()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, FlatScoring());
        match.SkipToQuestionOpen();

        match.Press("A1", 0);
        var teammate = match.Press("A3", 1);

        Assert.Single(teammate.OfType<AnswerLocked>());
        Assert.Equal(2, match.Engine.CurrentRound!.Answers.Count);
    }
}
