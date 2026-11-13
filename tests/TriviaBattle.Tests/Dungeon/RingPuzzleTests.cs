using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The ring puzzle: a picture cut into rings, each turned out of line. A turns the ring anticlockwise, D clockwise, the
// action button moves to the next ring out. 3, 4, then 5 rings; 12 steps to a full turn. The intro is 3.5 s.
public class RingPuzzleTests
{
    private static TestMatch Match()
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: MinigameKind.RingPuzzle, MinigameAfterRoom: 1)])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)]);
        var settings = new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1 };
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat: the mini game starts
        match.Wait(3.5);                       // past the intro
        return match;
    }

    private static RingPuzzle Puzzle(TestMatch match) => (RingPuzzle)match.AdventurerOf("A1").Minigame!;

    private static MinigamePress Press(TestMatch match, int button) =>
        match.Press("A1", button, afterSeconds: 0.1).OfType<MinigamePlayed>().Single().Result;

    /// <summary>Turns every ring of this round back in line (clockwise), moving out ring by ring; returns the last press.</summary>
    private static MinigamePress SolveTheRound(TestMatch match)
    {
        var puzzle = Puzzle(match);
        match.Wait(Math.Max(0, (puzzle.RoundStartsAt - match.Now).TotalSeconds));
        var last = MinigamePress.Ignored;
        var offsets = puzzle.Offsets.ToArray(); // this round's (the last press starts the next)
        for (var ring = 0; ring < offsets.Length; ring++)
        {
            if (ring > 0)
                Press(match, ButtonPress.Action);
            var turns = (puzzle.Steps - offsets[ring]) % puzzle.Steps;
            for (var i = 0; i < turns; i++)
                last = Press(match, RingPuzzle.TurnRight);
        }
        return last;
    }

    [Fact]
    public void Every_ring_starts_out_of_line_and_on_the_middle()
    {
        var puzzle = Puzzle(Match());

        Assert.Equal((1, 3, 3, 0), (puzzle.Round, puzzle.Rounds, puzzle.Rings, puzzle.Selected));
        Assert.All(puzzle.Offsets, offset => Assert.InRange(offset, 1, puzzle.Steps - 1));
    }

    [Fact]
    public void A_and_D_turn_the_ring_the_action_button_moves_out_and_back_to_the_middle()
    {
        var match = Match();
        var puzzle = Puzzle(match);
        var start = puzzle.Offsets.ToArray();

        Press(match, RingPuzzle.TurnRight);
        Assert.Equal((start[0] + 1) % 12, puzzle.Offsets[0]);
        Press(match, RingPuzzle.TurnLeft);
        Press(match, RingPuzzle.TurnLeft);
        Assert.Equal((start[0] + 11) % 12, puzzle.Offsets[0]);

        Press(match, ButtonPress.Action);
        Press(match, ButtonPress.Action);
        Assert.Equal(2, puzzle.Selected);
        Press(match, RingPuzzle.TurnRight);
        Assert.Equal((start[2] + 1) % 12, puzzle.Offsets[2]);
        Assert.Equal(start[1], puzzle.Offsets[1]); // only the selected ring turns

        Press(match, ButtonPress.Action);
        Assert.Equal(0, puzzle.Selected); // past the outside ring: back to the middle
    }

    [Fact]
    public void B_and_C_do_nothing()
    {
        var match = Match();

        Assert.Empty(match.Press("A1", 1, afterSeconds: 0.1).OfType<MinigamePlayed>());
        Assert.Empty(match.Press("A1", 2, afterSeconds: 0.1).OfType<MinigamePlayed>());
        Assert.Equal(0, Puzzle(match).Presses);
    }

    [Fact]
    public void Solving_a_round_adds_a_ring_and_solving_the_last_wins_it()
    {
        var match = Match();
        var puzzle = Puzzle(match);

        Assert.Equal(MinigamePress.Cleared, SolveTheRound(match));
        Assert.Equal((2, 4, 3), (puzzle.Round, puzzle.Rings, puzzle.SolvedRings));
        // The last ring turns into place, the picture shows whole a second later, then 1.5 s of it before the next.
        Assert.Equal(match.Now + TimeSpan.FromSeconds(1), puzzle.WholeAt);
        Assert.Equal(puzzle.WholeAt + TimeSpan.FromSeconds(1.5), puzzle.RoundStartsAt);
        Assert.Empty(match.Press("A1", RingPuzzle.TurnRight, afterSeconds: 0.1)); // presses wait for it

        Assert.Equal(MinigamePress.Cleared, SolveTheRound(match));
        Assert.Equal(5, puzzle.Rings);
        Assert.Equal(MinigamePress.Won, SolveTheRound(match));
        Assert.True(puzzle.Won);
    }

    [Fact]
    public void A_win_shows_a_second_after_the_last_ring_and_its_sound_waits_for_it()
    {
        var match = Match();
        SolveTheRound(match);
        SolveTheRound(match);
        var puzzle = Puzzle(match);
        match.Wait((puzzle.RoundStartsAt - match.Now).TotalSeconds);
        var offsets = puzzle.Offsets.ToArray();
        var played = new List<MinigamePlayed>();
        for (var ring = 0; ring < offsets.Length; ring++)
        {
            if (ring > 0)
                Press(match, ButtonPress.Action);
            for (var i = 0; i < (puzzle.Steps - offsets[ring]) % puzzle.Steps; i++)
                played.AddRange(match.Press("A1", RingPuzzle.TurnRight, afterSeconds: 0.1).OfType<MinigamePlayed>());
        }

        var won = played.Last();
        Assert.Equal((MinigamePress.Won, 1.0), (won.Result, won.ShowsInSeconds));
        Assert.Equal(puzzle.FinishedAt + TimeSpan.FromSeconds(1), puzzle.ResultFrom);
        Assert.Equal(puzzle.ResultFrom + TimeSpan.FromSeconds(3.2), puzzle.ResultEndsAt);
        Assert.All(played.SkipLast(1), p => Assert.Equal(0, p.ShowsInSeconds)); // turns are heard at once
    }

    [Fact]
    public void Out_of_time_loses_it()
    {
        var match = Match();

        match.Wait(61);

        Assert.False(Puzzle(match).Won);
        Assert.NotNull(Puzzle(match).FinishedAt);
    }

    [Fact]
    public void The_screen_gets_the_rings_and_which_one_turns()
    {
        var match = Match();
        Press(match, ButtonPress.Action);

        var view = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.RingPuzzle!;

        Assert.Equal((3, 12, 1, 1), (view.Rings, view.Steps, view.Selected, view.Presses));
        Assert.Equal(Puzzle(match).Offsets, view.Offsets);
        Assert.Null(view.SolvedRings);
    }
}
