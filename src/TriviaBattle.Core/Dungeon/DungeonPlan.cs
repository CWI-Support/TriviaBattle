namespace TriviaBattle.Core.Dungeon;

/// <summary>
/// The dungeon as config/dungeon.json writes it: Depths of floors, and the monsters. Some floors are written out
/// room by room; others are made up fresh for each match (<see cref="FloorDef.IsMadeUp"/>): a few rooms, each
/// filled from the floor's pool of monsters to an HP budget, then the boss. <see cref="Build"/> lays out one
/// match's dungeon, the same for every party in that match, so a race is fair; the next match gets another.
/// </summary>
public sealed class DungeonPlan
{
    private readonly IReadOnlyDictionary<string, MonsterDef> _monsters;

    /// <exception cref="ArgumentException">The dungeon isn't valid (every problem is spelled out).</exception>
    public DungeonPlan(IReadOnlyList<DepthDef> depths, IReadOnlyList<MonsterDef> monsters, double loopHpBonus = 0.5, double loopDamageBonus = 0)
    {
        Depths = depths;
        Monsters = monsters;
        LoopHpBonus = loopHpBonus;
        LoopDamageBonus = loopDamageBonus;
        _monsters = monsters.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        var problems = Validate().ToList();
        if (problems.Count > 0)
            throw new ArgumentException("The dungeon isn't valid: " + string.Join(" ", problems));
        Build(new Random(0)); // the rest (written-out floors, the monsters) is checked by the map
    }

    public IReadOnlyList<DepthDef> Depths { get; }
    public IReadOnlyList<MonsterDef> Monsters { get; }
    public double LoopHpBonus { get; }
    public double LoopDamageBonus { get; }

    /// <summary>One match's dungeon: every made-up floor laid out with <paramref name="random"/>.</summary>
    public DungeonMap Build(Random random)
    {
        var depths = Depths
            .Select(depth => depth with { Floors = depth.Floors.Select(floor => floor.IsMadeUp ? LayOut(floor, random) : floor).ToList() })
            .ToList();
        return new DungeonMap(depths, Monsters, LoopHpBonus, LoopDamageBonus);
    }

    /// <summary>A made-up floor, written out: <see cref="FloorDef.RoomCount"/> rooms filled from the pool, then the boss's.</summary>
    private FloorDef LayOut(FloorDef floor, Random random)
    {
        var count = random.Next(floor.RoomCount![0], floor.RoomCount[1] + 1);
        var rooms = new List<IReadOnlyList<string>>();
        for (var i = 0; i < count; i++)
            rooms.Add(FillRoom(floor, random));
        if (floor.Boss != null)
            rooms.Add([floor.Boss]);
        return new FloorDef(Rooms: rooms, Questions: floor.Questions);
    }

    /// <summary>
    /// One room: monsters picked at random from the pool until they reach the room's HP budget. A monster that
    /// would take the room more than a quarter over its budget isn't picked (except the first, which always
    /// fits), so a room is one tough monster or a few weak ones, but always about as hard as the next.
    /// </summary>
    private IReadOnlyList<string> FillRoom(FloorDef floor, Random random)
    {
        var pool = floor.Pool!.Select(id => _monsters[id]).ToList();
        var room = new List<string>();
        var hp = 0;
        while (room.Count < floor.MaxPack && hp < floor.RoomHp)
        {
            var fits = pool.Where(m => room.Count == 0 || hp + m.Hp <= floor.RoomHp * 1.25).ToList();
            if (fits.Count == 0)
                break;
            var monster = fits[random.Next(fits.Count)];
            room.Add(monster.Id);
            hp += monster.Hp;
        }
        return room;
    }

    private IEnumerable<string> Validate()
    {
        foreach (var depth in Depths)
        {
            foreach (var floor in depth.Floors.Where(f => f.IsMadeUp))
            {
                if (floor.RoomCount is not [var fewest, var most] || fewest < 1 || most < fewest)
                    yield return $"A made-up floor of '{depth.Name}' needs RoomCount as [fewest, most], at least 1.";
                if (floor.RoomHp <= 0 || floor.MaxPack < 1)
                    yield return $"A made-up floor of '{depth.Name}' needs RoomHp and MaxPack above zero.";
                if (floor.Pool!.Count == 0)
                    yield return $"A made-up floor of '{depth.Name}' has an empty Pool.";
                foreach (var id in floor.Pool.Append(floor.Boss).OfType<string>().Where(id => !_monsters.ContainsKey(id)))
                    yield return $"'{depth.Name}' uses monster '{id}', which isn't defined.";
            }
            foreach (var floor in depth.Floors.Where(f => f.Questions != null && (f.Questions.Values.Any(w => w < 0) || f.Questions.Values.Sum() <= 0)))
                yield return $"A floor of '{depth.Name}' has Questions weights that don't add up to more than zero.";
        }
    }
}
