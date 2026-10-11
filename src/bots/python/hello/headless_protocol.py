"""
The Headless wire protocol in plain Python (standard library only).

Small binary packets over UDP, little-endian. This module is a reference implementation of the
client side; copy it into your own bot, or port it to your language. The byte layout is documented
in docs/protocol.md.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass
from enum import IntEnum

MAGIC = 0x534C4448  # b"HDLS" read as a little-endian u32
VERSION = 2
MAX_PACKET_SIZE = 1200
MAX_STRING_BYTES = 255

KEEP_ALIVE_INTERVAL = 1.0      # seconds between keep-alives
TIMEOUT = 5.0                  # give up after hearing nothing for this long
CONNECT_RETRY_INTERVAL = 0.5   # resend the connect request this often while waiting
COMMAND_RETRY_INTERVAL = 0.25  # resend a device command this often until it's acknowledged

SET = 1  # the only device command so far: set the device's setting to the value


class PacketType(IntEnum):
    # Client -> server
    CONNECT_REQUEST = 0x01
    KEEP_ALIVE = 0x02
    DISCONNECT = 0x03
    DEVICE_COMMAND = 0x04
    # Server -> client
    CONNECT_ACCEPTED = 0x81
    CONNECT_DENIED = 0x82
    SHIP_SNAPSHOT = 0x83
    PLAYER_SNAPSHOT = 0x84
    DISCONNECTED = 0x85
    COMMAND_ACK = 0x86
    SHIP_LOG = 0x87


class ClientRole(IntEnum):
    BOT = 1     # flies a ship; authenticates with the ship's bot token
    PLAYER = 2  # a human player; authenticates with the player's own token


class DenyReason(IntEnum):
    UNSUPPORTED_VERSION = 1
    INVALID_TOKEN = 2


class DisconnectReason(IntEnum):
    SERVER_SHUTDOWN = 1
    SUPERSEDED = 2
    TIMED_OUT = 3


class DeviceKind(IntEnum):
    ENGINE = 1     # rating: newtons at full throttle; setting 0..1; pushes along the heading
    GYROSCOPE = 2  # rating: radians/second at full; setting -1..1; positive turns counter-clockwise


class CommandResult(IntEnum):
    OK = 1
    STALE = 2           # this command, or a newer one for the same device, was already applied
    UNKNOWN_DEVICE = 3
    UNKNOWN_COMMAND = 4
    INVALID_VALUE = 5   # NaN or infinite
    OVER_BUDGET = 6     # the ship's command budget for this tick is used up; resend it


@dataclass(frozen=True)
class Ship:
    id: int
    owner_player_id: int
    x: float          # metres
    y: float
    vx: float         # metres per second
    vy: float
    heading: float    # radians: 0 is +x, counter-clockwise, in [-pi, pi)


@dataclass(frozen=True)
class Device:
    kind: DeviceKind
    rating: float     # what the device does at a setting of 1
    setting: float    # where it's set now


@dataclass(frozen=True)
class ConnectAccepted:
    nonce: int
    session: int
    subject_id: int      # ship id for a bot, player id for a player
    subject_name: str    # ship name for a bot, player name for a player
    tick_seconds: float


@dataclass(frozen=True)
class ConnectDenied:
    nonce: int
    reason: DenyReason


@dataclass(frozen=True)
class ShipSnapshot:
    session: int
    tick: int
    ship: Ship
    mass: float                  # kilograms
    devices: tuple[Device, ...]  # a device command addresses a device by its index here


@dataclass(frozen=True)
class PlayerSnapshot:
    session: int
    tick: int
    ships: tuple[Ship, ...]


@dataclass(frozen=True)
class Disconnected:
    session: int
    reason: DisconnectReason


@dataclass(frozen=True)
class CommandAck:
    session: int
    sequence: int
    result: CommandResult


@dataclass(frozen=True)
class ShipLogEntry:
    ship_id: int
    id: int              # counts up per ship
    tick: int            # the tick the command was for
    device: int
    kind: DeviceKind
    command: int
    value: float         # as sent, before clamping
    result: CommandResult


@dataclass(frozen=True)
class ShipLog:
    """Players only: their ships' most recent device commands. Entries repeat; dedupe by id."""
    session: int
    tick: int
    entries: tuple[ShipLogEntry, ...]


ServerPacket = ConnectAccepted | ConnectDenied | ShipSnapshot | PlayerSnapshot | Disconnected | CommandAck | ShipLog

_SHIP = struct.Struct("<iiddddd")  # id, owner_player_id, x, y, vx, vy, heading
_DEVICE = struct.Struct("<Bdd")    # kind, rating, setting
_LOG_ENTRY = struct.Struct("<iIqBBBdB")  # ship_id, id, tick, device, kind, command, value, result


# ---------------------------------------------------------------------------
# Encoding (client -> server)
# ---------------------------------------------------------------------------

def encode_connect_request(role: ClientRole, nonce: int, token: str) -> bytes:
    return struct.pack("<BIHBQ", PacketType.CONNECT_REQUEST, MAGIC, VERSION, role, nonce) + _string(token)


def encode_keep_alive(session: int) -> bytes:
    return struct.pack("<BQ", PacketType.KEEP_ALIVE, session)


def encode_disconnect(session: int) -> bytes:
    return struct.pack("<BQ", PacketType.DISCONNECT, session)


def encode_device_command(session: int, sequence: int, device: int, command: int, value: float) -> bytes:
    return struct.pack("<BQIBBd", PacketType.DEVICE_COMMAND, session, sequence, device, command, value)


def _string(value: str) -> bytes:
    data = value.encode("utf-8")
    if len(data) > MAX_STRING_BYTES:
        raise ValueError(f"string is {len(data)} bytes; the limit is {MAX_STRING_BYTES}")
    return bytes([len(data)]) + data


# ---------------------------------------------------------------------------
# Decoding (server -> client)
# ---------------------------------------------------------------------------

class _Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.pos = 0

    def take(self, fmt: str | struct.Struct):
        s = fmt if isinstance(fmt, struct.Struct) else struct.Struct("<" + fmt)
        if self.pos + s.size > len(self.data):
            raise ValueError("truncated packet")
        values = s.unpack_from(self.data, self.pos)
        self.pos += s.size
        return values

    def string(self) -> str:
        (length,) = self.take("B")
        if self.pos + length > len(self.data):
            raise ValueError("truncated string")
        value = self.data[self.pos:self.pos + length].decode("utf-8")
        self.pos += length
        return value

    def ship(self) -> Ship:
        return Ship(*self.take(_SHIP))

    def device(self) -> Device:
        kind, rating, setting = self.take(_DEVICE)
        return Device(DeviceKind(kind), rating, setting)

    def log_entry(self) -> ShipLogEntry:
        ship_id, entry_id, tick, device, kind, command, value, result = self.take(_LOG_ENTRY)
        return ShipLogEntry(ship_id, entry_id, tick, device, DeviceKind(kind), command, value, CommandResult(result))


def decode(data: bytes) -> ServerPacket | None:
    """Decodes a server packet. Returns None for anything malformed or unknown; just drop those."""
    if not data or len(data) > MAX_PACKET_SIZE:
        return None
    r = _Reader(data)
    try:
        (kind,) = r.take("B")
        if kind == PacketType.CONNECT_ACCEPTED:
            nonce, session, subject_id = r.take("QQi")
            name = r.string()
            (tick_seconds,) = r.take("f")
            packet = ConnectAccepted(nonce, session, subject_id, name, tick_seconds)
        elif kind == PacketType.CONNECT_DENIED:
            nonce, reason = r.take("QB")
            packet = ConnectDenied(nonce, DenyReason(reason))
        elif kind == PacketType.SHIP_SNAPSHOT:
            session, tick = r.take("Qq")
            ship = r.ship()
            mass, count = r.take("dB")
            packet = ShipSnapshot(session, tick, ship, mass, tuple(r.device() for _ in range(count)))
        elif kind == PacketType.PLAYER_SNAPSHOT:
            session, tick, count = r.take("QqH")
            packet = PlayerSnapshot(session, tick, tuple(r.ship() for _ in range(count)))
        elif kind == PacketType.DISCONNECTED:
            session, reason = r.take("QB")
            packet = Disconnected(session, DisconnectReason(reason))
        elif kind == PacketType.COMMAND_ACK:
            session, sequence, result = r.take("QIB")
            packet = CommandAck(session, sequence, CommandResult(result))
        elif kind == PacketType.SHIP_LOG:
            session, tick, count = r.take("QqB")
            packet = ShipLog(session, tick, tuple(r.log_entry() for _ in range(count)))
        else:
            return None
    except ValueError:  # truncated, bad UTF-8, or an unknown enum value
        return None
    return packet if r.pos == len(data) else None
