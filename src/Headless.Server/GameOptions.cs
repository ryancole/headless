using Headless.Sim;

namespace Headless.Server;

public sealed class GameOptions
{
    /// <summary>Wall-clock tick rate. Raise it to fast-forward the sim during development; sim time per tick is <see cref="SimConfig.TickSeconds"/>.</summary>
    public double TicksPerSecond { get; set; } = 2;

    public int Seed { get; set; } = 1;
    public SimConfig Sim { get; set; } = new();

    /// <summary>Ships spawned at startup, each controllable by the bot holding its token. Dev-only until accounts exist.</summary>
    public List<ShipSlot> Ships { get; set; } = [];
}

public sealed class ShipSlot
{
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
}
