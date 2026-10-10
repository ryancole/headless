using System.Buffers.Binary;
using System.Text;

namespace Headless.Protocol;

/// <summary>Encodes and decodes <see cref="Packet"/>s. All integers and floats are little-endian.</summary>
public static class PacketCodec
{
    /// <summary>Encodes <paramref name="packet"/> into a new array.</summary>
    /// <exception cref="ArgumentException">The packet doesn't fit in <see cref="ProtocolConstants.MaxPacketSize"/>, or a string is too long.</exception>
    public static byte[] Encode(Packet packet)
    {
        Span<byte> buffer = stackalloc byte[ProtocolConstants.MaxPacketSize];
        return buffer[..Encode(packet, buffer)].ToArray();
    }

    /// <summary>Encodes <paramref name="packet"/> into <paramref name="destination"/> and returns the number of bytes written.</summary>
    public static int Encode(Packet packet, Span<byte> destination)
    {
        var w = new Writer(destination);
        switch (packet)
        {
            case ConnectRequest p:
                w.U8((byte)PacketType.ConnectRequest);
                w.U32(ProtocolConstants.Magic);
                w.U16(p.Version);
                w.U8((byte)p.Role);
                w.U64(p.Nonce);
                w.String(p.Token);
                break;
            case KeepAlive p:
                w.U8((byte)PacketType.KeepAlive);
                w.U64(p.Session);
                break;
            case Disconnect p:
                w.U8((byte)PacketType.Disconnect);
                w.U64(p.Session);
                break;
            case ConnectAccepted p:
                w.U8((byte)PacketType.ConnectAccepted);
                w.U64(p.Nonce);
                w.U64(p.Session);
                w.I32(p.SubjectId);
                w.String(p.SubjectName);
                w.F32(p.TickSeconds);
                break;
            case ConnectDenied p:
                w.U8((byte)PacketType.ConnectDenied);
                w.U64(p.Nonce);
                w.U8((byte)p.Reason);
                break;
            case ShipSnapshot p:
                w.U8((byte)PacketType.ShipSnapshot);
                w.U64(p.Session);
                w.I64(p.Tick);
                w.Ship(p.Ship);
                break;
            case PlayerSnapshot p:
                w.U8((byte)PacketType.PlayerSnapshot);
                w.U64(p.Session);
                w.I64(p.Tick);
                if (p.Ships.Count > ushort.MaxValue) throw new ArgumentException("Too many ships.", nameof(packet));
                w.U16((ushort)p.Ships.Count);
                foreach (var ship in p.Ships) w.Ship(ship);
                break;
            case Disconnected p:
                w.U8((byte)PacketType.Disconnected);
                w.U64(p.Session);
                w.U8((byte)p.Reason);
                break;
            default:
                throw new ArgumentException($"Unknown packet {packet.GetType().Name}.", nameof(packet));
        }
        return w.Position;
    }

    /// <summary>
    /// Decodes a packet. Returns false for anything malformed: unknown type, wrong magic, truncated, trailing bytes,
    /// invalid enum values or strings. UDP receivers should silently drop such packets.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out Packet? packet)
    {
        packet = null;
        if (data.Length is 0 or > ProtocolConstants.MaxPacketSize) return false;

        var r = new Reader(data);
        try
        {
            packet = (PacketType)r.U8() switch
            {
                PacketType.ConnectRequest => r.U32() == ProtocolConstants.Magic
                    ? new ConnectRequest(r.U16(), r.Enum<ClientRole>(), r.U64(), r.String())
                    : null,
                PacketType.KeepAlive => new KeepAlive(r.U64()),
                PacketType.Disconnect => new Disconnect(r.U64()),
                PacketType.ConnectAccepted => new ConnectAccepted(r.U64(), r.U64(), r.I32(), r.String(), r.F32()),
                PacketType.ConnectDenied => new ConnectDenied(r.U64(), r.Enum<DenyReason>()),
                PacketType.ShipSnapshot => new ShipSnapshot(r.U64(), r.I64(), r.Ship()),
                PacketType.PlayerSnapshot => ReadPlayerSnapshot(ref r),
                PacketType.Disconnected => new Disconnected(r.U64(), r.Enum<DisconnectReason>()),
                _ => null,
            };
        }
        catch (MalformedPacketException)
        {
            packet = null;
        }

        if (packet is null || !r.AtEnd)
        {
            packet = null;
            return false;
        }
        return true;
    }

    private static PlayerSnapshot ReadPlayerSnapshot(ref Reader r)
    {
        var session = r.U64();
        var tick = r.I64();
        var count = r.U16();
        var ships = new ShipData[count];
        for (var i = 0; i < count; i++) ships[i] = r.Ship();
        return new PlayerSnapshot(session, tick, ships);
    }

    private sealed class MalformedPacketException : Exception;

    private ref struct Writer(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        public int Position { get; private set; }

        public void U8(byte v) => Take(1)[0] = v;
        public void U16(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), v);
        public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), v);
        public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), v);
        public void I32(int v) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), v);
        public void I64(long v) => BinaryPrimitives.WriteInt64LittleEndian(Take(8), v);
        public void F32(float v) => BinaryPrimitives.WriteSingleLittleEndian(Take(4), v);
        public void F64(double v) => BinaryPrimitives.WriteDoubleLittleEndian(Take(8), v);

        public void String(string v)
        {
            var length = Encoding.UTF8.GetByteCount(v);
            if (length > ProtocolConstants.MaxStringBytes)
                throw new ArgumentException($"String is {length} bytes; the limit is {ProtocolConstants.MaxStringBytes}.");
            U8((byte)length);
            Encoding.UTF8.GetBytes(v, Take(length));
        }

        public void Ship(ShipData s)
        {
            I32(s.Id);
            I32(s.OwnerPlayerId);
            F64(s.X);
            F64(s.Y);
        }

        private Span<byte> Take(int count)
        {
            if (Position + count > _buffer.Length)
                throw new ArgumentException($"Packet exceeds {_buffer.Length} bytes.");
            var span = _buffer.Slice(Position, count);
            Position += count;
            return span;
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly bool AtEnd => _position == _data.Length;

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

        public T Enum<T>() where T : struct, Enum
        {
            var value = (T)(object)U8();
            return System.Enum.IsDefined(value) ? value : throw new MalformedPacketException();
        }

        public string String()
        {
            var bytes = Take(U8());
            try { return StrictUtf8.GetString(bytes); }
            catch (ArgumentException) { throw new MalformedPacketException(); }
        }

        public ShipData Ship() => new(I32(), I32(), F64(), F64());

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length) throw new MalformedPacketException();
            var span = _data.Slice(_position, count);
            _position += count;
            return span;
        }
    }
}
