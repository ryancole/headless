namespace Headless.Protocol.Tests;

public class PacketCodecTests
{
    public static TheoryData<Packet> AllPackets() =>
    [
        new ConnectRequest(ProtocolConstants.Version, ClientRole.Bot, 0x0102030405060708, "dev-token-1"),
        new ConnectRequest(ProtocolConstants.Version, ClientRole.Player, 42, ""),
        new KeepAlive(0xFEEDFACECAFEBEEF),
        new Disconnect(7),
        new DeviceCommand(99, 3, 1, DeviceCommands.Set, -0.25),
        new ConnectAccepted(42, 99, 2, "Prospector", 0.5f),
        new ConnectDenied(42, DenyReason.InvalidToken),
        new ShipSnapshot(99, 1234, new ShipData(2, 1, -1.5, 1000.25, 3, -4, 1.5), 10_000,
            [new DeviceData(DeviceKind.Engine, 20_000, 1), new DeviceData(DeviceKind.Gyroscope, 0.5, -0.5)]),
        new ShipSnapshot(99, 1, new ShipData(2, 1, 0, 0, 0, 0, 0), 1, []),
        new PlayerSnapshot(99, 1234, [new ShipData(2, 1, 0, 0, 0, 0, 0), new ShipData(4, 1, 5, -6, 7, 8, -3)]),
        new PlayerSnapshot(99, 1, []),
        new Disconnected(99, DisconnectReason.ServerShutdown),
        new CommandAck(99, 3, CommandResult.OverBudget),
    ];

    [Theory]
    [MemberData(nameof(AllPackets))]
    public void Every_packet_round_trips(Packet packet)
    {
        var bytes = PacketCodec.Encode(packet);

        Assert.True(PacketCodec.TryDecode(bytes, out var decoded));
        Assert.Equal(packet, decoded);
    }

    [Theory]
    [MemberData(nameof(AllPackets))]
    public void Truncated_or_padded_packets_are_rejected(Packet packet)
    {
        var bytes = PacketCodec.Encode(packet);

        for (var length = 0; length < bytes.Length; length++)
            Assert.False(PacketCodec.TryDecode(bytes.AsSpan(0, length), out _), $"accepted {length} of {bytes.Length} bytes");
        Assert.False(PacketCodec.TryDecode([.. bytes, 0], out _));
    }

    [Fact]
    public void Connect_request_has_the_documented_layout()
    {
        var bytes = PacketCodec.Encode(new ConnectRequest(2, ClientRole.Player, 0x0807060504030201, "ab"));

        byte[] expected =
        [
            0x01,                                           // type
            (byte)'H', (byte)'D', (byte)'L', (byte)'S',     // magic
            0x02, 0x00,                                     // version (u16 LE)
            0x02,                                           // role: player
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, // nonce (u64 LE)
            0x02, (byte)'a', (byte)'b',                     // token: length + UTF-8
        ];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Device_command_has_the_documented_layout()
    {
        var bytes = PacketCodec.Encode(new DeviceCommand(0x0807060504030201, 0x0D0C0B0A, 1, DeviceCommands.Set, 0.5));

        byte[] expected =
        [
            0x04,                                           // type
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, // session (u64 LE)
            0x0A, 0x0B, 0x0C, 0x0D,                         // sequence (u32 LE)
            0x01,                                           // device index
            0x01,                                           // command: set
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x3F, // value: 0.5 (f64 LE)
        ];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Ship_snapshot_with_two_devices_is_108_bytes()
    {
        var bytes = PacketCodec.Encode(new ShipSnapshot(1, 2, new ShipData(3, 4, 5, 6, 7, 8, 9), 10,
            [new DeviceData(DeviceKind.Engine, 1, 0), new DeviceData(DeviceKind.Gyroscope, 1, 0)]));

        Assert.Equal(1 + 8 + 8 + (4 + 4 + 5 * 8) + 8 + 1 + 2 * (1 + 8 + 8), bytes.Length);
        Assert.Equal((byte)PacketType.ShipSnapshot, bytes[0]);
    }

    [Fact]
    public void Unknown_device_kinds_are_rejected()
    {
        var bytes = PacketCodec.Encode(new ShipSnapshot(1, 2, new ShipData(3, 4, 5, 6, 7, 8, 9), 10,
            [new DeviceData(DeviceKind.Engine, 1, 0)]));
        bytes[^17] = 99; // the device's kind byte

        Assert.False(PacketCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Wrong_magic_is_rejected()
    {
        var bytes = PacketCodec.Encode(new ConnectRequest(1, ClientRole.Bot, 1, "t"));
        bytes[1] ^= 0xFF;

        Assert.False(PacketCodec.TryDecode(bytes, out _));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x7F, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Unknown_or_empty_packets_are_rejected(byte[] data)
    {
        Assert.False(PacketCodec.TryDecode(data, out _));
    }

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        var bytes = PacketCodec.Encode(new ConnectDenied(1, DenyReason.InvalidToken));
        bytes[^1] = 200;

        Assert.False(PacketCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Invalid_utf8_is_rejected()
    {
        var bytes = PacketCodec.Encode(new ConnectRequest(1, ClientRole.Bot, 1, "a"));
        bytes[^1] = 0xFF;

        Assert.False(PacketCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Oversized_packets_are_rejected_both_ways()
    {
        var tooManyShips = Enumerable.Range(0, 25).Select(i => new ShipData(i, 1, 0, 0, 0, 0, 0)).ToArray();

        Assert.Throws<ArgumentException>(() => PacketCodec.Encode(new PlayerSnapshot(1, 1, tooManyShips)));
        Assert.False(PacketCodec.TryDecode(new byte[ProtocolConstants.MaxPacketSize + 1], out _));
    }

    [Fact]
    public void Strings_longer_than_255_bytes_cannot_be_encoded()
    {
        Assert.Throws<ArgumentException>(() => PacketCodec.Encode(new ConnectRequest(1, ClientRole.Bot, 1, new string('x', 256))));
    }
}
