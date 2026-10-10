using System.Net.WebSockets;
using Headless.Protocol.V1;
using Headless.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.AddSingleton<GameHost>();
builder.Services.AddHostedService<TickLoop>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

app.MapGet("/healthz", () => Results.Ok("ok"));

// Bot protocol v1. Authenticate with "Authorization: Bearer <ship token>". See docs/protocol-v1.md.
app.Map("/v1/bot", async (HttpContext ctx, GameHost host, IHostApplicationLifetime lifetime) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var auth = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || !host.TryGetShipForToken(auth[prefix.Length..].Trim(), out var shipId))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    var bot = new BotConnection(shipId, socket);
    host.Attach(bot);
    bot.Send(new HelloMessage("v1", shipId, host.Rules));

    // The server waits for open requests before exiting, and a bot connection never ends by itself,
    // so close it as soon as shutdown starts (e.g. Ctrl-C). 1001 "going away" tells the bot to reconnect later.
    using var onStopping = lifetime.ApplicationStopping.Register(
        () => bot.Close("server shutting down", WebSocketCloseStatus.EndpointUnavailable));
    try
    {
        await bot.RunAsync(ctx.RequestAborted);
    }
    finally
    {
        host.Detach(bot);
    }
});

app.Run();
