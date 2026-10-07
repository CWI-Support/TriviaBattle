using TriviaBattle.Core.Input;
using TriviaBattle.Core.Questions;

namespace TriviaBattle.Tests.Input;

public class ButtonMapTests
{
    // Same rule as config/buttons.json: buttonId = 10 + (player - 1) * 4 + answerIndex
    private static ButtonMap StandardMap() => new(
        from player in Enumerable.Range(1, 4)
        from answer in Enumerable.Range(0, 4)
        select new ButtonAssignment(10 + (player - 1) * 4 + answer, $"A{player}", answer));

    [Theory]
    [InlineData(10, "A1", 0)]
    [InlineData(13, "A1", 3)]
    [InlineData(14, "A2", 0)]
    [InlineData(21, "A3", 3)]
    [InlineData(25, "A4", 3)]
    public void Translates_button_id_to_station_and_answer(int buttonId, string station, int answer)
    {
        var press = StandardMap().Translate(new ButtonEvent(buttonId, 1, DateTimeOffset.UnixEpoch));

        Assert.NotNull(press);
        Assert.Equal(station, press.StationId);
        Assert.Equal(answer, press.AnswerIndex);
    }

    [Fact]
    public void Unknown_button_id_is_ignored()
    {
        Assert.Null(StandardMap().Translate(new ButtonEvent(99, 1, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void Finds_button_id_for_led_output()
    {
        Assert.Equal(17, StandardMap().FindButtonId("A2", 3));
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("d", 3)]
    public void Answer_letters_convert_to_indexes(string letter, int index)
    {
        Assert.Equal(index, ButtonMap.AnswerLetterToIndex(letter));
        Assert.Equal(letter.ToUpperInvariant(), ButtonMap.AnswerIndexToLetter(index));
    }

    [Fact]
    public void Shuffling_answers_keeps_the_correct_answer_correct()
    {
        var question = new Question(1, "Test", Difficulty.Easy, "?", ["Right", "W1", "W2", "W3"], CorrectIndex: 0);

        for (var seed = 0; seed < 20; seed++)
        {
            var shuffled = question.WithShuffledAnswers(new Random(seed));
            Assert.Equal("Right", shuffled.Answers[shuffled.CorrectIndex]);
        }
    }
}
