"""DUCK MOW - county-fair UI drawing kit.

Everything is drawn as supersampled masks (PIL) and composited as float colour in
numpy, so gradients, grain and shadows stay under control.

House style, from ART_BIBLE:
  card stock   tent_cream  #F5EAD6   (mower_cream #F4E7CF for the warmer variant)
  sign paint   tent_red    #D8534E   accents, stripes, rules
  sign ink     wood_dark   #6E4A2C   the hand-painted outline on everything
  timber       wood_warm   #9A6B41   frames, the dark card
  brass        brass       #C9A55A   pins, rims, boost
  shadow       amb_ground -> split_shadow, blue-tinted, never black
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import duckart as D

SS = 4              # supersample factor
INK = "wood_dark"
CREAM = "tent_cream"
CREAM2 = "mower_cream"
RED = "tent_red"
TIMBER = "wood_warm"
BRASS = "brass"
SHADOW = D.shade(D.mix("amb_ground", "split_shadow", 0.45), 0.35)


# --------------------------------------------------------------------------------------
class Mask:
    """A supersampled 'L' canvas you draw into, then resolve to a float HxW mask."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("L", (w * SS, h * SS), 0)
        self.d = ImageDraw.Draw(self.im)

    def s(self, v):
        return v * SS

    def poly(self, pts, fill=255):
        self.d.polygon([(x * SS, y * SS) for x, y in pts], fill=fill)

    def stroke(self, pts, width, fill=255, closed=True):
        p = [(x * SS, y * SS) for x, y in pts]
        if closed:
            p = p + [p[0]]
        self.d.line(p, fill=fill, width=max(1, int(width * SS)), joint="curve")

    def ellipse(self, cx, cy, rx, ry, fill=255):
        self.d.ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=fill)

    def rect(self, x0, y0, x1, y1, fill=255):
        self.d.rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], fill=fill)

    def pie(self, cx, cy, r, a0, a1, fill=255):
        self.d.pieslice([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS],
                        a0, a1, fill=fill)

    def resolve(self, blur=0.0):
        im = self.im
        if blur:
            im = im.filter(ImageFilter.GaussianBlur(blur * SS))
        im = im.resize((self.w, self.h), Image.LANCZOS)
        return np.clip(np.asarray(im, dtype=np.float64) / 255.0, 0, 1)


def rrect(x0, y0, x1, y1, r, steps=14):
    return D.rounded_rect_pts(x0, y0, x1, y1, r, steps)


def mask_rrect(w, h, x0, y0, x1, y1, r, wob=0.0, seed=0, steps=14):
    m = Mask(w, h)
    pts = rrect(x0, y0, x1, y1, r, steps)
    if wob:
        pts = D.wobble_poly(pts, amp=wob, seed=seed, freq=1.0)
    m.poly(pts)
    return m.resolve(), pts


# --------------------------------------------------------------------------------------
def over(dst_rgb, dst_a, src_rgb, src_a):
    """Standard source-over compositing on straight (non-premultiplied) colour."""
    src_a = np.asarray(src_a)
    if np.ndim(src_rgb) == 1:
        src_rgb = np.ones(dst_rgb.shape) * np.asarray(src_rgb)[None, None, :]
    out_a = src_a + dst_a * (1 - src_a)
    safe = np.maximum(out_a, 1e-6)
    out_rgb = (src_rgb * src_a[..., None] + dst_rgb * dst_a[..., None] * (1 - src_a[..., None])) / safe[..., None]
    return out_rgb, out_a


def blank(w, h):
    return np.zeros((h, w, 3)), np.zeros((h, w))


def soft_shadow(mask, dx=0, dy=5, blur=5.0, opacity=0.36):
    a = np.asarray(Image.fromarray(D.to8(mask), "L")
                   .filter(ImageFilter.GaussianBlur(blur)), dtype=np.float64) / 255.0
    a = np.roll(np.roll(a, dy, axis=0), dx, axis=1)
    if dy > 0:
        a[:dy] = 0
    elif dy < 0:
        a[dy:] = 0
    return np.clip(a * opacity, 0, 1)


def grain(w, h, seed=0, strength=0.055, freq=48):
    n = max(w, h)
    g = D.paper_grain(n, seed=seed, strength=strength, freq=freq)
    return g[:h, :w] if g.shape[0] >= h and g.shape[1] >= w else np.ones((h, w))


def vgrad(w, h, top=0.0, bottom=1.0):
    return np.tile(np.linspace(top, bottom, h)[:, None], (1, w))


def bevel(mask, light=(-0.55, -0.60), width=2.2, up=0.16, down=0.20):
    """Cheap painted bevel: brighten the edge facing the light, darken the far edge.
    Returns a multiplier field."""
    m = np.asarray(Image.fromarray(D.to8(mask), "L")
                   .filter(ImageFilter.GaussianBlur(width)), dtype=np.float64) / 255.0
    gy, gx = np.gradient(m)
    lx, ly = light
    n = np.sqrt(lx * lx + ly * ly)
    d = (gx * lx + gy * ly) / max(n, 1e-6)
    d = np.clip(d * 9.0, -1, 1)
    return 1.0 + np.clip(d, 0, 1) * up - np.clip(-d, 0, 1) * down


def stripes(w, h, period=26.0, angle=90.0, duty=0.5, phase=0.0, softness=1.4):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    a = np.radians(angle)
    p = (xx * np.cos(a) + yy * np.sin(a)) / period + phase
    t = np.abs(np.mod(p, 1.0) - 0.5) * 2.0
    return np.clip((duty - t) * period / softness + 0.5, 0, 1)


def save_ui(name, rgb, a, subdir="UI"):
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])
    p = D.outpath(subdir, name)
    Image.fromarray(D.to8(img), "RGBA").save(p, optimize=True)
    return p
