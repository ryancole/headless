namespace Headless.Sim;

public enum DeviceKind
{
    /// <summary>Pushes the ship forward, along its heading. Rating: thrust in newtons at full throttle.</summary>
    Engine = 1,

    /// <summary>Turns the ship. Rating: turn rate in radians per second at full setting; positive is counter-clockwise.</summary>
    Gyroscope = 2,
}

/// <summary>What a bot can ask a device to do.</summary>
public enum DeviceCommand
{
    /// <summary>Sets the device's <see cref="Device.Setting"/>. Out-of-range values are clamped.</summary>
    Set = 1,
}

public enum CommandOutcome
{
    Ok,
    UnknownDevice,
    UnknownCommand,

    /// <summary>NaN or infinite.</summary>
    InvalidValue,

    /// <summary>The ship has used its <see cref="SimConfig.CommandsPerTick"/> for this tick. Try again next tick.</summary>
    OverBudget,
}

/// <summary>
/// One command sent to a ship's device, as the ship remembers it. <paramref name="Id"/> counts up per ship;
/// <paramref name="Tick"/> is the tick the command was for (it takes effect then, if accepted).
/// </summary>
public sealed record CommandLogEntry(
    int ShipId, long Id, long Tick, int Device, DeviceKind Kind, DeviceCommand Command, double Value, CommandOutcome Outcome);

/// <summary>
/// A piece of ship hardware a bot controls, like the hardware on 0x10c's DCPU-16 bus. Every device has one setting,
/// a fraction of its <see cref="Rating"/>, which stays where the bot put it until the bot changes it.
/// </summary>
public sealed class Device
{
    public required DeviceKind Kind { get; init; }

    /// <summary>What the device does at a setting of 1, in the units given on <see cref="DeviceKind"/>.</summary>
    public required double Rating { get; init; }

    /// <summary>From <see cref="MinSetting"/> to 1. Engines don't run backwards; gyroscopes turn either way.</summary>
    public double Setting { get; internal set; }

    public double MinSetting => Kind == DeviceKind.Gyroscope ? -1 : 0;

    /// <summary>The device's output right now: newtons for an engine, radians per second for a gyroscope.</summary>
    public double Output => Setting * Rating;
}
