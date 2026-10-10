namespace Headless.Sim;

/// <summary>Something a ship's controller asks for. The simulation validates it and decides the outcome.</summary>
public abstract record Intent;

/// <summary>Set the persistent throttle vector. Length is clamped to 1.</summary>
public sealed record ThrustIntent(Vec2 Throttle) : Intent;

public sealed record MineIntent(int AsteroidId) : Intent;
public sealed record StopMiningIntent : Intent;
public sealed record DockIntent(int StationId) : Intent;
public sealed record UndockIntent : Intent;

/// <summary>Sell all whole ore units in cargo to the station the ship is docked at.</summary>
public sealed record SellOreIntent : Intent;

/// <summary>Buy as much fuel as the tank holds and the ship's credits allow.</summary>
public sealed record RefuelIntent : Intent;
