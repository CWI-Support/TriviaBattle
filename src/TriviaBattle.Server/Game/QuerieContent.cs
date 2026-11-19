using System.Text.Json;
using System.Text.RegularExpressions;
using TriviaBattle.Core.Dungeon;

namespace TriviaBattle.Server.Game;

/// <summary>
/// Loads Q.U.E.R.I.E.'s lines for the stations from config/querie.json: for each moment (Core/Dungeon/QuerieLines.cs,
/// in camelCase: "wrong", "oddOneOut"...) a list of lines, each { "text": "...", "audio": null } (or just the text).
/// "audio" is for later: a recorded clip of the line. Keys starting with "_" are notes. Read once at startup: a broken
/// file stops the server with the problem spelled out.
/// </summary>
public static partial class QuerieContent
{
    public const string FilePath = "config/querie.json";

    public static QuerieLines Load(string contentRoot)
    {
        var path = Path.Combine(contentRoot, FilePath);
        if (!File.Exists(path))
            return QuerieLines.None; // no file: he keeps quiet at the stations
        using var file = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var lines = new Dictionary<QuerieMoment, IReadOnlyList<QuerieLine>>();
        var problems = new List<string>();
        foreach (var moment in file.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
        {
            if (!Enum.TryParse<QuerieMoment>(moment.Name, ignoreCase: true, out var key))
            {
                problems.Add($"\"{moment.Name}\" isn't a moment ({string.Join(", ", Enum.GetNames<QuerieMoment>())})");
                continue;
            }
            var list = new List<QuerieLine>();
            foreach (var entry in moment.Value.EnumerateArray())
            {
                var line = entry.ValueKind == JsonValueKind.String
                    ? new QuerieLine(entry.GetString()!)
                    : new QuerieLine(
                        entry.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "",
                        entry.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.String ? audio.GetString() : null);
                problems.AddRange(ProblemsWith(moment.Name, line));
                list.Add(line);
            }
            lines[key] = list;
        }

        if (problems.Count > 0)
            throw new InvalidOperationException($"{FilePath}: {string.Join("; ", problems)}");
        return new QuerieLines(lines);
    }

    /// <summary>A line with no text, or a {placeholder} the game doesn't fill in.</summary>
    public static IEnumerable<string> ProblemsWith(string moment, QuerieLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
            yield return $"a line in \"{moment}\" has no text";
        foreach (Match placeholder in PlaceholderPattern().Matches(line.Text))
            if (!QuerieLines.Placeholders.Contains(placeholder.Groups[1].Value))
                yield return $"\"{line.Text}\" ({moment}) uses {{{placeholder.Groups[1].Value}}}: only {string.Join(", ", QuerieLines.Placeholders.Select(p => $"{{{p}}}"))} are filled in";
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();
}
