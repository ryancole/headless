using System;
using Godot;

namespace Headless.Game;

/// <summary>
/// Builds a basic ship interior: three rooms in a row (cockpit, hold, engine room) joined by doorways. Placeholder
/// geometry, plain boxes in flat colours, until there's a modular kit with bevels and textures (docs/art-style.md).
/// </summary>
/// <remarks>Ship-local space, in metres: -Z is the bow, the floor is at y = 0, the centreline is x = 0.</remarks>
public partial class ShipInterior : Node3D
{
    private const float Height = 3.2f; // Floor to ceiling.
    private const float Wall = 0.3f; // Wall, floor and ceiling thickness.
    private const float DoorWidth = 1.6f, DoorHeight = 2.4f, Frame = 0.2f;

    private sealed record Room(float HalfWidth, float Fore, float Aft)
    {
        public float Centre => (Fore + Aft) / 2;
        public float Depth => Aft - Fore;
    }

    // Bow to stern; each room starts where the previous one ends.
    private static readonly Room Cockpit = new(HalfWidth: 2.5f, Fore: -11, Aft: -6);
    private static readonly Room Hold = new(HalfWidth: 4, Fore: -6, Aft: 2);
    private static readonly Room EngineRoom = new(HalfWidth: 3, Fore: 2, Aft: 8);
    private static readonly Room[] Rooms = [Cockpit, Hold, EngineRoom];

    // Muted structure, colour only on things that matter (docs/art-style.md, "Palette").
    private readonly Material _wall = Matte(new Color(0.34f, 0.38f, 0.33f)); // grey-green
    private readonly Material _floor = Matte(new Color(0.5f, 0.51f, 0.48f)); // light grey floor plates
    private readonly Material _ceiling = Matte(new Color(0.22f, 0.24f, 0.24f)); // gunmetal
    private readonly Material _trim = Matte(new Color(0.3f, 0.32f, 0.24f), metallic: 0.3f); // olive frames and ribs
    private readonly Material _metal = Matte(new Color(0.25f, 0.27f, 0.28f), metallic: 0.5f); // consoles, reactor
    private readonly Material _crate = Matte(new Color(0.38f, 0.4f, 0.28f));
    private readonly Material _orange = Matte(new Color(0.85f, 0.4f, 0.1f));

    private static readonly Color Cyan = new(0.3f, 0.8f, 1), Green = new(0.4f, 1, 0.5f);

    /// <summary>The cockpit terminal showing the commands the ship's bot sends.</summary>
    public CommandLogScreen CommandLog { get; private set; } = null!;
    private static readonly Color LampWhite = new(1, 0.95f, 0.85f), EngineOrange = new(1, 0.5f, 0.2f);

    public override void _Ready()
    {
        foreach (var room in Rooms) Shell(room);

        // Bulkheads: solid at bow and stern, with a doorway between neighbouring rooms.
        Bulkhead(Cockpit.Fore - Wall / 2, Cockpit.HalfWidth, door: false);
        for (var i = 1; i < Rooms.Length; i++)
            Bulkhead(Rooms[i].Fore, Math.Max(Rooms[i - 1].HalfWidth, Rooms[i].HalfWidth), door: true);
        Bulkhead(EngineRoom.Aft + Wall / 2, EngineRoom.HalfWidth, door: false);

        FurnishCockpit();
        FurnishHold();
        FurnishEngineRoom();
    }

    /// <summary>A room's floor, ceiling and side walls. The side walls overlap the floor and ceiling edges.</summary>
    private void Shell(Room r)
    {
        var w = r.HalfWidth;
        Block(new(-w, -Wall, r.Fore), new(w, 0, r.Aft), _floor);
        Block(new(-w, Height, r.Fore), new(w, Height + Wall, r.Aft), _ceiling);
        Block(new(-w - Wall, -Wall, r.Fore), new(-w, Height + Wall, r.Aft), _wall);
        Block(new(w, -Wall, r.Fore), new(w + Wall, Height + Wall, r.Aft), _wall);
    }

    /// <summary>A wall across the ship at <paramref name="z"/>, optionally with a framed doorway on the centreline.</summary>
    private void Bulkhead(float z, float halfWidth, bool door)
    {
        float outer = halfWidth + Wall, front = z - Wall / 2, back = z + Wall / 2;
        if (!door)
        {
            Block(new(-outer, -Wall, front), new(outer, Height + Wall, back), _wall);
            return;
        }

        // The opening is cut to the outside of the frame, so the frame never shares a visible face with the wall.
        float open = DoorWidth / 2 + Frame, top = DoorHeight + Frame;
        Block(new(-outer, -Wall, front), new(-open, Height + Wall, back), _wall);
        Block(new(open, -Wall, front), new(outer, Height + Wall, back), _wall);
        Block(new(-open, top, front), new(open, Height + Wall, back), _wall);

        // A chunky frame that stands proud of the wall on both sides, with a green light strip over the door.
        const float proud = 0.1f;
        Block(new(-open, 0, front - proud), new(-DoorWidth / 2, DoorHeight, back + proud), _trim);
        Block(new(DoorWidth / 2, 0, front - proud), new(open, DoorHeight, back + proud), _trim);
        Block(new(-open, DoorHeight, front - proud), new(open, top, back + proud), _trim);
        foreach (var (face, side, yaw) in new[] { (front - 0.02f, -1, 0f), (back + 0.02f, 1, 180f) })
        {
            Box(new(0, top + 0.15f, face), new(DoorWidth, 0.08f, 0.04f), Glow(Green), solid: false);
            PanelLight(new(0, top + 0.15f, face + side * 0.05f), yaw, Green, energy: 0.3f, range: 2.5f);
        }
    }

    private void FurnishCockpit()
    {
        var r = Cockpit;
        // Console along the bow wall: a desk, three monitors above it and a row of small blue lights.
        Box(new(0, 0.5f, r.Fore + 0.4f), new(3.6f, 1, 0.8f), _metal);
        // The middle monitor is the command log, mostly dark so it gives off less light; the other two are plain
        // glowing panels for now. Each casts its own light into the cockpit.
        for (var i = -1; i <= 1; i++)
        {
            Box(new(i * 1.1f, 1.75f, r.Fore + 0.05f), new(1, 0.75f, 0.1f), _metal);
            if (i != 0) Box(new(i * 1.1f, 1.75f, r.Fore + 0.11f), new(0.86f, 0.62f, 0.02f), Glow(Cyan), solid: false);
            PanelLight(new(i * 1.1f, 1.75f, r.Fore + 0.16f), yaw: 180, Cyan, energy: i == 0 ? 0.15f : 0.5f, range: 4);
        }
        CommandLog = new CommandLogScreen { Position = new(0, 1.75f, r.Fore + 0.105f) };
        AddChild(CommandLog);
        for (var i = 0; i < 8; i++)
            Box(new(-1.4f + i * 0.4f, 1.01f, r.Fore + 0.65f), new(0.08f, 0.02f, 0.08f), Glow(Cyan), solid: false);

        // Pilot's seat.
        Box(new(0, 0.25f, r.Fore + 1.5f), new(0.6f, 0.5f, 0.6f), _trim);
        Box(new(0, 0.8f, r.Fore + 1.75f), new(0.6f, 1.1f, 0.12f), _trim);

        Lamp(new(0, 0, r.Centre + 0.5f), LampWhite, energy: 0.8f, range: 6);
    }

    private void FurnishHold()
    {
        var r = Hold;
        Ribs(r);

        // Cargo, with one orange crate as the accent.
        Box(new(3.0f, 0.5f, -4.6f), new(1, 1, 1), _crate);
        Box(new(3.05f, 1.4f, -4.5f), new(0.8f, 0.8f, 0.8f), _crate);
        Box(new(3.1f, 0.4f, -3.3f), new(0.8f, 0.8f, 0.8f), _crate);
        Box(new(-2.9f, 0.35f, 0.9f), new(1.2f, 0.7f, 0.8f), _orange);

        // Wall terminal on the port side.
        Box(new(-r.HalfWidth + 0.06f, 1.5f, -2), new(0.12f, 0.8f, 1.1f), _metal);
        Box(new(-r.HalfWidth + 0.13f, 1.5f, -2), new(0.02f, 0.6f, 0.9f), Glow(Cyan), solid: false);
        PanelLight(new(-r.HalfWidth + 0.18f, 1.5f, -2), yaw: -90, Cyan, energy: 0.3f, range: 3);

        Lamp(new(0, 0, r.Fore + 2.5f), LampWhite, energy: 1, range: 7);
        Lamp(new(0, 0, r.Aft - 2.5f), LampWhite, energy: 1, range: 7);
    }

    private void FurnishEngineRoom()
    {
        var r = EngineRoom;
        Ribs(r);

        // Reactor: a floor-to-ceiling drum with glowing cyan bands.
        var reactor = new Vector3(0, Height / 2, r.Centre + 0.5f);
        Cylinder(reactor, radius: 1, height: Height, _metal);
        foreach (var y in new[] { 0.8f, 1.6f, 2.4f })
            Cylinder(reactor with { Y = y }, radius: 1.04f, height: 0.12f, Glow(Cyan), solid: false);

        // The bands light the room all the way round: a ring of lights just off the drum, each shining outward.
        for (var yaw = 0f; yaw < 360; yaw += 90)
        {
            var outward = Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(yaw));
            PanelLight(reactor + outward * 1.1f, yaw, Cyan, energy: 0.8f, range: 5);
        }

        // Dim orange working light in the corners.
        Lamp(new(-r.HalfWidth + 1, 0, r.Fore + 1), EngineOrange, energy: 0.8f, range: 6);
        Lamp(new(r.HalfWidth - 1, 0, r.Aft - 1), EngineOrange, energy: 0.8f, range: 6);
    }

    /// <summary>Structural ribs every 2 m along both side walls.</summary>
    private void Ribs(Room r)
    {
        for (var z = r.Fore + 1; z < r.Aft - 0.5f; z += 2)
        foreach (var side in new[] { -1, 1 })
            Box(new(side * (r.HalfWidth - 0.075f), Height / 2, z), new(0.15f, Height, 0.25f), _trim);
    }

    /// <summary>
    /// A ceiling lamp over <paramref name="at"/> (x, z): a shadowed spotlight pointing at the floor, whose light
    /// scatters in the volumetric fog (Main.tscn) as a real shaft that anything standing in it shadows, and a shadowed
    /// point light that lights the rest of the room but leaves the fog alone (so the shaft keeps its edges).
    /// </summary>
    private void Lamp(Vector3 at, Color color, float energy, float range)
    {
        Box(new(at.X, Height - 0.05f, at.Z), new(0.8f, 0.1f, 0.8f), _metal, solid: false);
        Box(new(at.X, Height - 0.11f, at.Z), new(0.6f, 0.02f, 0.6f), Glow(color, energy: 4), solid: false);
        var position = new Vector3(at.X, Height - 0.13f, at.Z);
        AddChild(new SpotLight3D
        {
            Position = position,
            RotationDegrees = new(-90, 0, 0), // Straight down.
            LightColor = color,
            LightEnergy = energy,
            SpotRange = Height + 1,
            SpotAngle = 35,
            SpotAngleAttenuation = 0.5f, // A crisp edge to the cone.
            LightVolumetricFogEnergy = 6,
            ShadowEnabled = true,
            ShadowBlur = 0.5f, // Fairly hard-edged.
        });
        AddChild(new OmniLight3D
        {
            Position = position with { Y = Height - 0.4f },
            LightColor = color,
            LightEnergy = energy * 0.7f,
            OmniRange = range,
            LightVolumetricFogEnergy = 0,
            ShadowEnabled = true,
            ShadowBlur = 0.5f,
        });
    }

    /// <summary>
    /// The light from a glowing screen or strip, which its emissive material doesn't give off by itself: a dim,
    /// shadowed spotlight just in front of it, shining out over the hemisphere it faces. <paramref name="yaw"/> turns
    /// it from facing the bow (-Z) to the left, in degrees (180 faces the stern, -90 faces starboard).
    /// </summary>
    private void PanelLight(Vector3 at, float yaw, Color color, float energy, float range) => AddChild(new SpotLight3D
    {
        Position = at,
        RotationDegrees = new(0, yaw, 0),
        LightColor = color,
        LightEnergy = energy,
        SpotRange = range,
        SpotAngle = 85,
        LightVolumetricFogEnergy = 0.5f,
        ShadowEnabled = true,
        ShadowBlur = 0.5f,
    });

    private void Block(Vector3 min, Vector3 max, Material material) => Box((min + max) / 2, max - min, material);

    private void Box(Vector3 centre, Vector3 size, Material material, bool solid = true) =>
        Add(centre, new BoxMesh { Size = size }, solid ? new BoxShape3D { Size = size } : null, material);

    private void Cylinder(Vector3 centre, float radius, float height, Material material, bool solid = true) =>
        Add(centre, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height },
            solid ? new CylinderShape3D { Radius = radius, Height = height } : null, material);

    /// <summary>A mesh at <paramref name="centre"/>, inside a static body if it has a collision shape.</summary>
    private void Add(Vector3 centre, Mesh mesh, Shape3D? shape, Material material)
    {
        var visual = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        if (shape is null)
        {
            visual.Position = centre;
            AddChild(visual);
            return;
        }
        var body = new StaticBody3D { Position = centre };
        body.AddChild(new CollisionShape3D { Shape = shape });
        body.AddChild(visual);
        AddChild(body);
    }

    private static Material Matte(Color color, float metallic = 0.1f) =>
        new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, Metallic = metallic };

    private static Material Glow(Color color, float energy = 2) => new StandardMaterial3D
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };
}
