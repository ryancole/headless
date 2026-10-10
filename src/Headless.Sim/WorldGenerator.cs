namespace Headless.Sim;

public static class WorldGenerator
{
    /// <summary>
    /// A small starter system: one trading station at the origin and a ring of asteroids 4–25 km out.
    /// The same seed always produces the same world.
    /// </summary>
    public static World CreateStarterSystem(int seed, SimConfig? config = null)
    {
        var world = new World(config ?? new SimConfig());
        var rng = new Random(seed);

        world.AddStation("Meridian Exchange", Vec2.Zero, orePrice: 10, fuelPrice: 1);

        for (var i = 0; i < 8; i++)
        {
            var angle = rng.NextDouble() * Math.Tau;
            var distance = 4_000 + rng.NextDouble() * 21_000;
            var position = new Vec2(Math.Cos(angle) * distance, Math.Sin(angle) * distance);
            var radius = 50 + rng.NextDouble() * 250;
            var ore = Math.Round(200 + rng.NextDouble() * 800);
            world.AddAsteroid(position, radius, ore);
        }

        return world;
    }
}
