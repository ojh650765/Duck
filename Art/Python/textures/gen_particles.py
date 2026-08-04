"""DUCK MOW - Particles/ sprite sheets and single puffs.

Sheets are laid out as a 2x2 grid, read row-major (0=TL, 1=TR, 2=BL, 3=BR) which is
Unity's Texture Sheet Animation default (Start Frame 0..3, X=2 Y=2).
Colours: clippings use ART_BIBLE cut_tip/cut_base/stripe_light, dust uses dirt,
confetti uses the tent + duck + mower palette, droplets use the pond hexes.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import duckart as D


def _cell_grid(cells, cell_px, out_px, path):
    """cells: list of (rgb HxWx3, alpha HxW) already at cell_px. Assembles 2x2."""
    S = cell_px * 2
    sheet = np.zeros((S, S, 4))
    for k, (rgb, a) in enumerate(cells):
        r, c = divmod(k, 2)
        sheet[r * cell_px:(r + 1) * cell_px, c * cell_px:(c + 1) * cell_px] = np.dstack([rgb, a])
    im = Image.fromarray(D.to8(np.clip(sheet, 0, 1)), "RGBA")
    if out_px != S:
        im = im.resize((out_px, out_px), Image.LANCZOS)
    im.save(path, optimize=True)


# ======================================================================================
def clippings(size=128):
    """2x2 sheet of freshly cut grass clippings - short bent blades."""
    cell = size // 2
    SS = cell * 4
    cells = []
    specs = [(0.62, 0.34, 12.0, 0.075), (0.50, -0.55, -32.0, 0.062),
             (0.72, 0.18, 68.0, 0.070), (0.44, -0.28, -74.0, 0.058)]
    for k, (length, bend, rot, width) in enumerate(specs):
        yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float64)
        x = xx / (SS - 1) - 0.5
        y = yy / (SS - 1) - 0.5
        th = np.radians(rot)
        u = x * np.cos(th) - y * np.sin(th)
        v = x * np.sin(th) + y * np.cos(th)

        # blade runs along v in [-L/2, +L/2], bending in u
        t = np.clip((v + length / 2) / length, 0, 1)
        curve = bend * (t ** 2 - 0.28)
        w = width * (0.42 + 0.75 * np.sin(np.clip(t, 0, 1) * np.pi) ** 0.6) * (1.0 - t * 0.55)
        w = np.maximum(w, 1e-4)
        d = np.abs(u - curve) / w
        inside = (v > -length / 2) & (v < length / 2)
        a = np.clip((1.0 - d) / 0.34, 0, 1) * inside
        # rounded ends
        endfade = np.clip((0.5 - np.abs(t - 0.5)) / 0.06, 0, 1)
        a = np.clip(a * (0.35 + 0.85 * endfade), 0, 1)

        # tip is the bright chartreuse, root keeps the cut-base green
        col = D.mix("cut_base", "cut_tip", np.clip(t * 1.15, 0, 1))
        col = D.mix(col, "stripe_light", 0.20 + 0.25 * k / 3.0)
        # a darker crease down one side so the clipping has a fold
        crease = np.exp(-((u - curve + w * 0.35) / (w * 0.42)) ** 2)
        col = col * (1 - (crease * 0.30)[..., None]) + \
            D.shade("cut_base", 0.35)[None, None, :] * (crease * 0.30)[..., None]

        img = Image.fromarray(D.to8(np.dstack([np.clip(col, 0, 1), a])), "RGBA") \
            .resize((cell, cell), Image.LANCZOS)
        arr = np.asarray(img, dtype=np.float64) / 255.0
        cells.append((arr[..., :3], arr[..., 3]))
    _cell_grid(cells, cell, size, D.outpath("Particles", "clipping_sprite_128.png"))


# ======================================================================================
def dust_puff(size=128):
    """Soft warm dust. Not a gaussian: it has internal billows so a scaling puff
    looks like it is churning."""
    SS = size * 4
    yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float64)
    c = (SS - 1) / 2
    r = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / (SS * 0.5)
    ang = np.arctan2(yy - c, xx - c)

    lob = np.zeros_like(r)
    rng = np.random.default_rng(17)
    for k in range(2, 8):
        lob += np.sin(ang * k + rng.random() * 6.283) * (0.085 / (k - 1))
    billow = D.fbm(SS, 5, 4, 0.55, 2, 1201) - 0.5
    rr = r * (1.0 + lob) + billow * 0.16

    a = np.clip((0.92 - rr) / 0.55, 0, 1) ** 1.5
    a *= (0.62 + 0.66 * D.fbm(SS, 8, 3, 0.5, 2, 1301))
    a = np.clip(a, 0, 1) * 0.90
    a = D.blur(a, SS / 55.0)

    # dust is lit from above: pale crown, warmer shaded belly
    shade_t = np.clip((yy / SS - 0.28) / 0.55, 0, 1)
    col = D.mix(D.mix("dirt", "fence_white", 0.60), D.mix("dirt", "wood_warm", 0.35), shade_t)
    col *= (0.92 + 0.16 * D.fbm(SS, 6, 3, 0.5, 2, 1401))[..., None]

    Image.fromarray(D.to8(np.dstack([np.clip(col, 0, 1), a])), "RGBA") \
        .resize((size, size), Image.LANCZOS) \
        .save(D.outpath("Particles", "dust_puff_128.png"), optimize=True)


# ======================================================================================
def spark(size=128):
    """Bonk/impact spark: hot cream core, brass falloff, four-point star flare.
    Additive-friendly (RGB stays bright where A is low)."""
    SS = size * 4
    yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float64)
    c = (SS - 1) / 2
    dx = (xx - c) / (SS * 0.5); dy = (yy - c) / (SS * 0.5)
    r = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)

    core = np.exp(-(r / 0.085) ** 2)
    glow = np.exp(-(r / 0.30) ** 1.35) * 0.62
    # four-point star, slightly uneven so it is not a plus sign
    star = (np.exp(-(np.abs(dy) / 0.028) ** 1.3) * np.exp(-(np.abs(dx) / 0.62) ** 1.6) * 0.72 +
            np.exp(-(np.abs(dx) / 0.022) ** 1.3) * np.exp(-(np.abs(dy) / 0.44) ** 1.6) * 0.55)
    # a couple of thrown filaments
    fil = np.zeros_like(r)
    rng = np.random.default_rng(5)
    for a0 in rng.uniform(0, 6.283, 5):
        dth = np.abs(((ang - a0 + np.pi) % (2 * np.pi)) - np.pi)
        fil += np.exp(-(dth / 0.055) ** 2) * np.exp(-(r / 0.42) ** 1.6) * 0.34

    a = np.clip(core + glow + star + fil, 0, 1)
    heat = np.clip(core * 1.4 + star * 0.6 + glow * 0.4, 0, 1)
    col = D.mix(D.mix("duck_orange", "brass", 0.35), "sun", np.clip(heat * 1.25, 0, 1))
    col = D.mix(col, (1.0, 1.0, 1.0), np.clip((heat - 0.75) * 3.0, 0, 1) * 0.8)

    Image.fromarray(D.to8(np.dstack([np.clip(col, 0, 1), a])), "RGBA") \
        .resize((size, size), Image.LANCZOS) \
        .save(D.outpath("Particles", "spark_128.png"), optimize=True)


# ======================================================================================
def confetti(size=128):
    """2x2 sheet of paper shapes: a flat rectangle, a curled streamer, a disc and a
    torn triangle. One colour per cell, tinted per-particle in the system."""
    cell = size // 2
    SS = cell * 4
    cols = ["tent_red", "mower_cream", "duck_orange", "pond_shallow"]
    cells = []
    for k in range(4):
        im = Image.new("L", (SS, SS), 0)
        d = ImageDraw.Draw(im)
        cx = cy = SS / 2
        if k == 0:      # rectangle with a slight fold
            pts = D.wobble_poly([(cx - SS * .28, cy - SS * .17), (cx + SS * .30, cy - SS * .21),
                                 (cx + SS * .27, cy + SS * .19), (cx - SS * .30, cy + SS * .16)],
                                amp=SS * .012, seed=1)
            d.polygon(pts, fill=255)
        elif k == 1:    # curled streamer
            n = 46
            pts_a, pts_b = [], []
            for i in range(n):
                t = i / (n - 1)
                x = cx + (t - 0.5) * SS * 0.76
                y = cy + np.sin(t * 5.6) * SS * 0.17
                w = SS * 0.075 * (0.45 + 0.65 * np.abs(np.cos(t * 5.6)))
                pts_a.append((x, y - w)); pts_b.append((x, y + w))
            d.polygon(pts_a + pts_b[::-1], fill=255)
        elif k == 2:    # disc, slightly egg-shaped
            d.ellipse([cx - SS * .27, cy - SS * .24, cx + SS * .25, cy + SS * .27], fill=255)
        else:           # torn triangle
            pts = D.wobble_poly([(cx, cy - SS * .30), (cx + SS * .29, cy + SS * .24),
                                 (cx - SS * .27, cy + SS * .21)], amp=SS * .018, seed=3)
            d.polygon(pts, fill=255)
        a = np.asarray(im.filter(ImageFilter.GaussianBlur(SS / 110.0)), dtype=np.float64) / 255.0

        base = D.hexcol(cols[k])
        # paper has a light and a dark face; a soft diagonal ramp fakes the twist
        yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float64)
        ramp = np.clip((xx * 0.6 + yy * 0.8) / (SS * 1.4) * 1.6 - 0.15, 0, 1)
        col = base[None, None, :] * (0.78 + 0.34 * ramp)[..., None]
        col *= D.paper_grain(SS, seed=k * 7 + 3, strength=0.05, freq=48)[..., None]

        img = Image.fromarray(D.to8(np.dstack([np.clip(col, 0, 1), a])), "RGBA") \
            .resize((cell, cell), Image.LANCZOS)
        arr = np.asarray(img, dtype=np.float64) / 255.0
        cells.append((arr[..., :3], arr[..., 3]))
    _cell_grid(cells, cell, size, D.outpath("Particles", "confetti_128.png"))


# ======================================================================================
def water_droplet(size=128):
    """Sprinkler droplet: pond blue, bright specular pip, darker refracting rim."""
    SS = size * 4
    yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float64)
    x = xx / (SS - 1) - 0.5
    y = yy / (SS - 1) - 0.5

    # teardrop: a circle at the bottom, a concave cusp taper to a point at the top
    apex, cy0, R = -0.36, 0.10, 0.235
    taper = R * np.clip((y - apex) / (cy0 - apex), 0, 1) ** 1.55
    ball = np.sqrt(np.clip(R * R - (y - cy0) ** 2, 0, None))
    w = np.where(y >= cy0, ball, taper)
    w = np.maximum(w, 1e-4)
    dcx = np.abs(x) / w
    a = np.clip((1.0 - dcx) / 0.14, 0, 1) * ((y > apex) & (y < cy0 + R))
    a = np.clip(a, 0, 1)
    a = D.blur(a, SS / 220.0)

    # shading: dark refracting rim, lighter body, hot specular pip upper-left
    inner = np.clip(1.0 - dcx, 0, 1)
    body = D.mix("pond", "pond_shallow", np.clip(inner * 1.25, 0, 1))
    rim = np.clip((0.30 - inner) / 0.30, 0, 1) ** 1.4
    col = body * (1 - (rim * 0.45)[..., None]) + \
        D.shade("pond", 0.40)[None, None, :] * (rim * 0.45)[..., None]
    pip = np.exp(-(((x + 0.085) / 0.055) ** 2 + ((y + 0.055) / 0.075) ** 2))
    col = col * (1 - (pip * 0.85)[..., None]) + \
        D.mix("sun", (1, 1, 1), 0.5)[None, None, :] * (pip * 0.85)[..., None]
    # a caustic bright spot low in the drop
    caus = np.exp(-(((x - 0.05) / 0.075) ** 2 + ((y - 0.16) / 0.06) ** 2)) * 0.45
    col = col * (1 - caus[..., None]) + D.mix("pond_shallow", "sun", 0.35)[None, None, :] * caus[..., None]

    a = a * (0.72 + 0.28 * np.clip(rim + pip, 0, 1))
    Image.fromarray(D.to8(np.dstack([np.clip(col, 0, 1), np.clip(a, 0, 1)])), "RGBA") \
        .resize((size, size), Image.LANCZOS) \
        .save(D.outpath("Particles", "water_droplet_128.png"), optimize=True)


if __name__ == "__main__":
    print("clippings..."); clippings()
    print("dust...");      dust_puff()
    print("spark...");     spark()
    print("confetti...");  confetti()
    print("droplet...");   water_droplet()
    print("done")
