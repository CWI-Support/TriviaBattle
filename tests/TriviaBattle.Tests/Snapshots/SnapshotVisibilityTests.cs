using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Snapshots;

/// <summary>What each screen may and may not see. Teams in TestMatch: Red = A1+A3, Blue = A2+A4.</summary>
public class SnapshotVisibilityTests
{
    [Fact]
    public void Correct_answer_is_hidden_until_reveal_except_from_admin()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();

        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Main).Question!.CorrectIndex);
        Assert.Null(SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).Question!.CorrectIndex);
        Assert.Equal(0, SnapshotBuilder.Build(match.Engine, Viewer.Admin).Question!.CorrectIndex);

        match.FinishPhase(); // reveal

        Assert.Equal(0, SnapshotBuilder.Build(match.Engine, Viewer.Main).Question!.CorrectIndex);
    }

    [Fact]
    public void Main_screen_sees_who_answered_but_not_what_until_reveal()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();
        match.Press("A2", 3);

        var before = SnapshotBuilder.Build(match.Engine, Viewer.Main).Players.Single(p => p.StationId == "A2");
        Assert.True(before.HasAnswered);
        Assert.Null(before.Answer);

        match.FinishPhase();

        var after = SnapshotBuilder.Build(match.Engine, Viewer.Main).Players.Single(p => p.StationId == "A2");
        Assert.Equal(3, after.Answer!.Index);
        Assert.False(after.Result!.IsCorrect);
    }

    [Fact]
    public void Player_sees_own_answer_but_not_opponents()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();
        match.Press("A1", 1);
        match.Press("A2", 2);

        var snapshot = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1"));

        Assert.Equal(1, snapshot.You!.Answer!.Index);
        Assert.Null(snapshot.Players.Single(p => p.StationId == "A2").Answer);
    }

    [Fact]
    public void Group_mode_teammate_sees_locked_team_answer_and_who_pressed_it()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer);
        match.SkipToQuestionOpen();
        match.Press("A1", 2); // Ann locks Red's answer

        var teammate = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3"));
        Assert.Equal(2, teammate.You!.Answer!.Index);
        Assert.Equal("Ann", teammate.You.Answer.PressedByName);

        var opponent = SnapshotBuilder.Build(match.Engine, Viewer.Player("A2"));
        Assert.Null(opponent.You!.Answer);
        Assert.True(opponent.Teams.Single(t => t.Id == "red").HasAnswered);
        Assert.Null(opponent.Teams.Single(t => t.Id == "red").Answer);
    }

    [Fact]
    public void Combined_mode_teammates_do_not_see_each_others_answers_before_reveal()
    {
        var match = new TestMatch(GameModeKind.TeamCombinedScore);
        match.SkipToQuestionOpen();
        match.Press("A1", 2);

        var teammate = SnapshotBuilder.Build(match.Engine, Viewer.Player("A3"));

        Assert.Null(teammate.You!.Answer);
        Assert.Null(teammate.Players.Single(p => p.StationId == "A1").Answer);
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
