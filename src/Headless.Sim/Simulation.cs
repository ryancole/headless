namespace Headless.Sim;

/// <summary>
/// The authoritative, deterministic game loop. Controllers <see cref="Submit"/> intents at any time; each
/// <see cref="Step"/> applies the queued intents (within each ship's per-tick budget), integrates physics and
/// resolves mining. Not thread-safe: the host must serialize calls.
/// </summary>
public sealed class Simulation(World world)
{
    private readonly SortedDictionary<int, List<Intent>> _pending = [];

    public World World { get; } = world;
    private SimConfig Config => World.Config;

    /// <summary>Queues an intent for the next <see cref="Step"/>. Validation happens when the tick runs.</summary>
    public void Submit(int shipId, Intent intent)
    {
        if (!World.Ships.ContainsKey(shipId))
            throw new ArgumentException($"Unknown ship {shipId}.", nameof(shipId));

        if (!_pending.TryGetValue(shipId, out var queue))
            _pending[shipId] = queue = [];
        queue.Add(intent);
    }

    /// <summary>
    /// Offline fallback for a ship whose controller disconnected: hold course (cut thrust and coast).
    /// Docking and mining continue as they were.
    /// </summary>
    public void ReleaseControl(int shipId)
    {
        _pending.Remove(shipId);
        World.Ships[shipId].Throttle = Vec2.Zero;
    }

    /// <summary>Advances the world by one tick and returns what happened to each ship.</summary>
    public IReadOnlyDictionary<int, ShipTickReport> Step()
    {
        var reports = World.Ships.Keys.ToDictionary(id => id, _ => new ShipTickReport());

        foreach (var (shipId, intents) in _pending)
            ApplyIntents(World.Ships[shipId], intents, reports[shipId]);
        _pending.Clear();

        foreach (var ship in World.Ships.Values)
        {
            Integrate(ship, reports[ship.Id]);
            Mine(ship, reports[ship.Id]);
        }

        World.Tick++;
        return reports;
    }

    public SensorSnapshot Sense(int shipId)
    {
        var self = World.Ships[shipId];
        var rangeSq = Config.SensorRange * Config.SensorRange;
        bool InRange(Vec2 p) => (p - self.Position).LengthSquared <= rangeSq;

        return new SensorSnapshot(
            World.Tick,
            new ShipState(self.Id, self.Name, self.Position, self.Velocity, self.Throttle, self.Fuel, self.Cargo,
                self.Credits, self.DockedAtStationId, self.MiningAsteroidId),
            World.Stations.Values.Select(s => new StationInfo(s.Id, s.Name, s.Position, s.OrePrice, s.FuelPrice)).ToList(),
            World.Asteroids.Values.Where(a => InRange(a.Position))
                .Select(a => new AsteroidContact(a.Id, a.Position, a.Radius, a.Ore)).ToList(),
            World.Ships.Values.Where(s => s.Id != shipId && InRange(s.Position))
                .Select(s => new ShipContact(s.Id, s.Name, s.Position, s.Velocity)).ToList());
    }

    private void ApplyIntents(Ship ship, List<Intent> intents, ShipTickReport report)
    {
        for (var i = 0; i < intents.Count; i++)
        {
            var intent = intents[i];
            var error = i < Config.MaxIntentsPerTick
                ? Apply(ship, intent, report)
                : (ErrorCodes.BudgetExceeded, $"At most {Config.MaxIntentsPerTick} intents per tick.");

            if (error is { } e)
                report.Errors.Add(new IntentError(i, intent, e.Code, e.Message));
        }
    }

    /// <returns>An error if the intent was rejected, otherwise null.</returns>
    private (string Code, string Message)? Apply(Ship ship, Intent intent, ShipTickReport report)
    {
        switch (intent)
        {
            case ThrustIntent { Throttle: var throttle }:
                if (!throttle.IsFinite) return (ErrorCodes.InvalidValue, "Throttle must be finite.");
                if (ship.IsDocked) return (ErrorCodes.Docked, "Undock before thrusting.");
                ship.Throttle = throttle.ClampLength(1);
                return null;

            case MineIntent { AsteroidId: var asteroidId }:
                if (!World.Asteroids.TryGetValue(asteroidId, out var asteroid))
                    return (ErrorCodes.UnknownTarget, $"No asteroid {asteroidId}.");
                if (ship.IsDocked) return (ErrorCodes.Docked, "Undock before mining.");
                if (asteroid.Ore <= 0) return (ErrorCodes.Depleted, "Asteroid has no ore left.");
                if (ship.Cargo >= Config.ShipCargoCapacity) return (ErrorCodes.CargoFull, "Cargo hold is full.");
                if (Vec2.Distance(ship.Position, asteroid.Position) > Config.MiningRange)
                    return (ErrorCodes.OutOfRange, $"Must be within {Config.MiningRange} m of the asteroid.");
                if (ship.Velocity.Length > Config.MaxInteractionSpeed)
                    return (ErrorCodes.TooFast, $"Relative speed must be at most {Config.MaxInteractionSpeed} m/s.");
                ship.MiningAsteroidId = asteroid.Id;
                report.Events.Add(new ShipEvent(EventCodes.MiningStarted, $"Mining asteroid {asteroid.Id}."));
                return null;

            case StopMiningIntent:
                StopMining(ship, report, "Stopped by controller.");
                return null;

            case DockIntent { StationId: var stationId }:
                if (!World.Stations.TryGetValue(stationId, out var station))
                    return (ErrorCodes.UnknownTarget, $"No station {stationId}.");
                if (ship.IsDocked) return (ErrorCodes.Docked, "Already docked.");
                if (Vec2.Distance(ship.Position, station.Position) > Config.DockingRange)
                    return (ErrorCodes.OutOfRange, $"Must be within {Config.DockingRange} m of the station.");
                if (ship.Velocity.Length > Config.MaxInteractionSpeed)
                    return (ErrorCodes.TooFast, $"Relative speed must be at most {Config.MaxInteractionSpeed} m/s.");
                StopMining(ship, report, "Docked.");
                ship.DockedAtStationId = station.Id;
                ship.Position = station.Position;
                ship.Velocity = Vec2.Zero;
                ship.Throttle = Vec2.Zero;
                report.Events.Add(new ShipEvent(EventCodes.Docked, $"Docked at {station.Name}."));
                return null;

            case UndockIntent:
                if (!ship.IsDocked) return (ErrorCodes.NotDocked, "Not docked.");
                ship.DockedAtStationId = null;
                report.Events.Add(new ShipEvent(EventCodes.Undocked, "Undocked."));
                return null;

            case SellOreIntent:
            {
                if (DockedStation(ship) is not { } market) return (ErrorCodes.NotDocked, "Dock at a station to sell.");
                var units = Math.Floor(ship.Cargo);
                if (units <= 0) return null;
                var earned = (long)Math.Floor(units * market.OrePrice);
                ship.Cargo -= units;
                ship.Credits += earned;
                report.Events.Add(new ShipEvent(EventCodes.OreSold, $"Sold {units} ore for {earned} credits."));
                return null;
            }

            case RefuelIntent:
            {
                if (DockedStation(ship) is not { } depot) return (ErrorCodes.NotDocked, "Dock at a station to refuel.");
                var missing = Config.ShipMaxFuel - ship.Fuel;
                if (missing <= 0) return null;
                if (ship.Credits <= 0) return (ErrorCodes.InsufficientCredits, "No credits to buy fuel.");
                var units = depot.FuelPrice > 0 ? Math.Min(missing, ship.Credits / depot.FuelPrice) : missing;
                var cost = Math.Min(ship.Credits, (long)Math.Ceiling(units * depot.FuelPrice));
                ship.Fuel += units;
                ship.Credits -= cost;
                report.Events.Add(new ShipEvent(EventCodes.Refueled, $"Bought {units:0.##} fuel for {cost} credits."));
                return null;
            }

            default:
                return (ErrorCodes.InvalidValue, $"Unsupported intent {intent.GetType().Name}.");
        }
    }

    private void Integrate(Ship ship, ShipTickReport report)
    {
        if (ship.IsDocked) return;

        var dt = Config.TickSeconds;
        var throttle = ship.Throttle;
        var fuelNeeded = throttle.Length * Config.FuelPerThrustSecond * dt;

        if (fuelNeeded > ship.Fuel)
        {
            // Burn what's left for a partial tick of thrust, then the engines cut out.
            throttle *= fuelNeeded > 0 ? ship.Fuel / fuelNeeded : 0;
            ship.Fuel = 0;
            ship.Throttle = Vec2.Zero;
            report.Events.Add(new ShipEvent(EventCodes.OutOfFuel, "Fuel exhausted; engines cut."));
        }
        else
        {
            ship.Fuel -= fuelNeeded;
        }

        // Exact for constant acceleration over the tick.
        var accel = throttle * Config.ShipMaxAcceleration;
        ship.Position += ship.Velocity * dt + accel * (0.5 * dt * dt);
        ship.Velocity += accel * dt;
    }

    private void Mine(Ship ship, ShipTickReport report)
    {
        if (ship.MiningAsteroidId is not { } asteroidId) return;
        var asteroid = World.Asteroids[asteroidId];

        if (Vec2.Distance(ship.Position, asteroid.Position) > Config.MiningRange)
        {
            StopMining(ship, report, "Drifted out of range.");
            return;
        }

        var amount = Math.Min(Config.MiningRatePerSecond * Config.TickSeconds,
            Math.Min(asteroid.Ore, Config.ShipCargoCapacity - ship.Cargo));
        asteroid.Ore -= amount;
        ship.Cargo += amount;

        if (ship.Cargo >= Config.ShipCargoCapacity) StopMining(ship, report, "Cargo hold full.");
        else if (asteroid.Ore <= 0) StopMining(ship, report, "Asteroid depleted.");
    }

    private static void StopMining(Ship ship, ShipTickReport report, string reason)
    {
        if (ship.MiningAsteroidId is null) return;
        ship.MiningAsteroidId = null;
        report.Events.Add(new ShipEvent(EventCodes.MiningStopped, reason));
    }

    private Station? DockedStation(Ship ship) =>
        ship.DockedAtStationId is { } id ? World.Stations[id] : null;
}
