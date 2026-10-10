namespace Headless.Sim;

/// <summary>
/// A human player. Players don't fly ships (bots do); they ride in them. Owning a ship and being aboard one
/// are separate: a player will be able to board ships they don't own. Ownership lives on the ship
/// (<see cref="Ship.OwnerPlayerId"/>), not here. Where a player is hasn't been modelled yet.
/// </summary>
public sealed class Player
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}
