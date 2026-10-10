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

// Protocol v1, see docs/protocol-v1.md. Both endpoints authenticate with "Authorization: Bearer <token>".
// The hello is queued before attaching, so it always arrives ahead of the first snapshot.

// Bots fly ships. Token: the ship's bot token.
app.Map("/v1/bot", (HttpContext ctx, GameHost host, IHostApplicationLifetime lifetime) =>
    ClientEndpoint.ServeAsync(ctx, lifetime,
        authenticate: token => host.TryGetShipForBotToken(token, out var shipId) ? shipId : null,
        connected: (shipId, bot) =>
        {
            bot.Send(new HelloMessage("v1", shipId, host.Rules));
            host.AttachBot(shipId, bot);
        },
        disconnected: host.DetachBot));

// Players are the humans, who ride in ships. Token: the player's own token.
app.Map("/v1/player", (HttpContext ctx, GameHost host, IHostApplicationLifetime lifetime) =>
    ClientEndpoint.ServeAsync(ctx, lifetime,
        authenticate: token => host.TryGetPlayerForToken(token, out var playerId) ? playerId : null,
        connected: (playerId, client) =>
        {
            client.Send(new PlayerHelloMessage("v1", playerId, host.Rules));
            host.AttachPlayer(playerId, client);
        },
        disconnected: host.DetachPlayer));

app.Run();
