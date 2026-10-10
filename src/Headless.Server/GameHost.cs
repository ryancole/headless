using Headless.Protocol.V1;
using Headless.Sim;
using Microsoft.Extensions.Options;

namespace Headless.Server;

/// <summary>
/// Owns the simulation and the connected bots. All sim access goes through <see cref="_gate"/>; network I/O
/// happens outside it so a slow bot can never stall the tick.
/// </summary>
public sealed class GameHost
{
    private readonly Lock _gate = new();
    private readonly Simulation _sim;
    private readonly Dictionary<string, int> _shipIdsByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<int, BotConnection> _bots = [];
    private readonly long _locationLogEveryTicks;
    private readonly ILogger<GameHost> _logger;

    public GameHost(IOptions<GameOptions> options, ILogger<GameHost> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var world = new World(opts.Sim);

        // Spawn ships 1 km apart along the x axis.
        foreach (var (slot, index) in opts.Ships.Select((s, i) => (s, i)))
        {
            if (string.IsNullOrWhiteSpace(slot.Token))
                throw new InvalidOperationException($"Ship '{slot.Name}' has no token configured.");
            var ship = world.AddShip(slot.Name, new Vec2(index * 1_000, 0));
            _shipIdsByToken.Add(slot.Token, ship.Id);
            _logger.LogInformation("Spawned ship {ShipId} '{Name}' at {Position}", ship.Id, ship.Name, Format(ship.Position));
        }

        _sim = new Simulation(world);
        Rules = ProtocolMapper.ToRules(world.Config);
        _locationLogEveryTicks = opts.LocationLogSeconds > 0
            ? Math.Max(1, (long)Math.Round(opts.LocationLogSeconds * opts.TicksPerSecond))
            : 0;
    }

    public ShipRules Rules { get; }

    public bool TryGetShipForToken(string token, out int shipId) => _shipIdsByToken.TryGetValue(token, out shipId);

    /// <summary>Makes <paramref name="bot"/> the ship's controller, disconnecting any previous one.</summary>
    public void Attach(BotConnection bot)
    {
        BotConnection? previous;
        lock (_gate)
        {
            _bots.TryGetValue(bot.ShipId, out previous);
            _bots[bot.ShipId] = bot;
        }
        previous?.Close("superseded by a new connection");
        _logger.LogInformation("Bot connected to ship {ShipId}", bot.ShipId);
    }

    public void Detach(BotConnection bot)
    {
        lock (_gate)
        {
            // A superseded connection must not remove its replacement.
            if (!_bots.TryGetValue(bot.ShipId, out var current) || current != bot) return;
            _bots.Remove(bot.ShipId);
        }
        _logger.LogInformation("Bot disconnected from ship {ShipId}", bot.ShipId);
    }

    public void Tick()
    {
        List<(BotConnection Bot, SnapshotMessage Snapshot)> outgoing;
        List<(ShipState State, bool Controlled)>? locations = null;
        long tick;
        lock (_gate)
        {
            _sim.Step();
            outgoing = _bots.Values.Select(bot => (bot, ProtocolMapper.ToSnapshot(_sim.Sense(bot.ShipId)))).ToList();

            tick = _sim.World.Tick;
            if (_locationLogEveryTicks > 0 && tick % _locationLogEveryTicks == 0)
                locations = _sim.World.Ships.Keys.Select(id => (_sim.Sense(id).Self, _bots.ContainsKey(id))).ToList();
        }

        foreach (var (bot, snapshot) in outgoing) bot.Send(snapshot);

        if (locations is null) return;
        foreach (var (state, controlled) in locations)
            _logger.LogInformation("[tick {Tick}] ship {ShipId} '{Name}' at {Position} ({Control})",
                tick, state.Id, state.Name, Format(state.Position), controlled ? "bot connected" : "no bot");
    }

    private static string Format(Vec2 v) => $"({v.X:0.0}, {v.Y:0.0})";
}
