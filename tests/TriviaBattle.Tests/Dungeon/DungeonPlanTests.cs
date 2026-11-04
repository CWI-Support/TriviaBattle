using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Tests.Dungeon;

// Floors made up fresh for each match (DungeonPlan), and the questions each floor asks (FloorDef.Questions).
public class DungeonPlanTests
{
    private static readonly MonsterDef[] Monsters =
    [
        new("rat", "Rat", "rat", Hp: 20, Damage: 5, AttackSeconds: 10),
        new("slime", "Slime", "slime", Hp: 30, Damage: 5, AttackSeconds: 10),
        new("ogre", "Ogre", "ogre", Hp: 60, Damage: 10, AttackSeconds: 10),
        new("king", "Rat King", "rat", Hp: 100, Damage: 15, AttackSeconds: 10, IsBoss: true),
    ];

    private static readonly FloorDef MadeUp = new(RoomCount: [2, 3], RoomHp: 50, Pool: ["rat", "slime", "ogre"], Boss: "king",
        Questions: new Dictionary<Difficulty, int> { [Difficulty.Hard] = 1 });

    private static DungeonPlan Plan(params FloorDef[] floors) => new([new DepthDef("d", "Depth", floors)], Monsters);

    [Fact]
    public void A_made_up_floor_has_its_rooms_then_the_boss()
    {
        var plan = Plan(MadeUp);

        for (var seed = 0; seed < 50; seed++)
        {
            var map = plan.Build(new Random(seed));
            var rooms = Enumerable.Range(1, map.RoomsOn(1)).Select(room => map.RoomAt(1, room)).ToList();

            Assert.InRange(rooms.Count, 3, 4); // 2 or 3 rooms, then the boss
            Assert.Equal("Rat King", Assert.Single(rooms[^1]).Monster.Name);
            foreach (var room in rooms.SkipLast(1))
            {
                Assert.InRange(room.Count, 1, 3);
                var hp = room.Sum(e => e.Monster.Hp);
                Assert.True(room.Count == 1 || hp <= 50 * 1.25, $"a room of {hp} HP is well over its 50"); // only a single monster may be over
                Assert.DoesNotContain(room, e => e.Monster.IsBoss);
            }
        }
    }

    [Fact]
    public void Each_match_gets_its_own_layout_but_one_layout_is_the_same_for_everyone_in_it()
    {
        var plan = Plan(MadeUp);
        string Layout(DungeonMap map) => string.Join(" | ", Enumerable.Range(1, map.RoomsOn(1))
            .Select(room => string.Join(",", map.RoomAt(1, room).Select(e => e.Monster.Id))));

        var layouts = Enumerable.Range(0, 20).Select(seed => Layout(plan.Build(new Random(seed)))).ToList();

        Assert.True(layouts.Distinct().Count() > 1);                             // matches differ
        Assert.Equal(Layout(plan.Build(new Random(7))), Layout(plan.Build(new Random(7)))); // one layout is one layout
    }

    [Fact]
    public void Every_party_in_a_match_fights_the_same_rooms()
    {
        var map = Plan(MadeUp).Build(new Random(3));
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1", "A2"],
            dungeon: map, questions: TestMatch.MakeQuestions(10, Difficulty.Hard));

        Assert.Equal(match.DelveOf("A1").Foes.Select(f => f.Monster.Id), match.DelveOf("A2").Foes.Select(f => f.Monster.Id));
    }

    [Fact]
    public void Mistakes_in_a_made_up_floor_are_spelled_out()
    {
        var error = Assert.Throws<ArgumentException>(() => Plan(MadeUp with { Pool = ["rat", "ghost"], RoomCount = [3, 2] }));

        Assert.Contains("ghost", error.Message);
        Assert.Contains("RoomCount", error.Message);
    }

    [Fact]
    public void A_made_up_floor_must_be_laid_out_before_it_makes_a_map()
    {
        var error = Assert.Throws<ArgumentException>(() => new DungeonMap([new DepthDef("d", "Depth", [MadeUp])], Monsters));

        Assert.Contains("DungeonPlan.Build", error.Message);
    }

    // ---------------------------------------------------------------- Each floor's questions

    /// <summary>Floor 1 asks only easy questions, floor 2 only hard ones. One rat a floor (20 HP: one easy hit).</summary>
    private static readonly DungeonMap Ramp = new(
        [new DepthDef("d", "Depth", [
            new FloorDef(["rat"], Questions: new Dictionary<Difficulty, int> { [Difficulty.Easy] = 1 }),
            new FloorDef(["rat"], Questions: new Dictionary<Difficulty, int> { [Difficulty.Hard] = 1 })])],
        Monsters);

    private static IReadOnlyList<Question> MixedQuestions() =>
        [.. TestMatch.MakeQuestions(10, Difficulty.Hard, firstId: 1), .. TestMatch.MakeQuestions(10, Difficulty.Easy, firstId: 100)];

    [Fact]
    public void Each_floor_asks_its_own_mix_of_questions()
    {
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, stations: ["A1"],
            dungeon: Ramp, questions: MixedQuestions()); // the hard ones come first in the pool
        match.StartPlaying();
        Assert.Equal(Difficulty.Easy, match.StreamOf("A1").CurrentQuestion.Difficulty); // floor 1: easy, even so

        match.Press("A1", 0, afterSeconds: 4); // the rat dies: floor 2

        Assert.Equal(2, match.DelveOf("A1").Floor);
        Assert.Equal(Difficulty.Hard, match.StreamOf("A1").CurrentQuestion.Difficulty);
    }

    [Fact]
    public void A_stream_falls_back_on_any_question_when_none_of_the_wanted_difficulty_is_left()
    {
        var stream = new QuestionStream("A1", TestMatch.MakeQuestions(2, Difficulty.Easy));
        stream.Start(TestMatch.Start);

        stream.Record(Answer(stream), TestMatch.Start, wanted: Difficulty.Hard);

        Assert.Equal(2, stream.CurrentQuestion.Id); // no hard ones: the next in order
        stream.Record(Answer(stream), TestMatch.Start, wanted: Difficulty.Easy);
        Assert.Equal(1, stream.CurrentQuestion.Id); // all asked: it starts over
    }

    private static GivenAnswer Answer(QuestionStream stream) =>
        new(stream.OwnerId, stream.CurrentNumber, stream.CurrentQuestion, "A1", 0, true, 20, TestMatch.Start, TimeSpan.FromSeconds(1));
}
