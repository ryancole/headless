using System.Text.Json.Serialization;

namespace Headless.Protocol.V1;

// Wire types for bot protocol v1. These are a stable contract, deliberately separate from the sim's internal
// types so the simulation can change without breaking bots. Serialize with ProtocolJson.Options (snake_case).
// See docs/protocol-v1.md.

public sealed record Vector(double X, double Y);

// ---- Server -> bot ----
// Bots don't send any messages yet.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(SnapshotMessage), "snapshot")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record ServerMessage;

/// <summary>Sent once after the connection is accepted.</summary>
public sealed record HelloMessage(string Protocol, int ShipId, ShipRules Rules) : ServerMessage;

/// <summary>Constants a bot needs to plan. Bots should read these rather than hard-coding them.</summary>
public sealed record ShipRules(double TickSeconds);

/// <summary>Sent after every tick with the ship's current state.</summary>
public sealed record SnapshotMessage(long Tick, ShipDto Ship) : ServerMessage;

/// <summary>A message the server couldn't process. Nothing in it was applied.</summary>
public sealed record ErrorMessage(string Code, string Message) : ServerMessage;

public sealed record ShipDto(int Id, string Name, Vector Position);
