namespace Headless.Sim;

/// <summary>What one ship knows at a given tick. Built server-side; never contains hidden data.</summary>
public sealed record SensorSnapshot(long Tick, ShipState Self);

public sealed record ShipState(int Id, string Name, Vec2 Position);
