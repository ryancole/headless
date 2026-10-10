using System.Text.Json.Serialization;

namespace Headless.Protocol.V1;

// Wire types for protocol v1. These are a stable contract, deliberately separate from the sim's internal
// types so the simulation can change without breaking clients. Serialize with ProtocolJson.Options (snake_case).
// Bots use /v1/bot (hello, snapshot); players use /v1/player (player_hello, player_snapshot).
// See docs/protocol-v1.md.

public sealed record Vector(double X, double Y);

// ---- Server -> client ----
// Clients don't send any messages yet.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(SnapshotMessage), "snapshot")]
[JsonDerivedType(typeof(PlayerHelloMessage), "player_hello")]
[JsonDerivedType(typeof(PlayerSnapshotMessage), "player_snapshot")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record ServerMessage;

/// <summary>Sent once after a bot connection is accepted.</summary>
public sealed record HelloMessage(string Protocol, int ShipId, GameRules Rules) : ServerMessage;

/// <summary>Game constants. Clients should read these rather than hard-coding them.</summary>
public sealed record GameRules(double TickSeconds);

/// <summary>Sent to a bot after every tick with its ship's current state.</summary>
public sealed record SnapshotMessage(long Tick, ShipDto Ship) : ServerMessage;

/// <summary>A message the server couldn't process. Nothing in it was applied.</summary>
public sealed record ErrorMessage(string Code, string Message) : ServerMessage;

/// <summary>Sent once after a player connection is accepted.</summary>
public sealed record PlayerHelloMessage(string Protocol, int PlayerId, GameRules Rules) : ServerMessage;

/// <summary>Sent to a player after every tick: the player, and the ships they own.</summary>
public sealed record PlayerSnapshotMessage(long Tick, PlayerDto Player, IReadOnlyList<ShipDto> Ships) : ServerMessage;

public sealed record ShipDto(int Id, string Name, int OwnerPlayerId, Vector Position);

public sealed record PlayerDto(int Id, string Name);
