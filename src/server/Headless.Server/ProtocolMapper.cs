using Headless.Protocol;
using Headless.Sim;

namespace Headless.Server;

/// <summary>Translates sim types to wire types.</summary>
public static class ProtocolMapper
{
    public static ShipData ToData(ShipState s) => new(s.Id, s.OwnerPlayerId, s.Position.X, s.Position.Y);
}
