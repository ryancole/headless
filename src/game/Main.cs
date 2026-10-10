using Godot;

namespace Headless.Game;

/// <summary>Entry scene. Placeholder until the client connects to the server.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print("Headless client started.");
    }
}
