# Hello (Python stock bot)

The smallest possible bot: it connects with a ship token and prints the ship's position every tick.
See [`hello.py`](hello.py).

## Run it

Requires Python 3.10+. Start the server first (see the repo README), then:

```bash
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt   # macOS/Linux: .venv/bin/pip
.venv/Scripts/python hello.py --token dev-token-1
```

Options: `--url` (default `ws://localhost:5080/v1/bot`), `--token`. You can also set them with the
`HEADLESS_URL` and `HEADLESS_TOKEN` environment variables.

The protocol is documented in [`docs/protocol-v1.md`](../../../../docs/protocol-v1.md).
