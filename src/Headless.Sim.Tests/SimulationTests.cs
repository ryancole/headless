namespace Headless.Sim.Tests;

public class SimulationTests
{
    [Fact]
    public void Ships_get_unique_ids_and_keep_their_spawn_position()
    {
        var world = new World(new SimConfig());
        var a = world.AddShip("a", new Vec2(10, 20));
        var b = world.AddShip("b", new Vec2(-5, 0));

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(new Vec2(10, 20), a.Position);
        Assert.Equal(new Vec2(-5, 0), b.Position);
    }

    [Fact]
    public void Step_advances_the_tick_and_ships_stay_put()
    {
        var world = new World(new SimConfig());
        var ship = world.AddShip("a", new Vec2(10, 20));
        var sim = new Simulation(world);

        sim.Step();
        sim.Step();

        Assert.Equal(2, world.Tick);
        Assert.Equal(new Vec2(10, 20), ship.Position);
    }

    [Fact]
    public void Snapshot_reports_ship_position()
    {
        var world = new World(new SimConfig());
        var ship = world.AddShip("a", new Vec2(5, 6));
        var sim = new Simulation(world);
        sim.Step();

        var snapshot = sim.Sense(ship.Id);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(new ShipState(ship.Id, "a", new Vec2(5, 6)), snapshot.Self);
    }
}
