using System.Text.Json.Serialization;

namespace Headless.Protocol.V1;

// Wire types for bot protocol v1. These are a stable contract, deliberately separate from the sim's internal
// types so the simulation can change without breaking bots. Serialize with ProtocolJson.Options (snake_case).
// See docs/protocol-v1.md.

public sealed record Vector(double X, double Y);

// ---- Server -> bot ----

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(SnapshotMessage), "snapshot")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record ServerMessage;

/// <summary>Sent once after the connection is accepted.</summary>
public sealed record HelloMessage(string Protocol, int ShipId, ShipRules Rules) : ServerMessage;

/// <summary>Constants a bot needs to plan. Bots should read these rather than hard-coding them.</summary>
public sealed record ShipRules(
    double TickSeconds,
    int MaxIntentsPerTick,
    double MaxAcceleration,
    double MaxFuel,
    double FuelPerThrustSecond,
    double CargoCapacity,
    double MiningRatePerSecond,
    double MiningRange,
    double DockingRange,
    double MaxInteractionSpeed,
    double SensorRange);

/// <summary>Sent after every tick: what the ship's sensors see, plus the outcome of the intents applied that tick.</summary>
public sealed record SnapshotMessage(
    long Tick,
    ShipDto Ship,
    IReadOnlyList<StationDto> Stations,
    IReadOnlyList<AsteroidDto> Asteroids,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<EventDto> Events,
    IReadOnlyList<IntentErrorDto> Errors) : ServerMessage;

/// <summary>A message the server couldn't process (e.g. malformed JSON). Nothing in it was applied.</summary>
public sealed record ErrorMessage(string Code, string Message) : ServerMessage;

public sealed record ShipDto(
    int Id,
    string Name,
    Vector Position,
    Vector Velocity,
    Vector Throttle,
    double Fuel,
    double Cargo,
    long Credits,
    int? DockedAtStationId,
    int? MiningAsteroidId);

public sealed record StationDto(int Id, string Name, Vector Position, double OrePrice, double FuelPrice);
public sealed record AsteroidDto(int Id, Vector Position, double Radius, double Ore);
public sealed record ContactDto(int Id, string Name, Vector Position, Vector Velocity);
public sealed record EventDto(string Code, string Message);

/// <summary><see cref="Index"/> is the intent's position among everything the bot sent for that tick.</summary>
public sealed record IntentErrorDto(int Index, string Intent, string Code, string Message);

// ---- Bot -> server ----

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(IntentsMessage), "intents")]
public abstract record ClientMessage;

/// <summary>Intents to apply on the next tick. May be sent more than once per tick; all count against the budget.</summary>
public sealed record IntentsMessage(IReadOnlyList<IntentDto> Intents) : ClientMessage;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ThrustDto), "thrust")]
[JsonDerivedType(typeof(MineDto), "mine")]
[JsonDerivedType(typeof(StopMiningDto), "stop_mining")]
[JsonDerivedType(typeof(DockDto), "dock")]
[JsonDerivedType(typeof(UndockDto), "undock")]
[JsonDerivedType(typeof(SellDto), "sell")]
[JsonDerivedType(typeof(RefuelDto), "refuel")]
public abstract record IntentDto;

public sealed record ThrustDto(double X, double Y) : IntentDto;
public sealed record MineDto(int AsteroidId) : IntentDto;
public sealed record StopMiningDto : IntentDto;
public sealed record DockDto(int StationId) : IntentDto;
public sealed record UndockDto : IntentDto;
public sealed record SellDto : IntentDto;
public sealed record RefuelDto : IntentDto;
