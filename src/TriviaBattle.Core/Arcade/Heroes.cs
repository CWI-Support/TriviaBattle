using TriviaBattle.Core.Dungeon;

namespace TriviaBattle.Core.Arcade;

/// <summary>
/// The heroes a player can pick at their station, in the order the station shows them. Each one is a
/// <see cref="HeroClass"/>: the key is the class's name in lower case ("tank"). The keys are also the screens'
/// keys for each hero's 3D model and pictures (js/battle3d/models.js, media/heroes); their names and what
/// they do are in the theme's copy ("heroes.tank", "heroes.tankLine").
///
/// To add a hero: add a <see cref="HeroClass"/> value (and its rules in DungeonRun), its key here, its model in
/// HEROES (js/battle3d/models.js), its two lines in the copy, and save its pictures with /dev/heroes.
/// </summary>
public static class Heroes
{
    public static readonly IReadOnlyList<string> All = ["tank", "healer", "wizard", "rogue", "ranger", "pugilist", "warlock"];

    /// <summary>Who you play when nobody chose (a match started from the kiosk).</summary>
    public const string Default = "tank";

    public static bool IsKnown(string? key) => key == null || All.Contains(key);

    /// <summary>The class a hero key plays as (an unknown key plays the default).</summary>
    public static HeroClass ClassOf(string? key) =>
        Enum.TryParse<HeroClass>(key, ignoreCase: true, out var heroClass) && All.Contains(key!) ? heroClass : ClassOf(Default);
}

/// <summary>
/// Made-up hero names for players without a name of their own (a guest, or a card seen for the first time).
/// Silly but kind, and at most <see cref="Matches.MatchSetupValidator.MaxNameLength"/> characters.
/// </summary>
public static class HeroNames
{
    private static readonly string[] Titles = ["Sir", "Lady", "Dame", "Lord", "Captain", "Doctor", "Baron", "Squire", "Count"];

    private static readonly string[] Names =
    [
        "Bonkington", "Quizzalot", "Snackwell", "Factsworth", "Bravesock", "Puddlefoot", "Brainsworth",
        "Riddlesby", "Noodleton", "Trivington", "Pebblewick", "Sparkleby", "Wobbleton", "Cluesworth",
        "Muffinbeard", "Ponderwell", "Fizzlewit", "Thimblebum", "Knowsalot", "Biscuitron",
    ];

    /// <summary>A name nobody in <paramref name="taken"/> has yet (if one is left).</summary>
    public static string Make(Random random, IReadOnlyCollection<string> taken)
    {
        for (var tries = 0; tries < 50; tries++)
        {
            var name = $"{Titles[random.Next(Titles.Length)]} {Names[random.Next(Names.Length)]}";
            if (name.Length <= Matches.MatchSetupValidator.MaxNameLength && !taken.Contains(name))
                return name;
        }
        return $"Hero {random.Next(100, 1000)}";
    }
}
