using TriviaBattle.Core.Questions;

namespace TriviaBattle.Core.Dungeon.Minigames;

/// <summary>
/// The treasure room, one of the special rooms behind a fork's doors (<see cref="Fork"/>): a lightning round. Questions
/// from the player's own stream (easy ones: <see cref="TreasureSettings.Difficulty"/>) come one after another with no
/// pause, and <see cref="TreasureSettings.RightNeeded"/> right inside the time limit opens the chest. A wrong answer
/// doesn't end it, but the buttons are dead for a moment (<see cref="TreasureSettings.LockoutSeconds"/>) and it's on to
/// the next question, so mashing the buttons doesn't open it.
///
/// The chest holds a power-up: <see cref="DungeonRun"/> puts one in their hands when it opens (<see cref="Prize"/>), or,
/// with their hands already full, a potion they drink there and then (<see cref="Healed"/>). That's the prize, so a win
/// here doesn't earn a reward pick as other mini games' do. The snapshot never carries the right answer to the question up.
/// </summary>
public sealed class TreasureRoom : Minigame
{
    private readonly TreasureSettings _settings;
    private readonly IReadOnlyList<Question> _questions;

    public TreasureRoom(DateTimeOffset introAt, MinigameSettings settings, IReadOnlyList<Question> questions)
        : base(MinigameKind.Treasure, introAt, settings.IntroSeconds, settings.Treasure.Seconds, settings.ResultSeconds)
    {
        if (questions.Count == 0)
            throw new ArgumentException("The treasure room needs questions to ask.", nameof(questions));
        _settings = settings.Treasure;
        _questions = questions;
    }

    /// <summary>How many questions it takes from their stream: plenty for the right ones and the wrong ones in between.</summary>
    public static int QuestionsNeeded(TreasureSettings settings) => settings.RightNeeded * 3;

    public int RightNeeded => _settings.RightNeeded;

    /// <summary>How many questions have been answered; the one up is the next (they come round again if it ever runs out).</summary>
    public int Answered { get; private set; }

    /// <summary>The question up now.</summary>
    public Question Current => _questions[Answered % _questions.Count];

    public int Right { get; private set; }
    public int Wrong { get; private set; }

    /// <summary>After a wrong answer, the buttons are dead until then.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>The last answer: right or not, and when (the screen flashes it). Null before the first.</summary>
    public (bool WasRight, DateTimeOffset At)? Last { get; private set; }

    /// <summary>What was in the chest, once it's open: the power-up they got (null with their hands full: then <see cref="Healed"/>).</summary>
    public PowerUp? Prize { get; private set; }

    /// <summary>Their hands were full, so the chest's potion was drunk: the HP it healed (0 at full health).</summary>
    public int Healed { get; private set; }

    /// <summary>The chest's prize is the power-up, not a reward pick.</summary>
    public override bool WinEarnsReward => false;

    /// <summary>The chest springs open, and what was in it shows, before the result.</summary>
    public override double WinPauseSeconds => _settings.OpenSeconds;

    /// <summary>What was in the chest (<see cref="DungeonRun"/> decides it as it opens).</summary>
    public void Award(PowerUp? prize, int healed)
    {
        Prize = prize;
        Healed = healed;
    }

    protected override MinigamePress Play(int answerIndex, DateTimeOffset at)
    {
        if (answerIndex is < 0 or > 3 || at < LockedUntil)
            return MinigamePress.Ignored; // the action button, or the buttons are dead after a wrong answer

        var right = answerIndex == Current.CorrectIndex;
        Last = (right, at);
        Answered++;
        if (right)
        {
            Right++;
            return Right >= RightNeeded ? MinigamePress.Won : MinigamePress.Hit;
        }
        Wrong++;
        LockedUntil = at + TimeSpan.FromSeconds(_settings.LockoutSeconds);
        return MinigamePress.Miss;
    }
}
