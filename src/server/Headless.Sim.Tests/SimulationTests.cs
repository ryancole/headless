namespace Headless.Sim.Tests;

public class SimulationTests
{
    private static (World World, Player Player, Ship Ship) WorldWithPlayerAndShip(Vec2 shipPosition)
    {
        var world = new World(new SimConfig());
        var player = world.AddPlayer("p");
        var ship = world.AddShip("s", shipPosition, player);
        return (world, player, ship);
    }

    [Fact]
    public void Players_and_ships_share_one_id_space()
    {
        var (_, player, ship) = WorldWithPlayerAndShip(Vec2.Zero);

        Assert.NotEqual(player.Id, ship.Id);
    }

    [Fact]
    public void Ships_record_their_owner()
    {
        var (_, player, ship) = WorldWithPlayerAndShip(Vec2.Zero);

        Assert.Equal(player.Id, ship.OwnerPlayerId);
    }

    [Fact]
    public void Ships_need_an_owner_from_the_same_world()
    {
        var world = new World(new SimConfig());
        world.AddPlayer("local");
        var stranger = new World(new SimConfig()).AddPlayer("stranger"); // same id as "local"

        Assert.Throws<ArgumentException>(() => world.AddShip("s", Vec2.Zero, stranger));
        Assert.Empty(world.Ships);
    }

    [Fact]
    public void Step_advances_the_tick_and_ships_stay_put()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(new Vec2(10, 20));
        var sim = new Simulation(world);

        sim.Step();
        sim.Step();

        Assert.Equal(2, world.Tick);
        Assert.Equal(new Vec2(10, 20), ship.Position);
    }

    [Fact]
    public void Ship_snapshot_reports_position_and_owner()
    {
        var (world, player, ship) = WorldWithPlayerAndShip(new Vec2(5, 6));
        var sim = new Simulation(world);
        sim.Step();

        var snapshot = sim.Sense(ship.Id);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(new ShipState(ship.Id, "s", player.Id, new Vec2(5, 6)), snapshot.Self);
    }

    [Fact]
    public void Player_snapshot_lists_only_the_ships_they_own()
    {
        var (world, player, ship) = WorldWithPlayerAndShip(new Vec2(5, 6));
        var other = world.AddPlayer("other");
        world.AddShip("not mine", Vec2.Zero, other);
        var sim = new Simulation(world);

        var snapshot = sim.SensePlayer(player.Id);

        Assert.Equal(new PlayerState(player.Id, "p"), snapshot.Self);
        Assert.Equal([sim.Sense(ship.Id).Self], snapshot.Ships);
    }

    [Fact]
    public void Player_snapshot_is_empty_for_a_player_without_ships()
    {
        var world = new World(new SimConfig());
        var player = world.AddPlayer("p");

        Assert.Empty(new Simulation(world).SensePlayer(player.Id).Ships);
    }
}
