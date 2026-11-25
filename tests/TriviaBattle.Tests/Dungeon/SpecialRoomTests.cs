using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Questions;
using TriviaBattle.Core.Snapshots;

namespace TriviaBattle.Tests.Dungeon;

// The special rooms behind a fork's doors (see ForkTests), each played after a 3.5 s intro:
//   - Treasure: 5 right in 10 seconds opens the chest (a power-up; hands full, a potion); a wrong answer kills the buttons for 1 s.
//   - Trap: 6 true/false statements (A true, B false), 4 s each and a 1.4 s reveal after; each miss takes 8% of max HP
//     (never the last), and every one right is a full heal.
//   - High Stakes: A bets, D passes (8 s to choose); betting asks one brutal question from its own bank (or a hard one
//     from the stream: 12 s, a second to read it first): right, a reward pick; wrong or too slow, a quarter of their max HP.
// Every test question's right answer is A.
public class SpecialRoomTests
{
    /// <summary>
    /// A solo player in the room behind door A, its intro over: playing. Empty-handed (the rat may have dropped something).
    /// </summary>
    private static TestMatch InRoom(MinigameKind room, bool rewards = false, QuerieLines? querie = null, HighStakesQuestions? highStakes = null)
    {
        var match = ForkTests.AtTheFork(ForkTests.MapWith(room), rewards, querie, highStakes);
        match.AdventurerOf("A1").DropHeld();
        match.Press("A1", 0, afterSeconds: 1.1); // door A
        match.Wait(2.1);                         // it opens: the room starts
        match.Wait(3.5);                         // its intro
        return match;
    }

    private static T Room<T>(TestMatch match) where T : Minigame => Assert.IsType<T>(match.AdventurerOf("A1").Minigame);

    private static HighStakesView BetOnScreen(TestMatch match) => SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.HighStakes!;

    // ---------- Treasure ----------

    [Fact]
    public void Five_right_in_time_opens_the_chest_and_its_power_up_is_theirs()
    {
        var match = InRoom(MinigameKind.Treasure, rewards: true);
        for (var i = 0; i < 4; i++)
            Assert.Contains(match.Press("A1", 0, afterSeconds: 0.3), e => e is MinigamePlayed { Result: MinigamePress.Hit });

        var events = match.Press("A1", 0, afterSeconds: 0.3);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
        var room = Room<TreasureRoom>(match);
        var found = Assert.Single(events.OfType<PowerUpFound>());
        Assert.Equal(room.Prize, found.PowerUp);
        Assert.Equal(room.Prize, match.AdventurerOf("A1").Held);
        match.Wait(1.2 + 3.2 + 0.1); // the chest opens, then the result
        Assert.Null(match.AdventurerOf("A1").Offer); // the chest was the prize: no reward pick
        Assert.False(match.AdventurerOf("A1").IsAway);
    }

    [Fact]
    public void With_their_hands_full_the_chest_holds_a_potion_drunk_there_and_then()
    {
        var match = InRoom(MinigameKind.Treasure);
        var ann = match.AdventurerOf("A1");
        ann.PickUp(PowerUp.Shield);
        ann.Blast(30);
        var hp = ann.Hp;

        for (var i = 0; i < 5; i++)
            match.Press("A1", 0, afterSeconds: 0.3);

        var room = Room<TreasureRoom>(match);
        Assert.Null(room.Prize);
        Assert.Equal(PowerUp.Shield, ann.Held);
        Assert.Equal(30, room.Healed);
        Assert.Equal(hp + 30, ann.Hp);
    }

    [Fact]
    public void A_wrong_answer_kills_the_buttons_for_a_moment_and_moves_on()
    {
        var match = InRoom(MinigameKind.Treasure);

        Assert.Contains(match.Press("A1", 1, afterSeconds: 0.3), e => e is MinigamePlayed { Result: MinigamePress.Miss });
        Assert.Empty(match.Press("A1", 0, afterSeconds: 0.5));
        Assert.Contains(match.Press("A1", 0, afterSeconds: 0.6), e => e is MinigamePlayed { Result: MinigamePress.Hit });

        var room = Room<TreasureRoom>(match);
        Assert.Equal(1, room.Wrong);
        Assert.Equal(1, room.Right);
        Assert.Equal(2, room.Answered); // on to the third question
    }

    [Fact]
    public void Out_of_time_the_chest_stays_shut()
    {
        var match = InRoom(MinigameKind.Treasure);
        match.Press("A1", 0, afterSeconds: 0.3);
        match.Press("A1", 0, afterSeconds: 0.3);

        var events = match.Wait(10);

        Assert.Contains(events, e => e is MinigameLost);
        Assert.Null(match.AdventurerOf("A1").Held);
    }

    // ---------- Trap ----------

    private static int RightAnswer(TrapRoom trap) => trap.Current.IsTrue ? 0 : 1;
    private static int WrongAnswer(TrapRoom trap) => trap.Current.IsTrue ? 1 : 0;

    /// <summary>Waits for the statement up to be asked (the last one's reveal), then answers it after <paramref name="afterSeconds"/>.</summary>
    private static IReadOnlyList<MatchEvent> Answer(TestMatch match, bool right, double afterSeconds = 0.5)
    {
        var trap = Room<TrapRoom>(match);
        if (trap.AskedAt > match.Now)
            match.Wait((trap.AskedAt - match.Now).TotalSeconds);
        return match.Press("A1", right ? RightAnswer(trap) : WrongAnswer(trap), afterSeconds);
    }

    [Fact]
    public void A_wrong_answer_springs_the_trap()
    {
        var match = InRoom(MinigameKind.Trap);
        var ann = match.AdventurerOf("A1");
        var hp = ann.Hp;

        Assert.Contains(Answer(match, right: false), e => e is MinigamePlayed { Result: MinigamePress.Miss });

        var trap = Room<TrapRoom>(match);
        var spring = (int)Math.Round(ann.MaxHp * 0.08);
        Assert.Equal(hp - spring, ann.Hp);
        Assert.Equal(1, trap.Misses);
        Assert.Equal(spring, trap.Damage);
        Assert.Contains(Answer(match, right: true), e => e is MinigamePlayed { Result: MinigamePress.Hit });
        Assert.Equal(hp - spring, ann.Hp);
    }

    [Fact]
    public void Too_slow_springs_it_too()
    {
        var match = InRoom(MinigameKind.Trap);
        var ann = match.AdventurerOf("A1");
        var hp = ann.Hp;

        var events = match.Wait(4.1);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Miss });
        Assert.True(ann.Hp < hp);
        var last = Room<TrapRoom>(match).Last!.Value;
        Assert.Null(last.Said);
        Assert.False(last.WasRight);
    }

    [Fact]
    public void The_trap_never_takes_the_last_hp()
    {
        var match = InRoom(MinigameKind.Trap);
        var ann = match.AdventurerOf("A1");
        ann.Blast(ann.Hp); // down to their last

        Answer(match, right: false);

        Assert.Equal(1, ann.Hp);
        Assert.False(ann.IsDowned);
    }

    [Fact]
    public void Every_one_right_heals_them_fully()
    {
        var match = InRoom(MinigameKind.Trap, rewards: true);
        var ann = match.AdventurerOf("A1");
        ann.Blast(30); // hurt on the way in

        for (var i = 0; i < 5; i++)
            Answer(match, right: true);
        var events = Answer(match, right: true);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
        Assert.Equal(ann.MaxHp, ann.Hp);
        Assert.Equal(30, Room<TrapRoom>(match).Healed);
        match.Wait(1.4 + 3.2 + 0.1); // the last one's truth, then the result
        Assert.Null(ann.Offer); // the heal was the prize: no reward pick
        Assert.False(ann.IsAway);
    }

    [Fact]
    public void One_miss_gets_them_through_hurt_with_nothing()
    {
        var match = InRoom(MinigameKind.Trap, rewards: true);
        var ann = match.AdventurerOf("A1");

        Answer(match, right: false);
        for (var i = 0; i < 4; i++)
            Answer(match, right: true);
        var events = Answer(match, right: true);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Lost });
        Assert.Equal(ann.MaxHp - (int)Math.Round(ann.MaxHp * 0.08), ann.Hp);
        Assert.Equal(0, Room<TrapRoom>(match).Healed);
        match.Wait(3.3);
        Assert.Null(ann.Offer);
    }

    [Fact]
    public void The_screen_learns_whether_a_statement_was_true_only_once_it_s_answered()
    {
        var match = InRoom(MinigameKind.Trap);
        var trap = Room<TrapRoom>(match);
        var first = trap.Current;

        var before = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.Trap!;
        Assert.Equal(first.ShownAnswer, before.Shown);
        Assert.Null(before.Last);

        Answer(match, right: true);
        var after = SnapshotBuilder.Build(match.Engine, Viewer.Player("A1")).You!.Minigame!.Trap!;

        Assert.Equal(first.IsTrue, after.Last!.WasTrue);
        Assert.Equal("Right", after.Last.Correct);
        Assert.True(after.Last.WasRight);
        Assert.Equal(2, after.Number);
    }

    // ---------- High Stakes ----------

    [Fact]
    public void Passing_wins_nothing_and_loses_nothing()
    {
        var match = InRoom(MinigameKind.HighStakes, rewards: true);
        var hp = match.AdventurerOf("A1").Hp;

        var events = match.Press("A1", 3, afterSeconds: 0.5);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Left });
        Assert.True(Room<HighStakesRoom>(match).Passed);
        match.Wait(3.3);
        var ann = match.AdventurerOf("A1");
        Assert.Equal(hp, ann.Hp);
        Assert.Null(ann.Offer);
        Assert.False(ann.IsAway);
    }

    [Fact]
    public void Not_choosing_in_time_is_passing()
    {
        var match = InRoom(MinigameKind.HighStakes);

        Assert.Empty(match.Press("A1", 1, afterSeconds: 0.5)); // only A and D mean anything
        var events = match.Wait(7.6);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Left });
        Assert.True(Room<HighStakesRoom>(match).Passed);
    }

    [Fact]
    public void A_winning_bet_earns_a_reward_pick()
    {
        var match = InRoom(MinigameKind.HighStakes, rewards: true);

        Assert.Contains(match.Press("A1", 0, afterSeconds: 0.5), e => e is MinigamePlayed { Result: MinigamePress.Hit }); // bets
        Assert.Empty(match.Press("A1", 0, afterSeconds: 0.2)); // reading it: the press that bet can't answer it too
        var events = match.Press("A1", 0, afterSeconds: 1);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
        match.Wait(1.2 + 3.2 + 0.1); // the right answer shows, then the result
        Assert.True(match.AdventurerOf("A1").Offer?.ForMinigame);
    }

    [Fact]
    public void A_losing_bet_takes_a_quarter_of_their_hp()
    {
        var match = InRoom(MinigameKind.HighStakes, rewards: true);
        var ann = match.AdventurerOf("A1");
        var hp = ann.Hp;
        match.Press("A1", 0, afterSeconds: 0.5);

        var events = match.Press("A1", 2, afterSeconds: 1.2);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Lost });
        var price = (int)Math.Round(ann.MaxHp * 0.25);
        Assert.Equal(hp - price, ann.Hp);
        Assert.Equal(price, Room<HighStakesRoom>(match).Penalty);
        match.Wait(3.3);
        Assert.Null(ann.Offer);
    }

    [Fact]
    public void Betting_and_running_out_of_time_costs_the_same()
    {
        var match = InRoom(MinigameKind.HighStakes);
        var ann = match.AdventurerOf("A1");
        var hp = ann.Hp;
        match.Press("A1", 0, afterSeconds: 0.5);

        var events = match.Wait(13.1);

        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Lost });
        Assert.Equal(hp - (int)Math.Round(ann.MaxHp * 0.25), ann.Hp);
        Assert.Null(Room<HighStakesRoom>(match).Answer!.Value.Chosen);
    }

    [Fact]
    public void The_question_shows_once_they_bet_and_its_answer_once_they_answer()
    {
        var match = InRoom(MinigameKind.HighStakes);
        var ann = match.AdventurerOf("A1");

        var choosing = BetOnScreen(match);
        Assert.Null(choosing.Text);
        Assert.Null(choosing.Answers);
        Assert.Equal((int)Math.Round(ann.MaxHp * 0.25), choosing.PenaltyHp);

        match.Press("A1", 0, afterSeconds: 0.5);
        var asked = BetOnScreen(match);
        Assert.NotNull(asked.Text);
        Assert.Equal(4, asked.Answers!.Count);
        Assert.Null(asked.Correct);

        match.Press("A1", 1, afterSeconds: 1.1);
        var answered = BetOnScreen(match);
        Assert.Equal(1, answered.Chosen);
        Assert.Equal(0, answered.Correct);
    }

    /// <summary>A brutal question in its own bank (its right answer "Yes").</summary>
    private static Question Brutal(int id, string category) =>
        new(-id, category, Difficulty.Hard, $"Brutal {id}?", ["Yes", "No", "Maybe", "Never"], 0);

    [Fact]
    public void High_stakes_asks_from_its_own_bank_the_match_s_categories_first()
    {
        // The test match's questions are all "Test".
        var bank = new HighStakesQuestions([Brutal(1, "Other"), Brutal(2, "Test"), Brutal(3, "Other")]);
        var match = InRoom(MinigameKind.HighStakes, highStakes: bank);

        match.Press("A1", 0, afterSeconds: 0.5);

        var asked = BetOnScreen(match);
        Assert.Equal("Brutal 2?", asked.Text);
        Assert.Equal("Test", asked.Category);
        Assert.Contains("Yes", asked.Answers!);
    }

    [Fact]
    public void With_none_in_its_categories_any_of_the_bank_will_do()
    {
        var bank = new HighStakesQuestions([Brutal(7, "Other")]);
        var match = InRoom(MinigameKind.HighStakes, highStakes: bank);
        var bet = Room<HighStakesRoom>(match);

        match.Press("A1", 0, afterSeconds: 0.5);
        var answer = bet.Question.Answers.ToList().IndexOf("Yes"); // its answers are shuffled
        var events = match.Press("A1", answer, afterSeconds: 1.1);

        Assert.Equal("Brutal 7?", bet.Question.Text);
        Assert.Contains(events, e => e is MinigamePlayed { Result: MinigamePress.Won });
    }

    // ---------- Q.U.E.R.I.E. ----------

    /// <summary>One line per moment, "Moment: {name}", so a test can tell which one he picked.</summary>
    private static readonly QuerieLines Lines = new(Enum.GetValues<QuerieMoment>()
        .ToDictionary(m => m, m => (IReadOnlyList<QuerieLine>)[new QuerieLine($"{m}: {{name}}")]));

    [Fact]
    public void Querie_greets_the_doors_and_has_a_word_for_passing_on_the_bet()
    {
        var match = ForkTests.AtTheFork(ForkTests.MapWith(MinigameKind.HighStakes), querie: Lines);
        Assert.Equal("ForkOpen: Ann", match.AdventurerOf("A1").LastQuip?.Text);
        match.Press("A1", 0, afterSeconds: 1.1);
        match.Wait(2.1); // the door opens: High Stakes starts
        match.Wait(3.5); // its intro

        var events = match.Press("A1", 3, afterSeconds: 0.5);

        Assert.Contains(events, e => e is QuerieSpoke { Quip.Moment: QuerieMoment.HighStakesPassed });
    }

    [Fact]
    public void Querie_has_his_say_when_a_bet_is_lost()
    {
        var match = InRoom(MinigameKind.HighStakes, querie: Lines);
        match.Press("A1", 0, afterSeconds: 0.5);

        var events = match.Press("A1", 1, afterSeconds: 1.1);

        Assert.Contains(events, e => e is QuerieSpoke { Quip.Moment: QuerieMoment.HighStakesLost });
    }

    // ---------- Questions for the rooms ----------

    [Fact]
    public void A_room_s_questions_come_from_the_stream_and_aren_t_asked_again()
    {
        var questions = new[] { Difficulty.Easy, Difficulty.Hard, Difficulty.Easy, Difficulty.Hard, Difficulty.Easy }
            .Select((d, i) => new Question(i + 1, "Test", d, $"Q{i + 1}", ["a", "b", "c", "d"], 0))
            .ToList();
        var stream = new QuestionStream("A1", questions);

        var hard = stream.TakeExtra(Difficulty.Hard, 3);

        Assert.Equal([2, 4, 3], hard.Select(q => q.Id)); // the hard ones first, then another; never the one up (1)
        Assert.Equal(1, stream.CurrentQuestion.Id);
        stream.Record(new GivenAnswer("A1", 1, questions[0], "A1", 0, true, 10, TestMatch.Start, TimeSpan.FromSeconds(1)), TestMatch.Start);
        Assert.Equal(5, stream.CurrentQuestion.Id); // the one question nobody's had yet
    }
}
