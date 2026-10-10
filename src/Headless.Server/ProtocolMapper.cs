using Headless.Protocol.V1;
using Headless.Sim;

namespace Headless.Server;

/// <summary>Translates between sim types and the v1 wire types.</summary>
public static class ProtocolMapper
{
    public static Intent ToIntent(IntentDto dto) => dto switch
    {
        ThrustDto t => new ThrustIntent(new Vec2(t.X, t.Y)),
        MineDto m => new MineIntent(m.AsteroidId),
        StopMiningDto => new StopMiningIntent(),
        DockDto d => new DockIntent(d.StationId),
        UndockDto => new UndockIntent(),
        SellDto => new SellOreIntent(),
        RefuelDto => new RefuelIntent(),
        _ => throw new ArgumentOutOfRangeException(nameof(dto), dto.GetType().Name, "Unmapped intent."),
    };

    public static ShipRules ToRules(SimConfig c) => new(
        c.TickSeconds, c.MaxIntentsPerTick, c.ShipMaxAcceleration, c.ShipMaxFuel, c.FuelPerThrustSecond,
        c.ShipCargoCapacity, c.MiningRatePerSecond, c.MiningRange, c.DockingRange, c.MaxInteractionSpeed, c.SensorRange);

    public static SnapshotMessage ToSnapshot(SensorSnapshot s, ShipTickReport report) => new(
        s.Tick,
        new ShipDto(s.Self.Id, s.Self.Name, V(s.Self.Position), V(s.Self.Velocity), V(s.Self.Throttle), s.Self.Fuel,
            s.Self.Cargo, s.Self.Credits, s.Self.DockedAtStationId, s.Self.MiningAsteroidId),
        s.Stations.Select(x => new StationDto(x.Id, x.Name, V(x.Position), x.OrePrice, x.FuelPrice)).ToList(),
        s.Asteroids.Select(x => new AsteroidDto(x.Id, V(x.Position), x.Radius, x.Ore)).ToList(),
        s.Ships.Select(x => new ContactDto(x.Id, x.Name, V(x.Position), V(x.Velocity))).ToList(),
        report.Events.Select(e => new EventDto(e.Code, e.Message)).ToList(),
        report.Errors.Select(e => new IntentErrorDto(e.Index, IntentName(e.Intent), e.Code, e.Message)).ToList());

    private static Vector V(Vec2 v) => new(v.X, v.Y);

    private static string IntentName(Intent intent) => intent switch
    {
        ThrustIntent => "thrust",
        MineIntent => "mine",
        StopMiningIntent => "stop_mining",
        DockIntent => "dock",
        UndockIntent => "undock",
        SellOreIntent => "sell",
        RefuelIntent => "refuel",
        _ => intent.GetType().Name,
    };
}
