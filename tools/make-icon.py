"""Draws the ScreenCapture icon (matte black rounded square, white crosshair + hollow circle, red-orange dot)
as vector art with supersampling and writes multi-size .ico files.

  python3 tools/make-icon.py            -> app.ico (normal) and app_busy.ico (OCR-busy tray variant)

Small sizes (16-32) use hand-tuned, thicker strokes and a larger circle/dot so they stay legible.
The busy variant tints the crosshair amber and turns the corner dot amber, so it is distinct from the red dot.
"""
import io, struct, sys
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SS = 8  # supersampling factor

BG = (26, 26, 26, 255)          # #1A1A1A
EDGE = (255, 255, 255, 40)      # faint rim so the square reads on dark taskbars
WHITE = (255, 255, 255, 255)
RED = (219, 58, 46, 255)        # #DB3A2E
AMBER = (255, 184, 28, 255)     # #FFB81C (busy)

# size: margin, radius, stroke, arm (center->end), gap (center->arm start), circle r, circle stroke, dot r, dot center (fraction of size)
P = {
    16:  (0.0, 3.2, 0, 0, 0, 0, 0, 1.45, (0.8125, 0.1875)),   # crosshair is pixel-drawn (PIXEL_CROSS)
    20:  (0.0, 4.0, 0, 0, 0, 0, 0, 1.9, (0.8, 0.2)),
    24:  (0.5, 4.8, 2.00, 9.0, 4.0, 2.8, 1.40, 2.6, (0.785, 0.215)),
    32:  (1.0, 6.4, 2.00, 11.0, 5.0, 3.5, 1.70, 3.2, (0.785, 0.215)),
    40:  (1.0, 8.0, 2.10, 12.8, 5.6, 3.9, 1.80, 3.6, (0.795, 0.205)),
    48:  (1.5, 9.6, 2.30, 14.8, 6.2, 4.3, 1.90, 4.1, (0.800, 0.200)),
    64:  (2.0, 12.8, 2.60, 19.0, 7.6, 5.2, 2.20, 5.0, (0.805, 0.195)),
    256: (8.0, 52.0, 4.20, 62.0, 17.0, 10.5, 3.80, 12.5, (0.805, 0.195)),
}


# Hand-placed 1px crosshair for the smallest sizes (center pixel, ring half-size, arm start, arm end offsets).
PIXEL_CROSS = {16: (7, 1, 3, 5), 20: (9, 2, 4, 7)}


def pixel_cross(img, size, fg):
    c, ring, a0, a1 = PIXEL_CROSS[size]
    px = img.load()
    # ring: square outline of half-size `ring` with the corners dropped when ring > 1 (reads as a circle)
    for x in range(c - ring, c + ring + 1):
        for y in range(c - ring, c + ring + 1):
            edge = max(abs(x - c), abs(y - c)) == ring
            corner = ring > 1 and abs(x - c) == ring and abs(y - c) == ring
            if edge and not corner:
                px[x, y] = fg
    for o in range(a0, a1 + 1):
        for x, y in ((c - o, c), (c + o, c), (c, c - o), (c, c + o)):
            px[x, y] = fg


def frame(size, busy=False):
    m, rad, sw, arm, gap, cr, csw, dr, (dx, dy) = P[size]
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = SS
    box = [m * k, m * k, (size - m) * k - 1, (size - m) * k - 1]
    d.rounded_rectangle(box, radius=rad * k, fill=BG)
    if size <= 64:
        d.rounded_rectangle(box, radius=rad * k, outline=EDGE, width=max(1, int(0.8 * k)))
    fg = AMBER if busy else WHITE
    dcx, dcy = dx * size * k, dy * size * k
    d.ellipse([dcx - dr * k, dcy - dr * k, dcx + dr * k, dcy + dr * k], fill=AMBER if busy else RED)
    if size in PIXEL_CROSS:
        small = img.resize((size, size), Image.LANCZOS)
        pixel_cross(small, size, fg)
        return small
    c = size / 2 * k
    h = sw * k / 2
    # four arms (rectangles, so ends are square and crisp)
    d.rectangle([c - arm * k, c - h, c - gap * k, c + h], fill=fg)
    d.rectangle([c + gap * k, c - h, c + arm * k, c + h], fill=fg)
    d.rectangle([c - h, c - arm * k, c + h, c - gap * k], fill=fg)
    d.rectangle([c - h, c + gap * k, c + h, c + arm * k], fill=fg)
    d.ellipse([c - cr * k, c - cr * k, c + cr * k, c + cr * k], outline=fg, width=max(1, round(csw * k)))
    return img.resize((size, size), Image.LANCZOS)


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
