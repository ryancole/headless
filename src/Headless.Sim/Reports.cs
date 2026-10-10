namespace Headless.Sim;

public static class ErrorCodes
{
    public const string BudgetExceeded = "budget_exceeded";
    public const string InvalidValue = "invalid_value";
    public const string UnknownTarget = "unknown_target";
    public const string OutOfRange = "out_of_range";
    public const string TooFast = "too_fast";
    public const string Docked = "docked";
    public const string NotDocked = "not_docked";
    public const string CargoFull = "cargo_full";
    public const string Depleted = "depleted";
    public const string InsufficientCredits = "insufficient_credits";
}

public static class EventCodes
{
    public const string Docked = "docked";
    public const string Undocked = "undocked";
    public const string MiningStarted = "mining_started";
    public const string MiningStopped = "mining_stopped";
    public const string OreSold = "ore_sold";
    public const string Refueled = "refueled";
    public const string OutOfFuel = "out_of_fuel";
}

/// <summary>An intent the simulation rejected. <see cref="Index"/> is its position in that tick's batch for the ship.</summary>
public sealed record IntentError(int Index, Intent Intent, string Code, string Message);

/// <summary>Something that happened to a ship during a tick.</summary>
public sealed record ShipEvent(string Code, string Message);

/// <summary>Per-ship outcome of one tick.</summary>
public sealed class ShipTickReport
{
    public List<IntentError> Errors { get; } = [];
    public List<ShipEvent> Events { get; } = [];
}
