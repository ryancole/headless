using Godot;

namespace Headless.Game;

/// <summary>
/// First-person walking: WASD to move, mouse to look, Space to jump, Shift to run. Esc frees the mouse; click to
/// grab it again.
/// </summary>
/// <remarks>
/// Client-side only for now: the server doesn't know where the player is. The player is a child of the ship, so
/// positions are ship-local and the ship can move later without dragging the player through its walls.
/// </remarks>
public partial class Player : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 4;
    [Export] public float RunSpeed { get; set; } = 6.5f;
    [Export] public float JumpSpeed { get; set; } = 4.5f;

    /// <summary>Radians turned per pixel of mouse movement.</summary>
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;

    private readonly float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        Bind("move_forward", Key.W);
        Bind("move_back", Key.S);
        Bind("move_left", Key.A);
        Bind("move_right", Key.D);
        Bind("jump", Key.Space);
        Bind("run", Key.Shift);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                // Yaw turns the body, pitch tilts only the camera (and stops short of straight up or down).
                RotateY(-motion.Relative.X * MouseSensitivity);
                var pitch = _camera.Rotation.X - motion.Relative.Y * MouseSensitivity;
                _camera.Rotation = new Vector3(Mathf.Clamp(pitch, -1.5f, 1.5f), 0, 0);
                break;
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;
        if (!IsOnFloor()) velocity.Y -= _gravity * (float)delta;
        else if (Input.IsActionJustPressed("jump")) velocity.Y = JumpSpeed;

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var direction = Basis * new Vector3(input.X, 0, input.Y);
        var speed = Input.IsActionPressed("run") ? RunSpeed : WalkSpeed;
        velocity.X = direction.X * speed;
        velocity.Z = direction.Z * speed;

        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>Adds a key binding in code, so it needs no project settings. Physical keys: same place on any layout.</summary>
    private static void Bind(string action, Key key)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
