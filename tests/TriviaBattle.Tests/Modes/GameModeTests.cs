using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;

namespace TriviaBattle.Tests.Modes;

/// <summary>
/// How each game mode shares out question streams and points. 100 points per correct answer.
/// In every test question, A is correct. Teams: Red = A1 + A3, Blue = A2 + A4.
/// </summary>
public class GameModeTests
{
    private static MatchSettings Settings() => new() { CorrectPoints = 100, WrongAnswerPenalty = WrongAnswerPenalty.Lockout };

    // ---------------- Free-for-all ----------------

    [Fact]
    public void FreeForAll_each_player_has_their_own_stream_and_score()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings());
        match.StartPlaying();

        match.Press("A1", 0); // right
        match.Press("A1", 0); // right
        match.Press("A2", 1); // wrong
        match.Press("A3", 0); // right

        Assert.Equal(4, match.Engine.Streams.Count);
        Assert.Equal(200, match.ScoreOf("A1"));
        Assert.Equal(0, match.ScoreOf("A2"));
        Assert.Equal(100, match.ScoreOf("A3"));
        Assert.Equal(0, match.ScoreOf("A4"));
    }

    [Fact]
    public void FreeForAll_standings_rank_players()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, Settings());
        match.StartPlaying();
        match.Press("A2", 0);

        var standings = match.Engine.GetStandings();

        Assert.Equal("A2", standings[0].Id);
        Assert.False(standings[0].IsTeam);
        Assert.Equal(4, standings.Count);
    }

    // ---------------- 2v2 Group (one stream per team) ----------------

    [Fact]
    public void Group_teammates_share_one_stream()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, Settings());
        match.StartPlaying();

        Assert.Equal(2, match.Engine.Streams.Count);
        Assert.Same(match.StreamOf("A1"), match.StreamOf("A3"));

        match.Press("A1", 0);

        Assert.Equal(2, match.StreamOf("A3").CurrentNumber); // the teammate's screen moved on too
        Assert.Equal(1, match.StreamOf("A2").CurrentNumber); // the other team didn't
    }

    [Fact]
    public void Group_a_teammate_pressing_at_the_same_moment_does_not_answer_the_next_question()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, Settings());
        match.StartPlaying();

        match.Press("A1", 0);
        var teammate = match.Press("A3", 0, afterSeconds: 0.05); // pressed together, a moment later

        Assert.Empty(teammate);
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber);
    }

    [Fact]
    public void Group_team_scores_as_a_unit_and_presser_gets_credit()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, Settings());
        match.StartPlaying();

        match.Press("A3", 0); // Red: correct, pressed by A3
        match.Press("A1", 0); // Red: correct, pressed by A1
        match.Press("A2", 1); // Blue: wrong

        Assert.Equal(200, match.TeamScore("red"));
        Assert.Equal(0, match.TeamScore("blue"));
        Assert.Equal(100, match.ScoreOf("A3")); // contributions
        Assert.Equal(100, match.ScoreOf("A1"));
    }

    [Fact]
    public void Group_a_wrong_answer_locks_out_the_whole_team()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, Settings());
        match.StartPlaying();

        match.Press("A1", 2); // wrong

        Assert.Empty(match.Press("A3", 0, afterSeconds: 0.5));
    }

    [Fact]
    public void Group_standings_rank_teams()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer, Settings());
        match.StartPlaying();
        match.Press("A4", 0); // Blue correct

        var standings = match.Engine.GetStandings();

        Assert.Equal(2, standings.Count);
        Assert.Equal("blue", standings[0].Id);
        Assert.True(standings[0].IsTeam);
    }

    // ---------------- 2v2 Combined (own streams, summed) ----------------

    [Fact]
    public void Combined_everyone_has_their_own_stream_and_team_score_is_the_sum()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, Settings());
        match.StartPlaying();

        match.Press("A1", 0); // Red, right
        match.Press("A3", 0); // Red, right
        match.Press("A2", 0); // Blue, right
        match.Press("A4", 3); // Blue, wrong

        Assert.Equal(4, match.Engine.Streams.Count);
        Assert.Equal(200, match.TeamScore("red"));
        Assert.Equal(100, match.TeamScore("blue"));
        Assert.Equal(100, match.ScoreOf("A1"));
        Assert.Equal(0, match.ScoreOf("A4"));
    }

    [Fact]
    public void Combined_a_lockout_does_not_freeze_the_teammate()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore, Settings());
        match.StartPlaying();

        match.Press("A1", 1); // wrong: A1 locked out
        var teammate = match.Press("A3", 0, afterSeconds: 0);

        Assert.Single(teammate.OfType<AnswerGiven>());
    }
}
