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

    /// <summary>
    /// Reads and checks the file. A broken file stops the server at startup with the problem spelled out. Each
    /// match lays out its own dungeon from the plan (<see cref="DungeonPlan.Build"/>), so made-up floors differ.
    /// </summary>
    public static DungeonPlan Load(string contentRoot)
    {
        var path = Path.Combine(contentRoot, FilePath);
        var file = JsonSerializer.Deserialize<DungeonFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException($"{FilePath} is empty.");

        try
        {
            return new DungeonPlan(file.Depths ?? [], file.Monsters ?? [], file.LoopHpBonus, file.LoopDamageBonus);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{FilePath}: {ex.Message}", ex);
        }
    }

    private sealed record DungeonFile(double LoopHpBonus, List<DepthDef>? Depths, List<MonsterDef>? Monsters, double LoopDamageBonus = 0);
}
