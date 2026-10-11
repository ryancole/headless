namespace Headless.Sim;

/// <summary>What one ship knows at a given tick. Built server-side; never contains hidden data.</summary>
public sealed record SensorSnapshot(long Tick, ShipState Self, double Mass, IReadOnlyList<DeviceState> Devices);

public sealed record ShipState(int Id, string Name, int OwnerPlayerId, Vec2 Position, Vec2 Velocity, double Heading);

public sealed record DeviceState(DeviceKind Kind, double Rating, double Setting);

/// <summary>What one player knows at a given tick: themselves, and the ships they own.</summary>
public sealed record PlayerSnapshot(long Tick, PlayerState Self, IReadOnlyList<ShipState> Ships);

public sealed record PlayerState(int Id, string Name);
