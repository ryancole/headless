# Core loop

**Decision (2026-10-10): mine → haul → sell → refuel.**

A ship undocks, flies to an asteroid, mines until its hold is full, flies back to a station, sells the ore,
buys fuel, and goes again. Credits are the score for now.

## Why this loop

- **It is the first milestone.** One ship, one bot, one asteroid, one station. Nothing else is needed to make it playable.
- **Decision quality wins, not throughput.** With Newtonian flight and a slow tick, the interesting choices are
  *which* rock, *how fast* to fly (fuel spent braking costs credits), *when* to head home, and how to handle
  surprises. A bot that sends more requests gains nothing; the per-tick intent budget makes sure of that.
- **It's easy to teach.** A first stock bot is about 100 lines and still leaves plenty to improve, which is what
  the stock bots are for.
- **It grows into the other loops.** Trading and hauling sit on the same pieces (cargo, stations, prices, fuel).
  Adding more ore types, station prices that move with supply, and contracts turns mining into trading
  without a rewrite.
- **It gives humans something to do later.** Mining rigs break, holds need managing, and a laden hauler is
  worth boarding.

## Rules as implemented (v1)

| Thing | Value (defaults in `SimConfig`) |
|---|---|
| Tick | 0.5 s of sim time; the server runs 2 ticks/s of wall time by default |
| Intent budget | 4 per ship per tick; extras are rejected with `budget_exceeded` |
| Thrust | up to 10 m/s², throttle vector of length 0..1 that persists until changed |
| Fuel | 1000 units, full throttle burns 1 unit/s; an empty tank cuts the engines |
| Cargo | 100 ore units |
| Mining | 2 ore/s within 500 m of the asteroid; must be under 5 m/s to start |
| Docking | within 500 m of the station and under 5 m/s |
| Market | starter station buys ore at 10 credits and sells fuel at 1 credit |
| Sensors | asteroids and ships within 50 km; stations are always visible (on the charts) |
| Offline | a disconnected ship holds course: thrust cut, coasting, mining continues if in range |

The starter system (`WorldGenerator.CreateStarterSystem`) has one station at the origin and 8 asteroids
4–25 km out, each holding 200–1000 ore. Asteroids don't respawn yet.

## Next levers (not built yet)

- Station prices that drop as ore is sold and recover over time, so several miners compete for the best market.
- Multiple ore types and stations with different demand, which turns this into a trading loop.
- Asteroid respawn or slow regeneration.
- Ship upgrades (hold size, engines, mining lasers) as a credit sink.
- Collisions and hazards.
