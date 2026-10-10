namespace Headless.Sim;

/// <summary>All simulation state. Entities share one id space; collections iterate in id order for determinism.</summary>
public sealed class World(SimConfig config)
{
    private readonly SortedDictionary<int, Ship> _ships = [];
    private readonly SortedDictionary<int, Asteroid> _asteroids = [];
    private readonly SortedDictionary<int, Station> _stations = [];
    private int _nextId = 1;

    public SimConfig Config { get; } = config;
    public long Tick { get; internal set; }

    public IReadOnlyDictionary<int, Ship> Ships => _ships;
    public IReadOnlyDictionary<int, Asteroid> Asteroids => _asteroids;
    public IReadOnlyDictionary<int, Station> Stations => _stations;

    public Station AddStation(string name, Vec2 position, double orePrice, double fuelPrice)
    {
        var station = new Station { Id = _nextId++, Name = name, Position = position, OrePrice = orePrice, FuelPrice = fuelPrice };
        _stations.Add(station.Id, station);
        return station;
    }

    public Asteroid AddAsteroid(Vec2 position, double radius, double ore)
    {
        var asteroid = new Asteroid { Id = _nextId++, Position = position, Radius = radius, Ore = ore };
        _asteroids.Add(asteroid.Id, asteroid);
        return asteroid;
    }

    /// <summary>Adds a ship in open space with full fuel and starting credits.</summary>
    public Ship AddShip(string name, Vec2 position, Vec2 velocity = default)
    {
        var ship = new Ship
        {
            Id = _nextId++,
            Name = name,
            Position = position,
            Velocity = velocity,
            Fuel = Config.ShipMaxFuel,
            Credits = Config.ShipStartingCredits,
        };
        _ships.Add(ship.Id, ship);
        return ship;
    }

    /// <summary>Adds a ship docked at <paramref name="station"/>.</summary>
    public Ship AddShipDocked(string name, Station station)
    {
        var ship = AddShip(name, station.Position);
        ship.DockedAtStationId = station.Id;
        return ship;
    }
}
