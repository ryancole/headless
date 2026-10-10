namespace Headless.Sim;

/// <summary>2D vector in metres (or m/s, m/s²). Space is a plane for now; the 3D part of the game lives inside ships.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>Returns this vector scaled down so its length is at most <paramref name="max"/>.</summary>
    public Vec2 ClampLength(double max)
    {
        var len = Length;
        return len > max && len > 0 ? this * (max / len) : this;
    }

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => a * s;
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
}
