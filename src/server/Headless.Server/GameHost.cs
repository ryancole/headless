using System.Net;
using System.Security.Cryptography;
using Headless.Protocol;
using Headless.Sim;
using Microsoft.Extensions.Options;

namespace Headless.Server;

/// <summary>A packet the server wants sent.</summary>
public readonly record struct Outgoing(IPEndPoint To, Packet Packet);

/// <summary>
/// Owns the simulation and the client sessions. Pure game and connection logic: packets come in, packets to send
/// come out, and the caller supplies the clock, so it can be tested without sockets. Thread-safe: every entry
/// point takes <see cref="_gate"/>.
/// </summary>
/// <remarks>
/// Sessions: at most one bot per ship (it flies the ship) and one connection per player. A client is identified
/// by its address plus the random session id handed out on connect, which every later packet must carry.
/// </remarks>
public sealed class GameHost
{
    private readonly Lock _gate = new();
    private readonly Simulation _sim;
    private readonly Dictionary<string, int> _shipIdsByBotToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _playerIdsByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<IPEndPoint, Session> _sessionsByAddress = [];
    private readonly Dictionary<int, Session> _botsByShip = [];
    private readonly Dictionary<int, Session> _playersById = [];
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
        _locationLogEveryTicks = opts.LocationLogSeconds > 0
            ? Math.Max(1, (long)Math.Round(opts.LocationLogSeconds * opts.TicksPerSecond))
            : 0;
    }

    /// <summary>Handles one datagram. Malformed packets and packets from unknown sessions are silently dropped.</summary>
    public IReadOnlyList<Outgoing> Receive(IPEndPoint from, ReadOnlySpan<byte> data, long nowMs)
    {
        if (!PacketCodec.TryDecode(data, out var packet)) return [];

        lock (_gate)
        {
            switch (packet)
            {
                case ConnectRequest request:
                    return Connect(from, request, nowMs);

                case KeepAlive { Session: var id } when FindSession(from, id) is { } session:
                    session.LastHeardMs = nowMs;
                    return [];

                case Disconnect { Session: var id } when FindSession(from, id) is { } session:
                    Remove(session);
                    _logger.LogInformation("Disconnected: {Session} (client left)", session);
                    return [];

                case Protocol.DeviceCommand command when FindSession(from, command.Session) is { Role: ClientRole.Bot } session:
                    session.LastHeardMs = nowMs;
                    return [new(from, new CommandAck(session.Id, command.Sequence, Apply(session, command)))];

                default:
                    return []; // Server-to-client packet types, a stale or spoofed session, or a player sending commands.
            }
        }
    }

    /// <summary>Advances the sim one tick, drops timed-out sessions, and returns every client's snapshot.</summary>
    public IReadOnlyList<Outgoing> Tick(long nowMs)
    {
        var outgoing = new List<Outgoing>();
        List<string>? locationLines = null;
        long tick;

        lock (_gate)
        {
            foreach (var session in _sessionsByAddress.Values.Where(s => nowMs - s.LastHeardMs > (long)ProtocolConstants.Timeout.TotalMilliseconds).ToList())
            {
                Remove(session);
                outgoing.Add(new(session.Address, new Disconnected(session.Id, DisconnectReason.TimedOut)));
                _logger.LogInformation("Disconnected: {Session} (timed out)", session);
            }

            _sim.Step();
            tick = _sim.World.Tick;

            foreach (var session in _sessionsByAddress.Values)
            {
                Packet snapshot = session.Role switch
                {
                    ClientRole.Bot => ProtocolMapper.ToPacket(session.Id, _sim.Sense(session.SubjectId)),
                    _ => new Protocol.PlayerSnapshot(session.Id, tick, _sim.SensePlayer(session.SubjectId).Ships.Select(ProtocolMapper.ToData).ToList()),
                };
                outgoing.Add(new(session.Address, snapshot));
            }

            if (_locationLogEveryTicks > 0 && tick % _locationLogEveryTicks == 0)
                locationLines = DescribeShips();
        }

        foreach (var line in locationLines ?? [])
            _logger.LogInformation("[tick {Tick}] {Ship}", tick, line);
        return outgoing;
    }

    /// <summary>Ends every session, e.g. on shutdown, and returns the notices to send.</summary>
    public IReadOnlyList<Outgoing> DisconnectAll(DisconnectReason reason)
    {
        lock (_gate)
        {
            var outgoing = _sessionsByAddress.Values.Select(s => new Outgoing(s.Address, new Disconnected(s.Id, reason))).ToList();
            _sessionsByAddress.Clear();
            _botsByShip.Clear();
            _playersById.Clear();
            return outgoing;
        }
    }

    private List<Outgoing> Connect(IPEndPoint from, ConnectRequest request, long nowMs)
    {
        if (request.Version != ProtocolConstants.Version)
            return [new(from, new ConnectDenied(request.Nonce, DenyReason.UnsupportedVersion))];

        if (!TryAuthenticate(request.Role, request.Token, out var subjectId, out var subjectName))
        {
            _logger.LogInformation("Denied {Role} connection from {Address}: invalid token", request.Role, from);
            return [new(from, new ConnectDenied(request.Nonce, DenyReason.InvalidToken))];
        }

        var outgoing = new List<Outgoing>();
        if (_sessionsByAddress.TryGetValue(from, out var existing))
        {
            // A resend of a request we already accepted (the client hasn't seen our answer yet): answer again.
            if (existing.Nonce == request.Nonce && existing.Role == request.Role && existing.SubjectId == subjectId)
            {
                existing.LastHeardMs = nowMs;
                return [new(from, Accepted(existing, subjectName))];
            }
            // Otherwise the client at this address started over; its old session is gone.
            Remove(existing);
            _logger.LogInformation("Disconnected: {Session} (reconnected)", existing);
        }

        var bySubject = SessionsFor(request.Role);
        if (bySubject.TryGetValue(subjectId, out var previous))
        {
            Remove(previous);
            outgoing.Add(new(previous.Address, new Disconnected(previous.Id, DisconnectReason.Superseded)));
            _logger.LogInformation("Disconnected: {Session} (superseded by a new connection)", previous);
        }

        var session = new Session(from, NewSessionId(), request.Role, subjectId, request.Nonce) { LastHeardMs = nowMs };
        _sessionsByAddress.Add(from, session);
        bySubject.Add(subjectId, session);
        _logger.LogInformation("Connected: {Session}", session);

        outgoing.Add(new(from, Accepted(session, subjectName)));
        return outgoing;
    }

    /// <summary>
    /// Applies a bot's command unless it's a resend or arrived after a newer one for the same device. Commands only
    /// set values, so the newest per device is all that matters, and a lost ack just means the bot resends.
    /// </summary>
    private CommandResult Apply(Session session, Protocol.DeviceCommand command)
    {
        if (session.LastSequenceByDevice.TryGetValue(command.Device, out var last) && command.Sequence <= last)
            return CommandResult.Stale;

        var outcome = _sim.Command(session.SubjectId, command.Device, (Sim.DeviceCommand)command.Command, command.Value);
        if (outcome == CommandOutcome.Ok) session.LastSequenceByDevice[command.Device] = command.Sequence;
        return ProtocolMapper.ToResult(outcome);
    }

    private ConnectAccepted Accepted(Session session, string subjectName) =>
        new(session.Nonce, session.Id, session.SubjectId, subjectName, (float)_sim.World.Config.TickSeconds);

    private bool TryAuthenticate(ClientRole role, string token, out int subjectId, out string subjectName)
    {
        subjectName = "";
        switch (role)
        {
            case ClientRole.Bot when _shipIdsByBotToken.TryGetValue(token, out subjectId):
                subjectName = _sim.World.Ships[subjectId].Name;
                return true;
            case ClientRole.Player when _playerIdsByToken.TryGetValue(token, out subjectId):
                subjectName = _sim.World.Players[subjectId].Name;
                return true;
            default:
                subjectId = 0;
                return false;
        }
    }

    private Session? FindSession(IPEndPoint from, ulong id) =>
        _sessionsByAddress.TryGetValue(from, out var session) && session.Id == id ? session : null;

    private Dictionary<int, Session> SessionsFor(ClientRole role) => role == ClientRole.Bot ? _botsByShip : _playersById;

    private void Remove(Session session)
    {
        _sessionsByAddress.Remove(session.Address);
        SessionsFor(session.Role).Remove(session.SubjectId);
    }

    /// <summary>One line per ship: where it is, whether its bot is connected, and who owns it.</summary>
    private List<string> DescribeShips()
    {
        var world = _sim.World;
        return world.Ships.Values.Select(ship =>
        {
            var owner = world.Players[ship.OwnerPlayerId];
            return $"ship {ship.Id} '{ship.Name}' at {Format(ship.Position)}, "
                + $"heading {ship.Heading * 180 / Math.PI:0}°, speed {ship.Velocity.Length:0.0} m/s, "
                + $"{(_botsByShip.ContainsKey(ship.Id) ? "bot connected" : "no bot")}, "
                + $"owner: '{owner.Name}' ({(_playersById.ContainsKey(owner.Id) ? "online" : "offline")})";
        }).ToList();
    }

    /// <summary>A random, non-zero session id. Unguessable, so another host can't inject packets into a session.</summary>
    private static ulong NewSessionId()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong id;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            id = BitConverter.ToUInt64(bytes);
        } while (id == 0);
        return id;
    }

    private static void RequireUniqueToken(HashSet<string> tokens, string token, string owner)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"{owner} has no token configured.");
        if (!tokens.Add(token))
            throw new InvalidOperationException($"{owner} reuses a token that's already configured elsewhere.");
    }

    private static string Format(Vec2 v) => $"({v.X:0.0}, {v.Y:0.0})";

    private sealed record Session(IPEndPoint Address, ulong Id, ClientRole Role, int SubjectId, ulong Nonce)
    {
        public long LastHeardMs { get; set; }

        /// <summary>Bots only: the sequence number of the last command applied to each device.</summary>
        public Dictionary<byte, uint> LastSequenceByDevice { get; } = [];

        public override string ToString() =>
            $"{(Role == ClientRole.Bot ? "bot for ship" : "player")} {SubjectId} from {Address}";
    }
}
