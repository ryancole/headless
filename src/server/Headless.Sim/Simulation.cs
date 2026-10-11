namespace Headless.Sim;

/// <summary>
/// The authoritative, deterministic game loop. Bots set their ships' devices with <see cref="Command"/>; each
/// <see cref="Step"/> turns and pushes every ship by its devices' settings (Newtonian, no drag) and moves it.
/// Not thread-safe: the host must serialize calls.
/// </summary>
public sealed class Simulation(World world)
{
    public World World { get; } = world;

    /// <summary>Advances the world by one tick.</summary>
    public void Step()
    {
        var dt = World.Config.TickSeconds;
        foreach (var ship in World.Ships.Values)
        {
            // Turn first, then thrust along the new heading. Semi-implicit Euler: velocity first, then position.
            var turnRate = ship.Devices.Where(d => d.Kind == DeviceKind.Gyroscope).Sum(d => d.Output);
            ship.Heading = WrapAngle(ship.Heading + turnRate * dt);

            var thrust = ship.Devices.Where(d => d.Kind == DeviceKind.Engine).Sum(d => d.Output);
            ship.Velocity += Vec2.FromAngle(ship.Heading) * (thrust / ship.Mass * dt);
            ship.Position += ship.Velocity * dt;

            ship.CommandsThisTick = 0;
        }
        World.Tick++;
    }

    /// <summary>
    /// A bot's command to one of its ship's devices. Takes effect from the next <see cref="Step"/>. Every command
    /// that reaches a device goes in the ship's <see cref="Ship.CommandLog"/>, refused or not.
    /// </summary>
    public CommandOutcome Command(int shipId, int deviceIndex, DeviceCommand command, double value)
    {
        var ship = World.Ships[shipId];
        if (deviceIndex < 0 || deviceIndex >= ship.Devices.Count) return CommandOutcome.UnknownDevice;

        var outcome = Apply(ship, ship.Devices[deviceIndex], command, value);
        ship.Record(World.Tick + 1, deviceIndex, command, value, outcome, World.Config.CommandLogLength);
        return outcome;
    }

    private CommandOutcome Apply(Ship ship, Device device, DeviceCommand command, double value)
    {
        if (command != DeviceCommand.Set) return CommandOutcome.UnknownCommand;
        if (!double.IsFinite(value)) return CommandOutcome.InvalidValue;
        if (ship.CommandsThisTick >= World.Config.CommandsPerTick) return CommandOutcome.OverBudget;

        ship.CommandsThisTick++;
        device.Setting = Math.Clamp(value, device.MinSetting, 1);
        return CommandOutcome.Ok;
    }

    public SensorSnapshot Sense(int shipId)
    {
        var ship = World.Ships[shipId];
        var devices = ship.Devices.Select(d => new DeviceState(d.Kind, d.Rating, d.Setting)).ToList();
        return new SensorSnapshot(World.Tick, ShipStateOf(ship), ship.Mass, devices);
    }

    public PlayerSnapshot SensePlayer(int playerId)
    {
        var player = World.Players[playerId];
        var owned = World.Ships.Values.Where(s => s.OwnerPlayerId == playerId).ToList();
        var log = owned.SelectMany(s => s.CommandLog).ToList();
        return new PlayerSnapshot(World.Tick, new PlayerState(player.Id, player.Name), owned.Select(ShipStateOf).ToList(), log);
    }

    private static ShipState ShipStateOf(Ship ship) =>
        new(ship.Id, ship.Name, ship.OwnerPlayerId, ship.Position, ship.Velocity, ship.Heading);

    /// <summary>Wraps an angle into [-π, π).</summary>
    private static double WrapAngle(double angle) => angle - 2 * Math.PI * Math.Floor((angle + Math.PI) / (2 * Math.PI));
}
