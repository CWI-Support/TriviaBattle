namespace TriviaBattle.Core.Arcade;

/// <summary>
/// The heroes a player can pick at their station, in the order the station shows them. The keys are the
/// screens' keys for each hero's 3D model and pictures (js/battle3d/models.js, media/heroes); their names
/// are in the theme's copy ("heroes.knight"). Just a look for now: classes with their own powers come later.
/// </summary>
public static class Heroes
{
    public static readonly IReadOnlyList<string> All = ["knight", "barbarian", "mage", "rogue", "ranger"];

    /// <summary>Who you play when nobody chose (a match started from the kiosk).</summary>
    public const string Default = "knight";

    public static bool IsKnown(string? key) => key == null || All.Contains(key);
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
