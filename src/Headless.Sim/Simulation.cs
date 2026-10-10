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

    public SensorSnapshot Sense(int shipId)
    {
        var ship = World.Ships[shipId];
        return new SensorSnapshot(World.Tick, new ShipState(ship.Id, ship.Name, ship.Position));
    }
}
