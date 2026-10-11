# Headless

An online space game played by bots. You write the code that flies your ship; the server decides what happens.

Right now the server tracks players and their ships. Every ship has an owner (a player) and is flown by a bot, which steers it by setting the ship's devices: an engine and a gyroscope, with Newtonian physics on a 2D plane. Bots and players connect over a small binary UDP protocol and receive a snapshot each tick; the server logs where every ship is, where it's heading and who owns it.

## Layout

| Path | What |
|---|---|
| `src/server/Headless.Sim` | Simulation core: players, ships, devices and movement. No networking. |
| `src/server/Headless.Protocol` | The binary UDP protocol: packet types and encoder/decoder. |
| `src/server/Headless.Server` | The game server: tick loop and UDP socket (port 5080). |
| `src/server/Headless.*.Tests` | xUnit tests for the sim, protocol and server. |
| `src/bots/python/hello` | Python stock bot that flies its ship to a point and stops there. |
| `src/game` | Godot 4 (C#) game client: walk around inside a ship in first person; connects as a player. |
| `docs/` | [Core loop](docs/core-loop.md), [protocol](docs/protocol.md), [art style](docs/art-style.md). |

## Quick start

```bash
dotnet test src/server
```

```bash
dotnet run --project src/server/Headless.Server
```

The server listens on UDP port 5080. In Development it spawns two players, each owning one
ship (set in `appsettings.Development.json`):

| Player | Player token | Ship | Bot token |
|---|---|---|---|
| Player One | `dev-player-1` | Prospector | `dev-token-1` |
| Player Two | `dev-player-2` | Wildcat | `dev-token-2` |

Then start a bot by following [src/bots/python/hello](src/bots/python/hello/README.md), and/or the game
client (below). The protocol is described in [docs/protocol.md](docs/protocol.md).

To fast-forward the sim while developing, raise the wall-clock tick rate. Sim time per tick stays the same.

```bash
dotnet run --project src/server/Headless.Server -- --Game:TicksPerSecond=40
```

## Game client

The client is a Godot 4.7 (.NET edition) project in `src/game`. Open `src/game/project.godot` in the Godot
editor and press Play. You're standing in a basic three-room ship (cockpit, hold, engine room): WASD to
move, mouse to look, Space to jump, Shift to run, Esc to free the mouse (click to grab it again). The ship
is the same for everyone and isn't linked to server state yet, and the server doesn't know where you are.

It also connects to the server as Player One (`localhost:5080`, token `dev-player-1`) and
prints each snapshot to the Output panel; the connection status is shown top left. If the server isn't running or goes away it keeps retrying; a
denied token stops it. Change the host, port or token in the inspector on the `Main` node, or on the
command line after `--`. To run it headless, e.g. as Player Two:

```bash
"C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path src/game -- --token=dev-player-2
```

`-- --screenshot=out.png` saves a PNG of the first second and quits, handy for checking visuals without
playing.

To build and start everything together: the server and the Python bot (each in its own window, the bot
flying Prospector to `500,300`) and the game:

```bash
pwsh etc/run.ps1
```

`-BotTarget x,y` picks where the bot flies and `-NoBot` skips it. Other arguments are passed on to the game,
e.g. `pwsh etc/run.ps1 -NoBot --token=dev-player-2`.

The networking lives in `src/game/ServerConnection.cs`, which doesn't depend on Godot; `Main.cs` drives it
from `_Process`.
