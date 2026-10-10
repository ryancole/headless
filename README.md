# Headless

An online space game played by bots. You write the code that flies your ship; the server decides what happens.

Right now the server tracks players and their ships. Every ship has a position and an owner (a player), and is flown by a bot. Bots and players connect over a small binary UDP protocol and receive a snapshot each tick; the server logs where every ship is and who owns it.

## Layout

| Path | What |
|---|---|
| `src/server/Headless.Sim` | Simulation core: players, and ships with a position. No networking. |
| `src/server/Headless.Protocol` | The binary UDP protocol: packet types and encoder/decoder. |
| `src/server/Headless.Server` | The game server: tick loop and UDP socket (port 5080). |
| `src/server/Headless.*.Tests` | xUnit tests for the sim, protocol and server. |
| `src/bots/python/hello` | Python stock bot that connects and prints its ship's position. |
| `src/game` | Godot 4 (C#) game client. An empty scene for now. |
| `docs/` | [Core loop](docs/core-loop.md), [protocol](docs/protocol.md), [art style](docs/art-style.md). |

## Quick start

```bash
dotnet test src/server
```

```bash
dotnet run --project src/server/Headless.Server
```

The server listens on `http://localhost:5080`. In Development it spawns two players, each owning one
ship (set in `appsettings.Development.json`):

| Player | Player token | Ship | Bot token |
|---|---|---|---|
| Player One | `dev-player-1` | Prospector | `dev-token-1` |
| Player Two | `dev-player-2` | Wildcat | `dev-token-2` |

Then start a bot by following [src/bots/python/hello](src/bots/python/hello/README.md). The game client
doesn't connect as a player yet; the protocol is described in [docs/protocol.md](docs/protocol.md).

To fast-forward the sim while developing, raise the wall-clock tick rate. Sim time per tick stays the same.

```bash
dotnet run --project src/server/Headless.Server -- --Game:TicksPerSecond=40
```

## Game client

The client is a Godot 4.7 (.NET edition) project in `src/game`. Open `src/game/project.godot` in the Godot
editor and press Play. It doesn't connect to the server yet.
