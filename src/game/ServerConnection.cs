using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Headless.Protocol;

namespace Headless.Game;

public enum ConnectionState
{
    /// <summary>Sending <see cref="ConnectRequest"/>s, waiting for an answer.</summary>
    Connecting,
    Connected,
    /// <summary>Lost the connection; will try again after <see cref="ServerConnection.ReconnectDelay"/>.</summary>
    WaitingToReconnect,
    /// <summary>Gave up for good: denied, superseded or disposed. Retrying wouldn't help.</summary>
    Stopped,
}

/// <summary>
/// A client connection to the game server over the Headless UDP protocol (docs/protocol.md). Connects, keeps the
/// connection alive and reconnects when it's lost. Doesn't touch Godot: it could live in a shared client library
/// or a C# bot.
/// </summary>
/// <remarks>
/// Not thread-safe and runs no threads of its own. Call <see cref="Update"/> often (every frame); it reads
/// whatever has arrived without blocking, sends what's due and raises events on the caller's thread.
/// </remarks>
public sealed class ServerConnection(string host, int port, ClientRole role, string token) : IDisposable
{
    /// <summary>How long to wait before reconnecting after the connection is lost.</summary>
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly byte[] _receiveBuffer = new byte[ProtocolConstants.MaxPacketSize * 2]; // Room to spot oversized packets.
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Socket? _socket;
    private ulong _nonce;
    private ulong _session;
    private long _lastTick;
    private TimeSpan _attemptStarted, _nextSend, _lastHeard, _reconnectAt;

    // Starts "waiting" with a reconnect time of zero, so the first Update connects.
    public ConnectionState State { get; private set; } = ConnectionState.WaitingToReconnect;

    /// <summary>Status messages worth showing a person: connecting, denied, lost contact...</summary>
    public event Action<string>? Log;
    public event Action<ConnectAccepted>? Connected;
    /// <summary>A snapshot newer than any seen before on this connection. Stale ones are dropped.</summary>
    public event Action<PlayerSnapshot>? PlayerSnapshotReceived;
    /// <inheritdoc cref="PlayerSnapshotReceived"/>
    public event Action<ShipSnapshot>? ShipSnapshotReceived;
    /// <summary>Players: their ships' recent commands. Entries repeat between packets; deduplicate by entry id.</summary>
    public event Action<ShipLog>? ShipLogReceived;

    public void Update()
    {
        var now = _clock.Elapsed;
        if (State == ConnectionState.WaitingToReconnect && now >= _reconnectAt) StartConnecting(now);

        while (State is ConnectionState.Connecting or ConnectionState.Connected && TryReceive(out var packet))
            Handle(packet, now);

        switch (State)
        {
            case ConnectionState.Connecting when now - _attemptStarted > ProtocolConstants.Timeout:
                Lost("No answer from the server", now);
                break;
            case ConnectionState.Connecting when now >= _nextSend:
                Send(new ConnectRequest(ProtocolConstants.Version, role, _nonce, token));
                _nextSend = now + ProtocolConstants.ConnectRetryInterval;
                break;
            case ConnectionState.Connected when now - _lastHeard > ProtocolConstants.Timeout:
                Lost("Lost contact with the server", now);
                break;
            case ConnectionState.Connected when now >= _nextSend:
                Send(new KeepAlive(_session));
                _nextSend = now + ProtocolConstants.KeepAliveInterval;
                break;
        }
    }

    /// <summary>Tells the server we're leaving (if connected) and closes the socket.</summary>
    public void Dispose()
    {
        if (State == ConnectionState.Connected) Send(new Disconnect(_session));
        Stop();
    }

    private void StartConnecting(TimeSpan now)
    {
        try
        {
            var address = Dns.GetHostAddresses(host)[0];
            _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            _socket.Connect(address, port); // A connected UDP socket only receives packets from the server.
        }
        catch (SocketException ex)
        {
            Lost($"Can't reach {host}:{port}: {ex.Message}", now);
            return;
        }

        _nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        _attemptStarted = _nextSend = now;
        State = ConnectionState.Connecting;
        Log?.Invoke($"Connecting to {host}:{port} as {role.ToString().ToLowerInvariant()}");
    }

    private void Handle(Packet packet, TimeSpan now)
    {
        if (State == ConnectionState.Connecting)
        {
            switch (packet)
            {
                case ConnectAccepted p when p.Nonce == _nonce:
                    _session = p.Session;
                    _lastTick = -1;
                    _lastHeard = _nextSend = now;
                    State = ConnectionState.Connected;
                    Connected?.Invoke(p);
                    break;
                case ConnectDenied p when p.Nonce == _nonce:
                    Log?.Invoke($"Server denied the connection: {p.Reason}");
                    Stop();
                    break;
            }
            return;
        }

        var session = packet switch
        {
            PlayerSnapshot p => p.Session,
            ShipSnapshot p => p.Session,
            ShipLog p => p.Session,
            Disconnected p => p.Session,
            _ => 0UL, // Includes repeated ConnectAccepteds, answering requests resent before the first answer arrived.
        };
        if (session != _session) return;
        _lastHeard = now;

        switch (packet)
        {
            case PlayerSnapshot p when p.Tick > _lastTick:
                _lastTick = p.Tick;
                PlayerSnapshotReceived?.Invoke(p);
                break;
            case ShipSnapshot p when p.Tick > _lastTick:
                _lastTick = p.Tick;
                ShipSnapshotReceived?.Invoke(p);
                break;
            case ShipLog p:
                ShipLogReceived?.Invoke(p);
                break;
            case Disconnected { Reason: DisconnectReason.Superseded }:
                // Reconnecting would just knock the other connection off in turn.
                Log?.Invoke("Server disconnected us: another connection logged in with the same token");
                Stop();
                break;
            case Disconnected p:
                Lost($"Server disconnected us: {p.Reason}", now);
                break;
        }
    }

    /// <summary>The next valid packet from the server, if one has arrived. Never blocks.</summary>
    private bool TryReceive(out Packet packet)
    {
        while (true)
        {
            int length;
            try
            {
                length = _socket!.Receive(_receiveBuffer);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // ConnectionReset: on Windows, "port unreachable" when the server isn't running. MessageSize: an
                // oversized datagram. Neither is a packet from the server; keep reading.
                continue;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                packet = null!;
                return false;
            }

            if (PacketCodec.TryDecode(_receiveBuffer.AsSpan(0, length), out var decoded))
            {
                packet = decoded!;
                return true;
            }
        }
    }

    private void Send(Packet packet)
    {
        try
        {
            _socket!.Send(PacketCodec.Encode(packet));
        }
        catch (SocketException)
        {
            // UDP is fire-and-forget; if the server stays unreachable, the timeouts notice.
        }
    }

    private void Lost(string reason, TimeSpan now)
    {
        CloseSocket();
        State = ConnectionState.WaitingToReconnect;
        _reconnectAt = now + ReconnectDelay;
        Log?.Invoke($"{reason}. Reconnecting in {ReconnectDelay.TotalSeconds:0}s");
    }

    private void Stop()
    {
        CloseSocket();
        State = ConnectionState.Stopped;
    }

    private void CloseSocket()
    {
        _socket?.Dispose();
        _socket = null;
    }
}
