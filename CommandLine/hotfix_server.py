#!/usr/bin/env python3
"""Local-only static server for FYAsset publish mirrors."""

from __future__ import annotations

import argparse
import json
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse


METADATA_NAMES = {
    "PackageIndex.json",
    "catalog.json",
    "AAManifest.json",
    "AAManifest.bin",
    "ABManifest.json",
    "ABManifest.bin",
}


class HotfixRequestHandler(SimpleHTTPRequestHandler):
    server_version = "FYAssetHotfixServer/1.0"

    def __init__(self, *args, directory: str, token: str, **kwargs):
        self._token = token
        super().__init__(*args, directory=directory, **kwargs)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/__fyasset_health":
            self._write_json(
                200,
                {
                    "ok": True,
                    "token": self._token,
                    "root": str(Path(self.directory).resolve()),
                },
            )
            return

        if parsed.path == "/__fyasset_shutdown":
            token = parse_qs(parsed.query).get("token", [""])[0]
            if token != self._token:
                self._write_json(403, {"ok": False})
                return

            self._write_json(200, {"ok": True})
            threading.Thread(target=self.server.shutdown, daemon=True).start()
            return

        super().do_GET()

    def end_headers(self) -> None:
        file_name = Path(urlparse(self.path).path).name
        if file_name in METADATA_NAMES:
            self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, _format: str, *args) -> None:
        return

    def _write_json(self, status: int, payload: dict) -> None:
        data = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--token", required=True)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    root.mkdir(parents=True, exist_ok=True)

    def handler(*handler_args, **handler_kwargs):
        return HotfixRequestHandler(
            *handler_args,
            directory=str(root),
            token=args.token,
            **handler_kwargs,
        )

    server = ThreadingHTTPServer(("127.0.0.1", args.port), handler)
    server.daemon_threads = True
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
