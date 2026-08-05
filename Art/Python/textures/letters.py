"""DUCK MOW - a tiny stroke-built display alphabet.

No system font is used anywhere in this pipeline: the Windows fonts on this machine
are not redistributable in a WebGL build, so every glyph baked into a texture is
drawn here from hand-authored strokes. The forms are chunky, round-capped and
slightly irregular - fairground signwriting, not a typeface.

Glyph space is the unit box, x right, y down.
"""
import numpy as np
from PIL import ImageDraw


# --------------------------------------------------------------------------------------
def catmull(pts, n=64, closed=False):
    """Catmull-Rom spline through the control points."""
    p = np.asarray(pts, dtype=np.float64)
    if closed:
        p = np.vstack([p[-1], p, p[0], p[1]])
    else:
        p = np.vstack([p[0] + (p[0] - p[1]) * 0.5, p, p[-1] + (p[-1] - p[-2]) * 0.5])
    out = []
    segs = len(p) - 3
    for i in range(segs):
        p0, p1, p2, p3 = p[i], p[i + 1], p[i + 2], p[i + 3]
        for k in range(n // segs + 1):
            t = k / (n // segs)
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t +
                              (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                              (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return [tuple(q) for q in out]


def arc(cx, cy, rx, ry, a0, a1, n=48):
    """Arc in degrees, measured anticlockwise from +x, in a y-down space."""
    a = np.radians(np.linspace(a0, a1, n))
    return [(cx + rx * np.cos(t), cy - ry * np.sin(t)) for t in a]


def _v(x0, y0, x1, y1, n=8):
    t = np.linspace(0, 1, n)
    return [(x0 + (x1 - x0) * s, y0 + (y1 - y0) * s) for s in t]


# --------------------------------------------------------------------------------------
# Each glyph is a list of strokes; each stroke is (points, width_scale).
GLYPHS = {}

GLYPHS["S"] = [(catmull([(0.80, 0.24), (0.58, 0.11), (0.28, 0.17), (0.26, 0.35),
                         (0.52, 0.47), (0.76, 0.58), (0.74, 0.80), (0.44, 0.90),
                         (0.20, 0.80)], 72), 1.0)]
GLYPHS["A"] = [(_v(0.50, 0.10, 0.13, 0.90), 1.0),
               (_v(0.50, 0.10, 0.87, 0.90), 1.0),
               (_v(0.265, 0.615, 0.735, 0.615), 0.82)]
GLYPHS["B"] = [(_v(0.27, 0.10, 0.27, 0.90), 1.0),
               (catmull([(0.27, 0.10), (0.62, 0.11), (0.73, 0.28), (0.58, 0.47),
                         (0.27, 0.50)], 44), 1.0),
               (catmull([(0.27, 0.50), (0.64, 0.52), (0.79, 0.68), (0.62, 0.87),
                         (0.27, 0.90)], 44), 1.0)]
GLYPHS["C"] = [(arc(0.52, 0.50, 0.33, 0.40, 52, 308, 60), 1.0)]
GLYPHS["D"] = [(_v(0.28, 0.10, 0.28, 0.90), 1.0),
               (catmull([(0.28, 0.10), (0.66, 0.14), (0.82, 0.40), (0.80, 0.62),
                         (0.62, 0.86), (0.28, 0.90)], 60), 1.0)]

GLYPHS["0"] = [(catmull([(0.50, 0.09), (0.78, 0.28), (0.78, 0.72), (0.50, 0.91),
                         (0.22, 0.72), (0.22, 0.28)], 72, closed=True), 1.0)]
GLYPHS["1"] = [(_v(0.50, 0.10, 0.50, 0.90), 1.0),
               (_v(0.50, 0.10, 0.26, 0.27), 0.85),
               (_v(0.28, 0.90, 0.74, 0.90), 0.85)]
GLYPHS["2"] = [(catmull([(0.20, 0.28), (0.34, 0.11), (0.66, 0.11), (0.76, 0.30),
                         (0.55, 0.53), (0.22, 0.88)], 60), 1.0),
               (_v(0.20, 0.89, 0.80, 0.89), 0.95)]
GLYPHS["3"] = [(catmull([(0.22, 0.20), (0.44, 0.09), (0.72, 0.17), (0.68, 0.36),
                         (0.46, 0.47)], 48), 1.0),
               (catmull([(0.46, 0.47), (0.76, 0.55), (0.78, 0.76), (0.54, 0.91),
                         (0.22, 0.83)], 52), 1.0)]
GLYPHS["4"] = [(_v(0.64, 0.10, 0.16, 0.66), 1.0),
               (_v(0.16, 0.66, 0.86, 0.66), 0.95),
               (_v(0.64, 0.10, 0.64, 0.90), 1.0)]
GLYPHS["5"] = [(_v(0.74, 0.11, 0.28, 0.11), 0.95),
               (_v(0.28, 0.11, 0.25, 0.44), 1.0),
               (catmull([(0.25, 0.44), (0.56, 0.39), (0.79, 0.53), (0.76, 0.76),
                         (0.50, 0.91), (0.21, 0.83)], 60), 1.0)]
GLYPHS["6"] = [(catmull([(0.72, 0.12), (0.42, 0.15), (0.24, 0.42), (0.23, 0.70),
                         (0.42, 0.90), (0.66, 0.88), (0.78, 0.70), (0.68, 0.52),
                         (0.42, 0.48), (0.26, 0.60)], 84), 1.0)]
GLYPHS["7"] = [(_v(0.18, 0.12, 0.82, 0.12), 1.0),
               (catmull([(0.82, 0.12), (0.66, 0.40), (0.50, 0.66), (0.42, 0.90)], 44), 1.0),
               (_v(0.36, 0.55, 0.68, 0.55), 0.7)]
GLYPHS["8"] = [(catmull([(0.50, 0.09), (0.73, 0.20), (0.70, 0.38), (0.50, 0.48),
                         (0.28, 0.38), (0.27, 0.20)], 56, closed=True), 1.0),
               (catmull([(0.50, 0.48), (0.79, 0.60), (0.76, 0.82), (0.50, 0.91),
                         (0.23, 0.82), (0.21, 0.60)], 60, closed=True), 1.0)]
GLYPHS["9"] = [(catmull([(0.30, 0.88), (0.60, 0.85), (0.77, 0.58), (0.78, 0.30),
                         (0.58, 0.10), (0.35, 0.13), (0.23, 0.30), (0.33, 0.47),
                         (0.60, 0.51), (0.75, 0.40)], 84), 1.0)]
GLYPHS["X"] = [(_v(0.20, 0.22, 0.80, 0.78), 1.0), (_v(0.80, 0.22, 0.20, 0.78), 1.0)]
GLYPHS["%"] = [(catmull([(0.28, 0.13), (0.40, 0.19), (0.34, 0.31), (0.22, 0.26)], 30,
                        closed=True), 0.72),
               (catmull([(0.72, 0.69), (0.84, 0.75), (0.78, 0.87), (0.66, 0.82)], 30,
                        closed=True), 0.72),
               (_v(0.80, 0.12, 0.24, 0.88), 0.85)]
GLYPHS["/"] = [(_v(0.74, 0.10, 0.30, 0.90), 0.9)]
GLYPHS["+"] = [(_v(0.50, 0.20, 0.50, 0.80), 0.9), (_v(0.22, 0.50, 0.78, 0.50), 0.9)]
GLYPHS["-"] = [(_v(0.22, 0.52, 0.78, 0.52), 0.9)]
GLYPHS["."] = [(catmull([(0.50, 0.78), (0.58, 0.84), (0.50, 0.92), (0.42, 0.84)], 24,
                        closed=True), 1.0)]


# --------------------------------------------------------------------------------------
def draw_glyph(draw: ImageDraw.ImageDraw, ch, x, y, size, width, fill=255,
               wobble=0.0, seed=0, aspect=1.0):
    """Paint one glyph with round-capped strokes of tapering width.
    (x, y) is the top-left of the glyph box; size is its height."""
    g = GLYPHS.get(ch.upper())
    if g is None:
        return
    rng = np.random.default_rng(seed)
    for pts, wsc in g:
        p = np.asarray(pts, dtype=np.float64)
        if wobble:
            n = len(p)
            t = np.linspace(0, 2 * np.pi, n)
            ph = rng.random(2) * 6.283
            p = p + np.stack([np.sin(t * 3 + ph[0]), np.sin(t * 2 + ph[1])], 1) * wobble
        px = x + p[:, 0] * size * aspect
        py = y + p[:, 1] * size
        w = width * wsc
        # taper: strokes are a touch fatter in the middle, like a loaded brush
        n = len(px)
        for i in range(n - 1):
            s = i / max(1, n - 2)
            ww = w * (0.86 + 0.22 * np.sin(s * np.pi))
            draw.line([px[i], py[i], px[i + 1], py[i + 1]], fill=fill,
                      width=max(1, int(round(ww))))
            r = ww / 2.0
            draw.ellipse([px[i] - r, py[i] - r, px[i] + r, py[i] + r], fill=fill)
        r = w / 2.0
        draw.ellipse([px[-1] - r, py[-1] - r, px[-1] + r, py[-1] + r], fill=fill)
