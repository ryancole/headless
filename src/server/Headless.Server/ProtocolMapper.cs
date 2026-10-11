using Headless.Protocol;
using Headless.Sim;

namespace Headless.Server;

/// <summary>Translates between sim types and wire types.</summary>
public static class ProtocolMapper
{
    public static ShipData ToData(ShipState s) =>
        new(s.Id, s.OwnerPlayerId, s.Position.X, s.Position.Y, s.Velocity.X, s.Velocity.Y, s.Heading);

    public static DeviceData ToData(DeviceState d) => new((Protocol.DeviceKind)d.Kind, d.Rating, d.Setting);

    public static Protocol.ShipSnapshot ToPacket(ulong session, SensorSnapshot s) =>
        new(session, s.Tick, ToData(s.Self), s.Mass, s.Devices.Select(ToData).ToList());

    public static ShipLogEntry ToData(CommandLogEntry e) =>
        new(e.ShipId, (uint)e.Id, e.Tick, (byte)e.Device, (Protocol.DeviceKind)e.Kind, (byte)e.Command, e.Value, ToResult(e.Outcome));

    public static CommandResult ToResult(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Ok => CommandResult.Ok,
        CommandOutcome.UnknownDevice => CommandResult.UnknownDevice,
        CommandOutcome.UnknownCommand => CommandResult.UnknownCommand,
        CommandOutcome.InvalidValue => CommandResult.InvalidValue,
        CommandOutcome.OverBudget => CommandResult.OverBudget,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
