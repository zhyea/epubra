"""Generate Epubra app icon (.ico with multi-resolution PNG frames).

Design (faithful to the reference):
  - White canvas background
  - Black rounded-rectangle outer frame
  - Cyan (#03DCFF) book body with a black corner fold at top-left
  - Cyan pen at the top-right, angled into the book
  - Black horizontal lines (two) on the book middle
  - Black "EPUB" wordmark at the bottom of the book
"""

from __future__ import annotations

import os
from PIL import Image, ImageDraw, ImageFont

# --- Palette ---
BG_OUTER = (1, 1, 1)            # black rounded outer frame
BG_CORNER_FOLD = (1, 1, 1)      # black corner fold
BG_LINES = (1, 1, 1)            # black horizontal lines / text
CYAN = (3, 220, 255)            # primary cyan  (sampled from reference)
CYAN_DEEP = (1, 200, 240)       # slight shadow for pen body
WHITE = (255, 255, 255)         # canvas


def _lerp(a: int, b: int, t: float) -> int:
    return int(round(a + (b - a) * t))


def _rounded_rect(draw: ImageDraw.ImageDraw, xy, radius: float, fill, outline=None, width: int = 1):
    draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)


def _draw_book(draw: ImageDraw.ImageDraw, S: int, ox: int = 0, oy: int = 0):
    """Cyan book with black top-left corner fold, plus black lines and EPUB wordmark.

    S is the target icon size. The book is centered on the cropped window
    so it stays proportional in the final icon regardless of the larger
    underlying canvas.
    """
    # Book rect relative to the target size S (not the canvas SC).
    pad_x = round(S * 0.18)
    pad_top = round(S * 0.13)
    pad_bot = round(S * 0.10)
    book_box = (
        pad_x + ox,
        pad_top + oy,
        S - pad_x + ox,
        S - pad_bot + oy,
    )
    bx0, by0, bx1, by1 = book_box
    bw, bh = bx1 - bx0, by1 - by0

    radius = round(S * 0.04)

    # 1) Book body (cyan) with rounded corners
    _rounded_rect(draw, book_box, radius=radius, fill=CYAN)

    # 2) Top-left corner fold — black triangle in the top-left corner
    fold = round(S * 0.085)
    fold_pts = [
        (bx0, by0),
        (bx0 + fold, by0),
        (bx0, by0 + fold),
    ]
    draw.polygon(fold_pts, fill=BG_CORNER_FOLD)
    inner_inset = max(2, round(S * 0.012))
    inner_pts = [
        (bx0 + inner_inset, by0 + inner_inset),
        (bx0 + fold, by0),
        (bx0, by0 + fold),
    ]
    draw.polygon(inner_pts, fill=CYAN)

    # 3) Two black horizontal lines
    line_y1 = by0 + round(bh * 0.42)
    line_y2 = by0 + round(bh * 0.53)
    line_x1 = bx0 + round(bw * 0.18)
    line_x2 = bx1 - round(bw * 0.18)
    line_w = max(2, round(S * 0.018))
    draw.rounded_rectangle(
        (line_x1, line_y1 - line_w // 2, line_x2, line_y1 + line_w // 2),
        radius=line_w // 2, fill=BG_LINES,
    )
    draw.rounded_rectangle(
        (line_x1, line_y2 - line_w // 2, line_x2, line_y2 + line_w // 2),
        radius=line_w // 2, fill=BG_LINES,
    )

    # 4) "EPUB" wordmark — bold, centered, black
    font_size = max(10, round(bh * 0.22))
    font = _load_bold_font(font_size)
    txt = "EPUB"
    try:
        bbox = draw.textbbox((0, 0), txt, font=font)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        tx = bx0 + (bw - tw) // 2 - bbox[0]
        ty = by0 + round(bh * 0.66) - bbox[1]
    except AttributeError:
        tw, th = draw.textsize(txt, font=font)
        tx = bx0 + (bw - tw) // 2
        ty = by0 + round(bh * 0.66)
    draw.text((tx, ty), txt, font=font, fill=BG_LINES)


def _load_bold_font(size: int):
    """Find a bold sans-serif font that exists on the system."""
    candidates = [
        r"C:\Windows\Fonts\segoeuib.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
        r"C:\Windows\Fonts\calibrib.ttf",
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    ]
    for c in candidates:
        if os.path.exists(c):
            return ImageFont.truetype(c, size)
    return ImageFont.load_default()


def _pen_geometry(S: int, offset=(0, 0)):
    """Return all pen geometry points as a dict.

    Coordinate system: tip at (tip_x, tip_y), tail extends up-right.
    Length/width scale with S so the icon scales proportionally.
    The tip sits inside the book (so the nib is visible on top); the tail
    (with the black cap) exits through the book's top edge into the dark
    frame area, so the cap appears as a small black block at the top-right.
    """
    import math

    ox, oy = offset
    tip_x = S * 0.50 + ox
    tip_y = S * 0.40 + oy

    length = round(S * 0.45)
    width = round(S * 0.060)

    angle = math.radians(-55)
    ex, ey = math.cos(angle), math.sin(angle)
    tail_x = tip_x + ex * length
    tail_y = tip_y + ey * length
    px, py = -ey, ex
    w = width / 2

    nib_len = round(S * 0.08)
    cap_len = round(S * 0.10)
    fer_len = round(S * 0.022)

    return dict(
        tip=(tip_x, tip_y), tail=(tail_x, tail_y),
        unit=(ex, ey), perp=(px, py), w=w,
        nib_len=nib_len, cap_len=cap_len, fer_len=fer_len,
        length=length, width=width,
    )


def _draw_pen_nib(draw: ImageDraw.ImageDraw, g: dict):
    """Dark-cyan nib triangle at the writing tip (drawn on top of the book)."""
    tip_x, tip_y = g["tip"]
    ex, ey = g["unit"]
    nw = g["width"] * 0.45
    nib_len = g["nib_len"]
    nx1, ny1 = tip_x + ex * nib_len, tip_y + ey * nib_len
    # Triangle pointing from tip outward (into the book body)
    p1 = (nx1 + (-ey) * nw, ny1 + ex * nw)
    p2 = (nx1 - (-ey) * nw, ny1 - ex * nw)
    p3 = (tip_x, tip_y)
    draw.polygon([p1, p2, p3], fill=CYAN_DEEP)


def _draw_pen_external(draw: ImageDraw.ImageDraw, g: dict):
    """Cyan pen body + black cap, drawn BEFORE the book so the book can
    cover the inner portion. Only the part outside the book stays visible."""
    tip_x, tip_y = g["tip"]
    tail_x, tail_y = g["tail"]
    ex, ey = g["unit"]
    px, py = g["perp"]
    w = g["w"]
    length = g["length"]

    # Body quad
    p1 = (tip_x + px * w, tip_y + py * w)
    p2 = (tip_x - px * w, tip_y - py * w)
    p3 = (tail_x - px * w, tail_y - py * w)
    p4 = (tail_x + px * w, tail_y + py * w)
    draw.polygon([p1, p2, p3, p4], fill=CYAN)

    # Ferrule (darker cyan band just before the cap)
    cap_len = g["cap_len"]
    fer_len = g["fer_len"]
    fer_x0 = tail_x + ex * (length - cap_len - fer_len)
    fer_y0 = tail_y + ey * (length - cap_len - fer_len)
    fer_x1 = tail_x + ex * (length - cap_len)
    fer_y1 = tail_y + ey * (length - cap_len)
    fp1 = (fer_x0 + px * w, fer_y0 + py * w)
    fp2 = (fer_x0 - px * w, fer_y0 - py * w)
    fp3 = (fer_x1 - px * w, fer_y1 - py * w)
    fp4 = (fer_x1 + px * w, fer_y1 + py * w)
    draw.polygon([fp1, fp2, fp3, fp4], fill=CYAN_DEEP)

    # Cap (black band at tail)
    cap_x0 = tail_x + ex * (length - cap_len)
    cap_y0 = tail_y + ey * (length - cap_len)
    cap_x1 = tail_x + ex * length
    cap_y1 = tail_y + ey * length
    cp1 = (cap_x0 + px * w, cap_y0 + py * w)
    cp2 = (cap_x0 - px * w, cap_y0 - py * w)
    cp3 = (cap_x1 - px * w, cap_y1 - py * w)
    cp4 = (cap_x1 + px * w, cap_y1 + py * w)
    draw.polygon([cp1, cp2, cp3, cp4], fill=BG_OUTER)


def render_icon(size: int) -> Image.Image:
    """Render the icon at the given pixel size. Returns an RGBA image.

    Draw order:
      1) Black rounded outer frame
      2) Pen body + ferrule + cap (drawn before the book)
      3) Cyan book with corner fold, lines, and EPUB wordmark
      4) Dark-cyan pen nib (drawn on top of the book for visibility)

    The pen is rendered first so the book can mask the portion of the pen
    that lies inside the book (same cyan, would otherwise be invisible).
    What remains visible is the nib (drawn on top) and the cap/eraser at
    the tail, which sits in the dark frame area at the top-right.

    Implementation note: the underlying canvas is 1.20x larger than the
    final size. The outer frame and book are drawn at *target* size
    coordinates so they stay proportional in the final icon.
    """
    SCALE = 1.30
    SC = round(size * SCALE)
    ox = (SC - size) // 2
    oy = (SC - size) // 2

    img = Image.new("RGBA", (SC, SC), WHITE + (255,))
    draw = ImageDraw.Draw(img)

    # 1) Outer black rounded frame
    margin = round(size * 0.025)
    frame_box = (ox + margin, oy + margin, ox + size - margin, oy + size - margin)
    _rounded_rect(
        draw,
        frame_box,
        radius=round(size * 0.20),
        fill=BG_OUTER,
    )

    # 2) Pen (drawn behind the book)
    geom = _pen_geometry(SC, offset=(ox, oy))
    _draw_pen_external(draw, geom)

    # 3) Book with corner fold, lines, and EPUB wordmark
    _draw_book(draw, size, ox=ox, oy=oy)

    # 4) Pen nib on top of the book
    _draw_pen_nib(draw, geom)

    # Crop center back to requested size
    return img.crop((ox, oy, ox + size, oy + size))


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    base_dir = r"D:\MyDevelop\JDevelop\workspace\epubra\src\Epubra.App\Assets"
    os.makedirs(base_dir, exist_ok=True)

    # Save a 1024x1024 master PNG (useful for store / marketing too)
    master = render_icon(1024)
    master.save(os.path.join(base_dir, "Epubra-1024.png"), "PNG")
    print("Saved master: Epubra-1024.png (1024x1024)")

    # Render each frame and pack into ICO
    frames = []
    for s in sizes:
        f = render_icon(s)
        frames.append(f)
        # Optional: save individual PNGs for inspection
        f.save(os.path.join(base_dir, f"Epubra-{s}.png"), "PNG")

    ico_path = os.path.join(base_dir, "Epubra.ico")
    # Pillow 12 ICO writer requires the base image to be at least the largest
    # target size; it will then auto-downscale per `sizes`.
    frames[-1].save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in sizes],
    )
    print(f"Saved icon: {ico_path} (sizes: {sizes})")
    print(f"  size on disk: {os.path.getsize(ico_path)} bytes")


if __name__ == "__main__":
    main()