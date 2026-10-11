# Hello (Python stock bot)

A small bot that flies its ship to a point and stops there. It finds the ship's engine and gyroscope,
works out how fast it can go and still turn round and brake in time, and sends device commands
(resending each until the server acknowledges it). See [`hello.py`](hello.py).

[`headless_protocol.py`](headless_protocol.py) is the wire protocol in plain Python: encoding, decoding
and constants. It's a reference implementation for bot authors; copy it or port it to your language.

## Run it

Requires Python 3.10+, and nothing else (standard library only). Start the server first (see the repo
README), then:

```bash
python hello.py --token dev-token-1 --target 500,300
```

Options: `--host` (default `localhost`), `--port` (default `5080`), `--token`, `--target` (where to fly,
as `x,y` in metres; default `500,300`). You can also set them with
the `HEADLESS_HOST`, `HEADLESS_PORT` and `HEADLESS_TOKEN` environment variables.

The protocol is documented in [`docs/protocol.md`](../../../../docs/protocol.md).
