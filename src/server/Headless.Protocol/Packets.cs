namespace Headless.Protocol;

// The Headless wire protocol: small binary packets over UDP. See docs/protocol.md for the byte layout.
// Types 0x01–0x7F travel client -> server; 0x80–0xFF travel server -> client.

public enum PacketType : byte
{
    // Client -> server
    ConnectRequest = 0x01,
    KeepAlive = 0x02,
    Disconnect = 0x03,

    // Server -> client
    ConnectAccepted = 0x81,
    ConnectDenied = 0x82,
    ShipSnapshot = 0x83,
    PlayerSnapshot = 0x84,
    Disconnected = 0x85,
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

// ---- Server -> client ----

/// <param name="Session">Identifies the connection; every later packet in either direction carries it.</param>
/// <param name="SubjectId">The ship id for a bot, the player id for a player.</param>
/// <param name="SubjectName">The ship's name for a bot, the player's name for a player.</param>
/// <param name="TickSeconds">Simulated seconds per tick.</param>
public sealed record ConnectAccepted(ulong Nonce, ulong Session, int SubjectId, string SubjectName, float TickSeconds) : Packet;

public sealed record ConnectDenied(ulong Nonce, DenyReason Reason) : Packet;

/// <summary>Sent to a bot every tick. Unreliable: clients should ignore snapshots older than the newest seen.</summary>
public sealed record ShipSnapshot(ulong Session, long Tick, ShipData Ship) : Packet;

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

/// <summary>A ship as it appears on the wire. Positions are metres on a 2D plane.</summary>
public readonly record struct ShipData(int Id, int OwnerPlayerId, double X, double Y);
