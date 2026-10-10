using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Headless.Protocol;
using Headless.Protocol.V1;

namespace Headless.Server;

/// <summary>One bot's WebSocket. Outgoing messages go through a small bounded queue that drops the oldest
/// snapshot when the bot falls behind — a slow bot sees fresher state, it never backs up the server.</summary>
public sealed class BotConnection(int shipId, WebSocket socket)
{
    private const int MaxMessageBytes = 64 * 1024;

    private readonly Channel<ServerMessage> _outbox = Channel.CreateBounded<ServerMessage>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _closed = new();
    private string _closeReason = "closed";
    private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;

    public int ShipId { get; } = shipId;

    public void Send(ServerMessage message) => _outbox.Writer.TryWrite(message);

    public void Close(string reason, WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure)
    {
        _closeReason = reason;
        _closeStatus = status;
        _closed.Cancel();
    }

    public async Task RunAsync(CancellationToken requestAborted)
    {
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _closed.Token);
        var send = SendLoopAsync(sendCts.Token);

        // Close() deliberately doesn't cancel the receive: cancelling a WebSocket receive aborts the socket
        // without a close frame. Instead we send our close frame below, and the bot's reply ends the loop.
        var receive = ReceiveLoopAsync(requestAborted);

        await Task.WhenAny(send, receive);
        await sendCts.CancelAsync();
        await Quietly(send);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await socket.CloseOutputAsync(_closeStatus, _closeReason, timeout.Token);
                await receive.WaitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                socket.Abort(); // The bot didn't finish the close handshake in time.
            }
        }
        await Quietly(receive);
    }

    private static async Task Quietly(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var message in _outbox.Reader.ReadAllAsync(ct))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>();
        while (!ct.IsCancellationRequested)
        {
            buffer.ResetWrittenCount();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(4096), ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                buffer.Advance(result.Count);
                if (buffer.WrittenCount > MaxMessageBytes)
                {
                    Close($"message exceeds {MaxMessageBytes} bytes");
                    return;
                }
            } while (!result.EndOfMessage);

            // Bots have nothing to send yet; tell them rather than silently dropping it.
            Send(new ErrorMessage("unsupported_message", "The server doesn't accept messages from bots yet."));
        }
    }
}
