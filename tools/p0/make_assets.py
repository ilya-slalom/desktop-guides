"""Generate simple, deterministic placeholder MSIX images for the P0 probe."""

from pathlib import Path
import struct
import zlib


OUTPUT = Path(__file__).resolve().parents[2] / "src" / "DesktopGuides.App" / "Assets"
SIZES = {
    "Logo.png": (50, 50),
    "Square44x44Logo.png": (44, 44),
    "Square150x150Logo.png": (150, 150),
    "Wide310x150Logo.png": (310, 150),
    "SplashScreen.png": (620, 300),
}


def chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(
        ">I", zlib.crc32(kind + payload) & 0xFFFFFFFF
    )


def png(width: int, height: int) -> bytes:
    rows = []
    for y in range(height):
        pixels = bytearray()
        for x in range(width):
            guide_line = x % 20 in (0, 1) or y % 20 in (0, 1)
            pixels.extend((39, 59, 85, 255) if guide_line else (23, 34, 49, 255))
        rows.append(b"\x00" + pixels)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
        + chunk(b"IEND", b"")
    )


OUTPUT.mkdir(parents=True, exist_ok=True)
for name, (width, height) in SIZES.items():
    (OUTPUT / name).write_bytes(png(width, height))
