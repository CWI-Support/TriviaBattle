using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Server.Game;

namespace TriviaBattle.Tests.Dungeon;

// Q.U.E.R.I.E. at the stations: after an answer he may have something to say (DungeonRun.Commentate). Ordinary moments
// only now and then, with a gap per player; big ones (a miss streak, the last life, the odd one out) whenever they happen;
// the party hears the ones about the party. Lines come from config/querie.json.
public class QuerieTests
{
    /// <summary>One line per moment, "Moment: {name}", so a test can tell which one he picked.</summary>
    private static readonly QuerieLines Lines = new(Enum.GetValues<QuerieMoment>()
        .ToDictionary(m => m, m => (IReadOnlyList<QuerieLine>)[new QuerieLine($"{m}: {{name}}")]));

    /// <summary>One room: a troll with HP to last all test, hitting for <paramref name="damage"/>.</summary>
    private static DungeonMap Map(int damage) => new(
        [new DepthDef("d", "Depth", [new FloorDef(["troll"])])],
        [new MonsterDef("troll", "Troll", "goblin", Hp: 100_000, Damage: damage, AttackSeconds: 10)]);

    /// <summary>
    /// A solo game (or a party of four) with him talking. <paramref name="chance"/>: how often an ordinary moment gets a
    /// remark (1 = always, 0 = only big moments); <paramref name="gap"/>: the seconds between ordinary remarks to a player.
    /// </summary>
    private static TestMatch Match(double chance = 1, double gap = 0, int damage = 1, int lives = 3, bool party = false, QuerieLines? lines = null)
    {
        var settings = new MatchSettings
        {
            Format = GameFormat.Dungeon,
            LockoutSeconds = 1,
            Dungeon =
            {
                AdventurerHp = 100, PartyHpPerMember = 0, Lives = lives, DownedSeconds = 5, PartyDownedSeconds = 20,
                Querie = { Chance = chance, SafeChance = chance, QuipEverySeconds = gap, BigGapSeconds = 0 },
            },
        };
        var match = party
            ? new TestMatch(GameModeKind.TeamCombinedScore, settings, dungeon: Map(damage), querie: lines ?? Lines,
                teams: [new TeamSetup("side1", "Ann's team", ["A1", "A2", "A3", "A4"])])
            : new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: Map(damage), querie: lines ?? Lines);
        match.StartPlaying();
        return match;
    }

    private static Quip? QuipIn(IReadOnlyList<MatchEvent> events) =>
        events.OfType<QuerieSpoke>().SingleOrDefault()?.Quip;

    [Fact]
    public void Without_lines_he_keeps_quiet()
    {
        var match = Match(lines: QuerieLines.None);

        var events = match.Press("A1", 1, afterSeconds: 1.5);

        Assert.Null(QuipIn(events));
        Assert.Null(match.AdventurerOf("A1").LastQuip);
    }

    [Fact]
    public void A_wrong_answer_gets_a_remark_with_their_name_in_it()
    {
        var match = Match();

        var quip = QuipIn(match.Press("A1", 1, afterSeconds: 1.5));

        Assert.NotNull(quip);
        Assert.Equal((QuerieMoment.Wrong, "Wrong: Ann", false), (quip.Moment, quip.Text, quip.PartyWide));
        Assert.Same(quip, match.AdventurerOf("A1").LastQuip);
    }

    [Fact]
    public void A_slow_answer_or_the_hourglass_running_out_is_slow()
    {
        var match = Match();
        Assert.Equal(QuerieMoment.Slow, QuipIn(match.Press("A1", 0, afterSeconds: 6))!.Moment); // right, but slow

        Assert.Equal(QuerieMoment.Slow, QuipIn(match.Wait(10.1))!.Moment); // the hourglass ran out and the troll hit
    }

    [Fact]
    public void Ordinary_moments_wait_their_turn()
    {
        var match = Match(gap: 11);
        Assert.NotNull(QuipIn(match.Press("A1", 1, afterSeconds: 1.5)));

        Assert.Null(QuipIn(match.Press("A1", 0, afterSeconds: 1.5))); // a right answer right after: nothing to say yet
    }

    [Fact]
    public void An_ordinary_moment_only_gets_a_remark_now_and_then()
    {
        var match = Match(chance: 0);

        Assert.Null(QuipIn(match.Press("A1", 1, afterSeconds: 1.5)));
        Assert.Null(QuipIn(match.Press("A1", 0, afterSeconds: 1.5)));
    }

    [Fact]
    public void A_third_miss_in_a_row_is_a_big_moment_that_skips_the_wait()
    {
        var match = Match(gap: 11);
        match.Press("A1", 1, afterSeconds: 1.5);
        Assert.Null(QuipIn(match.Press("A1", 1, afterSeconds: 1.5))); // the second: still his wait

        var third = QuipIn(match.Press("A1", 1, afterSeconds: 1.5));

        Assert.Equal(QuerieMoment.WrongStreak, third!.Moment);
    }

    [Fact]
    public void Down_to_the_last_life_always_gets_a_remark()
    {
        var match = Match(chance: 0, damage: 100, lives: 2);

        var quip = QuipIn(match.Press("A1", 1, afterSeconds: 1.5)); // downed: one life left

        Assert.Equal(QuerieMoment.LastLife, quip!.Moment);
    }

    [Fact]
    public void Right_answers_get_more_excited_the_closer_they_are_to_death()
    {
        var match = Match(damage: 40);
        Assert.Equal(QuerieMoment.RightSafe, QuipIn(match.Press("A1", 0, afterSeconds: 1.5))!.Moment);
        match.Press("A1", 1, afterSeconds: 1.5);
        match.Press("A1", 1, afterSeconds: 1.5); // 20 HP of 100 left

        Assert.Equal(QuerieMoment.RightDoomed, QuipIn(match.Press("A1", 0, afterSeconds: 1.5))!.Moment);
    }

    [Fact]
    public void The_one_who_misses_while_the_rest_of_the_party_got_theirs_gets_called_out_in_front_of_everyone()
    {
        var match = Match(chance: 0, party: true);
        match.Press("A1", 0, afterSeconds: 1.5);
        match.Press("A2", 0, afterSeconds: 0.5);
        match.Press("A3", 0, afterSeconds: 0.5);

        var quip = QuipIn(match.Press("A4", 1, afterSeconds: 0.5));

        Assert.NotNull(quip);
        Assert.Equal((QuerieMoment.OddOneOut, "OddOneOut: Dan", true), (quip.Moment, quip.Text, quip.PartyWide));
        foreach (var station in new[] { "A1", "A2", "A3", "A4" })
            Assert.Same(quip, match.AdventurerOf(station).LastQuip);
        Assert.False(SnapshotBuilder.Build(match.Engine, Viewer.Player("A2")).You!.Quip!.AboutYou);
        var dans = SnapshotBuilder.Build(match.Engine, Viewer.Player("A4")).You!.Quip!;
        Assert.Equal(("OddOneOut: Dan", true), (dans.Text, dans.AboutYou));
    }

    [Fact]
    public void The_one_right_answer_after_two_misses_is_carrying_the_party()
    {
        var match = Match(party: true);
        match.Press("A1", 1, afterSeconds: 1.5);
        match.Press("A2", 1, afterSeconds: 0.5);

        var quip = QuipIn(match.Press("A3", 0, afterSeconds: 0.5));

        Assert.Equal((QuerieMoment.Carrying, true), (quip!.Moment, quip.PartyWide));
        Assert.Same(quip, match.AdventurerOf("A1").LastQuip);
    }

    [Fact]
    public void In_a_solo_game_a_miss_is_never_the_odd_one_out()
    {
        var match = Match();

        Assert.Equal(QuerieMoment.Wrong, QuipIn(match.Press("A1", 1, afterSeconds: 1.5))!.Moment);
    }

    [Fact]
    public void A_remark_about_someone_else_goes_quiet_on_the_screen_after_the_others_heard_it()
    {
        var match = Match(chance: 0, party: true);
        match.Press("A1", 0, afterSeconds: 1.5);
        match.Press("A2", 0, afterSeconds: 0.5);
        match.Press("A3", 0, afterSeconds: 0.5);
        var callout = QuipIn(match.Press("A4", 1, afterSeconds: 0.5))!;

        // Too soon for another party-wide one: the next odd one out is just for them.
        match.Press("A4", 0, afterSeconds: 1.5);
        match.Press("A1", 0, afterSeconds: 0.1);
        match.Press("A3", 0, afterSeconds: 0.1);
        var quip = QuipIn(match.Press("A2", 1, afterSeconds: 0.1));

        Assert.Equal((QuerieMoment.OddOneOut, false), (quip!.Moment, quip.PartyWide));
        Assert.Same(callout, match.AdventurerOf("A1").LastQuip);
        Assert.Same(quip, match.AdventurerOf("A2").LastQuip);
    }

    // ---------- In the mini games ----------

    /// <summary>
    /// A solo match whose floor has a mini game after its first room (a rat, 20 HP: one right answer kills it). He only
    /// talks in the mini game here unless a test says so: no chance for ordinary moments, none for openings.
    /// </summary>
    private static TestMatch MinigameMatch(MinigameKind kind, double chance = 0, double startChance = 0,
        Action<MinigameSettings>? tune = null, QuerieLines? lines = null)
    {
        var map = new DungeonMap(
            [new DepthDef("d", "Depth", [new FloorDef(Rooms: [["rat"], ["rat"]], Minigame: kind, MinigameAfterRoom: 1)])],
            [
                new MonsterDef("rat", "Cellar Rat", "rat", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("slime", "Damp Slime", "slime", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("bat", "Bat Swarm", "bat", Hp: 20, Damage: 10, AttackSeconds: 10),
                new MonsterDef("goblin", "Goblin Bandit", "goblin", Hp: 20, Damage: 10, AttackSeconds: 10),
            ]);
        var settings = new MatchSettings
        {
            Format = GameFormat.Dungeon,
            LockoutSeconds = 1,
            Dungeon = { Querie = { Chance = chance, SafeChance = 0, MinigameStartChance = startChance, QuipEverySeconds = 0, BigGapSeconds = 0 } },
        };
        tune?.Invoke(settings.Dungeon.Minigames);
        var match = new TestMatch(GameModeKind.FreeForAll, settings, stations: ["A1"], dungeon: map, querie: lines ?? Lines);
        match.StartPlaying();
        return match;
    }

    /// <summary><see cref="Lines"/>, with some moments' lines replaced.</summary>
    private static QuerieLines LinesWith(params (QuerieMoment Moment, string Text)[] replaced) => new(Enum.GetValues<QuerieMoment>()
        .ToDictionary(m => m, m => (IReadOnlyList<QuerieLine>)[new QuerieLine(replaced.Where(r => r.Moment == m).Select(r => r.Text).FirstOrDefault() ?? $"{m}: {{name}}")]));

    /// <summary>Kills the rat (the mini game starts) and waits out the intro.</summary>
    private static Minigame IntoTheMinigame(TestMatch match)
    {
        match.Press("A1", 0, afterSeconds: 4);
        var game = match.AdventurerOf("A1").Minigame!;
        match.Wait((game.PlaysFrom - match.Now).TotalSeconds);
        return game;
    }

    /// <summary>Answers the pop quiz's question up, once the last answer's reveal is over.</summary>
    private static IReadOnlyList<MatchEvent> AnswerQuiz(TestMatch match, PopQuiz quiz, bool right)
    {
        var correct = quiz.Current.CorrectIndex;
        return match.Press("A1", right ? correct : (correct + 1) % 4, afterSeconds: Math.Max(0.5, (quiz.AskedAt - match.Now).TotalSeconds + 0.5));
    }

    [Fact]
    public void A_mini_game_s_opening_gets_a_remark_said_as_its_intro_starts()
    {
        var match = MinigameMatch(MinigameKind.RuneLock, startChance: 1);

        var quip = QuipIn(match.Press("A1", 0, afterSeconds: 4))!;

        Assert.Equal(QuerieMoment.MinigameStart, quip.Moment);
        Assert.Equal(match.AdventurerOf("A1").Minigame!.IntroAt, quip.At);
        Assert.Same(quip, match.AdventurerOf("A1").LastQuip);
    }

    [Fact]
    public void The_pop_quiz_opens_with_his_own_kind_of_remark()
    {
        var match = MinigameMatch(MinigameKind.PopQuiz, startChance: 1);

        Assert.Equal(QuerieMoment.PopQuizStart, QuipIn(match.Press("A1", 0, afterSeconds: 4))!.Moment);
    }

    [Fact]
    public void Running_out_of_time_always_gets_a_remark()
    {
        var match = MinigameMatch(MinigameKind.RuneLock);
        var game = IntoTheMinigame(match);

        var quip = QuipIn(match.Wait((game.EndsAt - match.Now).TotalSeconds + 0.1))!;

        Assert.Equal((QuerieMoment.MinigameLost, game.EndsAt), (quip.Moment, quip.At));
    }

    [Fact]
    public void A_win_always_gets_a_remark_as_the_win_shows()
    {
        var match = MinigameMatch(MinigameKind.PopQuiz, tune: m => m.PopQuiz.RightNeeded = 1);
        var quiz = (PopQuiz)IntoTheMinigame(match);

        var quip = QuipIn(AnswerQuiz(match, quiz, right: true))!;

        Assert.Equal(QuerieMoment.MinigameWon, quip.Moment);
        Assert.Equal(match.Now + TimeSpan.FromSeconds(quiz.WinPauseSeconds), quip.At); // the right answer shows a moment first
    }

    [Fact]
    public void A_miss_in_a_mini_game_gets_a_remark_now_and_then()
    {
        var match = MinigameMatch(MinigameKind.RuneEcho, chance: 1);
        var echo = (RuneEcho)IntoTheMinigame(match);
        match.Wait((echo.InputFrom - match.Now).TotalSeconds + 0.1); // the runes have been shown

        var quip = QuipIn(match.Press("A1", (echo.Expected + 1) % 4, afterSeconds: 0.1));

        Assert.Equal(QuerieMoment.MinigameMiss, quip!.Moment);
    }

    [Fact]
    public void A_wrong_pop_quiz_answer_always_gets_the_right_one_rubbed_in_and_forgetting_your_name_gets_its_own()
    {
        var match = MinigameMatch(MinigameKind.PopQuiz, lines: LinesWith((QuerieMoment.PopQuizWrong, "It was {answer}."), (QuerieMoment.PopQuizForgotName, "You're {answer}.")),
            tune: m => { m.PopQuiz.RightNeeded = 10; m.PopQuiz.Strikes = 10; });
        var quiz = (PopQuiz)IntoTheMinigame(match);
        while (quiz.Current.Topic == PopQuizTopic.Name)
            AnswerQuiz(match, quiz, right: true); // the name last, for this test

        var asked = quiz.Current;
        var wrong = QuipIn(AnswerQuiz(match, quiz, right: false))!;
        while (quiz.Current.Topic != PopQuizTopic.Name)
            Assert.Null(QuipIn(AnswerQuiz(match, quiz, right: true))); // right ones: now and then (never, here)
        var forgot = QuipIn(AnswerQuiz(match, quiz, right: false))!;

        Assert.Equal((QuerieMoment.PopQuizWrong, $"It was {asked.Answers[asked.CorrectIndex]}."), (wrong.Moment, wrong.Text));
        Assert.Equal((QuerieMoment.PopQuizForgotName, "You're Ann."), (forgot.Moment, forgot.Text));
    }

    [Fact]
    public void The_shipped_lines_load_and_every_moment_has_a_few()
    {
        var lines = QuerieContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server"));

        foreach (var moment in Enum.GetValues<QuerieMoment>())
            Assert.True(lines.For(moment).Count >= 4, $"{moment} has {lines.For(moment).Count} lines");
    }

    [Fact]
    public void A_line_with_a_placeholder_the_game_does_not_fill_in_is_a_problem()
    {
        Assert.Single(QuerieContent.ProblemsWith("wrong", new QuerieLine("Nice one, {nmae}.")));
        Assert.Empty(QuerieContent.ProblemsWith("wrong", new QuerieLine("Nice one, {name}. The {monster} on floor {floor} agrees.")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
