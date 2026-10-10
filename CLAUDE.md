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

- **Current focus (2026-10-10): nail down the server first.** Entities: a **player** (id, name) and a **ship** (id, name, `OwnerPlayerId`, position). Every ship has exactly one owner; ownership lives on the ship only, so players have no ship reference (a player's ships are found by owner id). Ships are autonomous and flown by a bot (`/v1/bot`, ship bot token); players connect separately (`/v1/player`, separate player token). **Owning a ship ≠ being aboard it**: players will be able to board ships they don't own, so don't tie a player's location to ownership or add a ship reference on the player. Where a player is aboard is not modelled yet. Each player starts with one ship. Nothing moves ships yet: no velocity, no thrust, no intents; clients can't send anything. The server logs each ship's position, bot status and owner, each second. Mining, stations, fuel, credits, sensors and movement were deliberately removed to keep it simple; add features back one at a time, only when asked.
- Core loop (mine → haul → sell → refuel) is chosen but deferred; see `docs/core-loop.md`.
- Built: sim core + tests, protocol v1 (`docs/protocol-v1.md`), WebSocket server, Python stock `hello` bot (connects, prints its ship's position).
- Layout (monorepo): `src/server/` holds the .NET solution (`Headless.slnx`, `Directory.Build.props`, `Headless.Sim`, `Headless.Protocol`, `Headless.Server`, `Headless.Sim.Tests`); `src/bots/python/hello` is the stock bot; `src/game` is the Godot 4.7.2 (.NET) client project (`Headless.Game.csproj`, net10.0, main scene `Main.tscn`). It's an empty scene and doesn't connect to the server yet. Godot is installed at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\` (use the `_console.exe` for headless runs, e.g. `--headless --path src/game --build-solutions --quit`).
- **Folder convention**: all code goes under `src/`, one folder per component (`src/server`, `src/bots`, `src/game`); scripts (build, run, dev tooling) go under `etc/`; docs live in `docs/`.
- `dotnet test src/server` runs the tests (solution is `src/server/Headless.slnx`). `dotnet run --project src/server/Headless.Server -- --Game:TicksPerSecond=40` fast-forwards the sim (dev tokens `dev-token-1` and `dev-token-2`).
- Protocol changes: update `Headless.Protocol/V1`, `ProtocolMapper`, `docs/protocol-v1.md` and the stock bots together.

## Open questions

- Whether ships exist in the world while their owner/bot is offline (hosting cost).
