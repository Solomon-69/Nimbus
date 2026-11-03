#!/usr/bin/env python3
"""Draws the Nimbus app icon (soft blue sky tile, warm sun peeking over a white cloud) as assets/Nimbus.ico.
Same picture as the Mac icon (make-icon.swift). Needs Pillow: pip install pillow. Run: python3 tools/make_ico.py"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # drawn big, then scaled down so the edges are smooth
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def sky_gradient():
    # top to bottom: deep blue, light blue, pale horizon (the Mac icon's three stops)
    stops = [(0.0, (79, 133, 219)), (0.62, (158, 204, 245)), (1.0, (214, 235, 250))]
    img = Image.new("RGB", (S, S))
    d = ImageDraw.Draw(img)
    for y in range(S):
        t = y / (S - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                k = (t - stops[i][0]) / (stops[i + 1][0] - stops[i][0])
                d.line([(0, y), (S, y)], fill=lerp(stops[i][1], stops[i + 1][1], k))
                break
    return img


def draw_icon():
    pad = round(S * 0.04)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=round(S * 0.225), fill=255)

    art = sky_gradient().convert("RGBA")
    d = ImageDraw.Draw(art)

    # sun with eight rays, upper right
    cx, cy, r = S * 0.62, S * 0.38, S * 0.15
    sun = (255, 204, 92, 255)
    for i in range(8):
        a = i * math.pi / 4
        d.line([(cx + math.cos(a) * (r + S * 0.045), cy + math.sin(a) * (r + S * 0.045)),
                (cx + math.cos(a) * (r + S * 0.11), cy + math.sin(a) * (r + S * 0.11))], fill=sun, width=round(S * 0.032))
        for end in (r + S * 0.045, r + S * 0.11):
            x, y = cx + math.cos(a) * end, cy + math.sin(a) * end
            d.ellipse([x - S * 0.016, y - S * 0.016, x + S * 0.016, y + S * 0.016], fill=sun)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=sun)

    # white cloud over the lower left (soft shadow first)
    def cloud(layer_draw, color):
        layer_draw.rounded_rectangle([S * 0.17, S * 0.58, S * 0.83, S * 0.76], radius=S * 0.09, fill=color)
        layer_draw.ellipse([S * 0.25, S * 0.43, S * 0.55, S * 0.73], fill=color)
        layer_draw.ellipse([S * 0.42, S * 0.36, S * 0.74, S * 0.70], fill=color)

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    cloud(ImageDraw.Draw(shadow), (40, 80, 140, 90))
    shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.018))
    art = Image.alpha_composite(art, Image.new("RGBA", (S, S), (0, 0, 0, 0)))
    shifted = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shifted.paste(shadow, (0, round(S * 0.02)))
    art = Image.alpha_composite(art, shifted)
    cloud(ImageDraw.Draw(art), (255, 255, 255, 255))

    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(art, (0, 0), mask)
    return out


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    target = os.path.join(here, "..", "assets")
    os.makedirs(target, exist_ok=True)
    icon = draw_icon()
    icon.resize((256, 256), Image.LANCZOS).save(os.path.join(target, "Nimbus-256.png"))
    icon.save(os.path.join(target, "Nimbus.ico"), format="ICO", sizes=[(s, s) for s in SIZES])
    print("wrote", os.path.join(target, "Nimbus.ico"))
