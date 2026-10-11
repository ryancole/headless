using System.Linq;
using Godot;
using Headless.Protocol;

namespace Headless.Game;

/// <summary>
/// Entry scene: the player walking around inside a ship, plus the connection to the server (shown in the HUD, and
/// snapshots logged). The ship interior isn't linked to server state yet.
/// </summary>
public partial class Main : Node3D
{
    [Export] public string Host { get; set; } = "localhost";
    [Export] public int Port { get; set; } = 5080;
    [Export] public string Token { get; set; } = "dev-player-1";

    private ServerConnection? _connection;
    private Label _status = null!;
    private string? _screenshotPath;
    private int _frames;

    public override void _Ready()
    {
        // Command-line overrides, after a "--": e.g. -- --token=dev-player-2
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--host=")) Host = arg["--host=".Length..];
            else if (arg.StartsWith("--port=")) Port = int.Parse(arg["--port=".Length..]);
            else if (arg.StartsWith("--token=")) Token = arg["--token=".Length..];
            else if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..]; // Save a PNG and quit.
        }

        _status = GetNode<Label>("Hud/Status");
        _connection = new ServerConnection(Host, Port, ClientRole.Player, Token);
        _connection.Log += message =>
        {
            GD.Print(message);
            _status.Text = message;
        };
        _connection.Connected += accepted =>
        {
            GD.Print($"Connected as player {accepted.SubjectId} '{accepted.SubjectName}' ({accepted.TickSeconds:0.0}s ticks)");
            _status.Text = $"Connected as {accepted.SubjectName}";
        };
        // The ship interior is the player's first ship, so its terminals show that ship's commands and status.
        var interior = GetNode<ShipInterior>("Ship/Interior");
        _connection.ShipLogReceived += interior.CommandLog.Show;
        _connection.PlayerSnapshotReceived += snapshot =>
        {
            if (snapshot.Ships.Count > 0) interior.CommandLog.ShipId = snapshot.Ships[0].Id;
        };
        _connection.PlayerSnapshotReceived += interior.Status.Show;
        _connection.PlayerSnapshotReceived += snapshot =>
            GD.Print($"[tick {snapshot.Tick}] " + string.Join(", ",
                snapshot.Ships.Select(ship => $"ship {ship.Id} at ({ship.X:0.0}, {ship.Y:0.0}), "
                    + $"heading {Mathf.RadToDeg(ship.Heading):0} deg, {new Vector2((float)ship.VelocityX, (float)ship.VelocityY).Length():0.0} m/s")));
    }

    // Polled from the main thread: no receive thread or queue needed, and handlers can touch the scene tree.
    public override void _Process(double delta)
    {
        _connection?.Update();

        // A second in, so shadows and glow have settled.
        if (_screenshotPath is not null && ++_frames == 60)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }

    public override void _ExitTree() => _connection?.Dispose(); // Sends Disconnect, so the server frees our slot now.
}
