using TriviaBattle.Core.Dungeon;
using TriviaBattle.Core.Dungeon.Minigames;
using TriviaBattle.Core.Input;
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
/// hard. Floor rewards are on (a player who earns one picks the card that helps most), and so are mini games (they stop the
/// needle on the mark 80% of the time). The session is clock time: the clock stops for mini games and reward picks.
/// If this fails after changing the content, the clock or the rewards, the session length has drifted.
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
        output.WriteLine($"  answers {games.Average(g => g.Answers):0.0}, right {games.Average(g => g.Right):0.0}, timed out {games.Average(g => g.TimedOut):0.0}, damage {games.Average(g => g.Damage):0}, kills {games.Average(g => g.Kills):0.0}, downed {games.Average(g => g.Downed):0.0}, rewards {games.Average(g => g.FloorRewards):0.0} + {games.Average(g => g.LocksOpened):0.0} from mini games");
        output.WriteLine($"  at the station {games.Average(g => g.WallSeconds):0} s (the clock stops for mini games and reward picks)");
        Assert.InRange(seconds, 180, 240);
        Assert.InRange(bosses, 1.5, 2.5);
    }

    [Fact]
    public void A_better_player_plays_longer_and_gets_deeper()
    {
        var plan = DungeonContent.Load(Path.Combine(RepoRoot(), "src", "TriviaBattle.Server"));

        var average = Enumerable.Range(1, 20).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Average)).ToList();
        var good = Enumerable.Range(1, 20).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Good)).ToList();
        var goodNoRewards = Enumerable.Range(1, 20).Select(seed => Play(plan.Build(new Random(seed)), new Random(seed), Good, rewardsOn: false)).ToList();

        output.WriteLine($"Average player {average.Average(g => g.Seconds):0} s, floor {average.Average(g => g.Floor):0.0}; " +
                         $"good player {good.Average(g => g.Seconds):0} s, floor {good.Average(g => g.Floor):0.0}, floor rewards {good.Average(g => g.FloorRewards):0.0}, downed {good.Average(g => g.Downed):0.0} (average: rewards {average.Average(g => g.FloorRewards):0.0}, downed {average.Average(g => g.Downed):0.0})");
        output.WriteLine($"Good player without rewards {goodNoRewards.Average(g => g.Seconds):0} s, floor {goodNoRewards.Average(g => g.Floor):0.0}, downed {goodNoRewards.Average(g => g.Downed):0.0}");
        output.WriteLine($"Out of lives: average {average.Count(g => g.OutOfLives)}/{average.Count}, good {good.Count(g => g.OutOfLives)}/{good.Count}, good without rewards {goodNoRewards.Count(g => g.OutOfLives)}/{goodNoRewards.Count}");
        Assert.True(good.Average(g => g.Seconds) > average.Average(g => g.Seconds) + 30);
        Assert.True(good.Average(g => g.Floor) > average.Average(g => g.Floor));

        // Floor rewards are for playing well: an average player earns some, a good one nearly every boss's.
        Assert.InRange(average.Average(g => g.FloorRewards), 0.3, 1.5);
        Assert.True(good.Average(g => g.FloorRewards) > 3 * average.Average(g => g.FloorRewards));

        // ...and they're what keeps a run alive: the deeper floors outscale even a good player without them.
        Assert.True(good.Average(g => g.Floor) > goodNoRewards.Average(g => g.Floor) + 1);
        Assert.True(good.Average(g => g.Seconds) > goodNoRewards.Average(g => g.Seconds) + 60);
    }

    /// <summary>How a simulated player plays: seconds to answer (from, plus up to span more), and how often they're right.</summary>
    /// <param name="OnTheMark">In a rune lock (a mini game): how often they stop the needle on the mark.</param>
    private sealed record Player(double ThinkFrom, double ThinkSpan, IReadOnlyDictionary<Difficulty, double> RightOften, double OnTheMark);

    private static readonly Player Average = new(3, 3, RightOften, OnTheMark: 0.8);
    private static readonly Player Good = new(1.5, 2.5, new Dictionary<Difficulty, double> { [Difficulty.Easy] = 0.95, [Difficulty.Medium] = 0.85, [Difficulty.Hard] = 0.7 }, OnTheMark: 0.9);

    /// <param name="Seconds">The paid session: clock time (the clock stops while they play a mini game or pick a reward).</param>
    /// <param name="WallSeconds">How long they were really at the station: the clock time plus the time it was stopped.</param>
    /// <param name="Rewards">Rewards picked: for a floor answered well, and for each mini game won (<paramref name="LocksOpened"/>).</param>
    private sealed record Game(double Seconds, int Bosses, int Floor, int Answers, int Right, int TimedOut, int Damage, int Kills, int Downed, int Rewards,
        double WallSeconds = 0, int LocksOpened = 0, bool OutOfLives = false)
    {
        public int FloorRewards => Rewards - LocksOpened;
    }

    /// <summary>
    /// The simulated player's taste in rewards, best first: what keeps the kills (and so the clock) coming, then
    /// what keeps them standing. (They play the Ranger, whose Mastery only shows questions earlier: least use.)
    /// </summary>
    private static readonly Reward[] Wanted = [Reward.Might, Reward.Time, Reward.Focus, Reward.Vitality, Reward.Armor, Reward.LastStand, Reward.Mastery];

    /// <summary>
    /// One step of a mini game. Waits out the intro and the result first; then each game its own way. The clock is stopped
    /// meanwhile, so only a win (a reward) matters. The player is as good at each as <see cref="Player.OnTheMark"/> says.
    /// </summary>
    private static void PlayTheMinigame(TestMatch match, Minigame game, Random player, Player how)
    {
        var waitUntil = game.FinishedAt != null ? game.ResultEndsAt!.Value : game.PlaysFrom;
        if (waitUntil > match.Now)
        {
            match.Wait((waitUntil - match.Now).TotalSeconds + 0.05);
            return;
        }
        switch (game)
        {
            case RuneLock runeLock:
                PlayTheLock(match, runeLock, player, how);
                break;
            case RuneEcho echo when echo.InputFrom > match.Now:
                match.Wait((echo.InputFrom - match.Now).TotalSeconds + 0.05); // watching the runes
                break;
            case RuneEcho echo:
                // Each rune pressed back right as often as they hit the lock's mark (a half-second each).
                match.Press("A1", player.NextDouble() < how.OnTheMark + 0.15 ? echo.Expected : (echo.Expected + 1) % 4, afterSeconds: 0.5);
                break;
            case ChestShuffle shuffle when shuffle.PickFrom > match.Now:
                match.Wait((shuffle.PickFrom - match.Now).TotalSeconds + 0.05); // watching the chests
                break;
            case ChestShuffle shuffle:
                // They keep their eye on the coin most of the time (a bit less as the rounds get faster).
                var coinAt = shuffle.FinalPlaces.ToList().IndexOf(shuffle.CoinChest);
                var followed = player.NextDouble() < how.OnTheMark + 0.1 - 0.1 * shuffle.Round;
                match.Press("A1", followed ? coinAt : (coinAt + 1) % ChestShuffle.Chests, afterSeconds: 1);
                break;
            case RingPuzzle puzzle when puzzle.RoundStartsAt > match.Now:
                match.Wait((puzzle.RoundStartsAt - match.Now).TotalSeconds + 0.05); // the solved picture
                break;
            case RingPuzzle puzzle:
                // A press every half second: the ring they're on the short way back in line (now and then the
                // wrong way), or on to the next ring once it's in line.
                var offset = puzzle.Offsets[puzzle.Selected];
                var shortWay = offset <= puzzle.Steps / 2 ? RingPuzzle.TurnLeft : RingPuzzle.TurnRight;
                var turn = player.NextDouble() < how.OnTheMark ? shortWay : RingPuzzle.TurnLeft + RingPuzzle.TurnRight - shortWay;
                match.Press("A1", offset == 0 ? ButtonPress.Action : turn, afterSeconds: 0.5);
                break;
            default:
                match.Wait(0.5);
                break;
        }
    }

    /// <summary>
    /// One step of a rune lock: wait for the intro, a lockout or the result to pass, or press: on the mark as often as
    /// this player manages, otherwise a little off it. The clock is stopped meanwhile, so only a win (a reward) matters.
    /// </summary>
    private static void PlayTheLock(TestMatch match, RuneLock game, Random player, Player how)
    {
        var waitUntil = game.FinishedAt != null ? game.ResultEndsAt!.Value : game.LockedUntil > match.Now ? game.LockedUntil.Value : game.PlaysFrom;
        if (waitUntil > match.Now)
        {
            match.Wait((waitUntil - match.Now).TotalSeconds + 0.05);
            return;
        }
        var aim = player.NextDouble() < how.OnTheMark ? game.MarkAngle : game.MarkAngle + 60;
        var ahead = game.DegreesPerSecond > 0 ? aim - game.NeedleAt(match.Now) : game.NeedleAt(match.Now) - aim;
        match.Press("A1", 0, afterSeconds: (((ahead % 360) + 360) % 360) / Math.Abs(game.DegreesPerSecond));
    }

    private static Game Play(DungeonMap map, Random player, Player how, bool rewardsOn = true)
    {
        var questions = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard }
            .SelectMany((difficulty, d) => TestMatch.MakeQuestions(80, difficulty, firstId: 1 + d * 100))
            .ToList();
        var match = new TestMatch(GameModeKind.FreeForAll, new MatchSettings { Format = GameFormat.Dungeon }, questions: questions,
            stations: ["A1"], battleBeats: true, dungeon: map, rewards: rewardsOn);
        match.StartPlaying();
        var started = match.Now;
        var endsAt = match.Engine.PhaseEndsAt;
        var (bosses, downed, rewards) = (0, 0, 0);

        while (match.Engine.Phase == MatchPhase.Playing)
        {
            endsAt = match.Engine.PhaseEndsAt;
            if (match.AdventurerOf("A1").Minigame is { } minigame)
            {
                PlayTheMinigame(match, minigame, player, how);
                continue;
            }
            if (match.AdventurerOf("A1").Offer is { } offer)
            {
                // A reward to pick: a couple of seconds to read the cards, then the one that helps most (see Wanted).
                match.Wait(Math.Max(0, (offer.OpensAt - match.Now).TotalSeconds) + 1 + player.NextDouble() * 2);
                var best = offer.Choices.OrderBy(r => Array.IndexOf(Wanted, r)).First();
                rewards += match.Press("A1", offer.Choices.ToList().IndexOf(best), afterSeconds: 0).OfType<RewardChosen>().Count();
                continue;
            }
            if (match.AdventurerOf("A1").IsOut)
            {
                match.Wait(0.1); // out of lives: the next tick ends the game
                continue;
            }
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
        // Over when Playing ended: the clock ran out (endsAt), or everyone was out of lives sooner (the next phase's start).
        var endedAt = match.Engine.PhaseStartedAt < endsAt ? match.Engine.PhaseStartedAt : endsAt;
        var wall = (endedAt - started).TotalSeconds;
        return new Game(wall - match.Engine.ClockHeldFor.TotalSeconds, bosses, delve.Floor, answers.Count, answers.Count(a => a.IsCorrect),
            answers.Count(a => a.TimedOut), delve.DamageDealt, delve.Kills, downed, rewards, wall, match.AdventurerOf("A1").MinigamesWon,
            match.AdventurerOf("A1").IsOut);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
