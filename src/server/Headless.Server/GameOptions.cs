using Headless.Sim;

namespace Headless.Server;

public sealed class GameOptions
{
    /// <summary>UDP port the server listens on (IPv4 and IPv6).</summary>
    public int Port { get; set; } = 5080;

    /// <summary>Wall-clock tick rate. Raise it to fast-forward the sim during development; sim time per tick is <see cref="SimConfig.TickSeconds"/>.</summary>
    public double TicksPerSecond { get; set; } = 2;

    /// <summary>How often (wall-clock seconds) the server logs every ship's location. 0 turns it off.</summary>
    public double LocationLogSeconds { get; set; } = 1;

    public SimConfig Sim { get; set; } = new();

    /// <summary>Players spawned at startup, each with one ship they own. Dev-only until accounts exist.</summary>
    public List<PlayerSlot> Players { get; set; } = [];
}

public sealed class PlayerSlot
{
    public string Name { get; set; } = "";

    /// <summary>Lets a client connect as this player.</summary>
    public string Token { get; set; } = "";

    public ShipSlot Ship { get; set; } = new();
}

public sealed class ShipSlot
{
    public string Name { get; set; } = "";

    /// <summary>Lets a bot connect and control this ship. Separate from the player's token.</summary>
    public string BotToken { get; set; } = "";
}
