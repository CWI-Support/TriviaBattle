using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;

namespace TriviaBattle.Tests.Scoring;

/// <summary>Points for right answers and the two wrong-answer penalties. In every test question, A is correct.</summary>
public class AnswerScoringTests
{
    [Fact]
    public void A_correct_answer_earns_CorrectPoints()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { CorrectPoints = 100 });
        match.StartPlaying();

        match.Press("A1", 0);
        match.Press("A1", 0);

        Assert.Equal(200, match.ScoreOf("A1"));
    }

    // ---------------- Lockout penalty ----------------

    [Fact]
    public void Lockout_a_wrong_answer_scores_nothing_and_freezes_the_buttons()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.Lockout, LockoutSeconds = 2 });
        match.StartPlaying();

        match.Press("A1", 1); // wrong

        Assert.Equal(0, match.ScoreOf("A1"));
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber); // moved on to the next question anyway
        Assert.Empty(match.Press("A1", 0, afterSeconds: 1.5)); // still locked out
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber);
    }

    [Fact]
    public void Lockout_ends_after_LockoutSeconds()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.Lockout, LockoutSeconds = 2 });
        match.StartPlaying();
        match.Press("A1", 1);

        var events = match.Wait(2);

        Assert.Contains(new LockoutEnded("A1"), events);
        Assert.Null(match.StreamOf("A1").LockedOutUntil);
        Assert.Single(match.Press("A1", 0, afterSeconds: 0).OfType<AnswerGiven>());
    }

    [Fact]
    public void Lockout_only_affects_whoever_answered_wrong()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.Lockout });
        match.StartPlaying();
        match.Press("A1", 1);

        Assert.Single(match.Press("A2", 0, afterSeconds: 0).OfType<AnswerGiven>());
    }

    // ---------------- Lose-points penalty ----------------

    [Fact]
    public void LosePoints_a_wrong_answer_takes_points_off_and_play_carries_straight_on()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings
        {
            WrongAnswerPenalty = WrongAnswerPenalty.LosePoints, CorrectPoints = 100, WrongAnswerPoints = 50,
        });
        match.StartPlaying();

        match.Press("A1", 0); // +100
        match.Press("A1", 3); // -50
        var next = match.Press("A1", 0); // no lockout: +100

        Assert.Single(next.OfType<AnswerGiven>());
        Assert.Null(match.StreamOf("A1").LockedOutUntil);
        Assert.Equal(150, match.ScoreOf("A1"));
    }

    [Fact]
    public void LosePoints_scores_can_go_below_zero()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.LosePoints, WrongAnswerPoints = 50 });
        match.StartPlaying();

        match.Press("A1", 2);

        Assert.Equal(-50, match.ScoreOf("A1"));
        Assert.Equal(1, match.Engine.Roster.FindPlayer("A1")!.Tally.WrongAnswers);
    }

    // ---------------- Tie-break ----------------

    [Fact]
    public void Standings_break_score_ties_by_speed()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();

        match.Press("A2", 0, afterSeconds: 1); // answered 1s after the question appeared
        match.Press("A1", 0, afterSeconds: 3); // same points, but 4s after its question appeared

        var standings = match.Engine.GetStandings();
        Assert.Equal("A2", standings[0].Id);
        Assert.Equal(1, standings[0].Rank);
        Assert.Equal(2, standings[1].Rank);
    }
}
