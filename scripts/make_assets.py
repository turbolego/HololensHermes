"""
make_assets.py
==============
Generates the minimum set of solid-colour PNG assets required by
Package.appxmanifest.  Run this only if the Assets folder is missing
or if you are bootstrapping the project from absolute scratch.

Usage:
    python scripts/make_assets.py

Output files (written to  <repo-root>/Assets/):
    LockScreenLogo.scale-200.png          96  x  96
    SplashScreen.scale-200.png          1240  x 600
    Square150x150Logo.scale-200.png      300  x 300
    Square44x44Logo.scale-200.png         88  x  88
    Square44x44Logo.targetsize-24_altform-unplated.png   24 x 24
    StoreLogo.png                         50  x  50
    Wide310x150Logo.scale-200.png        620  x 300
"""

import os
import struct
import zlib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS_DIR = os.path.join(os.path.dirname(SCRIPT_DIR), "Assets")

# (filename, width, height, R, G, B)
ASSETS = [
    ("LockScreenLogo.scale-200.png",                       96,   96, 0x7F, 0x9F, 0xFF),
    ("SplashScreen.scale-200.png",                       1240,  600, 0x1A, 0x1A, 0x2E),
    ("Square150x150Logo.scale-200.png",                   300,  300, 0x7F, 0x9F, 0xFF),
    ("Square44x44Logo.scale-200.png",                      88,   88, 0x7F, 0x9F, 0xFF),
    ("Square44x44Logo.targetsize-24_altform-unplated.png",  24,   24, 0x7F, 0x9F, 0xFF),
    ("StoreLogo.png",                                       50,   50, 0x7F, 0x9F, 0xFF),
    ("Wide310x150Logo.scale-200.png",                      620,  300, 0x7F, 0x9F, 0xFF),
]


def _png_chunk(tag: bytes, data: bytes) -> bytes:
    payload = tag + data
    return (struct.pack(">I", len(data))
            + payload
            + struct.pack(">I", zlib.crc32(payload) & 0xFFFFFFFF))


def make_png(path: str, w: int, h: int, r: int, g: int, b: int) -> None:
    """Write a minimal valid solid-colour RGB PNG."""
    sig  = b"\x89PNG\r\n\x1a\n"
    ihdr = _png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    row  = b"\x00" + bytes([r, g, b]) * w      # filter-type 0, then RGB pixels
    raw  = row * h
    idat = _png_chunk(b"IDAT", zlib.compress(raw, level=1))
    iend = _png_chunk(b"IEND", b"")
    with open(path, "wb") as fh:
        fh.write(sig + ihdr + idat + iend)


def main():
    os.makedirs(ASSETS_DIR, exist_ok=True)
    print(f"Writing assets to:  {ASSETS_DIR}\n")
    for filename, w, h, r, g, b in ASSETS:
        dest = os.path.join(ASSETS_DIR, filename)
        if os.path.exists(dest):
            print(f"  skip (exists)  {filename}")
            continue
        make_png(dest, w, h, r, g, b)
        print(f"  created        {filename}  ({w}x{h})")
    print("\nDone.")


if __name__ == "__main__":
    main()
