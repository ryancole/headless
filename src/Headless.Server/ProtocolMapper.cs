using Headless.Protocol.V1;
using Headless.Sim;

namespace Headless.Server;

/// <summary>Translates sim types to the v1 wire types.</summary>
public static class ProtocolMapper
{
    public static GameRules ToRules(SimConfig c) => new(c.TickSeconds);

    public static SnapshotMessage ToSnapshot(SensorSnapshot s) => new(s.Tick, ToDto(s.Self));

    public static PlayerSnapshotMessage ToPlayerSnapshot(PlayerSnapshot s) => new(
        s.Tick,
        new PlayerDto(s.Self.Id, s.Self.Name),
        s.Ships.Select(ToDto).ToList());

    private static ShipDto ToDto(ShipState s) =>
        new(s.Id, s.Name, s.OwnerPlayerId, new Vector(s.Position.X, s.Position.Y));
}
