namespace Headless.Sim;

/// <summary>
/// Tunable rules of the simulation. Defaults are sized so a typical asteroid round trip takes a few minutes
/// of sim time at full throttle, which keeps bot latency irrelevant.
/// </summary>
public sealed record SimConfig
{
    /// <summary>Simulated seconds per tick. Independent of how fast the server runs ticks in wall-clock time.</summary>
    public double TickSeconds { get; init; } = 0.5;

    /// <summary>Intents accepted per ship per tick; the rest are rejected with <see cref="ErrorCodes.BudgetExceeded"/>.</summary>
    public int MaxIntentsPerTick { get; init; } = 4;

    public double ShipMaxAcceleration { get; init; } = 10;   // m/s² at full throttle
    public double ShipMaxFuel { get; init; } = 1000;
    public double FuelPerThrustSecond { get; init; } = 1;     // at full throttle, scales linearly with throttle
    public double ShipCargoCapacity { get; init; } = 100;     // ore units
    public long ShipStartingCredits { get; init; } = 100;

    public double MiningRatePerSecond { get; init; } = 2;     // ore units per second
    public double MiningRange { get; init; } = 500;           // m, from asteroid centre
    public double DockingRange { get; init; } = 500;          // m, from station centre

    /// <summary>Maximum speed relative to the target for docking or starting to mine.</summary>
    public double MaxInteractionSpeed { get; init; } = 5;

    public double SensorRange { get; init; } = 50_000;
}
