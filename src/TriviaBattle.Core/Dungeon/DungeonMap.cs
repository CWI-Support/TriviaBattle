namespace TriviaBattle.Core.Dungeon;

/// <summary>One kind of monster, as written in config/dungeon.json.</summary>
/// <param name="Id">Key used by floors, e.g. "goblin-bandit".</param>
/// <param name="Sprite">
/// What the screens draw: a built-in placeholder sprite key ("goblin"), or a picture path
/// ("/media/dungeon/goblin.png") once the real pixel art exists.
/// </param>
/// <param name="Hp">Hit points on the first pass through the dungeon (deeper loops scale it up).</param>
/// <param name="Damage">HP it takes off an adventurer per hit (a wrong answer, or its hourglass running out).</param>
/// <param name="AttackSeconds">Its hourglass: answer within this or it attacks and the question is lost.</param>
public sealed record MonsterDef(string Id, string Name, string Sprite, int Hp, int Damage, double AttackSeconds, bool IsBoss = false);

/// <summary>One floor: the monsters met on it, in order. Clearing the last one clears the floor.</summary>
public sealed record FloorDef(IReadOnlyList<string> Monsters);

/// <summary>A Depth: a run of floors with its own name and look (its last floor is usually a boss).</summary>
public sealed record DepthDef(string Id, string Name, IReadOnlyList<FloorDef> Floors);

/// <summary>A monster met on a particular floor, with its hit points for that floor.</summary>
/// <param name="Floor">1-based floor number, counting from the top of the dungeon.</param>
/// <param name="Room">1-based: which monster of that floor this is.</param>
/// <param name="RoomsOnFloor">How many monsters the floor has.</param>
public sealed record Encounter(MonsterDef Monster, int MaxHp, DepthDef Depth, int Floor, int Room, int RoomsOnFloor);

/// <summary>
/// The whole dungeon: every Depth's floors, top to bottom, and the monsters on them.
/// Loaded from config/dungeon.json by the server, so floors and monsters are content, not code.
///
/// Past the last floor the dungeon starts again from the top, deeper: the floor numbers keep
/// counting up and every monster gets <see cref="LoopHpBonus"/> more HP per loop. That way a
/// very good party never runs out of dungeon.
/// </summary>
public sealed class DungeonMap
{
    private readonly IReadOnlyDictionary<string, MonsterDef> _monsters;
    private readonly List<(DepthDef Depth, FloorDef Floor)> _floors;

    public DungeonMap(IReadOnlyList<DepthDef> depths, IEnumerable<MonsterDef> monsters, double loopHpBonus = 0.5)
    {
        _monsters = monsters.ToDictionary(m => m.Id);
        _floors = depths.SelectMany(d => d.Floors.Select(f => (d, f))).ToList();
        Depths = depths;
        LoopHpBonus = loopHpBonus;

        var problems = Validate().ToList();
        if (problems.Count > 0)
            throw new ArgumentException("The dungeon isn't valid: " + string.Join(" ", problems));
    }

    public IReadOnlyList<DepthDef> Depths { get; }

    /// <summary>Extra HP per loop through the dungeon: 0.5 = monsters have 150% HP on the second loop.</summary>
    public double LoopHpBonus { get; }

    public int FloorCount => _floors.Count;

    /// <summary>The monster in a given room of a given floor (both 1-based).</summary>
    public Encounter EncounterAt(int floor, int room)
    {
        var index = (floor - 1) % _floors.Count;
        var loop = (floor - 1) / _floors.Count;
        var (depth, floorDef) = _floors[index];
        var monster = _monsters[floorDef.Monsters[room - 1]];
        var maxHp = (int)Math.Round(monster.Hp * (1 + LoopHpBonus * loop));
        return new Encounter(monster, maxHp, depth, floor, room, floorDef.Monsters.Count);
    }

    /// <summary>
    /// Every monster of the Depth that <paramref name="floor"/> is in, floor by floor, in order: the
    /// path down to its boss. Screens draw it as a trail of icons. Floor numbers are the real ones,
    /// so on a deeper loop they keep counting up.
    /// </summary>
    public IReadOnlyList<Encounter> DepthPath(int floor)
    {
        var index = (floor - 1) % _floors.Count;
        var depth = _floors[index].Depth;
        var firstIndex = _floors.FindIndex(f => ReferenceEquals(f.Depth, depth));
        var firstFloor = floor - (index - firstIndex);

        var path = new List<Encounter>();
        for (var f = 0; f < depth.Floors.Count; f++)
            for (var room = 1; room <= depth.Floors[f].Monsters.Count; room++)
                path.Add(EncounterAt(firstFloor + f, room));
        return path;
    }

    private IEnumerable<string> Validate()
    {
        if (_floors.Count == 0)
            yield return "It has no floors.";
        foreach (var (depth, floor) in _floors)
        {
            if (floor.Monsters.Count == 0)
                yield return $"A floor of '{depth.Name}' has no monsters.";
            foreach (var id in floor.Monsters.Where(id => !_monsters.ContainsKey(id)))
                yield return $"'{depth.Name}' uses monster '{id}', which isn't defined.";
        }
        foreach (var monster in _monsters.Values.Where(m => m.Hp <= 0 || m.AttackSeconds <= 0))
            yield return $"Monster '{monster.Id}' needs Hp and AttackSeconds above zero.";
    }
}
