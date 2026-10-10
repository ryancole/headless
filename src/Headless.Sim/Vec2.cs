namespace Headless.Sim;

/// <summary>2D position in metres.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);
}
