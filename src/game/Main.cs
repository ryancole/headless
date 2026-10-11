using System.Linq;
using Godot;
using Headless.Protocol;

namespace Headless.Game;

/// <summary>Entry scene. Connects to the server as a player and logs what it hears. Nothing visual yet.</summary>
public partial class Main : Node3D
{
    [Export] public string Host { get; set; } = "localhost";
    [Export] public int Port { get; set; } = 5080;
    [Export] public string Token { get; set; } = "dev-player-1";

    private ServerConnection? _connection;

    public override void _Ready()
    {
        // Command-line overrides, after a "--": e.g. -- --token=dev-player-2
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--host=")) Host = arg["--host=".Length..];
            else if (arg.StartsWith("--port=")) Port = int.Parse(arg["--port=".Length..]);
            else if (arg.StartsWith("--token=")) Token = arg["--token=".Length..];
        }

        _connection = new ServerConnection(Host, Port, ClientRole.Player, Token);
        _connection.Log += message => GD.Print(message);
        _connection.Connected += accepted =>
            GD.Print($"Connected as player {accepted.SubjectId} '{accepted.SubjectName}' ({accepted.TickSeconds:0.0}s ticks)");
        _connection.PlayerSnapshotReceived += snapshot =>
            GD.Print($"[tick {snapshot.Tick}] " + string.Join(", ",
                snapshot.Ships.Select(ship => $"ship {ship.Id} at ({ship.X:0.0}, {ship.Y:0.0})")));
    }

    // Polled from the main thread: no receive thread or queue needed, and handlers can touch the scene tree.
    public override void _Process(double delta) => _connection?.Update();

    public override void _ExitTree() => _connection?.Dispose(); // Sends Disconnect, so the server frees our slot now.
}
