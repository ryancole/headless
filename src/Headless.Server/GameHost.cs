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
    private readonly ILogger<GameHost> _logger;

    public GameHost(IOptions<GameOptions> options, ILogger<GameHost> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var world = WorldGenerator.CreateStarterSystem(opts.Seed, opts.Sim);
        var spawn = world.Stations.Values.First();

        foreach (var slot in opts.Ships)
        {
            if (string.IsNullOrWhiteSpace(slot.Token))
                throw new InvalidOperationException($"Ship '{slot.Name}' has no token configured.");
            var ship = world.AddShipDocked(slot.Name, spawn);
            _shipIdsByToken.Add(slot.Token, ship.Id);
            _logger.LogInformation("Spawned ship {ShipId} '{Name}' docked at {Station}", ship.Id, ship.Name, spawn.Name);
        }

        _sim = new Simulation(world);
        Rules = ProtocolMapper.ToRules(world.Config);
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
            // A superseded connection must not release control from its replacement.
            if (!_bots.TryGetValue(bot.ShipId, out var current) || current != bot) return;
            _bots.Remove(bot.ShipId);
            _sim.ReleaseControl(bot.ShipId);
        }
        _logger.LogInformation("Bot disconnected from ship {ShipId}; ship holding course", bot.ShipId);
    }

    public void Submit(BotConnection bot, IEnumerable<Intent> intents)
    {
        lock (_gate)
        {
            if (!_bots.TryGetValue(bot.ShipId, out var current) || current != bot) return;
            foreach (var intent in intents) _sim.Submit(bot.ShipId, intent);
        }
    }

    public void Tick()
    {
        List<(BotConnection Bot, SnapshotMessage Snapshot)> outgoing;
        lock (_gate)
        {
            var reports = _sim.Step();
            outgoing = _bots.Values
                .Select(bot => (bot, ProtocolMapper.ToSnapshot(_sim.Sense(bot.ShipId), reports[bot.ShipId])))
                .ToList();
        }

        foreach (var (bot, snapshot) in outgoing) bot.Send(snapshot);
    }
}
