using Godot;

namespace Headless.Game;

/// <summary>
/// An in-world terminal: a few lines of BBCode text rendered pixel-sharp into its own small <see cref="SubViewport"/>
/// and shown on a glowing quad (docs/art-style.md, "Screens"). Subclasses decide what it says via <see cref="Redraw"/>.
/// </summary>
public abstract partial class TerminalScreen : Node3D
{
    protected const int Width = 128, Height = 96; // Like 0x10c's monitors.

    protected static readonly Color Background = new(0.02f, 0.05f, 0.09f);
    protected static readonly Color Text = new(0.45f, 0.85f, 1);
    protected static readonly Color Header = new(0.5f, 1, 0.6f);
    protected static readonly Color Warning = new(1, 0.55f, 0.2f);
    protected const string Dim = "#555";

    /// <summary>The screen's size in metres.</summary>
    [Export] public Vector2 Size { get; set; } = new(0.84f, 0.63f);

    private RichTextLabel _label = null!;

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

    /// <summary>Rebuilds the screen's text. Safe to call before the screen is in the tree (it redraws once ready).</summary>
    protected void Refresh()
    {
        if (IsNodeReady()) Redraw();
    }

    /// <summary>Sets the text via <see cref="SetText"/>.</summary>
    protected abstract void Redraw();

    protected void SetText(string bbcode) => _label.Text = bbcode;

    protected static string Colored(Color color, string text) => $"[color=#{color.ToHtml(false)}]{text}[/color]";
}
