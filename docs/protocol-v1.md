# Bot protocol v1

Bots connect over a WebSocket and receive one snapshot per tick. All messages are UTF-8 JSON text frames
with `snake_case` keys and a `"type"` field.

For now the game is only ships with a position. Nothing moves them, and bots can't send any commands yet.

C# wire types live in `src/Headless.Protocol/V1/Messages.cs`.

## Connecting

```
GET ws://<host>/v1/bot
Authorization: Bearer <ship token>
```

- An invalid or missing token gets **HTTP 401** before the upgrade.
- One connection per ship. A new connection with the same token replaces the old one, and the old one is
  closed with the reason `superseded by a new connection`.
- Messages over 64 KiB close the connection.

## Server → bot

### `hello`

Sent once, right after connecting.

```json
{ "type": "hello", "protocol": "v1", "ship_id": 1, "rules": { "tick_seconds": 0.5 } }
```

### `snapshot`

Sent after every tick. Positions are in metres on a 2D plane.

```json
{ "type": "snapshot", "tick": 345, "ship": { "id": 1, "name": "Prospector", "position": { "x": 0, "y": 0 } } }
```

### `error`

```json
{ "type": "error", "code": "unsupported_message", "message": "The server doesn't accept messages from bots yet." }
```

Sent in reply to any message a bot sends, since bots have nothing to send yet.

## Bot → server

Nothing yet.

## Versioning

The path carries the version (`/v1/bot`). Breaking changes go in `/v2/...`, and v1 stays up for a
deprecation window. Adding new fields or message types is **not** breaking, so bots should ignore
anything they don't recognise.
