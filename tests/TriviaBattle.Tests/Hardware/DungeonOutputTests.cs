using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Output;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Hardware;

/// <summary>
/// The room's lights in Dungeon format: answer LEDs during a battle beat, the action button's LED, the
/// boss cue. Uses TestMatch.Dungeon: floor 1 = two rats (40 HP), floor 2 = a boss.
/// </summary>
public class DungeonOutputTests
{
    private readonly RecordingOutput _output = new();

    private (TestMatch Match, RoomOutputCoordinator Coordinator) Solo(bool battleBeats = true)
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, MatchSeconds = 300 },
            stations: ["A1"], battleBeats: battleBeats);
        var stations = new StationLayout(Enumerable.Range(1, 4).Select(n => new Station($"A{n}", "A", n, $"Player {n}", "#fff")));
        var buttons = new ButtonMap(
            (from n in Enumerable.Range(1, 4)
             from answer in Enumerable.Range(0, 4)
             select new ButtonAssignment(10 + (n - 1) * 4 + answer, $"A{n}", answer))
            .Concat(Enumerable.Range(1, 4).Select(n => new ButtonAssignment(25 + n, $"A{n}", ButtonPress.Action))));
        var coordinator = new RoomOutputCoordinator([_output], buttons, stations, new MatchClock(match));
        match.StartPlaying();
        coordinator.MatchChanged(match.Engine, [new PhaseChanged(MatchPhase.Playing)], [match.Engine]);
        return (match, coordinator);
    }

    [Fact]
    public void During_a_battle_beat_the_answer_buttons_go_dark_until_the_next_question_shows()
    {
        var (match, coordinator) = Solo();
        Assert.Equal(LedState.On, _output.Leds[10]);

        coordinator.MatchChanged(match.Engine, match.Press("A1", 0, afterSeconds: 4), [match.Engine]); // a hit: a 1.5 s beat
        Assert.All(Leds(10, 11, 12, 13), led => Assert.Equal(LedState.Off, led));

        var events = match.Wait(2);
        Assert.Contains(events, e => e is QuestionShown { OwnerId: "A1" });
        coordinator.MatchChanged(match.Engine, events, [match.Engine]);
        Assert.All(Leds(10, 11, 12, 13), led => Assert.Equal(LedState.On, led));
    }

    [Fact]
    public void The_action_button_lights_while_they_hold_a_power_up()
    {
        var (match, coordinator) = Solo();
        Assert.Equal(LedState.Off, _output.Leds[26]);

        coordinator.MatchChanged(match.Engine, match.Engine.GivePowerUp("A1", PowerUp.Shield), [match.Engine]);
        Assert.Equal(LedState.On, _output.Leds[26]);

        coordinator.MatchChanged(match.Engine, match.Press("A1", ButtonPress.Action, afterSeconds: 0.5), [match.Engine]);
        Assert.Equal(LedState.Off, _output.Leds[26]);
    }

    [Fact]
    public void The_room_takes_the_boss_cue_while_anyone_fights_a_boss()
    {
        var (match, coordinator) = Solo(battleBeats: false);
        Assert.Equal(LightingCue.Question, _output.Cues["A"]);

        while (!match.DelveOf("A1").Encounter.Monster.IsBoss)
            coordinator.MatchChanged(match.Engine, match.Press("A1", 0, afterSeconds: 4), [match.Engine]);

        Assert.Equal(LightingCue.Boss, _output.Cues["A"]);
    }

    private LedState[] Leds(params int[] ids) => ids.Select(id => _output.Leds[id]).ToArray();

    /// <summary>The test match's clock, so "is the beat still playing" is asked at the match's time.</summary>
    private sealed class MatchClock(TestMatch match) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => match.Now;
    }

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
