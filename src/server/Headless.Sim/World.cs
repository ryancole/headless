namespace Headless.Sim;

/// <summary>All simulation state. Players and ships share one id space; collections iterate in id order for determinism.</summary>
public sealed class World(SimConfig config)
{
    private readonly SortedDictionary<int, Player> _players = [];
    private readonly SortedDictionary<int, Ship> _ships = [];
    private int _nextId = 1;

    public SimConfig Config { get; } = config;
    public long Tick { get; internal set; }

    public IReadOnlyDictionary<int, Player> Players => _players;
    public IReadOnlyDictionary<int, Ship> Ships => _ships;

    public Player AddPlayer(string name)
    {
        var player = new Player { Id = _nextId++, Name = name };
        _players.Add(player.Id, player);
        return player;
    }

    /// <summary>
    /// Adds a ship owned by <paramref name="owner"/>, who must already be in this world. It starts at rest, pointing
    /// along +x, fitted with an engine (device 0) and a gyroscope (device 1).
    /// </summary>
    public Ship AddShip(string name, Vec2 position, Player owner)
    {
        if (!_players.TryGetValue(owner.Id, out var known) || known != owner)
            throw new ArgumentException($"Player {owner.Id} is not in this world.", nameof(owner));

        var ship = new Ship
        {
            Id = _nextId++,
            Name = name,
            OwnerPlayerId = owner.Id,
            Mass = Config.ShipMass,
            Devices =
            [
                new Device { Kind = DeviceKind.Engine, Rating = Config.EngineThrust },
                new Device { Kind = DeviceKind.Gyroscope, Rating = Config.GyroscopeTurnRate },
            ],
            Position = position,
        };
        _ships.Add(ship.Id, ship);
        return ship;
    }
}
