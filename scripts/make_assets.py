"""
make_assets.py
==============
Generates and repairs PNG assets required by Package.appxmanifest for the
HoloLens 1 UWP app.

This replaces the old solid-colour placeholder generator. Each asset is now
drawn as real vector-style artwork (satellite scene or airplane scene,
depending on the repo), supersampled and downscaled with LANCZOS so the icons
are crisp at every size (44px through 1240px).

Usage:
    python scripts/make_assets.py

Behavior:
- Draws artwork for every asset (never writes blank placeholders).
- Rewrites assets whose dimensions are wrong.
- Generates both scale-200 names and common unscaled aliases.
"""

import math
import os
import random
import sys

from PIL import Image, ImageDraw

random.seed(1337)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS_DIR = os.path.join(os.path.dirname(SCRIPT_DIR), "Assets")

REPO_NAME = os.path.basename(os.path.dirname(SCRIPT_DIR)).lower()
THEME = "airplane" if "airplane" in REPO_NAME else "satellite"


# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------

def vgrad(size, top, bottom):
    """Vertical gradient image."""
    w, h = size
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        col = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (w, y)], fill=col)
    return img


def downsample(img, size):
    return img.resize(size, Image.LANCZOS)


# ---------------------------------------------------------------------------
# Satellite artwork (space scene)
# ---------------------------------------------------------------------------

def draw_stars(d, w, h, n, max_y=None):
    max_y = max_y or h
    for _ in range(n):
        x = random.randint(0, w - 1)
        y = random.randint(0, max_y - 1)
        r = random.choice([1, 1, 1, 2, 2, 3])
        b = random.randint(140, 255)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(b, b, b))


def draw_earth(d, cx, cy, r):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(30, 110, 200))
    lands = [
        (cx - int(r * 0.55), cy - int(r * 0.25), int(r * 0.30)),
        (cx + int(r * 0.15), cy + int(r * 0.35), int(r * 0.26)),
        (cx - int(r * 0.10), cy - int(r * 0.45), int(r * 0.22)),
        (cx + int(r * 0.45), cy - int(r * 0.15), int(r * 0.18)),
        (cx - int(r * 0.35), cy + int(r * 0.15), int(r * 0.16)),
    ]
    for lx, ly, lr in lands:
        d.ellipse([lx - lr, ly - lr, lx + lr, ly + lr], fill=(70, 150, 70))
    for _ in range(7):
        t = random.random()
        cxx = cx + (t - 0.5) * 2 * r * 0.85
        cyy = cy + (random.random() - 0.5) * 2 * r * 0.85
        cr = random.randint(int(r * 0.06), int(r * 0.14))
        d.ellipse([cxx - cr, cyy - cr, cxx + cr, cyy + cr], fill=(230, 240, 250))
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(120, 190, 255), width=max(2, r // 40))


def draw_satellite(d, cx, cy, s):
    panel_w, panel_h = int(150 * s), int(86 * s)
    for sign in (-1, 1):
        px = cx + sign * int(112 * s)
        py = cy
        d.rectangle(
            [px - panel_w // 2, py - panel_h // 2, px + panel_w // 2, py + panel_h // 2],
            fill=(240, 190, 60),
            outline=(170, 120, 30),
            width=max(1, int(3 * s)),
        )
        for gx in range(-2, 3):
            x = px + gx * (panel_w // 4)
            d.line([(x, py - panel_h // 2), (x, py + panel_h // 2)], fill=(170, 120, 30), width=max(1, int(2 * s)))
        for gy in range(-1, 2):
            y = py + gy * (panel_h // 2)
            d.line([(px - panel_w // 2, y), (px + panel_w // 2, y)], fill=(170, 120, 30), width=max(1, int(2 * s)))
    body_w, body_h = int(96 * s), int(64 * s)
    d.rounded_rectangle(
        [cx - body_w // 2, cy - body_h // 2, cx + body_w // 2, cy + body_h // 2],
        radius=int(14 * s),
        fill=(235, 238, 245),
        outline=(120, 125, 135),
        width=max(1, int(3 * s)),
    )
    d.rectangle(
        [cx - int(6 * s), cy - body_h // 2 + int(6 * s), cx + int(6 * s), cy + body_h // 2 - int(6 * s)],
        fill=(90, 140, 220),
    )
    dish_cx = cx - int(40 * s)
    dish_cy = cy - int(56 * s)
    d.ellipse(
        [dish_cx - int(26 * s), dish_cy - int(26 * s), dish_cx + int(26 * s), dish_cy + int(26 * s)],
        fill=(200, 205, 215),
        outline=(120, 125, 135),
        width=max(1, int(3 * s)),
    )
    d.ellipse(
        [dish_cx - int(10 * s), dish_cy - int(10 * s), dish_cx + int(10 * s), dish_cy + int(10 * s)],
        fill=(150, 160, 175),
    )
    d.line(
        [(cx - int(24 * s), cy - int(22 * s)), (dish_cx + int(8 * s), dish_cy + int(8 * s))],
        fill=(120, 125, 135), width=max(1, int(4 * s)),
    )


def render_satellite_square(size):
    w = h = size
    img = vgrad((w, h), (8, 16, 48), (24, 52, 110))
    d = ImageDraw.Draw(img)
    draw_stars(d, w, h, int(w * 0.06))
    ring = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    rd.arc(
        [w * 0.02, h * 0.12, w * 0.98, h * 0.88],
        start=210, end=360,
        fill=(150, 200, 255, 120), width=max(2, w // 90),
    )
    img = Image.alpha_composite(img.convert("RGBA"), ring).convert("RGB")
    d = ImageDraw.Draw(img)
    r = int(size * 0.30)
    draw_earth(d, int(w * 0.30), int(h * 0.88), r)
    draw_satellite(d, int(w * 0.62), int(h * 0.44), size / 1080)
    return img


def render_satellite_wide(size):
    w, h = size
    img = vgrad((w, h), (8, 16, 48), (24, 52, 110))
    d = ImageDraw.Draw(img)
    draw_stars(d, w, h, int(w * 0.05))
    r = int(h * 0.62)
    draw_earth(d, int(w * 0.80), int(h * 1.35), r)
    draw_satellite(d, int(w * 0.35), int(h * 0.50), size[0] / 1240)
    return img


# ---------------------------------------------------------------------------
# Airplane artwork (sky scene)
# ---------------------------------------------------------------------------

def draw_cloud(d, cx, cy, s, shade=255):
    d.ellipse([cx - 40 * s, cy - 18 * s, cx + 40 * s, cy + 18 * s], fill=(shade, shade, shade))
    d.ellipse([cx - 24 * s, cy - 30 * s, cx + 24 * s, cy + 6 * s], fill=(shade, shade, shade))
    d.ellipse([cx - 2 * s, cy - 24 * s, cx + 46 * s, cy + 8 * s], fill=(shade, shade, shade))
    d.ellipse([cx - 46 * s, cy - 22 * s, cx + 2 * s, cy + 8 * s], fill=(shade, shade, shade))


def draw_airplane(img, cx, cy, s, angle_deg=-12):
    """Draw a stylized airplane rotated by angle_deg (nose up-right)."""
    d = ImageDraw.Draw(img)
    L = 420 * s
    pad = 120 * s
    canvas = Image.new("RGBA", (int(L + pad * 2), int(L + pad * 2)), (0, 0, 0, 0))
    cd = ImageDraw.Draw(canvas)
    ox = (L + pad * 2) / 2
    oy = (L + pad * 2) / 2
    body_len, body_h = 320 * s, 52 * s
    cd.rounded_rectangle(
        [ox - body_len / 2, oy - body_h / 2, ox + body_len / 2, oy + body_h / 2],
        radius=int(26 * s),
        fill=(250, 250, 252),
        outline=(150, 160, 175),
        width=max(1, int(3 * s)),
    )
    cd.polygon(
        [(ox + body_len / 2 - 10 * s, oy - body_h / 2 + 4 * s),
         (ox + body_len / 2 + 30 * s, oy),
         (ox + body_len / 2 - 10 * s, oy + body_h / 2 - 4 * s)],
        fill=(70, 130, 200),
    )
    cd.polygon(
        [(ox - body_len / 2 + 6 * s, oy - body_h / 2),
         (ox - body_len / 2 - 8 * s, oy - body_h / 2 - 46 * s),
         (ox - body_len / 2 + 34 * s, oy - body_h / 2)],
        fill=(210, 60, 70),
    )
    cd.polygon(
        [(ox - 30 * s, oy - 6 * s),
         (ox - 130 * s, oy - 92 * s),
         (ox - 78 * s, oy - 96 * s),
         (ox + 10 * s, oy - 12 * s)],
        fill=(240, 242, 246),
        outline=(150, 160, 175),
    )
    cd.polygon(
        [(ox - 30 * s, oy + 6 * s),
         (ox - 130 * s, oy + 92 * s),
         (ox - 78 * s, oy + 96 * s),
         (ox + 10 * s, oy + 12 * s)],
        fill=(240, 242, 246),
        outline=(150, 160, 175),
    )
    for i in range(6):
        wx = ox - 40 * s + i * 34 * s
        cd.ellipse([wx - 7 * s, oy - 20 * s, wx + 7 * s, oy - 8 * s], fill=(90, 150, 220))
    canvas = canvas.rotate(angle_deg, resample=Image.BICUBIC, center=(ox, oy))
    img.paste(canvas, (int(cx - (L + pad * 2) / 2), int(cy - (L + pad * 2) / 2)), canvas)


def render_airplane_square(size):
    w = h = size
    img = vgrad((w, h), (70, 150, 220), (165, 220, 250))
    d = ImageDraw.Draw(img)
    d.ellipse([w * 0.70, h * 0.10, w * 0.84, h * 0.24], fill=(255, 235, 150))
    d.ellipse([w * 0.72, h * 0.12, w * 0.82, h * 0.22], fill=(255, 245, 190))
    draw_cloud(d, w * 0.18, h * 0.78, size / 700)
    draw_cloud(d, w * 0.80, h * 0.86, size / 900)
    draw_cloud(d, w * 0.50, h * 0.92, size / 800)
    draw_airplane(img, w * 0.50, h * 0.48, size / 1080, angle_deg=-12)
    return img


def render_airplane_wide(size):
    w, h = size
    img = vgrad((w, h), (70, 150, 220), (165, 220, 250))
    d = ImageDraw.Draw(img)
    d.ellipse([w * 0.86, h * 0.06, w * 0.94, h * 0.18], fill=(255, 235, 150))
    draw_cloud(d, w * 0.12, h * 0.85, h / 300)
    draw_cloud(d, w * 0.85, h * 0.92, h / 340)
    draw_cloud(d, w * 0.50, h * 0.95, h / 380)
    draw_airplane(img, w * 0.50, h * 0.52, size[0] / 1240, angle_deg=-8)
    return img


# ---------------------------------------------------------------------------
# Output targets
# ---------------------------------------------------------------------------

SQUARE_TARGETS = [
    ("Square150x150Logo.png", 150, 150),
    ("Square150x150Logo.scale-200.png", 300, 300),
    ("Square44x44Logo.png", 44, 44),
    ("Square44x44Logo.scale-200.png", 88, 88),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
    ("StoreLogo.png", 50, 50),
    ("LockScreenLogo.scale-200.png", 48, 48),
]

WIDE_TARGETS = [
    ("Wide310x150Logo.png", 310, 150),
    ("Wide310x150Logo.scale-200.png", 620, 300),
    ("SplashScreen.png", 620, 300),
    ("SplashScreen.scale-200.png", 1240, 600),
]

MASTER = 1080


def main():
    os.makedirs(ASSETS_DIR, exist_ok=True)
    print(f"Writing {THEME} artwork to: {ASSETS_DIR}\n")

    if THEME == "airplane":
        square = render_airplane_square(MASTER)
        wide = render_airplane_wide((1240, 600))
    else:
        square = render_satellite_square(MASTER)
        wide = render_satellite_wide((1240, 600))

    for targets in (SQUARE_TARGETS, WIDE_TARGETS):
        src = square if targets is SQUARE_TARGETS else wide
        for filename, tw, th in targets:
            dest = os.path.join(ASSETS_DIR, filename)
            out = downsample(src, (tw, th))
            out.save(dest)
            print(f"  wrote {filename}  ({tw}x{th})")

    print("\nDone.")


if __name__ == "__main__":
    main()
