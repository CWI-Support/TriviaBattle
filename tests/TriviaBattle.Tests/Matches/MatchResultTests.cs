using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;

namespace TriviaBattle.Tests.Matches;

public class MatchResultTests
{
    [Fact]
    public void Result_captures_scores_ranks_and_every_answer()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, questionCount: 2);
        match.SkipToQuestionOpen();
        match.Press("A1", 0); // Red, right
        match.FinishPhase();   // reveal; others never answered

        var result = MatchResult.From(match.Engine, match.Now);

        Assert.Equal("TeamCombinedScore", result.Mode);
        Assert.Equal(1, result.QuestionsPlayed);
        Assert.Equal(4, result.Answers.Count); // one row per player for the revealed question
        Assert.Equal(1, result.Teams.Single(t => t.TeamId == "red").Rank);
        Assert.True(result.Participants.Single(p => p.StationId == "A1").Score > 0);
        Assert.All(result.Participants, p => Assert.Null(p.Rank)); // team modes rank teams, not players
    }

    [Fact]
    public void Free_for_all_result_ranks_players()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, questionCount: 1);
        match.SkipToQuestionOpen();
        match.Press("A3", 0);
        match.FinishPhase();

        var result = MatchResult.From(match.Engine, match.Now);

        Assert.Equal(1, result.Participants.Single(p => p.StationId == "A3").Rank);
        Assert.Empty(result.Teams);
    }
}
