namespace Headless.Sim;

/// <summary>An autonomous ship, flown by a bot. Owned by one player; players (owner or not) can ride in it.</summary>
public sealed class Ship
{
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Every ship has an owner, and it must be a player in the same world.</summary>
    public required int OwnerPlayerId { get; init; }

    public Vec2 Position { get; internal set; }
}
