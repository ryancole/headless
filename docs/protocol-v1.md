# Protocol v1

Clients connect over a WebSocket and receive one snapshot per tick. All messages are UTF-8 JSON text frames
with `snake_case` keys and a `"type"` field.

There are two kinds of client:

- **Bots** fly ships. A ship is autonomous: its owner's bot controls it.
- **Players** are the humans. A player doesn't fly ships; they ride in them.

Every ship has exactly one owner, a player (`owner_player_id`). A player's ships are the ships that name
them as owner. Owning a ship and being aboard one are separate: players will be able to board ships they
don't own. Which ship a player is aboard isn't part of the protocol yet.

For now the game is only players and ships with a position. Nothing moves ships, each player starts with
one ship, and clients can't send any commands yet.

C# wire types live in `src/Headless.Protocol/V1/Messages.cs`.

## Connecting

| Endpoint | Who | Token |
|---|---|---|
| `GET ws://<host>/v1/bot` | a bot | the ship's **bot token** |
| `GET ws://<host>/v1/player` | a player | the player's own **player token** |

Both authenticate with `Authorization: Bearer <token>`. The two kinds of token are separate on purpose: a
player can hand their ship's bot token to a bot without giving it their own.

- An invalid or missing token, or a token for the other endpoint, gets **HTTP 401** before the upgrade.
- One connection per ship (bots) and per player. A new connection with the same token replaces the old one,
  and the old one is closed with the reason `superseded by a new connection`.
- Messages over 64 KiB close the connection.
- When the server shuts down, it closes every connection with code **1001** and the reason
  `server shutting down`. Clients should wait a few seconds and reconnect.

Players and ships share one id space, so a player id and a ship id never collide.

## Shared types

A ship, as it appears in both bot and player messages. Positions are in metres on a 2D plane.

```json
{ "id": 2, "name": "Prospector", "owner_player_id": 1, "position": { "x": 0, "y": 0 } }
```

## Bot messages (server → bot)

### `hello`

Sent once, right after connecting.

```json
{ "type": "hello", "protocol": "v1", "ship_id": 2, "rules": { "tick_seconds": 0.5 } }
```

### `snapshot`

Sent after every tick.

```json
{ "type": "snapshot", "tick": 345, "ship": { "id": 2, "name": "Prospector", "owner_player_id": 1, "position": { "x": 0, "y": 0 } } }
```

## Player messages (server → player)

### `player_hello`

Sent once, right after connecting.

```json
{ "type": "player_hello", "protocol": "v1", "player_id": 1, "rules": { "tick_seconds": 0.5 } }
```

### `player_snapshot`

Sent after every tick: the player, and every ship they own (an empty list if they own none).

```json
{
  "type": "player_snapshot",
  "tick": 345,
  "player": { "id": 1, "name": "Player One" },
  "ships": [ { "id": 2, "name": "Prospector", "owner_player_id": 1, "position": { "x": 0, "y": 0 } } ]
}
```

## Errors (server → any client)

```json
{ "type": "error", "code": "unsupported_message", "message": "The server doesn't accept messages from clients yet." }
```

Sent in reply to any message a client sends, since clients have nothing to send yet.

## Client → server

Nothing yet.

## Versioning

The path carries the version (`/v1/...`). Breaking changes go in `/v2/...`, and v1 stays up for a
deprecation window. Adding new fields or message types is **not** breaking, so clients should ignore
anything they don't recognise.
