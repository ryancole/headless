namespace Headless.Sim;

public sealed class Ship
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public Vec2 Position { get; internal set; }
    public Vec2 Velocity { get; internal set; }

    /// <summary>Commanded thrust direction and magnitude, length 0..1. Persists until changed.</summary>
    public Vec2 Throttle { get; internal set; }

    public double Fuel { get; internal set; }
    public double Cargo { get; internal set; }
    public long Credits { get; internal set; }

    public int? DockedAtStationId { get; internal set; }
    public int? MiningAsteroidId { get; internal set; }

    public bool IsDocked => DockedAtStationId is not null;
}

public sealed class Asteroid
{
    public required int Id { get; init; }
    public required Vec2 Position { get; init; }
    public required double Radius { get; init; }
    public double Ore { get; internal set; }
}

public sealed class Station
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Vec2 Position { get; init; }
    public required double OrePrice { get; init; }   // credits paid per ore unit
    public required double FuelPrice { get; init; }  // credits charged per fuel unit
}
