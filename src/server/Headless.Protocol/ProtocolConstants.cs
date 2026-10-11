namespace Headless.Protocol;

public static class ProtocolConstants
{
    /// <summary>"HDLS" as little-endian bytes. Starts every <see cref="ConnectRequest"/>, so stray traffic is ignored cheaply.</summary>
    public const uint Magic = 0x534C4448;

    /// <summary>Bumped on any breaking change to the wire format. The server denies versions it doesn't speak.</summary>
    public const ushort Version = 2;

    /// <summary>Largest packet either side sends or accepts. Stays under typical internet MTUs to avoid fragmentation.</summary>
    public const int MaxPacketSize = 1200;

    /// <summary>Longest token or name, in UTF-8 bytes (strings carry a 1-byte length).</summary>
    public const int MaxStringBytes = 255;

    /// <summary>How often a client should send <see cref="KeepAlive"/>.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(1);

    /// <summary>Either side drops the connection after hearing nothing for this long.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>How often a bot resends a <see cref="DeviceCommand"/> that hasn't been acknowledged yet.</summary>
    public static readonly TimeSpan CommandRetryInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How often a client resends <see cref="ConnectRequest"/> while waiting for an answer.</summary>
    public static readonly TimeSpan ConnectRetryInterval = TimeSpan.FromMilliseconds(500);
}
