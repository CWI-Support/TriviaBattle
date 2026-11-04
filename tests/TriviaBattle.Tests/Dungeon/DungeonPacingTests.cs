using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Modes;
using TriviaBattle.Core.Questions;
using TriviaBattle.Server.Game;
using Xunit.Abstractions;

namespace TriviaBattle.Tests.Dungeon;

/// <summary>
/// Does the dungeon the server ships (config/dungeon.json, with the default settings) give a paid session of
/// about 3-4 minutes and about two bosses? Plays many simulated solo games, each on its own layout, with an
/// average player: 3 to 6 seconds to answer, right 80% of the time on easy questions, 65% on medium, 50% on
/// hard. If this fails after changing the content or the clock, the session length has drifted.
/// </summary>
public class DungeonPacingTests(ITestOutputHelper output)
{
    private static readonly Dictionary<Difficulty, double> RightOften = new() { [Difficulty.Easy] = 0.8, [Difficulty.Medium] = 0.65, [Difficulty.Hard] = 0.5 };

    [Fact]
    public void An_average_solo_player_gets_a_3_to_4_minute_session_and_about_two_bosses()
    {
        var plan = DungeonContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server"));

        var games = Enumerable.Range(1, 40).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Average)).ToList();

        var seconds = games.Average(g => g.Seconds);
        var bosses = games.Average(g => g.Bosses);
        output.WriteLine($"Average session {seconds:0} s ({games.Min(g => g.Seconds):0}-{games.Max(g => g.Seconds):0}), bosses {bosses:0.0}, floor reached {games.Average(g => g.Floor):0.0}");
        output.WriteLine($"  answers {games.Average(g => g.Answers):0.0}, right {games.Average(g => g.Right):0.0}, timed out {games.Average(g => g.TimedOut):0.0}, damage {games.Average(g => g.Damage):0}, kills {games.Average(g => g.Kills):0.0}, downed {games.Average(g => g.Downed):0.0}");
        Assert.InRange(seconds, 180, 240);
        Assert.InRange(bosses, 1.5, 2.5);
    }

    [Fact]
    public void A_better_player_plays_longer_and_gets_deeper()
    {
        var plan = DungeonContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server"));

        var average = Enumerable.Range(1, 20).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Average)).ToList();
        var good = Enumerable.Range(1, 20).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Good)).ToList();

        output.WriteLine($"Average player {average.Average(g => g.Seconds):0} s, floor {average.Average(g => g.Floor):0.0}; " +
                         $"good player {good.Average(g => g.Seconds):0} s, floor {good.Average(g => g.Floor):0.0}");
        Assert.True(good.Average(g => g.Seconds) > average.Average(g => g.Seconds) + 30);
        Assert.True(good.Average(g => g.Floor) > average.Average(g => g.Floor));
    }

    /// <summary>How a simulated player plays: seconds to answer (from, plus up to span more), and how often they're right.</summary>
    private sealed record Player(double ThinkFrom, double ThinkSpan, IReadOnlyDictionary<Difficulty, double> RightOften);

    private static readonly Player Average = new(3, 3, RightOften);
    private static readonly Player Good = new(1.5, 2.5, new Dictionary<Difficulty, double> { [Difficulty.Easy] = 0.95, [Difficulty.Medium] = 0.85, [Difficulty.Hard] = 0.7 });

    private sealed record Game(double Seconds, int Bosses, int Floor, int Answers, int Right, int TimedOut, int Damage, int Kills, int Downed);

    private static Game Play(DungeonMap map, Random player, Player how)
    {
        var questions = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard }
            .SelectMany((difficulty, d) => TestMatch.MakeQuestions(80, difficulty, firstId: 1 + d * 100))
            .ToList();
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, questions: questions,
            stations: ["A1"], battleBeats: true, dungeon: map);
        match.StartPlaying();
        var started = match.Now;
        var endsAt = match.Engine.PhaseEndsAt;
        var (bosses, downed) = (0, 0);

        while (match.Engine.Phase == MatchPhase.Playing)
        {
            endsAt = match.Engine.PhaseEndsAt;
            var stream = match.StreamOf("A1");
            var waitFor = new[] { stream.CurrentShownAt, stream.LockedOutUntil }.Max();
            if (waitFor > match.Now)
            {
                match.Wait((waitFor.Value - match.Now).TotalSeconds); // the battle beat, a lockout, being downed
                continue;
            }

            var number = stream.CurrentNumber;
            match.Wait(how.ThinkFrom + player.NextDouble() * how.ThinkSpan);
            if (match.Engine.Phase != MatchPhase.Playing || stream.CurrentNumber != number)
                continue; // the clock ran out, or too slow: the monster attacked and the question went

            var right = player.NextDouble() < how.RightOften[stream.CurrentQuestion.Difficulty];
            var events = match.Press("A1", right ? 0 : 1, afterSeconds: 0);
            bosses += events.OfType<MonsterHit>().Count(e => e.Hit.Killed && e.Hit.IsBoss);
            downed += events.OfType<AdventurerHit>().Count(e => e.Hit.Downed);
        }

        var answers = match.StreamOf("A1").Answers;
        var delve = match.DelveOf("A1");
        return new Game((endsAt - started).TotalSeconds, bosses, delve.Floor, answers.Count, answers.Count(a => a.IsCorrect),
            answers.Count(a => a.TimedOut), delve.DamageDealt, delve.Kills, downed);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
