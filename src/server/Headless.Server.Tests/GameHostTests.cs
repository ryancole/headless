using System.Net;
using Headless.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Server.Tests;

public class GameHostTests
{
    private static readonly IPEndPoint Alice = new(IPAddress.Loopback, 40001);
    private static readonly IPEndPoint Bob = new(IPAddress.Loopback, 40002);
    private static readonly long TimeoutMs = (long)ProtocolConstants.Timeout.TotalMilliseconds;

    // Player One (id 1) owns Prospector (id 2); Player Two (id 3) owns Wildcat (id 4).
    private static GameHost NewHost() => new(Options.Create(new GameOptions
    {
        LocationLogSeconds = 0,
        Players =
        [
            new() { Name = "Player One", Token = "p1", Ship = new() { Name = "Prospector", BotToken = "b1" } },
            new() { Name = "Player Two", Token = "p2", Ship = new() { Name = "Wildcat", BotToken = "b2" } },
        ],
    }), NullLogger<GameHost>.Instance);

    private static IReadOnlyList<Outgoing> Send(GameHost host, IPEndPoint from, Packet packet, long nowMs = 0) =>
        host.Receive(from, PacketCodec.Encode(packet), nowMs);

    private static ConnectAccepted Connect(GameHost host, IPEndPoint from, ClientRole role, string token, ulong nonce = 1, long nowMs = 0)
    {
        var reply = Assert.Single(Send(host, from, new ConnectRequest(ProtocolConstants.Version, role, nonce, token), nowMs));
        Assert.Equal(from, reply.To);
        return Assert.IsType<ConnectAccepted>(reply.Packet);
    }

    [Fact]
    public void Bot_connects_with_its_ship_token_and_gets_ship_snapshots()
    {
        var host = NewHost();

        var accepted = Connect(host, Alice, ClientRole.Bot, "b1", nonce: 77);
        var sent = host.Tick(0);

        Assert.Equal((77UL, 2, "Prospector", 0.5f), (accepted.Nonce, accepted.SubjectId, accepted.SubjectName, accepted.TickSeconds));
        Assert.NotEqual(0UL, accepted.Session);
        var snapshot = Assert.IsType<ShipSnapshot>(Assert.Single(sent).Packet);
        Assert.Equal(new ShipSnapshot(accepted.Session, 1, new ShipData(2, 1, 0, 0)), snapshot);
    }

    [Fact]
    public void Player_connects_with_their_own_token_and_sees_only_their_ships()
    {
        var host = NewHost();

        var accepted = Connect(host, Alice, ClientRole.Player, "p2");
        var snapshot = Assert.IsType<PlayerSnapshot>(Assert.Single(host.Tick(0)).Packet);

        Assert.Equal((3, "Player Two"), (accepted.SubjectId, accepted.SubjectName));
        Assert.Equal([new ShipData(4, 3, 1_000, 0)], snapshot.Ships);
    }

    [Theory]
    [InlineData(ClientRole.Bot, "nope")]
    [InlineData(ClientRole.Bot, "p1")]    // a player token is not a bot token
    [InlineData(ClientRole.Player, "b1")] // and vice versa
    public void Wrong_tokens_are_denied(ClientRole role, string token)
    {
        var host = NewHost();

        var reply = Assert.Single(Send(host, Alice, new ConnectRequest(ProtocolConstants.Version, role, 5, token)));

        Assert.Equal(new ConnectDenied(5, DenyReason.InvalidToken), reply.Packet);
        Assert.Empty(host.Tick(0));
    }

    [Fact]
    public void Other_protocol_versions_are_denied()
    {
        var host = NewHost();

        var reply = Assert.Single(Send(host, Alice, new ConnectRequest(ProtocolConstants.Version + 1, ClientRole.Bot, 5, "b1")));

        Assert.Equal(new ConnectDenied(5, DenyReason.UnsupportedVersion), reply.Packet);
    }

    [Fact]
    public void A_resent_connect_request_gets_the_same_session()
    {
        var host = NewHost();

        var first = Connect(host, Alice, ClientRole.Bot, "b1", nonce: 9);
        var again = Connect(host, Alice, ClientRole.Bot, "b1", nonce: 9);

        Assert.Equal(first, again);
        Assert.Single(host.Tick(0));
    }

    [Fact]
    public void A_new_connection_for_the_same_ship_supersedes_the_old_one()
    {
        var host = NewHost();
        var old = Connect(host, Alice, ClientRole.Bot, "b1");

        var replies = Send(host, Bob, new ConnectRequest(ProtocolConstants.Version, ClientRole.Bot, 2, "b1"));

        Assert.Contains(new Outgoing(Alice, new Disconnected(old.Session, DisconnectReason.Superseded)), replies);
        var accepted = Assert.IsType<ConnectAccepted>(Assert.Single(replies, r => r.To.Equals(Bob)).Packet);
        Assert.All(host.Tick(0), o => Assert.Equal(Bob, o.To));
        Assert.Equal(accepted.Session, ((ShipSnapshot)host.Tick(0).Single().Packet).Session);
    }

    [Fact]
    public void A_bot_and_a_player_can_be_connected_at_once()
    {
        var host = NewHost();
        Connect(host, Alice, ClientRole.Bot, "b1");
        Connect(host, Bob, ClientRole.Player, "p1");

        var sent = host.Tick(0);

        Assert.IsType<ShipSnapshot>(Assert.Single(sent, o => o.To.Equals(Alice)).Packet);
        Assert.IsType<PlayerSnapshot>(Assert.Single(sent, o => o.To.Equals(Bob)).Packet);
    }

    [Fact]
    public void Silent_clients_time_out()
    {
        var host = NewHost();
        var accepted = Connect(host, Alice, ClientRole.Bot, "b1", nowMs: 0);

        Assert.IsType<ShipSnapshot>(Assert.Single(host.Tick(TimeoutMs)).Packet);
        var sent = host.Tick(TimeoutMs + 1);

        Assert.Equal([new Outgoing(Alice, new Disconnected(accepted.Session, DisconnectReason.TimedOut))], sent);
        Assert.Empty(host.Tick(TimeoutMs + 2));
    }

    [Fact]
    public void Keep_alives_keep_the_session_open()
    {
        var host = NewHost();
        var accepted = Connect(host, Alice, ClientRole.Bot, "b1", nowMs: 0);

        Send(host, Alice, new KeepAlive(accepted.Session), nowMs: 4_000);

        Assert.IsType<ShipSnapshot>(Assert.Single(host.Tick(8_000)).Packet);
    }

    [Fact]
    public void Packets_with_the_wrong_session_or_address_are_ignored()
    {
        var host = NewHost();
        var accepted = Connect(host, Alice, ClientRole.Bot, "b1", nowMs: 0);

        Assert.Empty(Send(host, Alice, new KeepAlive(accepted.Session + 1), nowMs: 4_000)); // wrong session
        Assert.Empty(Send(host, Bob, new KeepAlive(accepted.Session), nowMs: 4_000));       // wrong address
        Send(host, Bob, new Disconnect(accepted.Session));

        Assert.IsType<Disconnected>(Assert.Single(host.Tick(TimeoutMs + 1)).Packet); // neither refreshed nor closed it
    }

    [Fact]
    public void Clients_can_disconnect()
    {
        var host = NewHost();
        var accepted = Connect(host, Alice, ClientRole.Bot, "b1");

        Send(host, Alice, new Disconnect(accepted.Session));

        Assert.Empty(host.Tick(0));
    }

    [Fact]
    public void Disconnect_all_tells_every_client()
    {
        var host = NewHost();
        var bot = Connect(host, Alice, ClientRole.Bot, "b1");
        var player = Connect(host, Bob, ClientRole.Player, "p1");

        var sent = host.DisconnectAll(DisconnectReason.ServerShutdown);

        Assert.Equal(2, sent.Count);
        Assert.Contains(new Outgoing(Alice, new Disconnected(bot.Session, DisconnectReason.ServerShutdown)), sent);
        Assert.Contains(new Outgoing(Bob, new Disconnected(player.Session, DisconnectReason.ServerShutdown)), sent);
        Assert.Empty(host.Tick(0));
    }

    [Fact]
    public void Garbage_and_server_packets_from_clients_are_ignored()
    {
        var host = NewHost();

        Assert.Empty(host.Receive(Alice, [0xDE, 0xAD, 0xBE, 0xEF], 0));
        Assert.Empty(Send(host, Alice, new Disconnected(1, DisconnectReason.ServerShutdown)));
        Assert.Empty(host.Tick(0));
    }

    [Fact]
    public void Reused_tokens_are_rejected_at_startup()
    {
        var options = Options.Create(new GameOptions
        {
            Players = [new() { Name = "P", Token = "same", Ship = new() { Name = "S", BotToken = "same" } }],
        });

        Assert.Throws<InvalidOperationException>(() => new GameHost(options, NullLogger<GameHost>.Instance));
    }
}
