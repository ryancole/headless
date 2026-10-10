# Headless — Project Context

Code name: **Headless** (headless server + ships flown with no one at the controls).

An online game designed to be played by bots. Players write their own bots (or pick open-source stock bots) that control their spaceships through a well-documented server API. Inspired by Notch's cancelled 0x10c: the ships are bot-controlled, while human players can walk around inside their ships in a 3D world.

Related games for reference: SpaceTraders (API-only, no first-party client), Screeps (code-controlled MMO), Space Engineers (in-game C# programmable blocks).

## Core concept

- The **ship** is each player's central unit. It is flown by an **external bot** the player runs locally (their own code, or a stock bot from a library).
- **Human players** can be inside ships in 3D (later phase) — repairing, overriding controls, boarding, etc. Humans and bots should need each other.
- The **server is 100% authoritative** (League of Legends model). Clients and bots send *intents*; the server decides outcomes.

## Key design decisions

- **Server owns all important state**: ship position/velocity, internal systems (power, fuel, damage, cargo), avatar positions.
- **Sensor-filtered state**: each bot only receives what its ship's sensors can detect, computed server-side (fog of war). Never send hidden data.
- **Per-tick action budget** per ship, not just request rate limits — competition should be about decision quality, not throughput.
- **Slow ship tick (~1–4 Hz)** with Newtonian travel taking minutes, so bot/LLM latency doesn't matter. Avatars (later) run at 30–60 Hz only near players, with client-side prediction and server correction.
- **Control arbitration**: decide what happens when a human takes the helm while the bot is steering (e.g. physical console override wins, bot is notified).
- **Offline fallback**: when a bot disconnects, the ship falls back to a defined behavior (hold course / station-keep / dock).
- **Bot auth**: per-ship tokens scoped to game actions only.
- **API versioned from day one** (v1, v2…), old versions kept alive for a while.
- **Ship interiors** (later): simulate avatars in ship-local coordinates, not world space.

## Open source split

- **Open (MIT or Apache 2.0)**: stock bots, client SDKs, local test server.
- **Closed**: the authoritative live server.
- Stock bots: readable, commented, competent but beatable — they double as API documentation.
- Possibly host stock bots server-side for instant onboarding; downloadable bot runner for local bots (sandbox community bots).

## Tech stack

- **Simulation core**: plain C# class library, deterministic fixed-rate `Tick()` consuming intents, unit tested. No networking. Reused as the local test server.
- **Server**: ASP.NET Core, hosted service running the tick loop. **Raw WebSockets + JSON** (not SignalR — keep it language-agnostic). OpenAPI spec for any HTTP endpoints.
- **Physics** (when needed): BepuPhysics2.
- **Stock bots**: Python and C# first.
- **No spectator/viewer for now** (decided 2026-10-10): focus on the game itself. Debug via bot logs and sim tests. Revisit later; no Node/web toolchain until then.
- **3D client (later)**: Godot 4 with C#, sharing protocol types / sim library with the server.
- No custom engine.

## Build order

1. **Decide the core loop** (mining / trading / hauling — open decision).
2. **Sim core** library + tests.
3. **Bot protocol spec** (intents, per-tick sensor snapshot, errors), then the WebSocket endpoint.
4. **First stock bot** — a simple miner in Python and C#. Fix protocol clunkiness now.
5. Then: accounts, persistence, multiple players, bot library, bot runner. 3D client last.

**First milestone**: one ship, flown by one bot, flies to an asteroid, mines, sells at a station. No 3D, no accounts, no persistence.

## Status

- **Current focus (2026-10-10): nail down the server first.** A ship is **only** an id, a name and a position. No velocity, no thrust, no intents; bots can't send anything yet. The server logs every ship's position each second. Mining, stations, fuel, credits, sensors and movement were deliberately removed to keep it simple; add features back one at a time, only when asked.
- Core loop (mine → haul → sell → refuel) is chosen but deferred; see `docs/core-loop.md`.
- Built: sim core + tests, protocol v1 (`docs/protocol-v1.md`), WebSocket server, Python stock `hello` bot (connects, prints its ship's position).
- Layout: `src/Headless.Sim`, `src/Headless.Protocol`, `src/Headless.Server`, `src/Headless.Sim.Tests`, `src/bots/python/hello`.
- **Folder convention**: all code (projects, tests, bots) goes under `src/`; scripts (build, run, dev tooling) go under `etc/`; docs live in `docs/`.
- `dotnet test src` runs the tests (solution is `src/Headless.slnx`). `dotnet run --project src/Headless.Server -- --Game:TicksPerSecond=40` fast-forwards the sim (dev tokens `dev-token-1` and `dev-token-2`).
- Protocol changes: update `Headless.Protocol/V1`, `ProtocolMapper`, `docs/protocol-v1.md` and the stock bots together.

## Open questions

- Whether ships exist in the world while their owner/bot is offline (hosting cost).
