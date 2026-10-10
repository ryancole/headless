namespace Headless.Sim;

/// <summary>What one ship knows about the world at a given tick. Built server-side; never contains hidden data.</summary>
public sealed record SensorSnapshot(
    long Tick,
    ShipState Self,
    IReadOnlyList<StationInfo> Stations,
    IReadOnlyList<AsteroidContact> Asteroids,
    IReadOnlyList<ShipContact> Ships);

public sealed record ShipState(
    int Id,
    string Name,
    Vec2 Position,
    Vec2 Velocity,
    Vec2 Throttle,
    double Fuel,
    double Cargo,
    long Credits,
    int? DockedAtStationId,
    int? MiningAsteroidId);

/// <summary>Stations are on every ship's charts, so they are always visible regardless of sensor range.</summary>
public sealed record StationInfo(int Id, string Name, Vec2 Position, double OrePrice, double FuelPrice);

public sealed record AsteroidContact(int Id, Vec2 Position, double Radius, double Ore);

public sealed record ShipContact(int Id, string Name, Vec2 Position, Vec2 Velocity);
