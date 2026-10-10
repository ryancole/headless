# Miner (Python stock bot)

Runs the basic loop: undock, fly to an asteroid, mine until full, fly home, sell, refuel, repeat.
It's about 150 commented lines in [`miner.py`](miner.py) and is meant to be read, forked and beaten.

## Run it

Requires Python 3.10+. Start the server first (see the repo README), then:

```bash
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt   # macOS/Linux: .venv/bin/pip
.venv/Scripts/python miner.py --token dev-token-1
```

Options: `--url` (default `ws://localhost:5080/v1/bot`), `--token`. You can also set them with the
`HEADLESS_URL` and `HEADLESS_TOKEN` environment variables.

The protocol is documented in [`docs/protocol-v1.md`](../../../../docs/protocol-v1.md).
