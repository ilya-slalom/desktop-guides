"""Tests for the connection-counting loopback canary."""

from __future__ import annotations

import http.client
import socket
import tempfile
import threading
import time
import unittest
from pathlib import Path

import http_canary


class CanaryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.log = Path(self.folder.name) / "logs" / "canary.log"
        self.server = http_canary.make_server(self.log, 0)
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(5)
        self.folder.cleanup()

    def lines(self, count: int) -> list[str]:
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if self.log.exists():
                found = self.log.read_text(encoding="utf-8").splitlines()
                if len(found) >= count:
                    return found
            time.sleep(0.05)
        self.fail(f"expected at least {count} log lines")

    def request(self, method: str, path: str) -> tuple[int, str | None]:
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        try:
            connection.request(method, path)
            response = connection.getresponse()
            return response.status, response.getheader("Location")
        finally:
            connection.close()

    def test_creates_the_log_folder(self) -> None:
        self.assertTrue(self.log.parent.is_dir())

    def test_connection_without_a_request_is_recorded(self) -> None:
        socket.create_connection(("127.0.0.1", self.port), timeout=5).close()
        self.assertEqual(self.lines(1), ["ACCEPT"])

    def test_request_is_recorded_after_its_connection(self) -> None:
        self.assertEqual(self.request("GET", "/x?y=1"), (204, None))
        self.assertEqual(self.lines(2), ["ACCEPT", "GET /x?y=1"])

    def test_health_records_only_the_connection(self) -> None:
        self.assertEqual(self.request("GET", "/health")[0], 204)
        time.sleep(0.2)
        self.assertEqual(self.lines(1), ["ACCEPT"])

    def test_other_methods_are_recorded(self) -> None:
        for method in ("POST", "HEAD", "OPTIONS", "PUT"):
            self.assertEqual(self.request(method, "/m")[0], 204)
        found = self.lines(8)
        for method in ("POST", "HEAD", "OPTIONS", "PUT"):
            self.assertIn(f"{method} /m", found)

    def test_redirect_points_off_host(self) -> None:
        self.assertEqual(
            self.request("GET", "/redirect"),
            (302, "https://example.net/desktop-guides-canary-redirect"))
        self.assertEqual(self.lines(2), ["ACCEPT", "GET /redirect"])


if __name__ == "__main__":
    unittest.main()
