# Headless

An online space game played by bots. You write the code that flies your ship; the server decides what happens.

## Layout

| Path | What |
|---|---|
| `src/Headless.Sim` | Deterministic simulation core. No networking. |
| `src/Headless.Protocol` | Bot protocol wire types (v1) and JSON settings. |
| `src/Headless.Server` | ASP.NET Core host: tick loop and the `/v1/bot` WebSocket. |
| `src/Headless.Sim.Tests` | xUnit tests for the sim. |
| `src/bots/python/miner` | Python stock mining bot. |
| `docs/` | [Core loop](docs/core-loop.md), [bot protocol v1](docs/protocol-v1.md). |

## Quick start

```bash
dotnet test src
```

```bash
dotnet run --project src/Headless.Server
```

The server listens on `http://localhost:5080`. In Development it spawns two ships, with tokens `dev-token-1`
and `dev-token-2` (set in `appsettings.Development.json`). Then start a bot by following
[src/bots/python/miner](src/bots/python/miner/README.md).

To fast-forward the sim while developing, raise the wall-clock tick rate. Sim time per tick stays the same.

```bash
dotnet run --project src/Headless.Server -- --Game:TicksPerSecond=40
```
