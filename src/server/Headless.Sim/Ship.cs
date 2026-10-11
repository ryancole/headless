namespace Headless.Sim;

/// <summary>An autonomous ship, flown by a bot. Owned by one player; players (owner or not) can ride in it.</summary>
public sealed class Ship
{
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Every ship has an owner, and it must be a player in the same world.</summary>
    public required int OwnerPlayerId { get; init; }

    /// <summary>In kilograms.</summary>
    public required double Mass { get; init; }

    /// <summary>The ship's hardware. A bot addresses a device by its index in this list.</summary>
    public required IReadOnlyList<Device> Devices { get; init; }

    public Vec2 Position { get; internal set; }

    /// <summary>Metres per second. Space has no drag: the ship keeps moving until it thrusts the other way.</summary>
    public Vec2 Velocity { get; internal set; }

    /// <summary>Which way the ship points, in radians: 0 is +x, counter-clockwise, in [-π, π).</summary>
    public double Heading { get; internal set; }

    /// <summary>Commands accepted so far this tick, against <see cref="SimConfig.CommandsPerTick"/>.</summary>
    internal int CommandsThisTick { get; set; }
}
