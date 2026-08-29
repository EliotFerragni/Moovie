#!/usr/bin/env python3
"""Builds the app icon set from one master image.

    python3 build/make-icons.py path/to/source.png

Writes into src/Moovie.App/Assets/Icon/:

    app.png    the square master, 1024, the source everything else comes from
    app.ico    Windows, 16 through 256, embedded in the executable
    app.icns   macOS, copied into the .app bundle by publish.sh
    app-256.png  what the window and taskbar use at runtime

Committing the outputs keeps the build free of an image dependency; committing
this script keeps them reproducible, so a new master is one command rather than
a set of binaries somebody has to take on trust.

Needs Pillow (pip install Pillow). Nothing else in the build does, which is why
this runs by hand rather than as part of publish.sh.
"""

import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src/Moovie.App/Assets/Icon"

MASTER = 1024
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]

# Below this, the artwork is cropped to its tile and the outer glow is dropped. The
# glow is most of the charm at 256 and pure overhead at 16, where it spends a ring of
# pixels on light and leaves the cow too small to read. Trimming it buys about a tenth
# more width for the drawing, which is the difference between horns and a smudge.
GLOW_CUTOFF = 64

# macOS reads whichever of these it wants for a given slot and scale factor. The
# four-character types are the format's own; the sizes are not negotiable.
ICNS_CHUNKS = [
    ("icp4", 16),
    ("icp5", 32),
    ("ic07", 128),
    ("ic08", 256),
    ("ic09", 512),
    ("ic10", 1024),
    ("ic11", 32),
    ("ic12", 64),
    ("ic13", 256),
    ("ic14", 512),
]


def square(image: Image.Image) -> Image.Image:
    """Trims the transparent border, then centres what is left on a square canvas."""
    image = image.convert("RGBA")

    bounds = image.getbbox()
    if bounds:
        image = image.crop(bounds)

    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))
    return canvas.resize((MASTER, MASTER), Image.LANCZOS)


def tighten(master: Image.Image) -> Image.Image:
    """The same artwork with the glow halo trimmed off, re-squared."""
    solid = master.getchannel("A").point(lambda v: 255 if v >= 200 else 0).getbbox()
    if not solid:
        return master

    cropped = master.crop(solid)
    side = max(cropped.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(cropped, ((side - cropped.width) // 2, (side - cropped.height) // 2))
    return canvas


def at(master: Image.Image, tight: Image.Image, size: int) -> Image.Image:
    source = tight if size <= GLOW_CUTOFF else master
    return source.resize((size, size), Image.LANCZOS)


def png_bytes(master: Image.Image, tight: Image.Image, size: int) -> bytes:
    from io import BytesIO

    buffer = BytesIO()
    at(master, tight, size).save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def write_icns(master: Image.Image, tight: Image.Image, path: Path) -> None:
    """
    Assembles the container by hand: 'icns', the total length, then one typed
    chunk per size. Pillow only writes this format on macOS, and the format is
    simple enough that depending on the host OS for it would be silly.
    """
    body = b""
    for kind, size in ICNS_CHUNKS:
        data = png_bytes(master, tight, size)
        body += kind.encode("ascii") + struct.pack(">I", len(data) + 8) + data

    path.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    source = Path(sys.argv[1])
    if not source.exists():
        print(f"no such file: {source}")
        return 1

    OUT.mkdir(parents=True, exist_ok=True)
    master = square(Image.open(source))
    tight = tighten(master)

    master.save(OUT / "app.png", format="PNG", optimize=True)
    at(master, tight, 256).save(OUT / "app-256.png", format="PNG", optimize=True)

    # Pillow resamples every entry off one image, so the per-size crop has to be baked
    # in by appending the variants rather than handing it a list of sizes.
    small = [at(master, tight, s) for s in ICO_SIZES if s <= GLOW_CUTOFF]
    large = [at(master, tight, s) for s in ICO_SIZES if s > GLOW_CUTOFF]
    large[-1].save(
        OUT / "app.ico",
        format="ICO",
        sizes=[(s, s) for s in ICO_SIZES],
        append_images=small + large[:-1],
    )
    write_icns(master, tight, OUT / "app.icns")

    for name in ("app.png", "app-256.png", "app.ico", "app.icns"):
        written = OUT / name
        print(f"  {written.relative_to(ROOT)}  {written.stat().st_size // 1024} kB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
