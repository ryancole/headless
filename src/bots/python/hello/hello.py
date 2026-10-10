"""
Headless stock bot: Hello (Python)

The smallest possible bot. It connects to the server with a ship token and prints where its ship is.
For now that's all the game does: ships have a position and nothing moves them.

The v1 bot protocol, as it stands:

    1. Connect to the server's WebSocket with your ship token.
    2. Receive a "hello" telling you which ship you control.
    3. Every tick, receive a "snapshot" with your ship's position.

Usage:
    python hello.py --url ws://localhost:5080/v1/bot --token dev-token-1
"""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
import os

from websockets.asyncio.client import connect
from websockets.exceptions import ConnectionClosed

log = logging.getLogger("hello")


async def run(url: str, token: str) -> None:
    headers = {"Authorization": f"Bearer {token}"}
    async with connect(url, additional_headers=headers) as ws:
        hello = json.loads(await ws.recv())
        if hello.get("type") != "hello":
            raise RuntimeError(f"expected hello, got {hello}")
        log.info("Controlling ship %s (protocol %s)", hello["ship_id"], hello["protocol"])

        async for raw in ws:
            msg = json.loads(raw)
            if msg["type"] == "error":
                log.warning("Server error: %s: %s", msg["code"], msg["message"])
            elif msg["type"] == "snapshot":
                ship = msg["ship"]
                pos = ship["position"]
                log.info("[tick %d] %s is at (%.1f, %.1f)", msg["tick"], ship["name"], pos["x"], pos["y"])


async def main() -> None:
    parser = argparse.ArgumentParser(description="Headless stock hello bot")
    parser.add_argument("--url", default=os.environ.get("HEADLESS_URL", "ws://localhost:5080/v1/bot"))
    parser.add_argument("--token", default=os.environ.get("HEADLESS_TOKEN"))
    args = parser.parse_args()
    if not args.token:
        parser.error("a ship token is required (--token or HEADLESS_TOKEN)")

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    # Reconnect forever.
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
