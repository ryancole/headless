namespace Headless.Sim;

/// <summary>
/// The authoritative, deterministic game loop. For now ships only have a position and nothing moves them,
/// so a tick just advances the clock. Not thread-safe: the host must serialize calls.
/// </summary>
public sealed class Simulation(World world)
{
    public World World { get; } = world;

    /// <summary>Advances the world by one tick.</summary>
    public void Step() => World.Tick++;

    public SensorSnapshot Sense(int shipId) => new(World.Tick, ShipStateOf(World.Ships[shipId]));

    public PlayerSnapshot SensePlayer(int playerId)
    {
        var player = World.Players[playerId];
        var ships = World.Ships.Values.Where(s => s.OwnerPlayerId == playerId).Select(ShipStateOf).ToList();
        return new PlayerSnapshot(World.Tick, new PlayerState(player.Id, player.Name), ships);
    }

    private static ShipState ShipStateOf(Ship ship) => new(ship.Id, ship.Name, ship.OwnerPlayerId, ship.Position);
}
