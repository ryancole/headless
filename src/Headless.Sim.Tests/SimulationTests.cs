namespace Headless.Sim.Tests;

public class SimulationTests
{
    private static readonly SimConfig Config = new();

    private static (Simulation Sim, World World) EmptyWorld(SimConfig? config = null)
    {
        var world = new World(config ?? Config);
        return (new Simulation(world), world);
    }

    private static IReadOnlyDictionary<int, ShipTickReport> StepWith(Simulation sim, int shipId, params Intent[] intents)
    {
        foreach (var intent in intents) sim.Submit(shipId, intent);
        return sim.Step();
    }

    [Fact]
    public void Thrust_accelerates_ship_and_burns_fuel()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);

        StepWith(sim, ship.Id, new ThrustIntent(new Vec2(1, 0)));

        var dt = Config.TickSeconds;
        Assert.Equal(Config.ShipMaxAcceleration * dt, ship.Velocity.X, 9);
        Assert.Equal(0.5 * Config.ShipMaxAcceleration * dt * dt, ship.Position.X, 9);
        Assert.Equal(Config.ShipMaxFuel - Config.FuelPerThrustSecond * dt, ship.Fuel, 9);
    }

    [Fact]
    public void Throttle_persists_and_ship_coasts_after_cut()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);

        StepWith(sim, ship.Id, new ThrustIntent(new Vec2(0, 1)));
        sim.Step();
        var speed = ship.Velocity.Y;
        Assert.Equal(2 * Config.ShipMaxAcceleration * Config.TickSeconds, speed, 9);

        StepWith(sim, ship.Id, new ThrustIntent(Vec2.Zero));
        var before = ship.Position;
        sim.Step();
        Assert.Equal(speed, ship.Velocity.Y, 9);
        Assert.Equal(before.Y + speed * Config.TickSeconds, ship.Position.Y, 9);
    }

    [Fact]
    public void Throttle_is_clamped_to_unit_length()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);

        StepWith(sim, ship.Id, new ThrustIntent(new Vec2(30, 40)));

        Assert.Equal(1, ship.Throttle.Length, 9);
    }

    [Fact]
    public void Non_finite_throttle_is_rejected()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);

        var reports = StepWith(sim, ship.Id, new ThrustIntent(new Vec2(double.NaN, 0)));

        Assert.Equal(ErrorCodes.InvalidValue, Assert.Single(reports[ship.Id].Errors).Code);
        Assert.Equal(Vec2.Zero, ship.Velocity);
    }

    [Fact]
    public void Running_out_of_fuel_cuts_engines()
    {
        var (sim, world) = EmptyWorld(Config with { ShipMaxFuel = 0.75 });
        var ship = world.AddShip("a", Vec2.Zero);

        StepWith(sim, ship.Id, new ThrustIntent(new Vec2(1, 0)));
        var reports = sim.Step(); // needs 0.5 fuel, only 0.25 left

        Assert.Contains(reports[ship.Id].Events, e => e.Code == EventCodes.OutOfFuel);
        Assert.Equal(0, ship.Fuel);
        Assert.Equal(Vec2.Zero, ship.Throttle);
        var speed = ship.Velocity.X;
        sim.Step();
        Assert.Equal(speed, ship.Velocity.X, 9);
    }

    [Fact]
    public void Intents_beyond_budget_are_rejected()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);
        var intents = Enumerable.Range(0, Config.MaxIntentsPerTick + 2)
            .Select(_ => (Intent)new ThrustIntent(new Vec2(1, 0))).ToArray();

        var reports = StepWith(sim, ship.Id, intents);

        var errors = reports[ship.Id].Errors;
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ErrorCodes.BudgetExceeded, e.Code));
        Assert.Equal([Config.MaxIntentsPerTick, Config.MaxIntentsPerTick + 1], errors.Select(e => e.Index));
    }

    [Fact]
    public void Mining_requires_range_and_low_speed()
    {
        var (sim, world) = EmptyWorld();
        var asteroid = world.AddAsteroid(new Vec2(10_000, 0), 100, 500);
        var far = world.AddShip("far", Vec2.Zero);
        var fast = world.AddShip("fast", new Vec2(10_000, 100), new Vec2(50, 0));

        sim.Submit(far.Id, new MineIntent(asteroid.Id));
        sim.Submit(fast.Id, new MineIntent(asteroid.Id));
        var reports = sim.Step();

        Assert.Equal(ErrorCodes.OutOfRange, Assert.Single(reports[far.Id].Errors).Code);
        Assert.Equal(ErrorCodes.TooFast, Assert.Single(reports[fast.Id].Errors).Code);
        Assert.Null(far.MiningAsteroidId);
        Assert.Null(fast.MiningAsteroidId);
    }

    [Fact]
    public void Mining_fills_cargo_then_stops()
    {
        var (sim, world) = EmptyWorld();
        var asteroid = world.AddAsteroid(new Vec2(100, 0), 50, 1_000);
        var ship = world.AddShip("a", Vec2.Zero);

        var first = StepWith(sim, ship.Id, new MineIntent(asteroid.Id));
        Assert.Contains(first[ship.Id].Events, e => e.Code == EventCodes.MiningStarted);
        Assert.Equal(Config.MiningRatePerSecond * Config.TickSeconds, ship.Cargo, 9);

        var ticksToFill = (int)Math.Ceiling(Config.ShipCargoCapacity / (Config.MiningRatePerSecond * Config.TickSeconds));
        IReadOnlyDictionary<int, ShipTickReport> last = first;
        for (var i = 1; i < ticksToFill; i++) last = sim.Step();

        Assert.Equal(Config.ShipCargoCapacity, ship.Cargo, 9);
        Assert.Equal(1_000 - Config.ShipCargoCapacity, asteroid.Ore, 9);
        Assert.Null(ship.MiningAsteroidId);
        Assert.Contains(last[ship.Id].Events, e => e.Code == EventCodes.MiningStopped);
    }

    [Fact]
    public void Mining_stops_when_asteroid_is_depleted()
    {
        var (sim, world) = EmptyWorld();
        var asteroid = world.AddAsteroid(new Vec2(100, 0), 50, 2.5);
        var ship = world.AddShip("a", Vec2.Zero);

        StepWith(sim, ship.Id, new MineIntent(asteroid.Id));
        sim.Step();
        sim.Step();

        Assert.Equal(0, asteroid.Ore);
        Assert.Equal(2.5, ship.Cargo, 9);
        Assert.Null(ship.MiningAsteroidId);

        var reports = StepWith(sim, ship.Id, new MineIntent(asteroid.Id));
        Assert.Equal(ErrorCodes.Depleted, Assert.Single(reports[ship.Id].Errors).Code);
    }

    [Fact]
    public void Mining_stops_when_ship_drifts_out_of_range()
    {
        var (sim, world) = EmptyWorld();
        var asteroid = world.AddAsteroid(Vec2.Zero, 50, 1_000);
        var ship = world.AddShip("a", new Vec2(Config.MiningRange - 1, 0), new Vec2(4, 0));

        StepWith(sim, ship.Id, new MineIntent(asteroid.Id));

        Assert.Null(ship.MiningAsteroidId);
        Assert.Equal(0, ship.Cargo);
    }

    [Fact]
    public void Dock_sell_refuel_undock()
    {
        var (sim, world) = EmptyWorld();
        var station = world.AddStation("s", Vec2.Zero, orePrice: 10, fuelPrice: 1);
        var ship = world.AddShip("a", new Vec2(200, 0), new Vec2(1, 0));
        ship.Cargo = 42.7;
        ship.Fuel = 900;
        ship.Credits = 0;

        var reports = StepWith(sim, ship.Id, new DockIntent(station.Id), new SellOreIntent(), new RefuelIntent());

        Assert.Empty(reports[ship.Id].Errors);
        Assert.True(ship.IsDocked);
        Assert.Equal(station.Position, ship.Position);
        Assert.Equal(Vec2.Zero, ship.Velocity);
        Assert.Equal(0.7, ship.Cargo, 9);
        Assert.Equal(Config.ShipMaxFuel, ship.Fuel, 9);
        Assert.Equal(420 - 100, ship.Credits);

        sim.Step(); // docked ships don't move
        Assert.Equal(station.Position, ship.Position);

        StepWith(sim, ship.Id, new UndockIntent());
        Assert.False(ship.IsDocked);
    }

    [Fact]
    public void Docking_requires_low_speed()
    {
        var (sim, world) = EmptyWorld();
        var station = world.AddStation("s", Vec2.Zero, 10, 1);
        var ship = world.AddShip("a", new Vec2(200, 0), new Vec2(-20, 0));

        var reports = StepWith(sim, ship.Id, new DockIntent(station.Id));

        Assert.Equal(ErrorCodes.TooFast, Assert.Single(reports[ship.Id].Errors).Code);
        Assert.False(ship.IsDocked);
    }

    [Fact]
    public void Refuel_is_limited_by_credits()
    {
        var (sim, world) = EmptyWorld();
        var station = world.AddStation("s", Vec2.Zero, 10, fuelPrice: 2);
        var ship = world.AddShipDocked("a", station);
        ship.Fuel = 0;
        ship.Credits = 50;

        StepWith(sim, ship.Id, new RefuelIntent());

        Assert.Equal(25, ship.Fuel, 9);
        Assert.Equal(0, ship.Credits);

        var reports = StepWith(sim, ship.Id, new RefuelIntent());
        Assert.Equal(ErrorCodes.InsufficientCredits, Assert.Single(reports[ship.Id].Errors).Code);
    }

    [Fact]
    public void Market_and_thrust_intents_respect_docked_state()
    {
        var (sim, world) = EmptyWorld();
        var station = world.AddStation("s", Vec2.Zero, 10, 1);
        var undocked = world.AddShip("a", new Vec2(5_000, 0));
        var docked = world.AddShipDocked("b", station);

        sim.Submit(undocked.Id, new SellOreIntent());
        sim.Submit(undocked.Id, new UndockIntent());
        sim.Submit(docked.Id, new ThrustIntent(new Vec2(1, 0)));
        var reports = sim.Step();

        Assert.Equal([ErrorCodes.NotDocked, ErrorCodes.NotDocked], reports[undocked.Id].Errors.Select(e => e.Code));
        Assert.Equal(ErrorCodes.Docked, Assert.Single(reports[docked.Id].Errors).Code);
    }

    [Fact]
    public void Release_control_holds_course()
    {
        var (sim, world) = EmptyWorld();
        var ship = world.AddShip("a", Vec2.Zero);
        StepWith(sim, ship.Id, new ThrustIntent(new Vec2(1, 0)));
        sim.Submit(ship.Id, new ThrustIntent(new Vec2(-1, 0)));

        sim.ReleaseControl(ship.Id);
        var speed = ship.Velocity.X;
        sim.Step();

        Assert.Equal(Vec2.Zero, ship.Throttle);
        Assert.Equal(speed, ship.Velocity.X, 9);
    }

    [Fact]
    public void Sensors_only_report_contacts_in_range_but_always_show_stations()
    {
        var (sim, world) = EmptyWorld(Config with { SensorRange = 1_000 });
        var station = world.AddStation("s", new Vec2(100_000, 0), 10, 1);
        var near = world.AddAsteroid(new Vec2(500, 0), 50, 100);
        world.AddAsteroid(new Vec2(5_000, 0), 50, 100);
        var self = world.AddShip("a", Vec2.Zero);
        var other = world.AddShip("b", new Vec2(0, 900));
        world.AddShip("c", new Vec2(0, 9_000));

        var snapshot = sim.Sense(self.Id);

        Assert.Equal(self.Id, snapshot.Self.Id);
        Assert.Equal(station.Id, Assert.Single(snapshot.Stations).Id);
        Assert.Equal(near.Id, Assert.Single(snapshot.Asteroids).Id);
        Assert.Equal(other.Id, Assert.Single(snapshot.Ships).Id);
    }

    [Fact]
    public void Same_seed_and_inputs_produce_identical_worlds()
    {
        static Simulation Run()
        {
            var world = WorldGenerator.CreateStarterSystem(seed: 1234);
            var station = world.Stations.Values.Single();
            var ship = world.AddShipDocked("a", station);
            var sim = new Simulation(world);
            sim.Submit(ship.Id, new UndockIntent());
            for (var t = 0; t < 200; t++)
            {
                sim.Submit(ship.Id, new ThrustIntent(new Vec2(Math.Cos(t * 0.1), Math.Sin(t * 0.07))));
                sim.Step();
            }
            return sim;
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.World.Tick, b.World.Tick);
        Assert.Equal(a.Sense(a.World.Ships.Keys.Single()), b.Sense(b.World.Ships.Keys.Single()), SnapshotComparer.Instance);
    }

    /// <summary>Records holding lists don't compare by value, so compare the parts that matter.</summary>
    private sealed class SnapshotComparer : IEqualityComparer<SensorSnapshot>
    {
        public static readonly SnapshotComparer Instance = new();
        public bool Equals(SensorSnapshot? x, SensorSnapshot? y) =>
            x!.Tick == y!.Tick && x.Self == y.Self
            && x.Stations.SequenceEqual(y.Stations) && x.Asteroids.SequenceEqual(y.Asteroids) && x.Ships.SequenceEqual(y.Ships);
        public int GetHashCode(SensorSnapshot obj) => obj.Tick.GetHashCode();
    }
}
