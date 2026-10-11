"""
Headless stock bot: Hello (Python)

A small bot that flies its ship to a point and stops there. Competent, not clever: it's meant to be
read, and to be beaten.

The protocol, as it stands (details in docs/protocol.md):

    1. Send a connect request with your token, resending until the server answers.
    2. The server accepts (giving you a session id) or denies.
    3. Every tick the server sends a snapshot of your ship: position, velocity, heading, mass and its
       devices. Snapshots can be lost or arrive out of order (it's UDP), so ignore any older than the
       newest you've seen.
    4. Send a keep-alive every second, or the server drops you after 5 seconds of silence.
    5. Fly the ship by setting its devices: an engine (throttle 0..1, pushes along the heading) and a
       gyroscope (turn rate -1..1). Each command is acknowledged; resend it until the ack arrives.

There's no drag in space: the ship keeps its speed until it thrusts the other way, and the engine
only pushes forward, so stopping means turning around and burning. This bot works out how fast it
can go and still have time to turn and brake before the target.

Standard library only. Usage:
    python hello.py --token dev-token-1 --target 500,300
"""

from __future__ import annotations

import argparse
import logging
import math
import os
import random
import socket
import time
from dataclasses import dataclass

import headless_protocol as hp

log = logging.getLogger("hello")

ARRIVED_DISTANCE = 1.0  # metres: close enough to call it there...
ARRIVED_SPEED = 0.02    # ...if we're also this slow (m/s)
DRIFT_LIMIT = 5.0       # metres: once there, only move again if we drift this far
CAUTION = 0.8           # fly at this fraction of the fastest speed we could still stop from
AIM_TOLERANCE = 0.2     # radians: only burn when pointing within this of where we want to push


class Rejected(Exception):
    """The server refused us; retrying won't help."""


# ---------------------------------------------------------------------------
# Flying: pure functions from what the ship knows to what to set the devices to
# ---------------------------------------------------------------------------

def wrap_angle(angle: float) -> float:
    """Wraps an angle into [-pi, pi)."""
    return (angle + math.pi) % (2 * math.pi) - math.pi


def steer(ship: hp.Ship, max_accel: float, max_turn_rate: float, target: tuple[float, float],
          tick_seconds: float) -> tuple[float, float]:
    """Returns (throttle, turn) settings that take the ship to the target and stop it there."""
    dx, dy = target[0] - ship.x, target[1] - ship.y
    distance = math.hypot(dx, dy)

    # The fastest we can go and still stop in time. Stopping takes up to half a turn to point backwards
    # (flip_time), then braking at full thrust: solve  v * flip_time + v^2 / (2 * a) = distance  for v.
    flip_time = math.pi / max_turn_rate
    stoppable = max_accel * (math.sqrt(flip_time ** 2 + 2 * distance / max_accel) - flip_time)
    want_speed = CAUTION * stoppable

    # The velocity we want minus the one we have: that's which way to push, and how hard.
    want_vx, want_vy = (dx / distance * want_speed, dy / distance * want_speed) if distance > 0 else (0.0, 0.0)
    error_x, error_y = want_vx - ship.vx, want_vy - ship.vy
    error = math.hypot(error_x, error_y)
    if error < max(0.01, 0.1 * want_speed):
        return 0.0, 0.0  # close enough to the right velocity: coast

    # Turn to face the push direction, as far as one tick allows. The gyroscope keeps turning at the
    # rate we set until we change it, so aim to land exactly on the heading by the end of the tick.
    off = wrap_angle(math.atan2(error_y, error_x) - ship.heading)
    turn = max(-1.0, min(1.0, off / (max_turn_rate * tick_seconds)))

    # Only burn when pointing the right way, and only as hard as needed to fix the error this tick.
    throttle = min(1.0, error / (max_accel * tick_seconds)) if abs(off) < AIM_TOLERANCE else 0.0
    return throttle, turn


class Pilot:
    """
    Flies to a target and holds there. It remembers whether it has arrived, so that slow drift doesn't
    set off a half-turn every few seconds: once there, it only moves again if it drifts DRIFT_LIMIT away.
    """

    def __init__(self, target: tuple[float, float]):
        self.target = target
        self.arrived = False

    def steer(self, ship: hp.Ship, max_accel: float, max_turn_rate: float, tick_seconds: float) -> tuple[float, float]:
        distance = math.hypot(self.target[0] - ship.x, self.target[1] - ship.y)
        if self.arrived and distance < DRIFT_LIMIT:
            return 0.0, 0.0
        if distance < ARRIVED_DISTANCE and math.hypot(ship.vx, ship.vy) < ARRIVED_SPEED:
            log.info("Arrived at (%.1f, %.1f)", ship.x, ship.y)
            self.arrived = True
            return 0.0, 0.0
        self.arrived = False
        return steer(ship, max_accel, max_turn_rate, self.target, tick_seconds)


# ---------------------------------------------------------------------------
# Talking to the server
# ---------------------------------------------------------------------------

@dataclass
class PendingCommand:
    sequence: int
    value: float
    last_sent: float = 0.0


class Commander:
    """
    Sends device commands reliably. UDP can drop packets, so each command is resent until the server
    acknowledges it. Commands just set values, so only the newest per device matters: a new one
    replaces any still pending, and the server ignores ones that arrive after a newer one.
    """

    def __init__(self, sock: socket.socket, session: int):
        self.sock = sock
        self.session = session
        self.next_sequence = 1
        self.pending: dict[int, PendingCommand] = {}  # by device index

    def set(self, device: int, value: float, current: float) -> None:
        """Asks for device's setting to become value, unless it's already that (or about to be)."""
        value = round(value, 2)  # finer than this isn't worth a command
        pending = self.pending.get(device)
        if (pending.value if pending else current) == value:
            return
        self.pending[device] = PendingCommand(self.next_sequence, value)
        self.next_sequence += 1
        self.resend_due()

    def resend_due(self) -> None:
        now = time.monotonic()
        for device, command in self.pending.items():
            if now - command.last_sent >= hp.COMMAND_RETRY_INTERVAL:
                self.sock.send(hp.encode_device_command(self.session, command.sequence, device, hp.SET, command.value))
                command.last_sent = now

    def acknowledged(self, ack: hp.CommandAck) -> None:
        for device, command in list(self.pending.items()):
            if command.sequence != ack.sequence:
                continue
            if ack.result == hp.CommandResult.OVER_BUDGET:
                return  # keep resending; it'll fit in a later tick
            if ack.result not in (hp.CommandResult.OK, hp.CommandResult.STALE):
                log.error("Device %d refused %.2f: %s", device, command.value, ack.result.name)
            del self.pending[device]


def open_socket(host: str, port: int) -> socket.socket:
    family, kind, proto, _, address = socket.getaddrinfo(host, port, type=socket.SOCK_DGRAM)[0]
    sock = socket.socket(family, kind, proto)
    sock.connect(address)  # A connected UDP socket only receives packets from the server.
    sock.settimeout(0.1)
    return sock


def receive(sock: socket.socket) -> hp.ServerPacket | None:
    """One packet from the server, or None if nothing (valid) arrived within the socket timeout."""
    try:
        return hp.decode(sock.recv(hp.MAX_PACKET_SIZE * 2))
    except (TimeoutError, ConnectionResetError):
        # ConnectionResetError: on Windows, "port unreachable" when the server isn't running yet.
        return None


def connect(sock: socket.socket, token: str) -> hp.ConnectAccepted:
    nonce = random.getrandbits(64)
    request = hp.encode_connect_request(hp.ClientRole.BOT, nonce, token)
    deadline = time.monotonic() + hp.TIMEOUT
    next_send = 0.0

    while time.monotonic() < deadline:
        if time.monotonic() >= next_send:
            sock.send(request)
            next_send = time.monotonic() + hp.CONNECT_RETRY_INTERVAL
        match receive(sock):
            case hp.ConnectAccepted(nonce=n) as accepted if n == nonce:
                return accepted
            case hp.ConnectDenied(nonce=n, reason=reason) if n == nonce:
                raise Rejected(f"server denied the connection: {reason.name}")
    raise TimeoutError("no answer from the server")


def find_device(snapshot: hp.ShipSnapshot, kind: hp.DeviceKind) -> int:
    """The index of the ship's first device of this kind. Ships say what they're fitted with."""
    for index, device in enumerate(snapshot.devices):
        if device.kind == kind:
            return index
    raise Rejected(f"this ship has no {kind.name.lower()}")


def fly(snapshot: hp.ShipSnapshot, commander: Commander, pilot: Pilot, tick_seconds: float) -> None:
    ship = snapshot.ship
    engine = find_device(snapshot, hp.DeviceKind.ENGINE)
    gyroscope = find_device(snapshot, hp.DeviceKind.GYROSCOPE)
    max_accel = snapshot.devices[engine].rating / snapshot.mass
    max_turn_rate = snapshot.devices[gyroscope].rating

    throttle, turn = pilot.steer(ship, max_accel, max_turn_rate, tick_seconds)
    commander.set(engine, throttle, snapshot.devices[engine].setting)
    commander.set(gyroscope, turn, snapshot.devices[gyroscope].setting)

    distance = math.hypot(pilot.target[0] - ship.x, pilot.target[1] - ship.y)
    log.info("[tick %d] at (%.1f, %.1f), %.1f m/s, heading %.0f deg, %.1f m to go -> throttle %.2f, turn %+.2f",
             snapshot.tick, ship.x, ship.y, math.hypot(ship.vx, ship.vy), math.degrees(ship.heading), distance,
             throttle, turn)


def run(host: str, port: int, token: str, target: tuple[float, float]) -> None:
    with open_socket(host, port) as sock:
        accepted = connect(sock, token)
        session = accepted.session
        log.info("Flying ship %d '%s' to (%.0f, %.0f) (%.1fs ticks)",
                 accepted.subject_id, accepted.subject_name, target[0], target[1], accepted.tick_seconds)

        commander = Commander(sock, session)
        pilot = Pilot(target)
        last_tick = -1
        last_heard = last_sent = time.monotonic()
        try:
            while True:
                now = time.monotonic()
                if now - last_sent >= hp.KEEP_ALIVE_INTERVAL:
                    sock.send(hp.encode_keep_alive(session))
                    last_sent = now
                if now - last_heard > hp.TIMEOUT:
                    log.warning("Lost contact with the server")
                    return
                commander.resend_due()

                packet = receive(sock)
                if packet is None or getattr(packet, "session", None) != session:
                    continue
                last_heard = time.monotonic()

                match packet:
                    case hp.ShipSnapshot(tick=tick) if tick > last_tick:
                        last_tick = tick
                        fly(packet, commander, pilot, accepted.tick_seconds)
                    case hp.CommandAck():
                        commander.acknowledged(packet)
                    case hp.Disconnected(reason=hp.DisconnectReason.SUPERSEDED):
                        # Reconnecting would just knock the other connection off in turn.
                        raise Rejected("another bot connected with this ship's token")
                    case hp.Disconnected(reason=reason):
                        log.warning("Server disconnected us: %s", reason.name)
                        return
        except KeyboardInterrupt:
            sock.send(hp.encode_disconnect(session))  # Be polite: free our slot right away.
            raise


def parse_point(text: str) -> tuple[float, float]:
    x, y = text.split(",")
    return float(x), float(y)


def main() -> None:
    parser = argparse.ArgumentParser(description="Headless stock hello bot")
    parser.add_argument("--host", default=os.environ.get("HEADLESS_HOST", "localhost"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("HEADLESS_PORT", "5080")))
    parser.add_argument("--token", default=os.environ.get("HEADLESS_TOKEN"))
    parser.add_argument("--target", type=parse_point, default=(500.0, 300.0), help="where to fly, as x,y in metres")
    args = parser.parse_args()
    if not args.token:
        parser.error("a ship token is required (--token or HEADLESS_TOKEN)")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    # Reconnect forever, unless the server tells us our token is no good.
    while True:
        try:
            run(args.host, args.port, args.token, args.target)
        except Rejected as ex:
            log.error("%s", ex)
            return
        except (TimeoutError, OSError) as ex:
            log.warning("%s", ex)
        log.info("Reconnecting in 3s")
        time.sleep(3)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
