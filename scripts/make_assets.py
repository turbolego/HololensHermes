"""
make_assets.py
==============
Generates and repairs PNG assets required by Package.appxmanifest for the
HoloLens 1 UWP app, using a master artwork image as the source.

The master image (`scripts/icon_master.jpg`) is the single source of truth
for the app icon artwork. Every asset (44px through 1240px) is derived from
it with LANCZOS resampling:

- Square targets: direct resize of the master.
- Wide targets (Wide310x150 / SplashScreen): center crop of the master to
  the target aspect ratio (2.07:1) so the subject stays framed, then resize.

Usage:
    python scripts/make_assets.py

Behavior:
- Rewrites assets whose dimensions are wrong.
- Generates both scale-200 names and common unscaled aliases.
- Also regenerates the Microsoft Store listing images (StoreListing/) from
  the same master.
"""

import os

from PIL import Image

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_DIR = os.path.dirname(SCRIPT_DIR)
ASSETS_DIR = os.path.join(REPO_DIR, "Assets")
STORE_DIR = os.path.join(REPO_DIR, "StoreListing")

MASTER_PATH = os.path.join(SCRIPT_DIR, "icon_master.jpg")

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def downsample(img, size):
    """LANCZOS down/up-scale to target size."""
    return img.resize(size, Image.LANCZOS)


def crop_wide(master, target_w, target_h):
    """Center-crop the square master to a wide aspect ratio (w:h)."""
    mw, mh = master.size
    target_ar = target_w / target_h
    crop_h = int(round(mw / target_ar))
    crop_h = min(crop_h, mh)
    # Bias the crop window toward the subject band (vertical center).
    top = max(0, (mh - crop_h) // 2)
    # Shift slightly up if the crop would cut the top of a tall subject.
    top = max(0, top - int(mh * 0.02))
    box = (0, top, mw, top + crop_h)
    return master.crop(box)


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

# Microsoft Store listing images (square boxes/tiles + 9:16 posters).
STORE_TARGETS = [
    ("1x1_Box_1080x1080.png", 1080, 1080),
    ("1x1_Box_2160x2160.png", 2160, 2160),
    ("1x1_Tile_71x71.png", 71, 71),
    ("1x1_Tile_150x150.png", 150, 150),
    ("1x1_Tile_300x300.png", 300, 300),
    ("9x16_Poster_720x1080.png", 720, 1080),
    ("9x16_Poster_1440x2160.png", 1440, 2160),
]


def main():
    if not os.path.exists(MASTER_PATH):
        raise FileNotFoundError(
            f"Master artwork not found: {MASTER_PATH}\n"
            "Place the app icon artwork at scripts/icon_master.jpg and re-run."
        )

    master = Image.open(MASTER_PATH).convert("RGB")
    print(f"Master artwork: {MASTER_PATH} ({master.size[0]}x{master.size[1]})")
    print(f"Writing assets to: {ASSETS_DIR} and {STORE_DIR}\n")

    os.makedirs(ASSETS_DIR, exist_ok=True)
    os.makedirs(STORE_DIR, exist_ok=True)

    # Square assets: direct resize.
    for filename, tw, th in SQUARE_TARGETS:
        out = downsample(master, (tw, th))
        out.save(os.path.join(ASSETS_DIR, filename))
        print(f"  Assets/{filename}  ({tw}x{th})")

    # Wide assets: aspect-crop then resize.
    for filename, tw, th in WIDE_TARGETS:
        cropped = crop_wide(master, tw, th)
        out = downsample(cropped, (tw, th))
        out.save(os.path.join(ASSETS_DIR, filename))
        print(f"  Assets/{filename}  ({tw}x{th}, crop {cropped.size[0]}x{cropped.size[1]})")

    # Store listing: reuse the same square/wide rendering.
    for filename, tw, th in STORE_TARGETS:
        if tw == th:
            out = downsample(master, (tw, th))
        else:
            # 9:16 poster — portrait, crop the master vertically.
            target_ar = tw / th  # 0.667
            mw, mh = master.size
            crop_w = int(round(mh * target_ar))
            crop_w = min(crop_w, mw)
            left = (mw - crop_w) // 2
            box = (left, 0, left + crop_w, mh)
            cropped = master.crop(box)
            out = downsample(cropped, (tw, th))
        out.save(os.path.join(STORE_DIR, filename))
        print(f"  StoreListing/{filename}  ({tw}x{th})")

    print("\nDone.")


if __name__ == "__main__":
    main()
