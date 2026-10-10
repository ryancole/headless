# Bot protocol v1

Bots connect over a WebSocket, receive one snapshot per tick, and send back intents. All messages are
UTF-8 JSON text frames with `snake_case` keys and a `"type"` field (which may appear anywhere in the object).
The server is authoritative: intents are requests, and the snapshot tells you what actually happened.

C# wire types live in `src/Headless.Protocol/V1/Messages.cs`.

## Connecting

```
GET ws://<host>/v1/bot
Authorization: Bearer <ship token>
```

- An invalid or missing token gets **HTTP 401** before the upgrade.
- One controller per ship. A new connection with the same token replaces the old one, and the old one is
  closed with the reason `superseded by a new connection`.
- On disconnect the ship **holds course**: thrust is cut and it coasts. Reconnect any time.
- Messages over 64 KiB close the connection.

## Timing

```
tick N runs ──► snapshot(N) ──► bot decides ──► intents ──► applied at the start of tick N+1
```

Intents received at any point between two ticks are queued and applied in arrival order at the start of the
next tick. Several `intents` messages in one tick are fine, but **every intent counts toward the per-tick
budget** (`rules.max_intents_per_tick`), whether or not it is valid. A bot that misses a tick loses nothing;
the throttle persists.

## Server → bot

### `hello`

Sent once, right after connecting.

```json
{
  "type": "hello",
  "protocol": "v1",
  "ship_id": 10,
  "rules": {
    "tick_seconds": 0.5,
    "max_intents_per_tick": 4,
    "max_acceleration": 10,
    "max_fuel": 1000,
    "fuel_per_thrust_second": 1,
    "cargo_capacity": 100,
    "mining_rate_per_second": 2,
    "mining_range": 500,
    "docking_range": 500,
    "max_interaction_speed": 5,
    "sensor_range": 50000
  }
}
```

Read these values instead of hard-coding them. They will vary between servers and over time.

### `snapshot`

Sent after every tick. Contains only what the ship's sensors can see.

```json
{
  "type": "snapshot",
  "tick": 345,
  "ship": {
    "id": 10, "name": "Prospector",
    "position": { "x": 0, "y": 0 }, "velocity": { "x": 0, "y": 0 }, "throttle": { "x": 0, "y": 0 },
    "fuel": 1000, "cargo": 0, "credits": 1014,
    "docked_at_station_id": 1, "mining_asteroid_id": null
  },
  "stations":  [ { "id": 1, "name": "Meridian Exchange", "position": { "x": 0, "y": 0 }, "ore_price": 10, "fuel_price": 1 } ],
  "asteroids": [ { "id": 7, "position": { "x": 5210.4, "y": -2214.9 }, "radius": 132.5, "ore": 412 } ],
  "contacts":  [ { "id": 11, "name": "Wildcat", "position": { "x": 900, "y": 120 }, "velocity": { "x": -3, "y": 0 } } ],
  "events":    [ { "code": "ore_sold", "message": "Sold 100 ore for 1000 credits." } ],
  "errors":    [ { "index": 2, "intent": "mine", "code": "out_of_range", "message": "Must be within 500 m of the asteroid." } ]
}
```

- Units: metres, m/s, sim seconds. Space is a 2D plane.
- `stations` are always listed. `asteroids` and `contacts` (other ships) only appear within `sensor_range`.
- `events` and `errors` describe the tick that just ran. `errors[].index` is the intent's position among
  everything the bot sent for that tick.

Event codes: `docked`, `undocked`, `mining_started`, `mining_stopped`, `ore_sold`, `refueled`, `out_of_fuel`.

### `error`

A message the server couldn't process at all, such as malformed JSON, an unknown `type`, or a missing field.
Nothing in that message was applied.

```json
{ "type": "error", "code": "malformed_message", "message": "..." }
```

Codes: `malformed_message`, `unsupported_message`.

## Bot → server

### `intents`

```json
{ "type": "intents", "intents": [ { "type": "thrust", "x": 0.6, "y": -0.8 } ] }
```

| Intent | Fields | Effect | Errors |
|---|---|---|---|
| `thrust` | `x`, `y` | Set the throttle vector. Length is clamped to 1. Persists until changed. Burns fuel in proportion to its length. | `invalid_value`, `docked` |
| `mine` | `asteroid_id` | Start mining. Stops by itself when the hold is full, the rock is empty, or the ship drifts out of range. | `unknown_target`, `docked`, `depleted`, `cargo_full`, `out_of_range`, `too_fast` |
| `stop_mining` | none | Stop mining. | none |
| `dock` | `station_id` | Dock: the ship snaps to the station, stops, and its throttle is zeroed. | `unknown_target`, `docked`, `out_of_range`, `too_fast` |
| `undock` | none | Leave the station at zero velocity. | `not_docked` |
| `sell` | none | Sell all whole ore units at the docked station. | `not_docked` |
| `refuel` | none | Buy as much fuel as fits in the tank and your credits cover. | `not_docked`, `insufficient_credits` |

Intents apply in order, so `[sell, refuel, undock]` in one message does all three in one tick.
Any intent past the budget is rejected with `budget_exceeded`.

## Versioning

The path carries the version (`/v1/bot`). Breaking changes go in `/v2/...`, and v1 stays up for a
deprecation window. Adding new fields, intents, or event and error codes is **not** breaking, so bots
should ignore anything they don't recognise.
