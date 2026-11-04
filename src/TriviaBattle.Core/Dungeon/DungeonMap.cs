using TriviaBattle.Core.Questions;

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

/// <summary>
/// One floor: its rooms, in order. Clearing the last room clears the floor. A room holds one monster or
/// several fought together (a pack): the party fights the one in front, the others wait their turn, and a
/// Wizard's fast answer hits them all. A floor is written one of three ways:
/// <list type="bullet">
///   <item><c>{ "Monsters": ["rat", "slime"] }</c>: a room each;</item>
///   <item><c>{ "Rooms": [["rat", "rat"], ["slime"]] }</c>: rooms written out;</item>
///   <item><c>{ "RoomCount": [2, 3], "RoomHp": 70, "Pool": ["rat", "slime"], "Boss": "goblin-quizmaster" }</c>: made up
///     fresh for each match (<see cref="DungeonPlan.Build"/>): 2 or 3 rooms, each filled from the pool to about 70 HP
///     (1 to <see cref="MaxPack"/> monsters), then the boss's room.</item>
/// </list>
/// </summary>
/// <param name="Monsters">One monster per room, in order.</param>
/// <param name="Rooms">Rooms with any number of monsters (the first in each is in front). Used instead of Monsters when given.</param>
/// <param name="RoomCount">A made-up floor: the fewest and most rooms before the boss, e.g. [2, 3].</param>
/// <param name="RoomHp">A made-up floor: about how much HP each room's monsters have between them (a little over is allowed).</param>
/// <param name="Pool">A made-up floor: the monsters its rooms are filled from.</param>
/// <param name="MaxPack">A made-up floor: the most monsters in one room.</param>
/// <param name="Boss">A made-up floor: the boss in its last room (none if not given).</param>
/// <param name="Questions">
/// How often each difficulty is asked on this floor, as weights: <c>{ "Easy": 80, "Medium": 20 }</c>. Not given: the
/// match's chosen difficulty comes first, as in Classic.
/// </param>
public sealed record FloorDef(
    IReadOnlyList<string>? Monsters = null,
    IReadOnlyList<IReadOnlyList<string>>? Rooms = null,
    IReadOnlyList<int>? RoomCount = null,
    int RoomHp = 0,
    IReadOnlyList<string>? Pool = null,
    int MaxPack = 3,
    string? Boss = null,
    IReadOnlyDictionary<Difficulty, int>? Questions = null)
{
    /// <summary>Made up fresh for each match from a pool, rather than written out.</summary>
    public bool IsMadeUp => Pool != null;

    /// <summary>Every room's monsters, for a floor written out (either way).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RoomList() =>
        Rooms ?? (Monsters ?? []).Select(id => (IReadOnlyList<string>)[id]).ToList();
}

/// <summary>A Depth: a run of floors with its own name and look (its last floor is usually a boss).</summary>
public sealed record DepthDef(string Id, string Name, IReadOnlyList<FloorDef> Floors);

/// <summary>A monster met in a particular room of a particular floor, with its hit points for that floor.</summary>
/// <param name="Floor">1-based floor number, counting from the top of the dungeon.</param>
/// <param name="Room">1-based: which room of that floor this is.</param>
/// <param name="RoomsOnFloor">How many rooms the floor has.</param>
/// <param name="Slot">1-based: its place in the room's pack (1 = in front).</param>
/// <param name="MonstersInRoom">How many monsters the room started with.</param>
/// <param name="MaxHp">Its HP on this floor (more on each loop through the dungeon; a party's are tougher still, see <see cref="Foe"/>).</param>
/// <param name="Damage">What its hit takes off an adventurer on this floor (more on each loop), before their class's share.</param>
public sealed record Encounter(MonsterDef Monster, int MaxHp, int Damage, DepthDef Depth, int Floor, int Room, int RoomsOnFloor, int Slot = 1, int MonstersInRoom = 1);

/// <summary>
/// The whole dungeon for one match: every Depth's floors, top to bottom, and the monsters in their rooms.
/// Made from config/dungeon.json (see <see cref="DungeonPlan"/>, which lays out the made-up floors fresh for
/// each match), so floors and monsters are content, not code.
///
/// Past the last floor the dungeon starts again from the top, deeper: the floor numbers keep
/// counting up and every monster gets <see cref="LoopHpBonus"/> more HP and <see cref="LoopDamageBonus"/> more
/// damage per loop. That way a very good party never runs out of dungeon.
/// </summary>
public sealed class DungeonMap
{
    private readonly IReadOnlyDictionary<string, MonsterDef> _monsters;
    private readonly List<(DepthDef Depth, FloorDef Floor, IReadOnlyList<IReadOnlyList<string>> Rooms)> _floors;

    /// <param name="depths">Every floor written out: made-up floors are laid out first, by <see cref="DungeonPlan.Build"/>.</param>
    public DungeonMap(IReadOnlyList<DepthDef> depths, IEnumerable<MonsterDef> monsters, double loopHpBonus = 0.5, double loopDamageBonus = 0)
    {
        _monsters = monsters.ToDictionary(m => m.Id);
        _floors = depths.SelectMany(d => d.Floors.Select(f => (d, f, f.RoomList()))).ToList();
        Depths = depths;
        LoopHpBonus = loopHpBonus;
        LoopDamageBonus = loopDamageBonus;

        var problems = Validate().ToList();
        if (problems.Count > 0)
            throw new ArgumentException("The dungeon isn't valid: " + string.Join(" ", problems));
    }

    public IReadOnlyList<DepthDef> Depths { get; }

    /// <summary>Extra HP per loop through the dungeon: 0.5 = monsters have 150% HP on the second loop.</summary>
    public double LoopHpBonus { get; }

    /// <summary>Extra damage per loop through the dungeon: 0.25 = monsters hit 125% as hard on the second loop.</summary>
    public double LoopDamageBonus { get; }

    public int FloorCount => _floors.Count;

    /// <summary>How many rooms a floor (1-based) has.</summary>
    public int RoomsOn(int floor) => _floors[(floor - 1) % _floors.Count].Rooms.Count;

    /// <summary>How often each difficulty is asked on a floor (1-based), as weights; null if the floor doesn't say.</summary>
    public IReadOnlyDictionary<Difficulty, int>? QuestionsOn(int floor) => _floors[(floor - 1) % _floors.Count].Floor.Questions;

    /// <summary>The monsters in a given room of a given floor (both 1-based), the one in front first.</summary>
    public IReadOnlyList<Encounter> RoomAt(int floor, int room)
    {
        var index = (floor - 1) % _floors.Count;
        var loop = (floor - 1) / _floors.Count;
        var (depth, _, rooms) = _floors[index];
        var ids = rooms[room - 1];
        return ids.Select((id, i) =>
        {
            var monster = _monsters[id];
            var maxHp = (int)Math.Round(monster.Hp * (1 + LoopHpBonus * loop));
            var damage = (int)Math.Round(monster.Damage * (1 + LoopDamageBonus * loop));
            return new Encounter(monster, maxHp, damage, depth, floor, room, rooms.Count, Slot: i + 1, MonstersInRoom: ids.Count);
        }).ToList();
    }

    /// <summary>
    /// Every monster of the Depth that <paramref name="floor"/> is in, floor by floor and room by room, in
    /// order: the path down to its boss. Screens draw it as a trail of icons (a pack's monsters share a room
    /// number). Floor numbers are the real ones, so on a deeper loop they keep counting up.
    /// </summary>
    public IReadOnlyList<Encounter> DepthPath(int floor)
    {
        var index = (floor - 1) % _floors.Count;
        var depth = _floors[index].Depth;
        var firstIndex = _floors.FindIndex(f => ReferenceEquals(f.Depth, depth));
        var firstFloor = floor - (index - firstIndex);

        var path = new List<Encounter>();
        for (var f = 0; f < depth.Floors.Count; f++)
            for (var room = 1; room <= RoomsOn(firstFloor + f); room++)
                path.AddRange(RoomAt(firstFloor + f, room));
        return path;
    }

    private IEnumerable<string> Validate()
    {
        if (_floors.Count == 0)
            yield return "It has no floors.";
        foreach (var (depth, floor, rooms) in _floors)
        {
            if (floor.IsMadeUp)
                yield return $"A floor of '{depth.Name}' is made up from a pool: lay it out with DungeonPlan.Build first.";
            else if (rooms.Count == 0)
                yield return $"A floor of '{depth.Name}' has no rooms.";
            if (rooms.Any(r => r.Count == 0))
                yield return $"A room on a floor of '{depth.Name}' has no monsters.";
            foreach (var id in rooms.SelectMany(r => r).Distinct().Where(id => !_monsters.ContainsKey(id)))
                yield return $"'{depth.Name}' uses monster '{id}', which isn't defined.";
        }
        foreach (var monster in _monsters.Values.Where(m => m.Hp <= 0 || m.AttackSeconds <= 0))
            yield return $"Monster '{monster.Id}' needs Hp and AttackSeconds above zero.";
    }
}
