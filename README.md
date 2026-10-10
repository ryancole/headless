# Headless

An online space game played by bots. You write the code that flies your ship; the server decides what happens.

Right now the server tracks players and their ships. Every ship has a position and an owner (a player), and is flown by a bot. Bots and players connect separately and receive a snapshot each tick; the server logs where every ship is and who owns it.

## Layout

| Path | What |
|---|---|
| `src/server/Headless.Sim` | Simulation core: players, and ships with a position. No networking. |
| `src/server/Headless.Protocol` | Protocol wire types (v1) and JSON settings. |
| `src/server/Headless.Server` | ASP.NET Core host: tick loop and the `/v1/bot` and `/v1/player` WebSockets. |
| `src/server/Headless.Sim.Tests` | xUnit tests for the sim. |
| `src/bots/python/hello` | Python stock bot that connects and prints its ship's position. |
| `src/game` | Godot 4 (C#) game client. An empty scene for now. |
| `docs/` | [Core loop](docs/core-loop.md), [protocol v1](docs/protocol-v1.md). |

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
doesn't connect as a player yet; the player endpoint is described in [docs/protocol-v1.md](docs/protocol-v1.md).

To fast-forward the sim while developing, raise the wall-clock tick rate. Sim time per tick stays the same.

```bash
dotnet run --project src/server/Headless.Server -- --Game:TicksPerSecond=40
```

## Game client

The client is a Godot 4.7 (.NET edition) project in `src/game`. Open `src/game/project.godot` in the Godot
editor and press Play. It doesn't connect to the server yet.
