using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;

namespace TriviaBattle.Tests.Matches;

public class MatchFlowTests
{
    [Fact]
    public void Match_starts_in_intro()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        Assert.Equal(MatchPhase.Intro, match.Engine.Phase);
        Assert.Null(match.Engine.CurrentRound);
    }

    [Fact]
    public void Phases_run_in_order_and_end_with_results()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, questionCount: 2);
        var seen = new List<MatchPhase> { match.Engine.Phase };

        while (!match.Engine.IsFinished)
        {
            match.FinishPhase();
            seen.Add(match.Engine.Phase);
        }

        MatchPhase[] expected =
        [
            MatchPhase.Intro,
            MatchPhase.QuestionLeadIn, MatchPhase.QuestionOpen, MatchPhase.Reveal, MatchPhase.Standings,
            MatchPhase.QuestionLeadIn, MatchPhase.QuestionOpen, MatchPhase.Reveal,
            MatchPhase.Results,
            MatchPhase.Finished,
        ];
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void Phase_does_not_advance_before_its_time_is_up()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        var events = match.Wait(match.Settings.IntroSeconds - 0.5);

        Assert.Empty(events);
        Assert.Equal(MatchPhase.Intro, match.Engine.Phase);
    }

    [Fact]
    public void Question_closes_early_once_everyone_has_answered()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();

        match.Press("A1", 0);
        match.Press("A2", 1);
        match.Press("A3", 2);
        Assert.Equal(MatchPhase.QuestionOpen, match.Engine.Phase);

        var events = match.Press("A4", 3);

        Assert.Equal(MatchPhase.Reveal, match.Engine.Phase);
        Assert.Contains(events, e => e is RoundRevealed);
    }

    [Fact]
    public void Question_stays_open_when_early_close_is_turned_off()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { EndQuestionWhenAllAnswered = false });
        match.SkipToQuestionOpen();

        foreach (var station in new[] { "A1", "A2", "A3", "A4" })
            match.Press(station, 0);

        Assert.Equal(MatchPhase.QuestionOpen, match.Engine.Phase);
    }

    [Fact]
    public void Presses_before_the_question_opens_are_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.FinishPhase(); // now in lead-in

        var events = match.Press("A1", 0);

        Assert.Empty(events);
        Assert.Empty(match.Engine.CurrentRound!.Answers);
    }

    [Fact]
    public void Presses_from_stations_not_in_the_match_are_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();

        Assert.Empty(match.Press("B1", 0));
    }

    [Fact]
    public void Abort_finishes_the_match_immediately()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();

        var events = match.Engine.Abort(match.Now);

        Assert.True(match.Engine.IsFinished);
        Assert.True(match.Engine.WasAborted);
        Assert.Contains(events, e => e is MatchFinished { WasAborted: true });
    }

    [Fact]
    public void Version_increases_whenever_state_changes()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        var before = match.Engine.Version;

        match.SkipToQuestionOpen();
        var afterPhases = match.Engine.Version;
        match.Press("A1", 0);

        Assert.True(afterPhases > before);
        Assert.True(match.Engine.Version > afterPhases);
    }
}
