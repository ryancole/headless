using Microsoft.Extensions.Options;

namespace Headless.Server;

/// <summary>Drives the simulation at a fixed wall-clock rate.</summary>
public sealed class TickLoop(GameHost host, IOptions<GameOptions> options, ILogger<TickLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(1 / options.Value.TicksPerSecond);
        logger.LogInformation("Tick loop running at {Rate} Hz ({SimSeconds}s sim time per tick)",
            options.Value.TicksPerSecond, options.Value.Sim.TickSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                host.Tick();
            }
            catch (Exception ex)
            {
                // A bug in one tick shouldn't take the server down; keep ticking and make noise.
                logger.LogError(ex, "Tick failed");
            }
        }
    }
}
