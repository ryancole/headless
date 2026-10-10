# Protocol

Headless speaks one small binary protocol over **UDP**, used by every client: bots and the game client
alike. There's no HTTP, WebSocket or JSON anywhere.

There are two kinds of client:

- **Bots** fly ships. A ship is autonomous: a bot controls it.
- **Players** are the humans. A player doesn't fly ships; they ride in them.

Every ship has exactly one owner, a player (`owner_player_id`). A player's ships are the ships that name
them as owner. Owning a ship and being aboard one are separate: players will be able to board ships they
don't own. Which ship a player is aboard isn't part of the protocol yet.

For now the game is only players and ships with a position. Nothing moves ships, each player starts with
one ship, and clients don't send any game commands yet.

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
5. **Ending.** A client sends `Disconnect` when it leaves. The server sends `Disconnected` with a reason:
   - `server_shutdown`: sent (twice) when the server stops. Wait a few seconds and reconnect.
   - `superseded`: another connection logged in with the same token. Only one bot per ship and one
     connection per player is allowed; the newest wins.
   - `timed_out`: the server heard nothing for 5 seconds.

The two kinds of token are separate on purpose: a player can hand their ship's bot token to a bot without
giving it their own. A bot token used with the player role, or vice versa, is denied.

## Packets

### Client → server

**`0x01` ConnectRequest**

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x01` |
| magic | u32 | `0x534C4448` (the bytes `H D L S`) |
| version | u16 | protocol version, currently `1` |
| role | u8 | `1` bot, `2` player |
| nonce | u64 | random, chosen by the client |
| token | string | the ship's bot token (bots) or the player's token (players) |

For example, a player connecting with nonce `0x0807060504030201` and token `ab`:

```
01  48 44 4C 53  01 00  02  01 02 03 04 05 06 07 08  02 61 62
```

**`0x02` KeepAlive**: `type u8`, `session u64`.

**`0x03` Disconnect**: `type u8`, `session u64`.

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

**`0x83` ShipSnapshot** (to bots, 41 bytes): `type u8`, `session u64`, `tick i64`, then one **ship**.

**`0x84` PlayerSnapshot** (to players): `type u8`, `session u64`, `tick i64`, `count u16`, then `count`
**ship**s: every ship the player owns. At most 49 ships fit in one packet.

**`0x85` Disconnected**: `type u8`, `session u64`, `reason u8`: `1` server shutdown, `2` superseded, `3` timed out.

A **ship** is 24 bytes:

| Field | Type | Notes |
|---|---|---|
| id | i32 | |
| owner_player_id | i32 | |
| x | f64 | metres |
| y | f64 | metres, on a 2D plane |

Players and ships share one id space, so a player id and a ship id never collide.

## Versioning

The `version` field in `ConnectRequest` is bumped on any breaking change, and the server denies versions it
doesn't speak (`unsupported_version`). New packet types are not breaking: receivers already drop types they
don't know.

## Not yet

- **Reliable delivery for client commands.** Clients don't send game commands yet. When they do (intents),
  they'll need acknowledgements or redundancy, since UDP can drop them.
- **Encryption.** Tokens travel in plain text. Fine on a local network for development, but not for a public
  server.
