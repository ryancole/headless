namespace Headless.Sim;

/// <summary>A 2D vector: a position in metres, a velocity in m/s, and so on.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>The unit vector pointing at <paramref name="angle"/> radians (0 is +x, counter-clockwise).</summary>
    public static Vec2 FromAngle(double angle) => new(Math.Cos(angle), Math.Sin(angle));

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);
}
