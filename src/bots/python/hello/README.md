# Hello (Python stock bot)

The smallest possible bot: it connects with its ship's bot token and prints the ship's position every
tick. See [`hello.py`](hello.py).

[`headless_protocol.py`](headless_protocol.py) is the wire protocol in plain Python: encoding, decoding
and constants. It's a reference implementation for bot authors; copy it or port it to your language.

## Run it

Requires Python 3.10+, and nothing else (standard library only). Start the server first (see the repo
README), then:

```bash
python hello.py --token dev-token-1
```

Options: `--host` (default `localhost`), `--port` (default `5080`), `--token`. You can also set them with
the `HEADLESS_HOST`, `HEADLESS_PORT` and `HEADLESS_TOKEN` environment variables.

The protocol is documented in [`docs/protocol.md`](../../../../docs/protocol.md).
