"""
Headless stock bot: Hello (Python)

The smallest possible bot. It connects to the server with its ship's bot token and prints where the
ship is. For now that's all the game does: ships have a position and nothing moves them.

The protocol, as it stands (details in docs/protocol.md):

    1. Send a connect request with your token, resending until the server answers.
    2. The server accepts (giving you a session id) or denies.
    3. Every tick the server sends a snapshot of your ship. Snapshots can be lost or arrive out of
       order (it's UDP), so ignore any older than the newest you've seen.
    4. Send a keep-alive every second, or the server drops you after 5 seconds of silence.

Standard library only. Usage:
    python hello.py --token dev-token-1
"""

from __future__ import annotations

import argparse
import logging
import os
import random
import socket
import time

import headless_protocol as hp

log = logging.getLogger("hello")


class Rejected(Exception):
    """The server refused us; retrying won't help."""


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


def run(host: str, port: int, token: str) -> None:
    with open_socket(host, port) as sock:
        accepted = connect(sock, token)
        session = accepted.session
        log.info("Controlling ship %d '%s' (%.1fs ticks)", accepted.subject_id, accepted.subject_name, accepted.tick_seconds)

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

                packet = receive(sock)
                if packet is None or getattr(packet, "session", None) != session:
                    continue
                last_heard = time.monotonic()

                match packet:
                    case hp.ShipSnapshot(tick=tick, ship=ship) if tick > last_tick:
                        last_tick = tick
                        log.info("[tick %d] ship %d is at (%.1f, %.1f)", tick, ship.id, ship.x, ship.y)
                    case hp.Disconnected(reason=reason):
                        log.warning("Server disconnected us: %s", reason.name)
                        return
        except KeyboardInterrupt:
            sock.send(hp.encode_disconnect(session))  # Be polite: free our slot right away.
            raise


def main() -> None:
    parser = argparse.ArgumentParser(description="Headless stock hello bot")
    parser.add_argument("--host", default=os.environ.get("HEADLESS_HOST", "localhost"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("HEADLESS_PORT", "5080")))
    parser.add_argument("--token", default=os.environ.get("HEADLESS_TOKEN"))
    args = parser.parse_args()
    if not args.token:
        parser.error("a ship token is required (--token or HEADLESS_TOKEN)")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    # Reconnect forever, unless the server tells us our token is no good.
    while True:
        try:
            run(args.host, args.port, args.token)
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
