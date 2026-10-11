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
- **Server**: .NET generic host (no ASP.NET, no HTTP) with a hosted service running the tick loop.
- **Protocol** (decided 2026-10-10): **one custom binary protocol over raw UDP for every client — bots and the game client.** No HTTP, WebSocket, JSON or third-party networking library. Spec in `docs/protocol.md`; it must stay simple enough to implement in any language with just a UDP socket (the Python bot uses only the standard library).
- **Physics** (when needed): BepuPhysics2.
- **Stock bots**: Python and C# first.
- **No spectator/viewer for now** (decided 2026-10-10): focus on the game itself. Debug via bot logs and sim tests. Revisit later; no Node/web toolchain until then.
- **3D client (later)**: Godot 4 with C#, sharing protocol types / sim library with the server.
- **Art style**: 0x10c prototype / Halo: CE look — chunky bevelled sci-fi interiors, muted grey-green world with saturated accents, dark rooms lit by a few hard-shadowed lights, glowing pixel-sharp screens. **Follow `docs/art-style.md` for any client visuals.**
- No custom engine.

## Build order

1. **Decide the core loop** (mining / trading / hauling — open decision).
2. **Sim core** library + tests.
3. **Protocol spec** (intents, per-tick sensor snapshot, errors), then the UDP server.
4. **First stock bot** — a simple miner in Python and C#. Fix protocol clunkiness now.
5. Then: accounts, persistence, multiple players, bot library, bot runner. 3D client last.

**First milestone**: one ship, flown by one bot, flies to an asteroid, mines, sells at a station. No 3D, no accounts, no persistence.

## Status

- **Current focus (2026-10-10): nail down the server first.** Entities: a **player** (id, name) and a **ship** (id, name, `OwnerPlayerId`, position). Every ship has exactly one owner; ownership lives on the ship only, so players have no ship reference (a player's ships are found by owner id). Ships are autonomous and flown by a bot (connects with role `bot` and the ship's bot token); players connect separately (role `player`, their own player token). **Owning a ship ≠ being aboard it**: players will be able to board ships they don't own, so don't tie a player's location to ownership or add a ship reference on the player. Where a player is aboard is not modelled yet. Each player starts with one ship. Nothing moves ships yet: no velocity, no thrust, no intents; clients send no game commands (only connect/keep-alive/disconnect). The server logs each ship's position, bot status and owner, each second. Mining, stations, fuel, credits, sensors and movement were deliberately removed to keep it simple; add features back one at a time, only when asked.
- Core loop (mine → haul → sell → refuel) is chosen but deferred; see `docs/core-loop.md`.
- Built: sim core, binary UDP protocol (`docs/protocol.md`, `Headless.Protocol`), UDP game server, tests for all three (`Headless.Sim.Tests`, `Headless.Protocol.Tests`, `Headless.Server.Tests`; `GameHost` is pure packets-in/packets-out with an injected clock so it's tested without sockets), Python stock `hello` bot (standard library only; `headless_protocol.py` is the reference implementation).
- Layout (monorepo): `src/server/` holds the .NET solution (`Headless.slnx`, `Directory.Build.props`, `Headless.Sim`, `Headless.Protocol`, `Headless.Server`, and their `.Tests` projects); `src/bots/python/hello` is the stock bot; `src/game` is the Godot 4.7.2 (.NET) client project (`Headless.Game.csproj`, net10.0, main scene `Main.tscn`). It references `Headless.Protocol` and connects as a player (`ServerConnection.cs`: plain non-blocking UDP socket polled from `Main._Process`, no Godot dependency, reconnects on timeout/shutdown, stops on denied/superseded), logging `ConnectAccepted` and each `PlayerSnapshot` with `GD.Print`. Host/port/token are exported on `Main` (defaults `localhost:5080`, `dev-player-1`) and can be overridden after `--` (e.g. `-- --token=dev-player-2`). The scene (`Main.tscn`) is a first-person walk around a placeholder ship interior: `ShipInterior.cs` builds three rooms (cockpit, hold, engine room) in code from a room table, as plain boxes with colliders, lit per `docs/art-style.md` (no bevels or textures yet); `Player.cs` is a `CharacterBody3D` (WASD/mouse/Space/Shift, keys bound in code) parented to the `Ship` node, so positions are ship-local. The avatar is **client-side only** for now (not server-authoritative, unlike the design goal), and the interior isn't linked to the player's ship from the server. HUD: `+` crosshair and connection status. `-- --screenshot=<path>` renders ~1 s, saves a PNG and quits (needs a window, not `--headless`). Godot is installed at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\` (use the `_console.exe` for headless runs, e.g. `--headless --path src/game --build-solutions --quit`).
- **Folder convention**: all code goes under `src/`, one folder per component (`src/server`, `src/bots`, `src/game`); scripts (build, run, dev tooling) go under `etc/`; docs live in `docs/`.
- `etc/run.ps1` builds both, starts the server in its own window (unless already running) and runs the game in the current terminal; extra args go to the game (e.g. `--token=dev-player-2`).
- `dotnet test src/server` runs the tests (solution is `src/server/Headless.slnx`). `dotnet run --project src/server/Headless.Server -- --Game:TicksPerSecond=40` fast-forwards the sim (dev tokens `dev-token-1` and `dev-token-2`).
- Protocol changes: update `Headless.Protocol` (+ its byte-layout tests), `GameHost`/`ProtocolMapper`, `docs/protocol.md` and `headless_protocol.py` together; bump `ProtocolConstants.Version` for breaking changes.

## Open questions

- Whether ships exist in the world while their owner/bot is offline (hosting cost).
