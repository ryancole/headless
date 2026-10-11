# Protocol

Headless speaks one small binary protocol over **UDP**, used by every client: bots and the game client
alike. There's no HTTP, WebSocket or JSON anywhere.

There are two kinds of client:

- **Bots** fly ships. A ship is autonomous: a bot controls it.
- **Players** are the humans. A player doesn't fly ships; they ride in them.

Every ship has exactly one owner, a player (`owner_player_id`). A player's ships are the ships that name
them as owner. Owning a ship and being aboard one are separate: players will be able to board ships they
don't own. Which ship a player is aboard isn't part of the protocol yet.

For now the game is only players and ships. Each player starts with one ship, and a bot flies it by
setting the ship's **devices** (see [Devices](#devices)). Players don't send game commands yet.

Implementations:

- C#: `src/server/Headless.Protocol` (`Packets.cs`, `PacketCodec.cs`, `ProtocolConstants.cs`). Shared by the
  server, the Godot client and C# bots.
- Python: `src/bots/python/hello/headless_protocol.py` (standard library only). A reference for porting.

## Basics

- One packet per UDP datagram, at most **1200 bytes** (keeps packets under typical internet MTUs).
- Every packet starts with a 1-byte **type**. Types `0x01`–`0x7F` go client → server; `0x80`–`0xFF` go
  server → client.
- All numbers are **little-endian**. `u8/u16/u32/u64` are unsigned integers, `i32/i64` signed, `f32/f64`
  IEEE 754 floats.
- A **string** is a `u8` byte length followed by that many bytes of UTF-8 (so at most 255 bytes).
- Receivers silently drop anything malformed: unknown type, truncated, trailing bytes, invalid UTF-8 or
  enum values. UDP has no error channel, and replying to junk would invite abuse.
- The server listens on port **5080** by default, IPv4 and IPv6.

## Connection lifecycle

```
client                                   server
  │── ConnectRequest (nonce, token) ──────▶│   resend every 0.5 s until answered
  │◀──────── ConnectAccepted (session) ────│   or ConnectDenied (reason)
  │                                        │
  │◀──────── ShipSnapshot / PlayerSnapshot │   every tick (2/s by default), unreliable
  │── KeepAlive (session) ────────────────▶│   every 1 s
  │── DeviceCommand (session, seq, ...) ──▶│   bots only; resend every 0.25 s until acked
  │◀──────── CommandAck (session, seq, ok) │
  │◀──────── ShipLog (session, entries)    │   players only, when their ships logged commands
  │                                        │
  │── Disconnect (session) ───────────────▶│   client leaves, or
  │◀──────── Disconnected (session, reason)│   server ends it
```

1. **Connect.** The client picks a random 64-bit **nonce** and sends `ConnectRequest`, resending it every
   0.5 s until it gets an answer whose nonce matches. A resent request after the server has accepted gets
   the same answer again.
2. **Accepted or denied.** `ConnectAccepted` carries a random 64-bit **session id**. Every later packet in
   either direction carries it, and the server ignores packets whose session id or source address don't
   match. That's what stops other hosts injecting packets into a session.
3. **Snapshots.** The server sends one snapshot per tick. They're unreliable: one may be lost or arrive out
   of order. Each carries the tick number; ignore any older than the newest you've seen.
4. **Staying alive.** Clients send `KeepAlive` every second. Either side gives up after **5 seconds** of
   silence: the server sends `Disconnected(timed_out)`, and clients should treat a silent server as gone.
5. **Commands.** Bots fly their ship with `DeviceCommand`s; see [Devices](#devices).
6. **Ending.** A client sends `Disconnect` when it leaves. The server sends `Disconnected` with a reason:
   - `server_shutdown`: sent (twice) when the server stops. Wait a few seconds and reconnect.
   - `superseded`: another connection logged in with the same token. Only one bot per ship and one
     connection per player is allowed; the newest wins.
   - `timed_out`: the server heard nothing for 5 seconds.

The two kinds of token are separate on purpose: a player can hand their ship's bot token to a bot without
giving it their own. A bot token used with the player role, or vice versa, is denied.

## Devices

A ship is fitted with **devices**, the ship's hardware, a bit like the hardware on 0x10c's DCPU-16. Every
ship snapshot lists them, and a bot addresses one by its index in that list. Ships can be fitted
differently, so a bot should look for the devices it needs rather than assume indexes. Today every ship has
an engine (index 0) and a gyroscope (index 1).

Every device has one **setting**, which stays where the bot put it until the bot changes it, and a
**rating**: what the device does at a setting of 1.

| Kind | Code | Setting | Rating | What it does |
|---|---|---|---|---|
| engine | `1` | 0 to 1 | newtons at full throttle | pushes the ship forward, along its heading |
| gyroscope | `2` | -1 to 1 | radians per second at full | turns the ship; positive is counter-clockwise |

The world is a 2D plane. Each tick (`tick_seconds` of simulated time, 0.5 s by default) the server turns
every ship by its gyroscope, then accelerates it along its new heading by engine thrust ÷ mass, then moves
it by its velocity. There's no drag: a ship keeps moving until it thrusts the other way, and since engines
only push forward, stopping means turning round and burning. Headings are radians: 0 points along +x,
counter-clockwise, in [-π, π).

### Commands

A `DeviceCommand` names a device, a **command** code and a value. There's one command so far:

| Command | Code | Value |
|---|---|---|
| set | `1` | the new setting; values outside the device's range are clamped |

Commands take effect from the next tick. Each ship accepts at most **4 commands per tick**: competition is
about good decisions, not many.

UDP can drop a command, so commands are made reliable like this:

- Each command carries a **sequence number**, counting up from 1 within a session.
- The server answers every copy it receives with a `CommandAck` carrying the same sequence number.
- The bot resends a command every **0.25 s** until the ack arrives.
- Commands only set values, so the newest per device is all that counts. The server ignores a command if
  it has already applied that one, or a newer one for the same device, and answers `stale`. A bot with a
  new value for a device just sends a new command and stops resending the old one.

`CommandAck` results:

| Result | Code | Meaning |
|---|---|---|
| `ok` | `1` | applied |
| `stale` | `2` | already applied, or a newer command for this device was; nothing changed |
| `unknown_device` | `3` | no device at that index |
| `unknown_command` | `4` | that device doesn't know that command |
| `invalid_value` | `5` | NaN or infinite |
| `over_budget` | `6` | this tick's commands are used up; keep resending and it'll fit next tick |

Only bots send commands. The server ignores them from players.

## Packets

### Client → server

**`0x01` ConnectRequest**

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x01` |
| magic | u32 | `0x534C4448` (the bytes `H D L S`) |
| version | u16 | protocol version, currently `2` |
| role | u8 | `1` bot, `2` player |
| nonce | u64 | random, chosen by the client |
| token | string | the ship's bot token (bots) or the player's token (players) |

For example, a player connecting with nonce `0x0807060504030201` and token `ab`:

```
01  48 44 4C 53  02 00  02  01 02 03 04 05 06 07 08  02 61 62
```

**`0x02` KeepAlive**: `type u8`, `session u64`.

**`0x03` Disconnect**: `type u8`, `session u64`.

**`0x04` DeviceCommand** (bots only, 23 bytes)

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x04` |
| session | u64 | |
| sequence | u32 | counts up from 1 within a session |
| device | u8 | index into the ship snapshot's device list |
| command | u8 | `1` set |
| value | f64 | |

For example, session `0x0807060504030201`, sequence `0x0D0C0B0A`, set device 1 to 0.5:

```
04  01 02 03 04 05 06 07 08  0A 0B 0C 0D  01  01  00 00 00 00 00 00 E0 3F
```

### Server → client

**`0x81` ConnectAccepted**

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x81` |
| nonce | u64 | echoed from the request |
| session | u64 | random, never 0 |
| subject_id | i32 | the ship id (bots) or player id (players) |
| subject_name | string | the ship's name (bots) or player's name (players) |
| tick_seconds | f32 | simulated seconds per tick |

**`0x82` ConnectDenied**: `type u8`, `nonce u64`, `reason u8`: `1` unsupported version, `2` invalid token.

**`0x83` ShipSnapshot** (to bots): `type u8`, `session u64`, `tick i64`, then one **ship**, `mass f64`
(kilograms), `count u8`, then `count` **device**s. 108 bytes with today's two devices.

**`0x84` PlayerSnapshot** (to players): `type u8`, `session u64`, `tick i64`, `count u16`, then `count`
**ship**s: every ship the player owns. At most 24 ships fit in one packet.

**`0x85` Disconnected**: `type u8`, `session u64`, `reason u8`: `1` server shutdown, `2` superseded, `3` timed out.

**`0x86` CommandAck**: `type u8`, `session u64`, `sequence u32` (echoed from the command), `result u8` (see
[Commands](#commands)).

**`0x87` ShipLog** (to players): `type u8`, `session u64`, `tick i64`, `count u8`, then `count` **log entries**,
oldest first. Each ship remembers the last 10 commands its devices received, refused or not (it's what the
cockpit terminal shows). After each tick that a player's ships have anything logged, the server sends the
latest entries, up to 40. The same entries repeat from tick to tick, so a lost packet costs nothing: show only
entries with an id newer than the last one shown. Duplicate commands that the server answered `stale` never
reach the ship, so they aren't logged.

A **log entry** is 28 bytes:

| Field | Type | Notes |
|---|---|---|
| ship_id | i32 | |
| id | u32 | counts up by one per ship, so a gap means entries were missed |
| tick | i64 | the tick the command was for; it takes effect then, if accepted |
| device | u8 | the device index |
| kind | u8 | the device's kind |
| command | u8 | as sent |
| value | f64 | as sent, before clamping |
| result | u8 | as in `CommandAck` (never `stale`) |

A **ship** is 48 bytes:

| Field | Type | Notes |
|---|---|---|
| id | i32 | |
| owner_player_id | i32 | |
| x | f64 | metres |
| y | f64 | metres, on a 2D plane |
| vx | f64 | metres per second |
| vy | f64 | metres per second |
| heading | f64 | radians: 0 is +x, counter-clockwise, in [-π, π) |

A **device** is 17 bytes:

| Field | Type | Notes |
|---|---|---|
| kind | u8 | `1` engine, `2` gyroscope |
| rating | f64 | what it does at a setting of 1 (see [Devices](#devices)) |
| setting | f64 | where it's set now |

Players and ships share one id space, so a player id and a ship id never collide.

## Versioning

The `version` field in `ConnectRequest` is bumped on any breaking change, and the server denies versions it
doesn't speak (`unsupported_version`). New packet types are not breaking: receivers already drop types they
don't know.

## Not yet

- **Encryption.** Tokens travel in plain text. Fine on a local network for development, but not for a public
  server.
