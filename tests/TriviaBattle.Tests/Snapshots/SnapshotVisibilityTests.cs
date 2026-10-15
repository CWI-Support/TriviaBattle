using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Snapshots;

/// <summary>What each screen may and may not see. Teams in TestMatch: Red = A1+A3, Blue = A2+A4.</summary>
public class SnapshotVisibilityTests
{
    [Fact]
    public void Player_sees_their_question_only_once_the_clock_is_running()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Question);

        match.StartPlaying();
        var you = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;

        Assert.Equal(match.StreamOf("A1").CurrentQuestion.Text, you.Question!.Text);
        Assert.Equal(1, you.Question.Number);
        Assert.Equal(match.Now.ToUnixTimeMilliseconds(), you.Question.ShownAtMs);
    }

    [Fact]
    public void Only_admin_is_told_the_answers_to_questions_still_on_screen()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();

        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Main).Streams);
        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).Streams);

        var admin = SnapshotBuilder.Build(match.Engine, Viewer.Admin).Streams!;
        Assert.Equal(4, admin.Count);
        Assert.All(admin, stream => Assert.Equal("Right", stream.CorrectAnswer));
    }

    [Fact]
    public void After_answering_a_player_sees_what_was_right()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        match.Press("A1", 2); // wrong

        var last = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.LastAnswer!;

        Assert.False(last.IsCorrect);
        Assert.Equal(2, last.ChosenIndex);
        Assert.Equal(0, last.CorrectIndex);
        Assert.Equal("Right", last.CorrectAnswer);
    }

    [Fact]
    public void Main_screen_sees_right_or_wrong_and_scores_but_not_questions()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.StartPlaying();
        match.Press("A2", 0);

        var main = SnapshotBuilder.Build(match.Engine, Viewer.Main);
        var bob = main.Players.Single(p => p.StationId == "A2");

        Assert.Null(main.You);
        Assert.True(bob.LastAnswer!.IsCorrect);
        Assert.Equal(1, bob.CorrectAnswers);
        Assert.Null(main.Players.Single(p => p.StationId == "A1").LastAnswer);
    }

    [Fact]
    public void Group_mode_teammates_see_the_team_question_and_who_answered()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer);
        match.StartPlaying();
        match.Press("A1", 0); // Ann answers for Red

        var ann = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!;
        var cat = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3")).You!;

        Assert.Equal(ann.Question!.Id, cat.Question!.Id);
        Assert.Equal(2, cat.Question.Number);
        Assert.Equal("Ann", cat.LastAnswer!.PressedByName);

        // Team answers show on the team, not the players.
        var main = SnapshotBuilder.Build(match.Engine, Viewer.Main);
        Assert.NotNull(main.Teams.Single(t => t.Id == "red").LastAnswer);
        Assert.Null(main.Players.Single(p => p.StationId == "A1").LastAnswer);
    }

    [Fact]
    public void Lockouts_are_shown_with_their_end_time()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { WrongAnswerPenalty = WrongAnswerPenalty.Lockout, LockoutSeconds = 2 });
        match.StartPlaying();
        match.Press("A1", 3);

        var expected = match.Now.AddSeconds(2).ToUnixTimeMilliseconds();
        Assert.Equal(expected, SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.LockedOutUntilMs);
        Assert.Equal(expected, SnapshotBuilder.Build(match.Engine, Viewer.Main).Players.Single(p => p.StationId == "A1").LockedOutUntilMs);
    }

    [Fact]
    public void Only_player_screens_get_a_you_section()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Main).You);
        Assert.NotNull(SnapshotBuilder.Build(match.Engine, Viewer.Player("A4")).You);
    }

    [Fact]
    public void Phase_end_time_is_sent_as_unix_milliseconds()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);

        var snapshot = SnapshotBuilder.Build(match.Engine, Viewer.Main);

        Assert.Equal(MatchPhase.Intro, snapshot.Phase);
        Assert.Equal(match.Engine.PhaseEndsAt.ToUnixTimeMilliseconds(), snapshot.PhaseEndsAtMs);
    }
}
