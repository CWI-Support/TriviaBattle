using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Output;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Hardware;

/// <summary>Which LEDs light up when. In every TestMatch question, A (index 0) is correct.</summary>
public class RoomOutputCoordinatorTests
{
    private readonly RecordingOutput _output = new();
    private readonly RoomOutputCoordinator _coordinator;

    public RoomOutputCoordinatorTests()
    {
        var stations = new StationLayout(Enumerable.Range(1, 4).Select(n => new Station($"A{n}", "A", n, $"Player {n}", "#fff")));
        var buttons = new ButtonMap(
            from n in Enumerable.Range(1, 4)
            from answer in Enumerable.Range(0, 4)
            select new ButtonAssignment(10 + (n - 1) * 4 + answer, $"A{n}", answer));
        _coordinator = new RoomOutputCoordinator([_output], buttons, stations);
    }

    [Fact]
    public void Question_open_lights_every_button_then_only_the_chosen_one()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();
        _coordinator.MatchChanged(match.Engine, [new PhaseChanged(MatchPhase.QuestionOpen)]);

        Assert.All(Enumerable.Range(10, 16), id => Assert.Equal(LedState.On, _output.Leds[id]));
        Assert.Equal(LightingCue.Question, _output.Cues["A"]);

        _coordinator.MatchChanged(match.Engine, match.Press("A1", 2)); // Player 1 presses C (button 12)

        Assert.Equal([LedState.Off, LedState.Off, LedState.On, LedState.Off], Leds(10, 11, 12, 13));
        Assert.Equal(LedState.On, _output.Leds[14]); // Player 2 hasn't answered yet
    }

    [Fact]
    public void In_group_mode_a_teammates_lock_in_shows_on_both_stations()
    {
        var match = new TestMatch(GameModeKind.TeamSharedAnswer); // Red = A1 + A3
        match.SkipToQuestionOpen();

        _coordinator.MatchChanged(match.Engine, match.Press("A1", 1));

        Assert.Equal([LedState.Off, LedState.On, LedState.Off, LedState.Off], Leds(18, 19, 20, 21)); // A3's buttons
    }

    [Fact]
    public void Reveal_blinks_the_correct_button()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();
        _coordinator.MatchChanged(match.Engine, match.FinishPhase());

        Assert.Equal([LedState.Blink, LedState.Off, LedState.Off, LedState.Off], Leds(10, 11, 12, 13));
        Assert.Equal(LightingCue.Reveal, _output.Cues["A"]);
    }

    [Fact]
    public void Match_end_turns_everything_off_and_idles_the_room()
    {
        var match = new TestMatch(GameModeKind.FreeForAll);
        match.SkipToQuestionOpen();

        _coordinator.MatchChanged(match.Engine, match.Engine.Abort(match.Now));

        Assert.All(_output.Leds.Values, led => Assert.Equal(LedState.Off, led));
        Assert.Equal(LightingCue.Idle, _output.Cues["A"]);
    }

    private LedState[] Leds(params int[] buttonIds) => buttonIds.Select(id => _output.Leds[id]).ToArray();

    private sealed class RecordingOutput : IRoomOutput
    {
        public Dictionary<int, LedState> Leds { get; } = new();
        public Dictionary<string, LightingCue> Cues { get; } = new();

        public void SetButtonLeds(IReadOnlyDictionary<int, LedState> leds)
        {
            foreach (var (id, state) in leds) Leds[id] = state;
        }

        public void SetLightingCue(string roomId, LightingCue cue) => Cues[roomId] = cue;
    }
}
