using System;
using Godot;
using Headless.Protocol;

namespace Headless.Game;

/// <summary>
/// An in-world terminal showing where the ship is and how it's moving: position, speed, heading (where the nose
/// points) and course (where it's actually going, which differs while drifting under Newtonian flight).
/// </summary>
public partial class ShipStatusScreen : TerminalScreen
{
    /// <summary>With no snapshot for this long, the screen says the data is stale.</summary>
    private const ulong SignalTimeoutMs = 3000;

    /// <summary>Below this speed the ship is treated as stopped, so its course is meaningless.</summary>
    private const double StoppedSpeed = 0.05;

    private long _tick;
    private ShipData? _ship;
    private ulong _lastUpdateMs;
    private bool _lost;

    /// <summary>Shows the first ship in the snapshot (the one this interior belongs to).</summary>
    public void Show(PlayerSnapshot snapshot)
    {
        _tick = snapshot.Tick;
        _ship = snapshot.Ships.Count > 0 ? snapshot.Ships[0] : null;
        _lastUpdateMs = Time.GetTicksMsec();
        _lost = false;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_ship is null || _lost || Time.GetTicksMsec() - _lastUpdateMs < SignalTimeoutMs) return;
        _lost = true;
        Refresh();
    }

    protected override void Redraw()
    {
        if (_ship is not { } ship)
        {
            SetText(Colored(Header, "NAV") + $"\n[color={Dim}]NO SIGNAL[/color]");
            return;
        }

        var speed = Math.Sqrt(ship.VelocityX * ship.VelocityX + ship.VelocityY * ship.VelocityY);
        var course = speed < StoppedSpeed ? "---" : Bearing(Math.Atan2(ship.VelocityY, ship.VelocityX));
        var lines = new[]
        {
            Colored(Header, $"NAV  SHIP {ship.Id}"),
            $"TICK  {_tick,10}",
            "",
            $"POS X {Distance(ship.X),12}",
            $"    Y {Distance(ship.Y),12}",
            $"SPEED {speed,8:0.0} m/s",
            $"HDG   {Bearing(ship.Heading),8} deg",
            $"CRS   {course,8} deg",
        };
        var text = string.Join("\n", lines);
        if (_lost) text += "\n\n" + Colored(Warning, "SIGNAL LOST");
        SetText(text);
    }

    /// <summary>Metres, switching to kilometres once the number gets too wide for the screen.</summary>
    private static string Distance(double metres) =>
        Math.Abs(metres) < 100_000 ? $"{metres + 0.0:0.0} m" : $"{metres / 1000:0.00} km"; // + 0.0 turns -0 into 0

    /// <summary>An angle in radians (0 = +X, counter-clockwise) as whole degrees 000..359.</summary>
    private static string Bearing(double radians)
    {
        var degrees = (int)Math.Round(Mathf.RadToDeg(radians)) % 360;
        return $"{(degrees + 360) % 360:000}";
    }
}
