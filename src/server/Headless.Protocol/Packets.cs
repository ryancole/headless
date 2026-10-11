namespace Headless.Protocol;

// The Headless wire protocol: small binary packets over UDP. See docs/protocol.md for the byte layout.
// Types 0x01–0x7F travel client -> server; 0x80–0xFF travel server -> client.

public enum PacketType : byte
{
    // Client -> server
    ConnectRequest = 0x01,
    KeepAlive = 0x02,
    Disconnect = 0x03,
    DeviceCommand = 0x04,

    // Server -> client
    ConnectAccepted = 0x81,
    ConnectDenied = 0x82,
    ShipSnapshot = 0x83,
    PlayerSnapshot = 0x84,
    Disconnected = 0x85,
    CommandAck = 0x86,
}

/// <summary>What a client connects as. Decides which token it must present and which snapshots it gets.</summary>
public enum ClientRole : byte
{
    /// <summary>Flies a ship. Authenticates with the ship's bot token.</summary>
    Bot = 1,

    /// <summary>A human player. Authenticates with the player's own token.</summary>
    Player = 2,
}

public enum DenyReason : byte
{
    UnsupportedVersion = 1,
    InvalidToken = 2,
}

public enum DisconnectReason : byte
{
    ServerShutdown = 1,
    Superseded = 2,
    TimedOut = 3,
}

/// <summary>Ship hardware a bot controls. Each device has one setting, a fraction of its rating.</summary>
public enum DeviceKind : byte
{
    /// <summary>Pushes the ship along its heading. Rating: newtons at full throttle. Setting: 0 to 1.</summary>
    Engine = 1,

    /// <summary>Turns the ship. Rating: radians per second at full. Setting: -1 to 1, positive is counter-clockwise.</summary>
    Gyroscope = 2,
}

/// <summary>Command codes for <see cref="DeviceCommand.Command"/>.</summary>
public static class DeviceCommands
{
    /// <summary>Sets the device's setting to the command's value. Out-of-range values are clamped.</summary>
    public const byte Set = 1;
}

public enum CommandResult : byte
{
    Ok = 1,

    /// <summary>This command, or a newer one for the same device, was already applied. Nothing changed.</summary>
    Stale = 2,

    UnknownDevice = 3,
    UnknownCommand = 4,

    /// <summary>The value was NaN or infinite.</summary>
    InvalidValue = 5,

    /// <summary>The ship has used its command budget for this tick. Resend it; it'll fit next tick.</summary>
    OverBudget = 6,
}

/// <summary>A decoded packet. Each subtype maps to exactly one <see cref="PacketType"/>.</summary>
public abstract record Packet;

// ---- Client -> server ----

/// <summary>
/// Opens a connection. <paramref name="Nonce"/> is chosen by the client and echoed in the reply, so the client can
/// match replies to attempts. Clients resend this until they get an answer.
/// </summary>
public sealed record ConnectRequest(ushort Version, ClientRole Role, ulong Nonce, string Token) : Packet;

/// <summary>Tells the server the client is still there. Send at least every <see cref="ProtocolConstants.KeepAliveInterval"/>.</summary>
public sealed record KeepAlive(ulong Session) : Packet;

/// <summary>The client is leaving.</summary>
public sealed record Disconnect(ulong Session) : Packet;

/// <summary>
/// A bot's command to one of its ship's devices. <paramref name="Sequence"/> counts up from 1 within a session; resend
/// until a <see cref="CommandAck"/> for it arrives. <paramref name="Device"/> and <paramref name="Command"/> are raw
/// bytes, not enums, so an unknown one gets a <see cref="CommandResult"/> rather than being dropped as malformed.
/// </summary>
public sealed record DeviceCommand(ulong Session, uint Sequence, byte Device, byte Command, double Value) : Packet;

// ---- Server -> client ----

/// <param name="Session">Identifies the connection; every later packet in either direction carries it.</param>
/// <param name="SubjectId">The ship id for a bot, the player id for a player.</param>
/// <param name="SubjectName">The ship's name for a bot, the player's name for a player.</param>
/// <param name="TickSeconds">Simulated seconds per tick.</param>
public sealed record ConnectAccepted(ulong Nonce, ulong Session, int SubjectId, string SubjectName, float TickSeconds) : Packet;

public sealed record ConnectDenied(ulong Nonce, DenyReason Reason) : Packet;

/// <summary>
/// Sent to a bot every tick: its ship, the ship's mass in kilograms and its devices, indexed as in
/// <see cref="DeviceCommand.Device"/>. Unreliable: clients should ignore snapshots older than the newest seen.
/// </summary>
public sealed record ShipSnapshot(ulong Session, long Tick, ShipData Ship, double Mass, IReadOnlyList<DeviceData> Devices) : Packet
{
    // Records compare lists by reference; compare contents so equal snapshots are equal.
    public bool Equals(ShipSnapshot? other) =>
        other is not null && Session == other.Session && Tick == other.Tick && Ship == other.Ship
        && Mass.Equals(other.Mass) && Devices.SequenceEqual(other.Devices);

    public override int GetHashCode() => HashCode.Combine(Session, Tick, Ship, Devices.Count);
}

/// <summary>Sent to a player every tick: the ships they own. Unreliable, like <see cref="ShipSnapshot"/>.</summary>
public sealed record PlayerSnapshot(ulong Session, long Tick, IReadOnlyList<ShipData> Ships) : Packet
{
    // Records compare lists by reference; compare contents so equal snapshots are equal.
    public bool Equals(PlayerSnapshot? other) =>
        other is not null && Session == other.Session && Tick == other.Tick && Ships.SequenceEqual(other.Ships);

    public override int GetHashCode() => HashCode.Combine(Session, Tick, Ships.Count);
}

/// <summary>The server ended the connection.</summary>
public sealed record Disconnected(ulong Session, DisconnectReason Reason) : Packet;

/// <summary>The answer to a <see cref="DeviceCommand"/>. Sent for every copy received, so a resend gets one too.</summary>
public sealed record CommandAck(ulong Session, uint Sequence, CommandResult Result) : Packet;

/// <summary>
/// A ship as it appears on the wire. Positions are metres on a 2D plane, velocities metres per second, and the heading
/// is radians: 0 points along +x, counter-clockwise, in [-π, π).
/// </summary>
public readonly record struct ShipData(int Id, int OwnerPlayerId, double X, double Y, double VelocityX, double VelocityY, double Heading);

/// <param name="Rating">What the device does at a setting of 1; units depend on <paramref name="Kind"/>.</param>
/// <param name="Setting">Where the bot last set it.</param>
public readonly record struct DeviceData(DeviceKind Kind, double Rating, double Setting);
