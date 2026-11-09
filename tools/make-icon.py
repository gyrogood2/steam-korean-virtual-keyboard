"""Generates app.ico (multi-size) for Steam Korean Virtual Keyboard.

Design: a Steam Controller silhouette with a keyboard in front of it, on a
black keycap. Tray sizes (<= 32 px) drop the small details so the shapes stay
readable at 16 px.

Run: python tools/make-icon.py
"""

import os
import struct
from io import BytesIO

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "app.ico")
PREVIEW = os.path.join(ROOT, "tools", "icon-preview.png")

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 4
TRAY_MAX = 32

BG_TOP = (28, 30, 34)
BG_BOTTOM = (4, 5, 7)
BODY_TOP = (244, 247, 250)
BODY_BOTTOM = (186, 195, 204)
KEYBOARD_TOP = (236, 242, 248)
KEYBOARD_BOTTOM = (176, 186, 196)
KEY_FILL = (22, 25, 30)
RIM = (58, 64, 72)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def vertical_gradient(size, top, bottom, alpha_top=255, alpha_bottom=255):
    column = Image.new("RGBA", (1, size))
    pixels = column.load()
    for y in range(size):
        t = y / max(1, size - 1)
        alpha = int(round(alpha_top + (alpha_bottom - alpha_top) * t))
        pixels[0, y] = lerp(top, bottom, t) + (alpha,)
    return column.resize((size, size), Image.BILINEAR)


def smooth_min(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def shell_mask(s):
    """Round controller shell: an oval body, two grips, and a bottom arch."""
    yy, xx = np.mgrid[0:s, 0:s]
    x = (xx + 0.5) / s
    y = (yy + 0.5) / s

    body = (
        np.abs((x - 0.50) / 0.44) ** 2.7 + np.abs((y - 0.40) / 0.38) ** 2.7
    ) ** (1.0 / 2.7) - 1.0
    body *= 0.32
    arch = np.sqrt((x - 0.50) ** 2 + (y - 0.844) ** 2) - 0.224

    distance = -smooth_min(-body, arch, 0.05)

    return Image.fromarray(np.where(distance < 0, 255, 0).astype(np.uint8), "L")


def controller_mask(s, tray):
    """Steam Controller (2026) silhouette: winged body with grips, D-pad and
    ABXY on top, two sticks, two square trackpads below."""
    mask = shell_mask(s)
    draw = ImageDraw.Draw(mask)

    def circle_ring(cx, cy, outer, inner):
        draw.ellipse(
            ((cx - outer) * s, (cy - outer) * s, (cx + outer) * s, (cy + outer) * s), fill=0
        )
        draw.ellipse(
            ((cx - inner) * s, (cy - inner) * s, (cx + inner) * s, (cy + inner) * s), fill=255
        )

    def square_ring(cx, cy, outer, inner, angle):
        for half, fill in ((outer, 0), (inner, 255)):
            size = int(round(2 * half * s)) + 8
            layer = Image.new("L", (size, size), 0)
            ImageDraw.Draw(layer).rounded_rectangle(
                (4, 4, size - 5, size - 5), radius=half * 0.28 * s, fill=255
            )
            if angle:
                layer = layer.rotate(angle, resample=Image.BICUBIC)
            mask.paste(
                fill,
                (int(round(cx * s - size / 2)), int(round(cy * s - size / 2))),
                layer,
            )

    # Sticks and trackpads.
    if tray:
        for cx in (0.345, 0.655):
            circle_ring(cx, 0.285, 0.090, 0.052)
    else:
        for cx in (0.345, 0.655):
            circle_ring(cx, 0.275, 0.062, 0.037)
        square_ring(0.345, 0.470, 0.090, 0.064, -7)
        square_ring(0.655, 0.470, 0.090, 0.064, 7)

    if not tray:
        # D-pad.
        dx, dy = 0.190 * s, 0.205 * s
        arm, thick = 0.058 * s, 0.021 * s
        draw.rounded_rectangle(
            (dx - arm, dy - thick, dx + arm, dy + thick), radius=thick, fill=0
        )
        draw.rounded_rectangle(
            (dx - thick, dy - arm, dx + thick, dy + arm), radius=thick, fill=0
        )

        # ABXY diamond.
        ax, ay = 0.810 * s, 0.205 * s
        offset, r = 0.044 * s, 0.018 * s
        for ox, oy in ((-offset, 0), (offset, 0), (0, -offset), (0, offset)):
            draw.ellipse((ax + ox - r, ay + oy - r, ax + ox + r, ay + oy + r), fill=0)

        # Menu pill between the trackpads.
        pw, ph = 0.026 * s, 0.008 * s
        px, py = 0.500 * s, 0.480 * s
        draw.rounded_rectangle((px - pw, py - ph, px + pw, py + ph), radius=ph, fill=0)

    return mask


def keyboard_box(s):
    return (0.285 * s, 0.605 * s, 0.715 * s, 0.845 * s)


def render(size):
    s = size * SUPERSAMPLE
    tray = size <= TRAY_MAX
    radius = 0.215 * s

    cap = Image.new("L", (s, s), 0)
    ImageDraw.Draw(cap).rounded_rectangle((0, 0, s - 1, s - 1), radius=radius, fill=255)

    base = vertical_gradient(s, BG_TOP, BG_BOTTOM)
    content = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Controller body.
    body = controller_mask(s, tray)
    body_color = vertical_gradient(s, BODY_TOP, BODY_BOTTOM)
    content = Image.alpha_composite(
        content, Image.composite(body_color, Image.new("RGBA", (s, s), (0, 0, 0, 0)), body)
    )

    # Keyboard sits in front, separated by a gap of background colour.
    kb = Image.new("L", (s, s), 0)
    ImageDraw.Draw(kb).rounded_rectangle(keyboard_box(s), radius=0.075 * s, fill=255)

    gap = 0.030 * s if tray else 0.022 * s
    halo = kb.filter(ImageFilter.MaxFilter(int(gap) * 2 + 1))
    content = Image.alpha_composite(
        content,
        Image.composite(Image.new("RGBA", (s, s), (0, 0, 0, 255)), Image.new("RGBA", (s, s), (0, 0, 0, 0)), halo),
    )

    kb_color = vertical_gradient(s, KEYBOARD_TOP, KEYBOARD_BOTTOM)
    content = Image.alpha_composite(
        content, Image.composite(kb_color, Image.new("RGBA", (s, s), (0, 0, 0, 0)), kb)
    )

    # Keycaps: six large keys at tray sizes, a full grid at large sizes.
    keys = Image.new("L", (s, s), 0)
    kd = ImageDraw.Draw(keys)
    left, top, right, bottom = keyboard_box(s)
    margin_x, margin_y = 0.055 * s, 0.060 * s
    width = (right - left) - 2 * margin_x
    height = (bottom - top) - 2 * margin_y

    columns, rows = (3, 2) if tray else (6, 3)
    gap_x, gap_y = (0.034 * s, 0.040 * s) if tray else (0.018 * s, 0.022 * s)
    key_w = (width - (columns - 1) * gap_x) / columns
    key_h = (height - (rows - 1) * gap_y) / rows

    for row in range(rows):
        for column in range(columns):
            x0 = left + margin_x + column * (key_w + gap_x)
            y0 = top + margin_y + row * (key_h + gap_y)
            kd.rounded_rectangle(
                (x0, y0, x0 + key_w, y0 + key_h),
                radius=min(key_w, key_h) * 0.35,
                fill=255,
            )

    content = Image.alpha_composite(
        content, Image.composite(Image.new("RGBA", (s, s), KEY_FILL + (255,)), Image.new("RGBA", (s, s), (0, 0, 0, 0)), keys)
    )

    # Centre the artwork vertically inside the tile.
    shift = int(round(0.0675 * s))
    content = content.transform((s, s), Image.AFFINE, (1, 0, 0, 0, 1, -shift), resample=Image.BILINEAR)
    base = Image.alpha_composite(base, content)

    rim = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle(
        (0, 0, s - 1, s - 1),
        radius=radius,
        outline=RIM + (150,),
        width=max(1, int(round(0.012 * s))),
    )
    base = Image.alpha_composite(base, rim)

    return base.resize((size, size), Image.LANCZOS)


def dib_bytes(image):
    """32bpp bottom-up DIB with an opaque AND mask, as stored inside .ico."""
    width, height = image.size
    pixels = image.convert("RGBA").load()

    xor = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            xor += bytes((b, g, r, a))

    row_bytes = ((width + 31) // 32) * 4
    mask = bytearray(row_bytes * height)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,           # biSize
        width,        # biWidth
        height * 2,   # biHeight (XOR + AND)
        1,            # biPlanes
        32,           # biBitCount
        0,            # biCompression
        len(xor) + len(mask),
        0, 0, 0, 0,
    )
    return header + bytes(xor) + bytes(mask)


def png_bytes(image):
    buffer = BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def build_ico(images):
    entries = []
    for size, image in images:
        entries.append((size, png_bytes(image) if size >= 128 else dib_bytes(image)))

    offset = 6 + 16 * len(entries)
    directory = b""
    payload = b""
    for size, data in entries:
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0, 0, 1, 32,
            len(data),
            offset + len(payload),
        )
        payload += data

    with open(OUT, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, len(entries)) + directory + payload)


def build_preview(images):
    sheet = Image.new("RGBA", (900, 250), (27, 40, 56, 255))
    lookup = dict(images)

    x = 16
    for size in (16, 24, 32, 48, 64, 128, 256):
        sheet.alpha_composite(lookup[size], (x, 58 - size // 2))
        x += size + 20

    x = 16
    for size, factor in ((16, 6), (24, 4), (32, 3)):
        zoom = lookup[size].resize((size * factor, size * factor), Image.NEAREST)
        sheet.alpha_composite(zoom, (x, 200 - zoom.size[1] // 2))
        x += zoom.size[0] + 20

    sheet.save(PREVIEW)


def main():
    images = [(size, render(size)) for size in SIZES]
    build_ico(images)
    build_preview(images)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
