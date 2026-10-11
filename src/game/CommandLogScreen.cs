using System.Collections.Generic;
using System.Linq;
using Godot;
using Headless.Protocol;

namespace Headless.Game;

/// <summary>
/// An in-world terminal showing the commands the ship's bot sends to its devices, newest at the bottom. Rendered
/// pixel-sharp into its own small <see cref="SubViewport"/> and shown on a glowing quad (docs/art-style.md, "Screens").
/// </summary>
public partial class CommandLogScreen : Node3D
{
    private const int Width = 128, Height = 96; // Like 0x10c's monitors.
    private const int VisibleLines = 8;

    private static readonly Color Background = new(0.02f, 0.05f, 0.09f);
    private static readonly Color Text = new(0.45f, 0.85f, 1);
    private static readonly Color Header = new(0.5f, 1, 0.6f);
    private static readonly Color Refused = new(1, 0.55f, 0.2f);

    /// <summary>The screen's size in metres.</summary>
    [Export] public Vector2 Size { get; set; } = new(0.84f, 0.63f);

    /// <summary>Which ship's log to show. Entries for other ships are ignored.</summary>
    public int ShipId
    {
        get => _shipId;
        set
        {
            if (_shipId == value) return;
            _shipId = value;
            Clear();
        }
    }

    private readonly List<string> _lines = [];
    private RichTextLabel _label = null!;
    private int _shipId;
    private uint _lastEntryId;

    public override void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(Width, Height),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var background = new ColorRect { Color = Background };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        viewport.AddChild(background);

        // A monospace system font with no antialiasing, so every glyph lands on whole pixels.
        var font = new SystemFont
        {
            FontNames = ["Consolas", "Lucida Console", "DejaVu Sans Mono", "monospace"],
            Antialiasing = TextServer.FontAntialiasing.None,
            SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
        };
        _label = new RichTextLabel { BbcodeEnabled = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off };
        _label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _label.OffsetLeft = _label.OffsetTop = 2;
        _label.AddThemeFontOverride("normal_font", font);
        _label.AddThemeFontSizeOverride("normal_font_size", 8);
        _label.AddThemeConstantOverride("line_separation", -1);
        _label.AddThemeColorOverride("default_color", Text);
        viewport.AddChild(_label);
        AddChild(viewport);

        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Size }, // Faces +Z, into the room.
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black, // Lit by its own glow only, so room lights don't wash it out.
                EmissionEnabled = true,
                EmissionTexture = viewport.GetTexture(),
                EmissionEnergyMultiplier = 2.5f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Redraw();
    }

    /// <summary>Adds the entries for <see cref="ShipId"/> that haven't been shown yet.</summary>
    public void Show(ShipLog log)
    {
        var entries = log.Entries.Where(e => e.ShipId == ShipId).ToList();
        if (entries.Count > 0 && entries[^1].Id < _lastEntryId) Clear(); // Ids went backwards: the server restarted.

        var fresh = entries.Where(e => e.Id > _lastEntryId).ToList();
        if (fresh.Count == 0) return;

        // Ids count up by one per command, so a jump means entries we never saw (the server keeps only the latest).
        if (_lastEntryId != 0 && fresh[0].Id != _lastEntryId + 1) _lines.Add("[color=#555]  ...[/color]");
        _lines.AddRange(fresh.Select(Describe));
        _lastEntryId = fresh[^1].Id;
        if (_lines.Count > VisibleLines) _lines.RemoveRange(0, _lines.Count - VisibleLines);
        Redraw();
    }

    /// <summary>One line, e.g. "  412 ENG SET  0.60", plus why if the ship refused it.</summary>
    private static string Describe(ShipLogEntry e)
    {
        var device = e.Kind switch { DeviceKind.Engine => "ENG", DeviceKind.Gyroscope => "GYR", _ => $"D{e.Device}" };
        var command = e.Command == DeviceCommands.Set ? "SET" : $"C{e.Command}";
        var line = $"{e.Tick,5} {device} {command} {e.Value + 0.0,5:0.00}"; // + 0.0 turns -0 into 0
        var refusal = e.Result switch
        {
            CommandResult.Ok => null,
            CommandResult.OverBudget => "BUDGET",
            CommandResult.InvalidValue => "INVALID",
            CommandResult.UnknownCommand => "UNKNOWN",
            _ => e.Result.ToString().ToUpperInvariant(),
        };
        return refusal is null ? line : $"[color=#{Refused.ToHtml(false)}]{line} {refusal}[/color]";
    }

    private void Clear()
    {
        _lines.Clear();
        _lastEntryId = 0;
        if (IsNodeReady()) Redraw();
    }

    private void Redraw()
    {
        var header = $"[color=#{Header.ToHtml(false)}]CMD LOG{(ShipId != 0 ? $"  SHIP {ShipId}" : "")}[/color]";
        var body = _lines.Count > 0 ? string.Join("\n", _lines) : "[color=#555]NO COMMANDS YET[/color]";
        _label.Text = header + "\n" + body;
    }
}
