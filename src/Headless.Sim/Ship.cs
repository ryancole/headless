namespace Headless.Sim;

public sealed class Ship
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public Vec2 Position { get; internal set; }
}
