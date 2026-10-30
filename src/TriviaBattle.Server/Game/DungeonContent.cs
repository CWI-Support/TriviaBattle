using System.Text.Json;
using TriviaBattle.Core.Dungeon;

namespace TriviaBattle.Server.Game;

/// <summary>Loads the dungeon (Depths, floors, monsters) from config/dungeon.json.</summary>
public static class DungeonContent
{
    public const string FilePath = "config/dungeon.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads and checks the file. A broken file stops the server at startup with the problem spelled out.</summary>
    public static DungeonMap Load(string contentRoot)
    {
        var path = Path.Combine(contentRoot, FilePath);
        var file = JsonSerializer.Deserialize<DungeonFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException($"{FilePath} is empty.");

        try
        {
            return new DungeonMap(file.Depths ?? [], file.Monsters ?? [], file.LoopHpBonus);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{FilePath}: {ex.Message}", ex);
        }
    }

    private sealed record DungeonFile(double LoopHpBonus, List<DepthDef>? Depths, List<MonsterDef>? Monsters);
}
