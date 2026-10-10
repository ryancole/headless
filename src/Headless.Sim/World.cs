namespace Headless.Sim;

/// <summary>All simulation state. Ships iterate in id order for determinism.</summary>
public sealed class World(SimConfig config)
{
    private readonly SortedDictionary<int, Ship> _ships = [];
    private int _nextId = 1;

    public SimConfig Config { get; } = config;
    public long Tick { get; internal set; }

    public IReadOnlyDictionary<int, Ship> Ships => _ships;

    public Ship AddShip(string name, Vec2 position)
    {
        var ship = new Ship { Id = _nextId++, Name = name, Position = position };
        _ships.Add(ship.Id, ship);
        return ship;
    }
}
