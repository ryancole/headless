"""
Headless stock bot: Miner (Python)

A simple, readable mining bot. It doubles as an example of the v1 bot protocol:

    1. Connect to the server's WebSocket with your ship token.
    2. Receive a "hello" with the game rules (acceleration, ranges, budgets...).
    3. Every tick, receive a "snapshot" of what your ship's sensors see.
    4. Reply with an "intents" message saying what you want to do next tick.

The strategy is a loop: undock -> fly to the best asteroid -> mine until full -> fly home ->
dock -> sell -> refuel -> repeat. It decides everything from the latest snapshot rather than
remembering state, so it recovers cleanly from reconnects and surprises.

It's competent but beatable. Ideas for doing better: pick asteroids by profit per second
instead of a fixed score, coast instead of burning the whole way, plan fuel more tightly,
avoid asteroids other ships are already mining.

Usage:
    python miner.py --url ws://localhost:5080/v1/bot --token dev-token-1
"""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
import math
import os

from websockets.asyncio.client import connect
from websockets.exceptions import ConnectionClosed

log = logging.getLogger("miner")

# How hard we're willing to brake, as a fraction of max acceleration. Leaving headroom
# absorbs the one-tick delay between seeing a snapshot and our thrust taking effect.
BRAKING_MARGIN = 0.6

# Don't go faster than this (m/s). Faster trips burn more fuel braking for little gain.
CRUISE_SPEED = 400.0

# Head home when the tank drops below this fraction, even if the hold isn't full.
FUEL_RESERVE = 0.3


# ---------------------------------------------------------------------------
# Small 2D vector helpers. Positions and velocities arrive as {"x": ..., "y": ...}.
# ---------------------------------------------------------------------------

def vec(d: dict) -> tuple[float, float]:
    return d["x"], d["y"]


def sub(a, b):
    return a[0] - b[0], a[1] - b[1]


def length(v) -> float:
    return math.hypot(v[0], v[1])


def scale(v, s: float):
    return v[0] * s, v[1] * s


def clamp_length(v, max_len: float):
    n = length(v)
    return scale(v, max_len / n) if n > max_len else v


# ---------------------------------------------------------------------------
# Piloting
# ---------------------------------------------------------------------------

def steer_toward(ship: dict, target, rules: dict):
    """
    Throttle that flies the ship to `target` and stops there.

    The idea: at distance d, the fastest speed from which we can still brake to a stop
    is sqrt(2 * a * d). Aim for that velocity (pointing at the target, capped at cruise speed)
    and thrust to close the gap between it and our current velocity.
    """
    pos, vel = vec(ship["position"]), vec(ship["velocity"])
    a = rules["max_acceleration"]
    dt = rules["tick_seconds"]

    offset = sub(target, pos)
    dist = length(offset)
    if dist < 1e-6:
        desired_vel = (0.0, 0.0)
    else:
        # Near the target, also cap speed so we don't overshoot within a single tick.
        speed = min(CRUISE_SPEED, math.sqrt(2 * a * BRAKING_MARGIN * dist), dist / (2 * dt))
        desired_vel = scale(offset, speed / dist)

    # Velocity change we want this tick, as a fraction of what full throttle delivers.
    dv = sub(desired_vel, vel)
    throttle = clamp_length(scale(dv, 1 / (a * dt)), 1.0)

    # Don't waste fuel on tiny corrections.
    if length(throttle) < 0.02:
        throttle = (0.0, 0.0)
    return throttle


def thrust(throttle) -> dict:
    return {"type": "thrust", "x": throttle[0], "y": throttle[1]}


def can_interact(ship: dict, target, max_range: float, rules: dict) -> bool:
    """Close enough and slow enough to dock with or mine `target`."""
    close = length(sub(target, vec(ship["position"]))) <= max_range * 0.9
    slow = length(vec(ship["velocity"])) <= rules["max_interaction_speed"] * 0.8
    return close and slow


# ---------------------------------------------------------------------------
# Strategy
# ---------------------------------------------------------------------------

def pick_asteroid(snapshot: dict) -> dict | None:
    """Prefer rich asteroids that are close. A crude score; a good place to start improving."""
    pos = vec(snapshot["ship"]["position"])
    candidates = [a for a in snapshot["asteroids"] if a["ore"] >= 1]
    if not candidates:
        return None
    return max(candidates, key=lambda a: min(a["ore"], 200) / (1000 + length(sub(vec(a["position"]), pos))))


def nearest_station(snapshot: dict) -> dict:
    pos = vec(snapshot["ship"]["position"])
    return min(snapshot["stations"], key=lambda s: length(sub(vec(s["position"]), pos)))


def decide(snapshot: dict, rules: dict) -> list[dict]:
    """Turn one snapshot into a list of intents for the next tick."""
    ship = snapshot["ship"]
    hold_full = ship["cargo"] >= rules["cargo_capacity"] - 1e-6
    fuel_low = ship["fuel"] < rules["max_fuel"] * FUEL_RESERVE
    asteroid = pick_asteroid(snapshot)

    # Docked: sell, top up, and leave if there's still somewhere worth mining.
    # Intents in one message apply in order, so this all happens in a single tick.
    if ship["docked_at_station_id"] is not None:
        intents = []
        if ship["cargo"] >= 1:
            intents.append({"type": "sell"})
        if ship["fuel"] < rules["max_fuel"]:
            intents.append({"type": "refuel"})
        if asteroid is not None:
            intents.append({"type": "undock"})
        return intents

    # Head home to sell when full, low on fuel, or nothing left to mine.
    if hold_full or fuel_low or (asteroid is None and ship["cargo"] >= 1):
        station = nearest_station(snapshot)
        target = vec(station["position"])
        if can_interact(ship, target, rules["docking_range"], rules):
            return [{"type": "dock", "station_id": station["id"]}]
        return [thrust(steer_toward(ship, target, rules))]

    if asteroid is None:
        return [thrust((0.0, 0.0))]  # Nothing in sensor range; sit tight.

    target = vec(asteroid["position"])

    # Already mining: just hold position over the rock.
    if ship["mining_asteroid_id"] is not None:
        return [thrust(steer_toward(ship, target, rules))]

    if can_interact(ship, target, rules["mining_range"], rules):
        return [thrust((0.0, 0.0)), {"type": "mine", "asteroid_id": asteroid["id"]}]
    return [thrust(steer_toward(ship, target, rules))]


# ---------------------------------------------------------------------------
# Connection loop
# ---------------------------------------------------------------------------

async def run(url: str, token: str) -> None:
    headers = {"Authorization": f"Bearer {token}"}
    async with connect(url, additional_headers=headers) as ws:
        hello = json.loads(await ws.recv())
        if hello.get("type") != "hello":
            raise RuntimeError(f"expected hello, got {hello}")
        rules = hello["rules"]
        log.info("Controlling ship %s (protocol %s)", hello["ship_id"], hello["protocol"])

        last_credits = None
        async for raw in ws:
            msg = json.loads(raw)
            if msg["type"] == "error":
                log.warning("Server error: %s: %s", msg["code"], msg["message"])
                continue
            if msg["type"] != "snapshot":
                continue

            for event in msg["events"]:
                log.info("[tick %d] %s: %s", msg["tick"], event["code"], event["message"])
            for error in msg["errors"]:
                log.warning("[tick %d] %s rejected: %s (%s)", msg["tick"], error["intent"], error["code"], error["message"])

            ship = msg["ship"]
            if ship["credits"] != last_credits:
                log.info("[tick %d] credits=%d cargo=%.1f fuel=%.1f",
                         msg["tick"], ship["credits"], ship["cargo"], ship["fuel"])
                last_credits = ship["credits"]

            intents = decide(msg, rules)
            if intents:
                await ws.send(json.dumps({"type": "intents", "intents": intents}))


async def main() -> None:
    parser = argparse.ArgumentParser(description="Headless stock mining bot")
    parser.add_argument("--url", default=os.environ.get("HEADLESS_URL", "ws://localhost:5080/v1/bot"))
    parser.add_argument("--token", default=os.environ.get("HEADLESS_TOKEN"))
    args = parser.parse_args()
    if not args.token:
        parser.error("a ship token is required (--token or HEADLESS_TOKEN)")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    # Reconnect forever. While we're away, the server holds our ship's course.
    while True:
        try:
            await run(args.url, args.token)
        except (ConnectionClosed, OSError) as ex:
            log.warning("Disconnected (%s); reconnecting in 3s", ex)
        await asyncio.sleep(3)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
