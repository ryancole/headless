namespace Headless.Sim.Tests;

public class SimulationTests
{
    // Device indexes on a standard ship (see World.AddShip).
    private const int Engine = 0, Gyroscope = 1;

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
    public void Ship_snapshot_reports_position_owner_and_devices()
    {
        var (world, player, ship) = WorldWithPlayerAndShip(new Vec2(5, 6));
        var sim = new Simulation(world);
        sim.Step();

        var snapshot = sim.Sense(ship.Id);

        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(new ShipState(ship.Id, "s", player.Id, new Vec2(5, 6), Vec2.Zero, 0), snapshot.Self);
        Assert.Equal(10_000, snapshot.Mass);
        Assert.Equal([new DeviceState(DeviceKind.Engine, 20_000, 0), new DeviceState(DeviceKind.Gyroscope, 0.5, 0)], snapshot.Devices);
    }

    [Fact]
    public void The_engine_accelerates_the_ship_along_its_heading()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);

        Assert.Equal(CommandOutcome.Ok, sim.Command(ship.Id, Engine, DeviceCommand.Set, 1));
        sim.Step(); // 20 kN on 10 t for 0.5 s: +1 m/s, then moves 0.5 m
        sim.Step();

        Assert.Equal(new Vec2(2, 0), ship.Velocity);
        Assert.Equal(new Vec2(1.5, 0), ship.Position);
    }

    [Fact]
    public void Ships_coast_when_the_engine_stops()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);
        sim.Command(ship.Id, Engine, DeviceCommand.Set, 1);
        sim.Step();

        sim.Command(ship.Id, Engine, DeviceCommand.Set, 0);
        sim.Step();
        sim.Step();

        Assert.Equal(new Vec2(1, 0), ship.Velocity);
        Assert.Equal(new Vec2(1.5, 0), ship.Position);
    }

    [Fact]
    public void The_gyroscope_turns_the_ship_and_the_heading_wraps()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);

        sim.Command(ship.Id, Gyroscope, DeviceCommand.Set, -1); // clockwise at 0.5 rad/s
        for (var i = 0; i < 14; i++) sim.Step();                // 7 s: -3.5 rad, past -π

        Assert.Equal(2 * Math.PI - 3.5, ship.Heading, precision: 9);
    }

    [Fact]
    public void Thrust_follows_the_heading()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);
        sim.Command(ship.Id, Gyroscope, DeviceCommand.Set, 1);
        sim.Command(ship.Id, Engine, DeviceCommand.Set, 1);

        sim.Step(); // turns 0.25 rad, then thrusts that way

        Assert.Equal(Math.Cos(0.25), ship.Velocity.X, precision: 9);
        Assert.Equal(Math.Sin(0.25), ship.Velocity.Y, precision: 9);
    }

    [Theory]
    [InlineData(Engine, 2.0, 1.0)]
    [InlineData(Engine, -1.0, 0.0)] // engines don't run backwards
    [InlineData(Gyroscope, -3.0, -1.0)]
    public void Settings_are_clamped_to_the_device_range(int device, double value, double expected)
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);

        Assert.Equal(CommandOutcome.Ok, new Simulation(world).Command(ship.Id, device, DeviceCommand.Set, value));
        Assert.Equal(expected, ship.Devices[device].Setting);
    }

    [Fact]
    public void Bad_commands_are_rejected_and_change_nothing()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);

        Assert.Equal(CommandOutcome.UnknownDevice, sim.Command(ship.Id, 2, DeviceCommand.Set, 1));
        Assert.Equal(CommandOutcome.UnknownDevice, sim.Command(ship.Id, -1, DeviceCommand.Set, 1));
        Assert.Equal(CommandOutcome.UnknownCommand, sim.Command(ship.Id, Engine, (DeviceCommand)99, 1));
        Assert.Equal(CommandOutcome.InvalidValue, sim.Command(ship.Id, Engine, DeviceCommand.Set, double.NaN));
        Assert.Equal(CommandOutcome.InvalidValue, sim.Command(ship.Id, Engine, DeviceCommand.Set, double.PositiveInfinity));
        Assert.All(ship.Devices, d => Assert.Equal(0, d.Setting));
    }

    [Fact]
    public void Each_ship_gets_a_command_budget_per_tick()
    {
        var (world, _, ship) = WorldWithPlayerAndShip(Vec2.Zero);
        var sim = new Simulation(world);

        for (var i = 0; i < world.Config.CommandsPerTick; i++)
            Assert.Equal(CommandOutcome.Ok, sim.Command(ship.Id, Engine, DeviceCommand.Set, 0.5));
        Assert.Equal(CommandOutcome.OverBudget, sim.Command(ship.Id, Engine, DeviceCommand.Set, 1));
        Assert.Equal(0.5, ship.Devices[Engine].Setting);

        sim.Step();
        Assert.Equal(CommandOutcome.Ok, sim.Command(ship.Id, Engine, DeviceCommand.Set, 1));
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
