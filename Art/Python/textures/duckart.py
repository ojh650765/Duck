"""
DUCK MOW - shared procedural texture library.

All colour constants come from Docs/ART_BIBLE.md section 3. Do not invent hues here;
if a new hue is needed, derive it from a bible colour with tint()/shade()/mix()
and document the derivation at the call site.

Everything that claims to tile is generated on a periodic lattice, so tiling is
exact by construction, not by mirror-blending.
"""
from __future__ import annotations

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

# --------------------------------------------------------------------------------------
# paths
# --------------------------------------------------------------------------------------
ROOT = r"C:\Duck"
OUT = os.path.join(ROOT, "Assets", "Art", "Textures")
PREVIEW = os.path.join(ROOT, "Art", "Python", "textures", "_preview")


def outpath(category: str, name: str) -> str:
    d = os.path.join(OUT, category)
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, name)


# --------------------------------------------------------------------------------------
# palette (ART_BIBLE section 3)
# --------------------------------------------------------------------------------------
PAL = {
    # grass
    "uncut_base": "#2F6B33", "uncut_tip": "#4E9440",
    "cut_base": "#6E9E37", "cut_tip": "#A8CB55",
    "stripe_light": "#B6D45F", "stripe_dark": "#8FB847",
    "cut_edge_shadow": "#24512A", "wheel_track": "#5B8331",
    # characters & props
    "duck_cream": "#F6EBD2", "duck_shadow_cream": "#DCC9A4",
    "duck_orange": "#F2A03D", "duck_orange_dark": "#D3792A",
    "mower_red": "#D6423C", "mower_red_deep": "#A32E2D",
    "mower_cream": "#F4E7CF", "engine_grey": "#4A4F55",
    "brass": "#C9A55A",
    "fence_white": "#F1EDE0",
    "tent_red": "#D8534E", "tent_cream": "#F5EAD6",
    "wood_warm": "#9A6B41", "wood_dark": "#6E4A2C",
    "pond": "#3E86A8", "pond_shallow": "#68B0C4",
    "chalk": "#F7F3E4",
    "hedge": "#2A5A34",
    "dirt": "#B99A6B",
    # sky & distance
    "sky_zenith": "#4E9BD4", "sky_horizon": "#CFE7F2",
    "sun": "#FFF3D0", "hills": "#7FA8A0", "haze": "#C6DCE4",
    # lighting (section 4)
    "sun_light": "#FFF1CE", "amb_sky": "#8CC0E8", "amb_equator": "#A9C79A",
    "amb_ground": "#4C6B44", "split_shadow": "#7FA0C8", "split_high": "#FFE9BF",
}


def hexcol(h) -> np.ndarray:
    """'#RRGGBB' -> float rgb 0..1. Also accepts a PAL key or an rgb tuple."""
    if isinstance(h, (tuple, list, np.ndarray)):
        return np.asarray(h, dtype=np.float64)
    if not h.startswith("#"):
        h = PAL[h]
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64) / 255.0


def mix(a, b, t):
    """Blend two colours. Either side may be a flat rgb or an already-spatial
    HxWx3 field; t may be scalar or HxW."""
    a, b = hexcol(a), hexcol(b)
    t = np.asarray(t)
    if t.ndim:
        if a.ndim == 1:
            a = a[None, None, :]
        if b.ndim == 1:
            b = b[None, None, :]
        return a * (1 - t[..., None]) + b * t[..., None]
    return a * (1 - t) + b * t


def tint(c, t):  # toward white
    return mix(c, (1.0, 1.0, 1.0), t)


def shade(c, t):  # toward the bible's cut-edge shadow, never toward black
    return mix(c, hexcol("cut_edge_shadow") * 0.55, t)


# --------------------------------------------------------------------------------------
# periodic noise
# --------------------------------------------------------------------------------------
def _smootherstep(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def value_noise(size, freq, seed=0):
    """Periodic value noise, returns HxW float 0..1. freq must divide nothing in
    particular but should be <= size."""
    rng = np.random.default_rng(seed)
    g = rng.random((freq, freq))
    c = np.arange(size) * (freq / size)
    i = np.floor(c).astype(np.int64)
    f = _smootherstep(c - i)
    i0, i1 = i % freq, (i + 1) % freq
    gy0 = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    gy1 = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return gy0 * (1 - f)[:, None] + gy1 * f[:, None]


def perlin(size, freq, seed=0):
    """Periodic gradient (Perlin) noise, returns HxW float roughly -1..1 normalised
    to 0..1."""
    rng = np.random.default_rng(seed)
    ang = rng.random((freq, freq)) * 2 * np.pi
    gx, gy = np.cos(ang), np.sin(ang)
    c = np.arange(size) * (freq / size)
    i = np.floor(c).astype(np.int64)
    f = c - i
    u = _smootherstep(f)
    i0, i1 = i % freq, (i + 1) % freq

    fx = f[None, :]
    fy = f[:, None]

    def dot(iy, ix, ox, oy):
        return gx[iy][:, ix] * (fx - ox) + gy[iy][:, ix] * (fy - oy)

    n00 = dot(i0, i0, 0, 0)
    n10 = dot(i0, i1, 1, 0)
    n01 = dot(i1, i0, 0, 1)
    n11 = dot(i1, i1, 1, 1)
    ux, uy = u[None, :], u[:, None]
    a = n00 * (1 - ux) + n10 * ux
    b = n01 * (1 - ux) + n11 * ux
    return (a * (1 - uy) + b * uy) * 0.7071 + 0.5


def fbm(size, base_freq=4, octaves=5, persistence=0.5, lacunarity=2, seed=0, kind="perlin"):
    fn = perlin if kind == "perlin" else value_noise
    total = np.zeros((size, size))
    amp, norm, f = 1.0, 0.0, base_freq
    for o in range(octaves):
        total += fn(size, max(1, int(round(f))), seed + o * 977) * amp
        norm += amp
        amp *= persistence
        f *= lacunarity
    return total / norm


def warp(field, wx, wy, amount):
    """Periodic domain warp of a HxW field by two warp fields, amount in pixels."""
    h, w = field.shape
    yy, xx = np.mgrid[0:h, 0:w]
    sx = (xx + (wx - 0.5) * 2 * amount)
    sy = (yy + (wy - 0.5) * 2 * amount)
    return bilinear_wrap(field, sx, sy)


def bilinear_wrap(field, sx, sy):
    h, w = field.shape
    x0 = np.floor(sx).astype(np.int64)
    y0 = np.floor(sy).astype(np.int64)
    fx = (sx - x0)[..., None] if field.ndim == 3 else (sx - x0)
    fy = (sy - y0)[..., None] if field.ndim == 3 else (sy - y0)
    x0m, x1m = x0 % w, (x0 + 1) % w
    y0m, y1m = y0 % h, (y0 + 1) % h
    a = field[y0m, x0m] * (1 - fx) + field[y0m, x1m] * fx
    b = field[y1m, x0m] * (1 - fx) + field[y1m, x1m] * fx
    return a * (1 - fy) + b * fy


def worley(size, cells, seed=0, n=2, jitter=1.0):
    """Periodic Worley. Returns list of the n smallest distances (normalised by
    cell size) plus a per-pixel id of the nearest cell."""
    rng = np.random.default_rng(seed)
    cs = size / cells
    cy, cx = np.mgrid[0:cells, 0:cells]
    px = (cx + 0.5 + (rng.random((cells, cells)) - 0.5) * jitter) * cs
    py = (cy + 0.5 + (rng.random((cells, cells)) - 0.5) * jitter) * cs
    cid = rng.random((cells, cells))

    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    gx = (xx // cs).astype(np.int64)
    gy = (yy // cs).astype(np.int64)

    best = np.full((n, size, size), 1e9)
    bestid = np.zeros((size, size))
    for oy in range(-1, 2):
        for ox in range(-1, 2):
            ix = (gx + ox) % cells
            iy = (gy + oy) % cells
            fx = px[iy, ix] + ox * 0  # px already absolute; fix wrap below
            fy = py[iy, ix]
            # unwrap: place the feature point in the neighbourhood of this pixel
            fx = px[iy, ix] + (gx + ox - ix) * cs
            fy = py[iy, ix] + (gy + oy - iy) * cs
            d = np.sqrt((fx - xx) ** 2 + (fy - yy) ** 2)
            newid = np.where(d < best[0], cid[iy, ix], bestid)
            bestid = newid
            alld = np.concatenate([best, d[None]], axis=0)
            alld = np.sort(alld, axis=0)[:n]
            best = alld
    return best / cs, bestid


# --------------------------------------------------------------------------------------
# filters
# --------------------------------------------------------------------------------------
def blur_wrap(a, sigma):
    from scipy.ndimage import gaussian_filter
    return gaussian_filter(a, sigma, mode="wrap")


def blur(a, sigma, mode="nearest"):
    from scipy.ndimage import gaussian_filter
    return gaussian_filter(a, sigma, mode=mode)


def normalise(a, lo=0.0, hi=1.0):
    mn, mx = float(a.min()), float(a.max())
    if mx - mn < 1e-9:
        return np.full_like(a, lo)
    return lo + (a - mn) / (mx - mn) * (hi - lo)


def height_to_normal(h, strength=2.0, wrap=True):
    """HxW height 0..1 -> HxWx3 tangent-space normal in 0..1 (OpenGL +Y up)."""
    m = "wrap" if wrap else "edge"
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    if not wrap:
        dx[:, 0] = dx[:, 1]; dx[:, -1] = dx[:, -2]
        dy[0] = dy[1]; dy[-1] = dy[-2]
    nx = -dx * strength * 32.0
    ny = dy * strength * 32.0   # +Y up (OpenGL); Unity's default normal map convention
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5


# --------------------------------------------------------------------------------------
# io
# --------------------------------------------------------------------------------------
def to8(a):
    return np.clip(np.asarray(a) * 255.0 + 0.5, 0, 255).astype(np.uint8)


def save(path, arr, mode=None):
    """arr float 0..1, HxW (L), HxWx3 (RGB), HxWx4 (RGBA)."""
    a = to8(arr)
    if a.ndim == 2:
        im = Image.fromarray(a, "L")
    elif a.shape[2] == 3:
        im = Image.fromarray(a, "RGB")
    else:
        im = Image.fromarray(a, "RGBA")
    if mode:
        im = im.convert(mode)
    im.save(path, optimize=True)
    return im


def load(path):
    return np.asarray(Image.open(path), dtype=np.float64) / 255.0


# --------------------------------------------------------------------------------------
# previews
# --------------------------------------------------------------------------------------
def checker(size, cell=16, a=0.62, b=0.50):
    yy, xx = np.mgrid[0:size[1], 0:size[0]]
    c = ((xx // cell + yy // cell) % 2).astype(np.float64)
    return a + c * (b - a)


def flatten_alpha(im: Image.Image, cell=16):
    im = im.convert("RGBA")
    bg = Image.fromarray(to8(np.dstack([checker(im.size, cell)] * 3)), "RGB")
    bg.paste(im, (0, 0), im)
    return bg


def tile_preview(path, out, n=2, scale=1.0, label=True):
    """2x2 tile sheet for seam checking."""
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    sheet = Image.new("RGBA", (w * n, h * n))
    for j in range(n):
        for i in range(n):
            sheet.paste(im, (i * w, j * h))
    if scale != 1.0:
        sheet = sheet.resize((int(sheet.width * scale), int(sheet.height * scale)), Image.LANCZOS)
    flatten_alpha(sheet).save(out)
    return out


def contact_sheet(items, out, cell=192, cols=4, bg=(38, 42, 38), pad=8, title_h=16):
    """items: list of (label, path). Renders a labelled grid on a dark background."""
    from PIL import ImageFont
    try:
        font = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 12)
    except Exception:
        font = ImageFont.load_default()
    rows = (len(items) + cols - 1) // cols
    W = cols * (cell + pad) + pad
    H = rows * (cell + pad + title_h) + pad
    sheet = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(sheet)
    for k, (label, p) in enumerate(items):
        r, c = divmod(k, cols)
        x = pad + c * (cell + pad)
        y = pad + r * (cell + pad + title_h)
        im = Image.open(p).convert("RGBA")
        im.thumbnail((cell, cell), Image.LANCZOS)
        im = flatten_alpha(im, cell=8)
        sheet.paste(im, (x + (cell - im.width) // 2, y + (cell - im.height) // 2))
        d.text((x, y + cell + 2), label, fill=(220, 226, 214), font=font)
    sheet.save(out)
    return out


# --------------------------------------------------------------------------------------
# hand-drawn primitives (PIL, supersampled)
# --------------------------------------------------------------------------------------
SS = 4  # supersample factor used by the UI generators


def wobble_poly(pts, amp=2.0, seed=0, freq=1.0):
    """Perturb a polygon's vertices with smooth noise so edges read as hand-painted
    rather than CAD. Integer harmonics only, so a closed loop stays closed."""
    rng = np.random.default_rng(seed)
    n = len(pts)
    ph = rng.random(4) * 2 * np.pi
    k = [max(1, int(round(v * freq))) for v in (3, 7, 2, 5)]
    out = []
    for i, (x, y) in enumerate(pts):
        t = i / max(1, n) * 2 * np.pi
        out.append((x + amp * (np.sin(t * k[0] + ph[0]) + 0.6 * np.sin(t * k[1] + ph[1])),
                    y + amp * (np.sin(t * k[2] + ph[2]) + 0.6 * np.sin(t * k[3] + ph[3]))))
    return out


def rounded_rect_pts(x0, y0, x1, y1, r, steps=10):
    """Point list for a rounded rectangle (so it can be wobbled before filling)."""
    pts = []
    corners = [(x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180), (x1 - r, y0 + r, 270)]
    for cx, cy, a0 in corners:
        for s in range(steps + 1):
            a = np.radians(a0 + 90 * s / steps)
            pts.append((cx + r * np.cos(a), cy + r * np.sin(a)))
    return pts


def drop_shadow(alpha_img: Image.Image, offset=(0, 6), blur_px=8, opacity=0.34, colour=(40, 60, 40)):
    """Returns an RGBA shadow layer from a source RGBA's alpha."""
    a = alpha_img.split()[-1]
    sh = Image.new("RGBA", alpha_img.size, colour + (0,))
    a2 = a.filter(ImageFilter.GaussianBlur(blur_px))
    a2 = a2.point(lambda v: int(v * opacity))
    sh.putalpha(a2)
    return ImageChops.offset(sh, offset[0], offset[1])


def paper_grain(size, seed=0, strength=0.05, freq=64):
    """Multiplicative paper grain, HxW float around 1.0."""
    n = fbm(size, base_freq=freq, octaves=3, persistence=0.55, seed=seed, kind="value")
    fine = value_noise(size, size // 2, seed + 31)
    g = 0.65 * n + 0.35 * fine
    return 1.0 + (g - g.mean()) * strength * 4.0


def apply_grain_rgba(im: Image.Image, seed=0, strength=0.05, freq=64):
    a = np.asarray(im.convert("RGBA"), dtype=np.float64) / 255.0
    g = paper_grain(im.size[0], seed=seed, strength=strength, freq=freq)
    if g.shape[0] != a.shape[0]:
        g = np.asarray(Image.fromarray(to8(normalise(g, 0, 1)))
                       .resize((a.shape[1], a.shape[0]), Image.BILINEAR), dtype=np.float64) / 255.0
        g = 1.0 + (g - g.mean()) * strength * 4.0
    a[..., :3] = np.clip(a[..., :3] * g[..., None], 0, 1)
    return Image.fromarray(to8(a), "RGBA")


def rgba(colour, a=255):
    c = hexcol(colour)
    return (int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), a)
