using System.Text.Json;
using System.Text.RegularExpressions;

namespace TriviaBattle.Tests.Display;

/// <summary>
/// Guards the theme folders (wwwroot/themes) against typos: every text key the screens ask for
/// must exist in the base copy, and themes may only override keys that exist.
/// </summary>
public partial class ThemeFilesTests
{
    private static readonly string WwwRoot = Path.Combine(RepoRoot(), "src", "TriviaBattle.Server", "wwwroot");

    [Fact]
    public void Every_text_key_used_by_the_screens_exists_in_the_base_copy()
    {
        var baseKeys = KeysOf(Path.Combine(WwwRoot, "themes", "neon", "copy.json"));

        var missing = Directory.GetFiles(Path.Combine(WwwRoot, "js"), "*.js")
            .SelectMany(file => UsedKeys().Matches(File.ReadAllText(file)).Select(m => (File: Path.GetFileName(file), Key: m.Groups[1].Value)))
            .Where(use => !baseKeys.Contains(use.Key))
            .Select(use => $"{use.File}: {use.Key}")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Themes_only_override_keys_that_exist()
    {
        var baseKeys = KeysOf(Path.Combine(WwwRoot, "themes", "neon", "copy.json"));

        foreach (var copyFile in Directory.GetFiles(Path.Combine(WwwRoot, "themes"), "copy.json", SearchOption.AllDirectories))
        {
            var unknown = KeysOf(copyFile).Except(baseKeys).ToList();
            Assert.True(unknown.Count == 0, $"{copyFile} has keys the screens never use: {string.Join(", ", unknown)}");
        }
    }

    [Fact]
    public void Every_theme_has_a_stylesheet_scoped_to_its_name()
    {
        foreach (var folder in Directory.GetDirectories(Path.Combine(WwwRoot, "themes")))
        {
            var name = Path.GetFileName(folder);
            var css = File.ReadAllText(Path.Combine(folder, "theme.css"));
            Assert.Contains($"[data-theme=\"{name}\"]", css);
        }
    }

    // Theme.text('some.key' ...) / Theme.plain('some.key' ...) with a literal key.
    [GeneratedRegex(@"Theme\.(?:text|plain)\(\s*'([\w.]+)'")]
    private static partial Regex UsedKeys();

    /// <summary>Dotted keys of every leaf in a copy.json ("player.correct"), ignoring "_comment"-style entries.</summary>
    private static HashSet<string> KeysOf(string copyFile)
    {
        var keys = new HashSet<string>();
        void Walk(JsonElement node, string prefix)
        {
            foreach (var property in node.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
            {
                var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                if (property.Value.ValueKind == JsonValueKind.Object) Walk(property.Value, key);
                else keys.Add(key);
            }
        }

        Walk(JsonDocument.Parse(File.ReadAllText(copyFile)).RootElement, "");
        return keys;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
