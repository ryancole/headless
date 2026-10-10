using Headless.Protocol.V1;
using Headless.Sim;

namespace Headless.Server;

/// <summary>Translates sim types to the v1 wire types.</summary>
public static class ProtocolMapper
{
    public static ShipRules ToRules(SimConfig c) => new(c.TickSeconds);

    public static SnapshotMessage ToSnapshot(SensorSnapshot s) =>
        new(s.Tick, new ShipDto(s.Self.Id, s.Self.Name, new Vector(s.Self.Position.X, s.Self.Position.Y)));
}
