"""Draws the ScreenCapture icon (matte black rounded square, white crosshair + hollow circle, red-orange dot)
as vector art with supersampling and writes multi-size .ico files.

  python3 tools/make-icon.py            -> app.ico (normal), app_busy.ico (OCR-busy tray variant), docs/icon.png (README)

Every size (tray 16 px up to 256 px) is the same design, downscaled with LANCZOS, so it looks identical.
The busy variant tints the crosshair amber and turns the corner dot amber, so it is distinct from the red dot.
"""
import io, struct, sys
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SS = 8  # supersampling factor (master is 2048 px)

BG = (26, 26, 26, 255)          # #1A1A1A
WHITE = (255, 255, 255, 255)
RED = (219, 58, 46, 255)        # #DB3A2E
AMBER = (255, 184, 28, 255)     # #FFB81C (busy)

# One design, laid out on a 256 px grid: margin, corner radius, arm stroke, arm end and arm start (from center),
# center ring radius and stroke, dot radius, dot center (fraction of size). Every size is this design scaled down.
M, RAD, SW, ARM, GAP, CR, CSW, DR, (DX, DY) = 8.0, 52.0, 4.2, 62.0, 17.0, 10.5, 3.8, 12.5, (0.805, 0.195)


def master(busy=False):
    """The 256 px design drawn at 256*SS for clean anti-aliasing."""
    k = SS
    S = 256 * k
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([M * k, M * k, (256 - M) * k - 1, (256 - M) * k - 1], radius=RAD * k, fill=BG)
    fg = AMBER if busy else WHITE
    dcx, dcy = DX * S, DY * S
    d.ellipse([dcx - DR * k, dcy - DR * k, dcx + DR * k, dcy + DR * k], fill=fg if busy else RED)
    c = S / 2
    h = SW * k / 2
    # four separate arms (rectangles, so ends are square), leaving a wide gap around the ring
    d.rectangle([c - ARM * k, c - h, c - GAP * k, c + h], fill=fg)
    d.rectangle([c + GAP * k, c - h, c + ARM * k, c + h], fill=fg)
    d.rectangle([c - h, c - ARM * k, c + h, c - GAP * k], fill=fg)
    d.rectangle([c - h, c + GAP * k, c + h, c + ARM * k], fill=fg)
    d.ellipse([c - CR * k, c - CR * k, c + CR * k, c + CR * k], outline=fg, width=max(1, round(CSW * k)))
    return img


def frame(size, busy=False, _cache={}):
    """Same proportions at every size: high-quality downscale of the master (no per-size redesign)."""
    if busy not in _cache:
        _cache[busy] = master(busy)
    return _cache[busy].resize((size, size), Image.LANCZOS)


def dib(img):
    w, h = img.size
    px = img.tobytes("raw", "BGRA")
    rows = [px[y * w * 4:(y + 1) * w * 4] for y in range(h)][::-1]
    mask_row = ((w + 31) // 32) * 4
    hdr = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    return hdr + b"".join(rows) + b"\0" * (mask_row * h)


def write_ico(path, frames):
    blobs = []
    for f in frames:
        if f.size[0] >= 256:
            b = io.BytesIO(); f.save(b, "PNG"); blobs.append(b.getvalue())
        else:
            blobs.append(dib(f))
    out = struct.pack("<HHH", 0, 1, len(frames))
    off = 6 + 16 * len(frames)
    for f, b in zip(frames, blobs):
        w = f.size[0]
        out += struct.pack("<BBBBHHII", w % 256, w % 256, 0, 0, 1, 32, len(b), off)
        off += len(b)
    with open(path, "wb") as fh:
        fh.write(out + b"".join(blobs))


if __name__ == "__main__":
    for busy, name in ((False, "app.ico"), (True, "app_busy.ico")):
        write_ico(name, [frame(s, busy) for s in SIZES])
        print("wrote", name)
    frame(256).save("docs/icon.png")
    print("wrote docs/icon.png")
