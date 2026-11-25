using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// Q.U.E.R.I.E., the dungeon's host, at the stations: after each answer (or the hourglass running out), whether he has
/// something to say to that player, and what kind of remark (<see cref="QuerieMoment"/>); the words come from
/// <see cref="QuerieLines"/>. He doesn't talk after every answer. Ordinary moments (a miss, a slow answer, a right answer
/// as death gets closer) only get a remark now and then, with a gap between remarks to the same player; big moments (a
/// third miss in a row, down to the last life, the one who got it wrong while the rest of the party got theirs right) get
/// one whenever they happen, a moment apart. A party hears the big ones about any of its members, but not too often.
/// He talks in the mini games too (<see cref="ReactToMinigame"/>), and most in the pop quiz.
/// Numbers: <see cref="QuerieSettings"/>. <see cref="DungeonRun.Commentate"/> and <see cref="DungeonRun.CommentateMinigame"/>
/// ask him and pass his remark on.
/// </summary>
public sealed class Querie(QuerieLines lines, QuerieSettings settings, Random random)
{
    private readonly Dictionary<string, DateTimeOffset> _lastQuipAt = new();    // by answer owner: their last remark
    private readonly Dictionary<string, DateTimeOffset> _lastCalloutAt = new(); // by party: the last one all of it heard
    private readonly Dictionary<QuerieMoment, Queue<int>> _bags = new();        // each moment's lines, in a shuffled order
    private readonly Dictionary<QuerieMoment, int> _lastLine = new();
    private int _number;

    /// <summary>His remark about this answer, or null when he keeps quiet.</summary>
    public Quip? React(QuerieScene scene, DateTimeOffset now)
    {
        if (!settings.Enabled)
            return null;
        var (moment, big, partyWide) = MomentOf(scene);
        if (moment is not { } said)
            return null;

        // A gap since his last remark to them (a short one for a big moment), and an ordinary moment only some of the time.
        var sinceLast = now - _lastQuipAt.GetValueOrDefault(scene.OwnerId, DateTimeOffset.MinValue);
        if (sinceLast < TimeSpan.FromSeconds(big ? settings.BigGapSeconds : settings.QuipEverySeconds))
            return null;
        if (!big && random.NextDouble() >= (said == QuerieMoment.RightSafe ? settings.SafeChance : settings.Chance))
            return null;
        // The whole party hears it only so often; otherwise it's just for them.
        if (partyWide && now - _lastCalloutAt.GetValueOrDefault(scene.PartyId, DateTimeOffset.MinValue) < TimeSpan.FromSeconds(settings.PartyCalloutEverySeconds))
            partyWide = false;

        var quip = Say(said, scene.OwnerId, partyWide, now, scene.Name, scene.Monster, scene.Floor.ToString(), scene.RivalAhead ?? "");
        if (quip != null && partyWide)
            _lastCalloutAt[scene.PartyId] = now;
        return quip;
    }

    /// <summary>
    /// His remark on a moment in someone's mini game (its opening, what a press did, or the time running out), or null when
    /// he keeps quiet. Just for them: everyone plays their own go. Openings now and then; wins and losses always (one
    /// each per game); a miss now and then, with the same gap as an ordinary moment. The pop quiz is his own kind of game:
    /// every wrong answer gets a remark (a moment apart), and a right one now and then.
    /// </summary>
    public Quip? ReactToMinigame(MinigameScene scene)
    {
        if (!settings.Enabled)
            return null;
        var quiz = scene.Kind == Minigames.MinigameKind.PopQuiz;
        var potato = scene.Kind == Minigames.MinigameKind.HotPotato;
        var bet = scene.Kind == Minigames.MinigameKind.HighStakes;
        var forgotName = scene.Asked?.Topic == Minigames.PopQuizTopic.Name;
        (QuerieMoment Moment, double Chance, double Gap)? what = scene.Beat switch
        {
            MinigameBeat.Start => (StartOf(scene.Kind), settings.MinigameStartChance, 0),
            MinigameBeat.Won => (potato ? QuerieMoment.HotPotatoSafe : bet ? QuerieMoment.HighStakesWon : QuerieMoment.MinigameWon, 1, 0),
            MinigameBeat.Lost => (quiz && forgotName ? QuerieMoment.PopQuizForgotName : potato ? QuerieMoment.HotPotatoBlast
                : bet ? QuerieMoment.HighStakesLost : QuerieMoment.MinigameLost, 1, 0),
            MinigameBeat.Left => (QuerieMoment.HighStakesPassed, 1, 0), // passed on High Stakes' bet
            MinigameBeat.Miss when quiz => (forgotName ? QuerieMoment.PopQuizForgotName : QuerieMoment.PopQuizWrong, 1, forgotName ? 0 : settings.BigGapSeconds),
            MinigameBeat.Miss when scene.Kind == Minigames.MinigameKind.Trap => (QuerieMoment.TrapSprung, settings.Chance, settings.QuipEverySeconds),
            MinigameBeat.Miss when scene.Kind == Minigames.MinigameKind.Treasure => null, // a wrong answer in the lightning round: no time to talk
            MinigameBeat.Miss => (QuerieMoment.MinigameMiss, settings.Chance, settings.QuipEverySeconds),
            MinigameBeat.Hit when quiz => (QuerieMoment.PopQuizRight, settings.Chance, settings.QuipEverySeconds),
            _ => null, // a hit in any other game: the game says it
        };
        if (what is not { } said)
            return null;
        var (moment, chance, gap) = said;
        if (scene.At - _lastQuipAt.GetValueOrDefault(scene.OwnerId, DateTimeOffset.MinValue) < TimeSpan.FromSeconds(gap))
            return null;
        if (chance < 1 && random.NextDouble() >= chance)
            return null;
        var answer = scene.Asked is { } asked ? asked.Answers[asked.CorrectIndex] : "";
        return Say(moment, scene.OwnerId, partyWide: false, scene.At, scene.Name, answer: answer, holder: scene.Holder ?? "");
    }

    /// <summary>The moment a mini game's opening is: most share one, some games (and each special room) have their own.</summary>
    private static QuerieMoment StartOf(Minigames.MinigameKind kind) => kind switch
    {
        Minigames.MinigameKind.PopQuiz => QuerieMoment.PopQuizStart,
        Minigames.MinigameKind.HotPotato => QuerieMoment.HotPotatoStart,
        Minigames.MinigameKind.Treasure => QuerieMoment.TreasureStart,
        Minigames.MinigameKind.Trap => QuerieMoment.TrapStart,
        Minigames.MinigameKind.HighStakes => QuerieMoment.HighStakesStart,
        _ => QuerieMoment.MinigameStart,
    };

    /// <summary>His remark as the doors of a fork before a boss come up (always, if he has lines for it): just for them.</summary>
    public Quip? ReactToFork(string ownerId, string name, DateTimeOffset at) =>
        settings.Enabled ? Say(QuerieMoment.ForkOpen, ownerId, partyWide: false, at, name) : null;

    /// <summary>The moment's next line, filled in and counted as their latest remark (null with no lines for it).</summary>
    private Quip? Say(QuerieMoment moment, string ownerId, bool partyWide, DateTimeOffset at,
        string name, string monster = "", string floor = "", string rival = "", string answer = "", string holder = "")
    {
        if (NextLine(moment) is not { } line)
            return null;
        _lastQuipAt[ownerId] = at;
        var text = line.Text
            .Replace("{name}", name)
            .Replace("{monster}", monster)
            .Replace("{floor}", floor)
            .Replace("{rival}", rival)
            .Replace("{answer}", answer)
            .Replace("{holder}", holder);
        return new Quip(++_number, moment, text, line.Audio, ownerId, partyWide, at);
    }

    /// <summary>What kind of moment this answer is (null: nothing he'd remark on), whether it's a big one, and whether the party hears it.</summary>
    private (QuerieMoment? Moment, bool Big, bool PartyWide) MomentOf(QuerieScene s)
    {
        var answer = s.Answer;
        var slow = answer.TimedOut || answer.ResponseTime.TotalSeconds >= settings.SlowSeconds;
        if (!answer.IsCorrect)
        {
            if (s.Taken is { Downed: true, Out: false } && s.LivesLeft == 1)
                return (QuerieMoment.LastLife, true, s.InParty);
            if (s.Taken is { Downed: true, Out: false } && s.InParty)
                return (QuerieMoment.DownedInParty, true, true);
            if (s.Taken?.Out == true)
                return (null, false, false); // OUT! says it all
            if (s.InParty && s.TeammatesRight >= 2 && s.TeammatesWrong == 0)
                return (QuerieMoment.OddOneOut, true, true);
            if (s.MissesInARow >= settings.WrongStreakAt)
                return (QuerieMoment.WrongStreak, true, false);
            return (slow ? QuerieMoment.Slow : QuerieMoment.Wrong, false, false);
        }

        if (s.InParty && s.TeammatesWrong >= 2 && s.TeammatesRight == 0)
            return (QuerieMoment.Carrying, false, true);
        if (slow)
            return (QuerieMoment.Slow, false, false);
        if (s.RivalAhead != null && random.NextDouble() < 0.5)
            return (QuerieMoment.BehindRival, false, false);
        return (DangerOf(s) switch
        {
            0 => QuerieMoment.RightSafe,
            1 => QuerieMoment.RightUneasy,
            _ => QuerieMoment.RightDoomed,
        }, false, false);
    }

    /// <summary>How close to death they are, 0 (safe) and up: hurt, badly hurt, the last life, a boss, deep down.</summary>
    private int DangerOf(QuerieScene s)
    {
        var danger = 0;
        if (s.HpShare <= 0.5) danger++;
        if (s.HpShare <= 0.25) danger++;
        if (s.LivesLeft == 1) danger++;
        if (s.IsBoss) danger++;
        if (s.Floor >= settings.DeepFloor) danger++;
        return danger;
    }

    /// <summary>The moment's next line: each in turn, in a shuffled order, so the same one doesn't come round again soon.</summary>
    private QuerieLine? NextLine(QuerieMoment moment)
    {
        var choices = lines.For(moment);
        if (choices.Count == 0)
            return null;
        if (!_bags.TryGetValue(moment, out var bag) || bag.Count == 0)
        {
            var order = Enumerable.Range(0, choices.Count).OrderBy(_ => random.Next()).ToList();
            if (order.Count > 1 && order[0] == _lastLine.GetValueOrDefault(moment, -1))
                (order[0], order[^1]) = (order[^1], order[0]); // not the one just said, the moment a new round starts
            _bags[moment] = bag = new Queue<int>(order);
        }
        var index = bag.Dequeue();
        _lastLine[moment] = index;
        return choices[index];
    }

}

/// <summary>What happened in a mini game, for Q.U.E.R.I.E.: it opened, a press hit or missed, it was won or lost, or they left it (passed on High Stakes' bet).</summary>
public enum MinigameBeat { Start, Hit, Miss, Won, Lost, Left }

/// <summary>
/// A moment in someone's mini game (<see cref="DungeonRun.CommentateMinigame"/> works it out), at <paramref name="At"/>
/// (an opening: when its intro starts). <paramref name="Asked"/>: in the pop quiz, the question the press just answered.
/// <paramref name="Holder"/>: in the hot potato, who's holding the bomb (once it's gone off, who it went off on).
/// </summary>
public sealed record MinigameScene(string OwnerId, string Name, Minigames.MinigameKind Kind, MinigameBeat Beat, Minigames.PopQuizQuestion? Asked, DateTimeOffset At,
    string? Holder = null);

/// <summary>
/// What Q.U.E.R.I.E. knows about an answer when deciding whether to say something (<see cref="DungeonRun.Commentate"/>
/// works it out). <paramref name="LivesLeft"/>: 0 with no limit on lives. <paramref name="TeammatesRight"/> and
/// <paramref name="TeammatesWrong"/>: the rest of the party's answers just now (their last, if recent).
/// <paramref name="RivalAhead"/>: a race or a team battle, the name of a rival side on a deeper floor (null if none).
/// </summary>
public sealed record QuerieScene(
    string OwnerId,
    string PartyId,
    string Name,
    GivenAnswer Answer,
    TakenHit? Taken,
    string Monster,
    int Floor,
    bool IsBoss,
    double HpShare,
    int LivesLeft,
    int MissesInARow,
    bool InParty,
    int TeammatesRight,
    int TeammatesWrong,
    string? RivalAhead);

/// <summary>
/// One remark of Q.U.E.R.I.E.'s: what he said (<paramref name="Text"/>, filled in; <paramref name="Audio"/>, its
/// recording, once there is one) about whom (<paramref name="AboutOwnerId"/>), and whether the whole party hears it.
/// <paramref name="Number"/> counts up through the match, so screens show each one once.
/// </summary>
public sealed record Quip(int Number, QuerieMoment Moment, string Text, string? Audio, string AboutOwnerId, bool PartyWide, DateTimeOffset At);
