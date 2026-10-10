namespace Headless.Sim;

/// <summary>Tunable rules of the simulation.</summary>
public sealed record SimConfig
{
    /// <summary>Simulated seconds per tick. Independent of how fast the server runs ticks in wall-clock time.</summary>
    public double TickSeconds { get; init; } = 0.5;
}
