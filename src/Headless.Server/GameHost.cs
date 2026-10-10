using Headless.Protocol.V1;
using Headless.Sim;
using Microsoft.Extensions.Options;

namespace Headless.Server;

/// <summary>
/// Owns the simulation and the connected clients: at most one bot per ship (it flies the ship) and one
/// connection per player. All sim access goes through <see cref="_gate"/>; network
/// I/O happens outside it so a slow client can never stall the tick.
/// </summary>
public sealed class GameHost
{
    private readonly Lock _gate = new();
    private readonly Simulation _sim;
    private readonly Dictionary<string, int> _shipIdsByBotToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _playerIdsByToken = new(StringComparer.Ordinal);
    private readonly ClientGroup _bots = new("bot for ship");
    private readonly ClientGroup _players = new("player");
    private readonly long _locationLogEveryTicks;
    private readonly ILogger<GameHost> _logger;

    public GameHost(IOptions<GameOptions> options, ILogger<GameHost> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var world = new World(opts.Sim);
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        // Each player starts with one ship they own. Ships spawn 1 km apart along the x axis.
        foreach (var (slot, index) in opts.Players.Select((s, i) => (s, i)))
        {
            RequireUniqueToken(tokens, slot.Token, $"Player '{slot.Name}'");
            RequireUniqueToken(tokens, slot.Ship.BotToken, $"Ship '{slot.Ship.Name}'");

            var player = world.AddPlayer(slot.Name);
            var ship = world.AddShip(slot.Ship.Name, new Vec2(index * 1_000, 0), player);
            _playerIdsByToken.Add(slot.Token, player.Id);
            _shipIdsByBotToken.Add(slot.Ship.BotToken, ship.Id);
            _logger.LogInformation("Spawned player {PlayerId} '{Player}' with ship {ShipId} '{Ship}' at {Position}",
                player.Id, player.Name, ship.Id, ship.Name, Format(ship.Position));
        }

        _sim = new Simulation(world);
        Rules = ProtocolMapper.ToRules(world.Config);
        _locationLogEveryTicks = opts.LocationLogSeconds > 0
            ? Math.Max(1, (long)Math.Round(opts.LocationLogSeconds * opts.TicksPerSecond))
            : 0;
    }

    public GameRules Rules { get; }

    public bool TryGetShipForBotToken(string token, out int shipId) => _shipIdsByBotToken.TryGetValue(token, out shipId);
    public bool TryGetPlayerForToken(string token, out int playerId) => _playerIdsByToken.TryGetValue(token, out playerId);

    /// <summary>Makes <paramref name="bot"/> the ship's controller, disconnecting any previous one.</summary>
    public void AttachBot(int shipId, ClientConnection bot) => Attach(_bots, shipId, bot);
    public void DetachBot(int shipId, ClientConnection bot) => Detach(_bots, shipId, bot);

    /// <summary>Connects <paramref name="client"/> as the player, disconnecting any previous connection for them.</summary>
    public void AttachPlayer(int playerId, ClientConnection client) => Attach(_players, playerId, client);
    public void DetachPlayer(int playerId, ClientConnection client) => Detach(_players, playerId, client);

    public void Tick()
    {
        var outgoing = new List<(ClientConnection Client, ServerMessage Message)>();
        List<string>? locationLines = null;
        long tick;
        lock (_gate)
        {
            _sim.Step();
            foreach (var (shipId, bot) in _bots.Connections)
                outgoing.Add((bot, ProtocolMapper.ToSnapshot(_sim.Sense(shipId))));
            foreach (var (playerId, client) in _players.Connections)
                outgoing.Add((client, ProtocolMapper.ToPlayerSnapshot(_sim.SensePlayer(playerId))));

            tick = _sim.World.Tick;
            if (_locationLogEveryTicks > 0 && tick % _locationLogEveryTicks == 0)
                locationLines = DescribeShips();
        }

        foreach (var (client, message) in outgoing) client.Send(message);

        foreach (var line in locationLines ?? [])
            _logger.LogInformation("[tick {Tick}] {Ship}", tick, line);
    }

    /// <summary>One line per ship: where it is, whether its bot is connected, and who owns it.</summary>
    private List<string> DescribeShips()
    {
        var world = _sim.World;
        return world.Ships.Values.Select(ship =>
        {
            var owner = world.Players[ship.OwnerPlayerId];
            return $"ship {ship.Id} '{ship.Name}' at {Format(ship.Position)}, "
                + $"{(_bots.IsConnected(ship.Id) ? "bot connected" : "no bot")}, "
                + $"owner: '{owner.Name}' ({(_players.IsConnected(owner.Id) ? "online" : "offline")})";
        }).ToList();
    }

    private void Attach(ClientGroup group, int id, ClientConnection client)
    {
        ClientConnection? previous;
        lock (_gate) previous = group.Set(id, client);
        previous?.Close("superseded by a new connection");
        _logger.LogInformation("Connected: {Who} {Id}", group.Label, id);
    }

    private void Detach(ClientGroup group, int id, ClientConnection client)
    {
        // A superseded connection must not remove its replacement.
        bool removed;
        lock (_gate) removed = group.Remove(id, client);
        if (removed) _logger.LogInformation("Disconnected: {Who} {Id}", group.Label, id);
    }

    private static void RequireUniqueToken(HashSet<string> tokens, string token, string owner)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"{owner} has no token configured.");
        if (!tokens.Add(token))
            throw new InvalidOperationException($"{owner} reuses a token that's already configured elsewhere.");
    }

    private static string Format(Vec2 v) => $"({v.X:0.0}, {v.Y:0.0})";

    /// <summary>The live connections of one kind of client, keyed by ship or player id. Guarded by <see cref="_gate"/>.</summary>
    private sealed class ClientGroup(string label)
    {
        private readonly Dictionary<int, ClientConnection> _connections = [];

        /// <summary>How log lines name a connection, e.g. "bot for ship" 2 or "player" 1.</summary>
        public string Label { get; } = label;
        public IEnumerable<KeyValuePair<int, ClientConnection>> Connections => _connections;

        public bool IsConnected(int id) => _connections.ContainsKey(id);

        /// <returns>The connection it replaced, if any.</returns>
        public ClientConnection? Set(int id, ClientConnection client)
        {
            _connections.TryGetValue(id, out var previous);
            _connections[id] = client;
            return previous;
        }

        public bool Remove(int id, ClientConnection client) =>
            _connections.TryGetValue(id, out var current) && current == client && _connections.Remove(id);
    }
}
