"""Loopback-only canary that records every accepted connection and request.

Guide fixtures point their references at it. Any line in the log, including
a bare ACCEPT from a preconnect, is a guide-originated connection.
"""

from __future__ import annotations

import argparse
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

REDIRECT_TARGET = "https://example.net/desktop-guides-canary-redirect"


class CanaryServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, log: Path, port: int) -> None:
        self.log = log
        self.lock = threading.Lock()
        super().__init__(("127.0.0.1", port), CanaryHandler)

    def record(self, line: str) -> None:
        with self.lock, self.log.open("a", encoding="utf-8") as log:
            log.write(line + "\n")

    def get_request(self) -> tuple[socket.socket, object]:
        request = super().get_request()
        self.record("ACCEPT")
        return request


class CanaryHandler(BaseHTTPRequestHandler):
    server: CanaryServer

    def do_GET(self) -> None:
        self.handle_request()

    def do_POST(self) -> None:
        self.handle_request()

    def do_HEAD(self) -> None:
        self.handle_request()

    def do_OPTIONS(self) -> None:
        self.handle_request()

    def do_PUT(self) -> None:
        self.handle_request()

    def handle_request(self) -> None:
        if self.path != "/health":
            self.server.record(f"{self.command} {self.path}")
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", REDIRECT_TARGET)
        else:
            self.send_response(204)
        self.end_headers()

    def log_message(self, format: str, *values: object) -> None:
        pass


def make_server(log: Path, port: int) -> CanaryServer:
    log.parent.mkdir(parents=True, exist_ok=True)
    return CanaryServer(log, port)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument("--port", default=8765, type=int)
    args = parser.parse_args()

    server = make_server(args.log, args.port)
    print(f"listening on 127.0.0.1:{server.server_address[1]}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
