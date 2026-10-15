using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;

namespace TriviaBattle.Tests.Matches;

public class MatchResultTests
{
    [Fact]
    public void Result_captures_scores_ranks_and_every_answer()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.LosePoints });
        match.StartPlaying();
        match.Press("A1", 0); // Red, right
        match.Press("A1", 0); // Red, right
        match.Press("A2", 3); // Blue, wrong
        match.FinishPhase();   // Results

        var result = MatchResult.From(match.Engine, match.Now);

        Assert.Equal("TeamCombinedScore", result.Mode);
        Assert.Equal(3, result.QuestionsPlayed);
        Assert.Equal([1, 2], result.Answers.Where(a => a.OwnerId == "A1").Select(a => a.RoundNumber));
        Assert.True(result.Answers.Single(a => a.OwnerId == "A2").Points < 0);
        Assert.Equal(1, result.Teams.Single(t => t.TeamId == "red").Rank);
        Assert.Equal(200, result.Participants.Single(p => p.StationId == "A1").Score);
        Assert.All(result.Participants, p => Assert.Null(p.Rank)); // team modes rank teams, not players
    }

    [Fact]
    public void Free_for_all_result_ranks_players()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        match.Press("A3", 0);
        match.FinishPhase();

        var result = MatchResult.From(match.Engine, match.Now);

        Assert.Equal(1, result.Participants.Single(p => p.StationId == "A3").Rank);
        Assert.Empty(result.Teams);
    }
}
