using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Tests.Matches;

public class MatchFlowTests
{
    [Fact]
    public void Match_starts_in_intro()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        Assert.Equal(MatchPhase.Intro, match.Engine.Phase);
        Assert.All(match.Engine.Streams, stream => Assert.Null(stream.CurrentShownAt));
    }

    [Fact]
    public void Phases_run_in_order_and_end_with_results()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        var seen = new List<MatchPhase> { match.Engine.Phase };

        while (!match.Engine.IsFinished)
        {
            match.FinishPhase();
            seen.Add(match.Engine.Phase);
        }

        Assert.Equal([MatchPhase.Intro, MatchPhase.Playing, MatchPhase.Results, MatchPhase.Finished], seen);
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
    public void The_clock_runs_for_MatchSeconds_however_many_questions_are_answered()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { MatchSeconds = 30 });
        match.StartPlaying();

        for (var i = 0; i < 5; i++)
            match.Press("A1", 0);

        Assert.Equal(MatchPhase.Playing, match.Engine.Phase);
        Assert.Equal(match.Engine.PhaseStartedAt.AddSeconds(30), match.Engine.PhaseEndsAt);
    }

    [Fact]
    public void Answering_moves_only_that_player_to_their_next_question()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        var firstQuestion = match.StreamOf("A1").CurrentQuestion;

        var events = match.Press("A1", 0);

        Assert.Single(events.OfType<AnswerGiven>());
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber);
        Assert.NotEqual(firstQuestion, match.StreamOf("A1").CurrentQuestion);
        Assert.Equal(match.Now, match.StreamOf("A1").CurrentShownAt);
        Assert.Equal(1, match.StreamOf("A2").CurrentNumber); // nobody else moved
    }

    [Fact]
    public void Response_time_is_measured_from_when_the_question_appeared()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();

        match.Press("A1", 0, afterSeconds: 2.5);
        var answer = (AnswerGiven)match.Press("A1", 0, afterSeconds: 1.5).Single();

        Assert.Equal(2, answer.Answer.Number);
        Assert.Equal(TimeSpan.FromSeconds(1.5), answer.Answer.ResponseTime);
    }

    [Fact]
    public void Presses_before_the_clock_starts_are_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        Assert.Empty(match.Press("A1", 0));
        Assert.Empty(match.StreamOf("A1").Answers);
    }

    [Fact]
    public void Presses_after_the_clock_runs_out_are_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        match.FinishPhase(); // Results

        Assert.Empty(match.Press("A1", 0));
    }

    [Fact]
    public void A_press_right_after_a_new_question_appears_is_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        match.Press("A1", 0);

        var tooSoon = match.Press("A1", 0, afterSeconds: match.Settings.NewQuestionGraceSeconds / 2);

        Assert.Empty(tooSoon);
        Assert.Equal(2, match.StreamOf("A1").CurrentNumber);
    }

    [Fact]
    public void Presses_from_stations_not_in_the_match_are_ignored()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();

        Assert.Empty(match.Press("B1", 0));
    }

    [Fact]
    public void Questions_start_again_from_the_top_when_someone_gets_through_them_all()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, questionCount: 2);
        match.StartPlaying();
        var first = match.StreamOf("A1").CurrentQuestion;

        match.Press("A1", 0);
        match.Press("A1", 0);

        Assert.Equal(3, match.StreamOf("A1").CurrentNumber);
        Assert.Equal(first, match.StreamOf("A1").CurrentQuestion);
    }

    [Fact]
    public void Each_player_gets_their_own_question_order()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, questionCount: 20);
        match.StartPlaying();

        var orderOf = (string station) => Enumerable.Range(0, 20).Select(_ =>
        {
            var id = match.StreamOf(station).CurrentQuestion.Id;
            match.Press(station, 0);
            return id;
        }).ToList();

        var a1 = orderOf("A1");
        var a2 = orderOf("A2");
        Assert.Equal(20, a1.Distinct().Count()); // every question once
        Assert.NotEqual(a1, a2);
    }

    [Fact]
    public void The_chosen_difficulty_comes_before_the_others()
    {
        // TestMatch chooses Easy. Mix in some Hard questions.
        var questions = TestMatch.MakeQuestions(3, Difficulty.Hard, firstId: 1)
            .Concat(TestMatch.MakeQuestions(3, Difficulty.Easy, firstId: 4))
            .ToList();
        var match = new TestMatch(GameModeKind.FreeForAll, questions: questions);
        match.StartPlaying();

        var difficulties = Enumerable.Range(0, 6).Select(_ =>
        {
            var difficulty = match.StreamOf("A1").CurrentQuestion.Difficulty;
            match.Press("A1", 0);
            return difficulty;
        }).ToList();

        Assert.Equal([Difficulty.Easy, Difficulty.Easy, Difficulty.Easy, Difficulty.Hard, Difficulty.Hard, Difficulty.Hard], difficulties);
    }

    [Fact]
    public void Abort_finishes_the_match_immediately()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();

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

        match.StartPlaying();
        var afterPhases = match.Engine.Version;
        match.Press("A1", 0);

        Assert.True(afterPhases > before);
        Assert.True(match.Engine.Version > afterPhases);
    }
}
