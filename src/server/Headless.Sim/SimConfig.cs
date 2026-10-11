namespace Headless.Sim;

/// <summary>Tunable rules of the simulation.</summary>
public sealed record SimConfig
{
    /// <summary>Simulated seconds per tick. Independent of how fast the server runs ticks in wall-clock time.</summary>
    public double TickSeconds { get; init; } = 0.5;

    /// <summary>How many device commands each ship accepts per tick. Rewards good decisions over many decisions.</summary>
    public int CommandsPerTick { get; init; } = 4;

    /// <summary>How many recent commands each ship keeps in its <see cref="Ship.CommandLog"/>.</summary>
    public int CommandLogLength { get; init; } = 10;

    // Every ship is fitted the same for now: one engine and one gyroscope. Full throttle accelerates at 2 m/s².

    /// <summary>In kilograms.</summary>
    public double ShipMass { get; init; } = 10_000;

    /// <summary>Newtons at full throttle.</summary>
    public double EngineThrust { get; init; } = 20_000;

    /// <summary>Radians per second at full setting.</summary>
    public double GyroscopeTurnRate { get; init; } = 0.5;
}
