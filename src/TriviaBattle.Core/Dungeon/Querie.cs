using TriviaBattle.Core.Matches;

namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// Q.U.E.R.I.E., the dungeon's host, at the stations: after each answer (or the hourglass running out), whether he has
/// something to say to that player, and what kind of remark (<see cref="QuerieMoment"/>); the words come from
/// <see cref="QuerieLines"/>. He doesn't talk after every answer. Ordinary moments (a miss, a slow answer, a right answer
/// as death gets closer) only get a remark now and then, with a gap between remarks to the same player; big moments (a
/// third miss in a row, down to the last life, the one who got it wrong while the rest of the party got theirs right) get
/// one whenever they happen, a moment apart. A party hears the big ones about any of its members, but not too often.
/// Numbers: <see cref="QuerieSettings"/>. <see cref="DungeonRun.Commentate"/> asks him and passes his remark on.
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

        if (NextLine(said) is not { } line)
            return null;
        _lastQuipAt[scene.OwnerId] = now;
        if (partyWide)
            _lastCalloutAt[scene.PartyId] = now;
        return new Quip(++_number, said, FillIn(line.Text, scene), line.Audio, scene.OwnerId, partyWide, now);
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

    private static string FillIn(string text, QuerieScene s) => text
        .Replace("{name}", s.Name)
        .Replace("{monster}", s.Monster)
        .Replace("{floor}", s.Floor.ToString())
        .Replace("{rival}", s.RivalAhead ?? "");
}

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
