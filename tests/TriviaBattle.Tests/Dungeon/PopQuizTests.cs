using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The pop quiz: questions about the player's own run (their floor, the monster they just killed, their name...), four
// answers each. 3 right wins, 3 wrong loses; the right answer shows for 1.4 s after each before the next question. Never
// a match's first mini game. The intro is 3.5 s.
public class PopQuizTests
{
    /// <summary>A match on the pop quiz (after floor 1's first room), past its intro. Four kinds of monster, so there's a choice of names.</summary>
    private static TestMatch Match()
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: MinigameKind.PopQuiz, MinigameAfterRoom: 1)])],
            [
                new MonsterDef("rat", "Cellar Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("slime", "Damp Slime", "slime", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("bat", "Bat Swarm", "bat", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("goblin", "Goblin Bandit", "goblin", Hp: 20, Damage: 10, AttackSeconds: 10),
            ]);
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon, LockoutSeconds = 1 }, stations: ["A1"], dungeon: map);
        match.StartPlaying();
        match.Press("A1", 0, afterSeconds: 4); // the rat: the mini game starts
        match.Wait(3.5);                       // past the intro
        return match;
    }

    private static PopQuiz QuizOf(TestMatch match) => (PopQuiz)match.AdventurerOf("A1").Minigame!;

    /// <summary>Answers the question up (right or wrong), once the last answer's reveal is over.</summary>
    private static IReadOnlyList<MatchEvent> Answer(TestMatch match, bool right)
    {
        var quiz = QuizOf(match);
        var wait = Math.Max(0.5, (quiz.AskedAt - match.Now).TotalSeconds + 0.5);
        var correct = quiz.Current.CorrectIndex;
        return match.Press("A1", right ? correct : (correct + 1) % 4, afterSeconds: wait);
    }

    private static PopQuizQuestion On(PopQuiz quiz, PopQuizTopic topic) => quiz.Questions.Single(q => q.Topic == topic);

    private static string RightAnswer(PopQuizQuestion question) => question.Answers[question.CorrectIndex];

    [Fact]
    public void Its_questions_are_about_the_run_so_far()
    {
        var quiz = QuizOf(Match());

        Assert.Equal("1", RightAnswer(On(quiz, PopQuizTopic.Floor)));
        Assert.Equal("Cellar Rat", RightAnswer(On(quiz, PopQuizTopic.LastMonster)));
        Assert.Equal("Ann", RightAnswer(On(quiz, PopQuizTopic.Name)));
        Assert.Equal("Ranger", RightAnswer(On(quiz, PopQuizTopic.Hero)));
        Assert.Equal("1", RightAnswer(On(quiz, PopQuizTopic.Kills)));
        Assert.Equal("3", RightAnswer(On(quiz, PopQuizTopic.Lives)));
        Assert.Equal("Right", RightAnswer(On(quiz, PopQuizTopic.LastAnswer))); // the test questions' right answer
        Assert.DoesNotContain(quiz.Questions, q => q.Topic == PopQuizTopic.LastBoss); // floor 1: no boss beaten yet
        Assert.All(quiz.Questions, q => Assert.Equal(4, q.Answers.Distinct().Count()));
    }

    [Fact]
    public void The_wrong_answers_are_the_same_kind_of_thing()
    {
        var quiz = QuizOf(Match());

        Assert.All(On(quiz, PopQuizTopic.LastMonster).Answers, a => Assert.Contains(a, new[] { "Cellar Rat", "Damp Slime", "Bat Swarm", "Goblin Bandit" }));
        Assert.All(On(quiz, PopQuizTopic.Floor).Answers, a => Assert.True(int.TryParse(a, out var floor) && floor >= 1));
        Assert.All(On(quiz, PopQuizTopic.Hero).Answers, a => Assert.True(Enum.TryParse<HeroClass>(a, out _)));
    }

    [Fact]
    public void Three_right_wins_it()
    {
        var match = Match();
        Answer(match, right: true);
        Answer(match, right: false);
        Answer(match, right: true);

        var events = Answer(match, right: true);

        var quiz = QuizOf(match);
        Assert.True(quiz.Won);
        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
    }

    [Fact]
    public void Three_wrong_loses_it()
    {
        var match = Match();
        Answer(match, right: false);
        Answer(match, right: true);
        Answer(match, right: false);

        Answer(match, right: false);

        var quiz = QuizOf(match);
        Assert.NotNull(quiz.FinishedAt);
        Assert.False(quiz.Won);
    }

    [Fact]
    public void After_an_answer_the_next_question_waits_for_the_right_one_to_show()
    {
        var match = Match();
        var first = QuizOf(match).Current;
        Answer(match, right: false);
        var quiz = QuizOf(match);
        var answeredAt = match.Now;

        match.Press("A1", 0, afterSeconds: 0.5); // too soon: the reveal's still up

        Assert.Equal((0, 1), (quiz.Right, quiz.Wrong));
        Assert.NotSame(first, quiz.Current);
        Assert.Equal(answeredAt + TimeSpan.FromSeconds(1.4), quiz.AskedAt);
    }

    [Fact]
    public void The_action_button_does_nothing()
    {
        var match = Match();

        match.Press("A1", ButtonPress.Action, afterSeconds: 0.5);

        Assert.Equal((0, 0), (QuizOf(match).Right, QuizOf(match).Wrong));
    }

    [Fact]
    public void The_screen_gets_the_question_up_and_only_learns_the_right_answer_once_it_is_answered()
    {
        var match = Match();
        var quiz = QuizOf(match);

        var before = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.PopQuiz!;
        Assert.Equal((quiz.Current.Topic, 1), (before.Topic, before.Number));
        Assert.Equal(quiz.Current.Answers, before.Answers);
        Assert.Null(before.Last);

        var asked = quiz.Current;
        Answer(match, right: false);

        var after = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.PopQuiz!;
        Assert.Equal((asked.Topic, asked.CorrectIndex, false, 2), (after.Last!.Topic, after.Last.Correct, after.Last.WasRight, after.Number));
    }

    [Fact]
    public void It_asks_about_the_boss_beaten_on_the_floor_above_and_skips_what_it_cannot_mix_up()
    {
        var facts = new PopQuizFacts("Ann", [], ["Sir A", "Lady B", "Dame C"], Floor: 3, LastMonster: "Rat", MonsterNames: ["Rat"],
            "Tank", Enum.GetNames<HeroClass>(), Kills: 9, Lives: null, MaxLives: 0, LastQuestion: null,
            LastBoss: "Goblin King", BossNames: ["Goblin King", "Lich", "Dragon", "Troll Chief"]);

        var quiz = new PopQuiz(TestMatch.Start, new MinigameSettings(), new Random(1), facts);

        Assert.Equal("Goblin King", RightAnswer(On(quiz, PopQuizTopic.LastBoss)));
        Assert.DoesNotContain(quiz.Questions, q => q.Topic == PopQuizTopic.LastMonster); // only one monster name to choose from
        Assert.DoesNotContain(quiz.Questions, q => q.Topic is PopQuizTopic.Lives or PopQuizTopic.LastAnswer);
    }

    [Fact]
    public void It_is_never_a_match_s_first_mini_game_but_fair_game_after()
    {
        var floors = Enumerable.Range(0, 5).Select(_ => new FloorDef(["rat", "rat", "rat"])).ToList();
        var firsts = new List<MinigameKind>();
        var laters = new List<MinigameKind>();
        for (var seed = 0; seed < 300; seed++)
        {
            var map = new DungeonMap([new DepthDef("d", "Depth", floors)], [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)],
                minigameChance: 1, minigameSeed: seed);
            firsts.Add(map.MinigameOn(1)!.Value.Kind);
            laters.AddRange(Enumerable.Range(2, 4).Select(f => map.MinigameOn(f)!.Value.Kind));
        }

        Assert.DoesNotContain(MinigameKind.PopQuiz, firsts);
        Assert.Contains(MinigameKind.PopQuiz, laters);
    }

    [Fact]
    public void A_floor_without_one_does_not_count_as_the_first()
    {
        // Floor 1 can't hold one (a single room), so floor 2's is the match's first.
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(["rat"]), new FloorDef(["rat", "rat"])])],
            [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)], minigameChance: 1);

        var kinds = Enumerable.Range(0, 300)
            .Select(seed => new DungeonMap(map.Depths, [new MonsterDef("rat", "Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10)], minigameChance: 1, minigameSeed: seed)
                .MinigameOn(2)!.Value.Kind)
            .ToList();

        Assert.Null(map.MinigameOn(1));
        Assert.DoesNotContain(MinigameKind.PopQuiz, kinds);
    }
}
