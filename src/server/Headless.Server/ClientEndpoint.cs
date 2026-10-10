using System.Net.WebSockets;

namespace Headless.Server;

/// <summary>The WebSocket plumbing shared by the bot and player endpoints: token auth, accept, run, clean up.</summary>
public static class ClientEndpoint
{
    /// <param name="authenticate">Maps a bearer token to the id it grants access to (a ship or a player), or null.</param>
    /// <param name="connected">Registers the connection and sends its hello.</param>
    /// <param name="disconnected">Unregisters the connection.</param>
    public static async Task ServeAsync(
        HttpContext ctx,
        IHostApplicationLifetime lifetime,
        Func<string, int?> authenticate,
        Action<int, ClientConnection> connected,
        Action<int, ClientConnection> disconnected)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var auth = ctx.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || authenticate(auth[prefix.Length..].Trim()) is not { } id)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        var client = new ClientConnection(socket);
        connected(id, client);

        // The server waits for open requests before exiting, and a client connection never ends by itself,
        // so close it as soon as shutdown starts (e.g. Ctrl-C). 1001 "going away" tells it to reconnect later.
        using var onStopping = lifetime.ApplicationStopping.Register(
            () => client.Close("server shutting down", WebSocketCloseStatus.EndpointUnavailable));
        try
        {
            await client.RunAsync(ctx.RequestAborted);
        }
        finally
        {
            disconnected(id, client);
        }
    }
}
