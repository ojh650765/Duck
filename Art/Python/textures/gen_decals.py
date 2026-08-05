"""DUCK MOW - Decals/ ground-projected transparent textures.

Chalk colour is ART_BIBLE 'chalk' #F7F3E4. Mud is derived from 'dirt' #B99A6B mixed
toward 'wood_dark' #6E4A2C. The shadow blob is 'amb_ground' #4C6B44 pushed 40%
toward the split-tone shadow hue #7FA0C8, because ART_BIBLE section 4 requires
shadows to be blue-tinted and transparent, never black.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import duckart as D


# ======================================================================================
# chalk
# ======================================================================================
def _chalk_grain(size, seed):
    """Crushed-chalk granularity: coarse dust clumps + individual grains, periodic."""
    clump = D.fbm(size, 14, 4, 0.55, 2, seed)
    grit, _ = D.worley(size, size // 5, seed=seed + 77, n=1, jitter=1.0)
    grit = np.clip(1.0 - grit[0] / 0.62, 0, 1) ** 0.7
    fine = D.fbm(size, size // 4, 2, 0.5, 2, seed + 991)
    return np.clip(0.42 + 0.72 * clump * (0.55 + 0.75 * grit) + 0.30 * (fine - 0.5), 0, 1.4)


def _chalk_rgb(size, seed, cover):
    """Chalk colour with a little tonal life: fresh chalk is bright, worn chalk
    picks up a hint of the grass it is sitting on."""
    warm = D.fbm(size, 9, 3, 0.5, 2, seed + 313)
    col = D.mix("chalk", D.mix("chalk", "cut_tip", 0.16), np.clip((0.62 - cover) * 1.5, 0, 1))
    col = col * (0.93 + 0.12 * warm)[..., None]
    return np.clip(col, 0, 1)


def chalk_line(size=256):
    """Soft chalk stroke, tiling along U (the stroke runs left->right).
    V is the cross-section. Stroke is ~34% of the tile height."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    v = yy / (size - 1)

    # the stroke wanders a little - a hand ran the marker along the grass
    wander = (D.fbm(size, 5, 3, 0.5, 2, 4001)[0:1, :] - 0.5) * 0.10
    wander = np.repeat(wander, size, 0)
    halfw = 0.17 * (0.78 + 0.44 * D.fbm(size, 7, 3, 0.5, 2, 4111)[0:1, :])
    halfw = np.repeat(halfw, size, 0)

    d = np.abs(v - 0.5 - wander) / halfw
    core = np.clip(1.0 - d, 0, 1)
    a = core ** 0.55                       # soft shoulders, dense middle
    a = a * (0.55 + 0.75 * _chalk_grain(size, 4201))

    # chalk skips where the grass was high
    skip = D.fbm(size, 11, 3, 0.5, 2, 4301)
    a *= np.clip((skip - 0.24) * 2.6, 0, 1) * 0.55 + 0.55
    a = np.clip(a, 0, 1) ** 1.05
    a[d > 1.35] = 0

    rgb = _chalk_rgb(size, 4401, a)
    D.save(D.outpath("Decals", "chalk_line_soft_256.png"), np.dstack([rgb, a]))


def chalk_corner(size=256):
    """Quarter-turn: enters at left edge (v=0.5), leaves at bottom edge (u=0.5).
    Arc centred on the tile centre with radius 0.5, so it butts onto chalk_line."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    u = xx / (size - 1); v = yy / (size - 1)
    r = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    ang = np.arctan2(v - 0.5, u - 0.5)

    wob = (D.fbm(size, 6, 3, 0.5, 2, 5001) - 0.5) * 0.035
    halfw = 0.17 * (0.80 + 0.40 * D.fbm(size, 8, 3, 0.5, 2, 5111))
    d = np.abs(r - 0.5 + wob) / halfw

    core = np.clip(1.0 - d, 0, 1)
    # only the quarter running from 180deg round to 90deg (left edge -> bottom edge)
    inarc = (ang >= np.pi / 2 - 0.02) | (ang <= -np.pi + 0.02)
    inarc = ((ang > np.pi / 2 - 0.03) & (ang < np.pi + 0.03))
    a = core ** 0.55 * inarc
    a = a * (0.55 + 0.75 * _chalk_grain(size, 5201))
    skip = D.fbm(size, 12, 3, 0.5, 2, 5301)
    a *= np.clip((skip - 0.24) * 2.6, 0, 1) * 0.55 + 0.55
    a = np.clip(a, 0, 1)
    # feather the two cut ends slightly so the join is not a razor edge
    a = np.asarray(Image.fromarray(D.to8(a), "L").filter(ImageFilter.GaussianBlur(0.7)),
                   dtype=np.float64) / 255.0
    a[d > 1.35] = 0

    rgb = _chalk_rgb(size, 5401, a)
    D.save(D.outpath("Decals", "chalk_corner_256.png"), np.dstack([rgb, a]))


def chalk_dash(size=256, dashes=3):
    """Dashed chalk, tiling along U. Used for the target-outline ghost."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    u = xx / size; v = yy / (size - 1)

    wander = np.repeat((D.fbm(size, 5, 3, 0.5, 2, 6001)[0:1, :] - 0.5) * 0.09, size, 0)
    halfw = np.repeat(0.165 * (0.8 + 0.4 * D.fbm(size, 7, 3, 0.5, 2, 6111)[0:1, :]), size, 0)
    d = np.abs(v - 0.5 - wander) / halfw
    core = np.clip(1.0 - d, 0, 1) ** 0.55

    # dash envelope: ~60% on, with long hand-lifted tapers at both ends
    t = np.mod(u * dashes, 1.0)
    env = np.clip((0.60 - np.abs(t - 0.32) * 2.0) / 0.34, 0, 1)
    env = env ** 0.55
    # the taper also narrows the stroke, so ends thin out instead of chopping off
    core = np.clip(1.0 - d / np.maximum(0.35 + 0.65 * env, 1e-3), 0, 1) ** 0.55
    jitter = np.repeat(D.fbm(size, dashes * 2, 2, 0.5, 2, 6211)[0:1, :], size, 0)
    env = np.clip(env * (0.72 + 0.5 * jitter), 0, 1)

    a = core * env * (0.55 + 0.75 * _chalk_grain(size, 6301))
    a = np.clip(a, 0, 1)
    a[d > 1.35] = 0
    rgb = _chalk_rgb(size, 6401, a)
    D.save(D.outpath("Decals", "chalk_dash_256.png"), np.dstack([rgb, a]))


# ======================================================================================
# tyre track
# ======================================================================================
def tyre_track(size=256):
    """Greyscale wheel track, tiling along V (direction of travel).
    U spans 1 tyre width plus shoulders: value 0 = untouched, 1 = fully pressed.
    Lawn-tractor turf tread: angled bars in a shallow chevron."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    u = xx / (size - 1); vv = yy / size

    # tyre footprint across U, soft shoulders
    fp = np.clip((0.40 - np.abs(u - 0.5)) / 0.085, 0, 1)
    fp = fp ** 0.7

    # chevron bars: mirrored diagonals meeting on the centreline
    side = np.sign(u - 0.5)
    lug_phase = vv * 7.0 + np.abs(u - 0.5) * 1.55 * side * side  # symmetric V
    lug_phase = vv * 7.0 - np.abs(u - 0.5) * 1.9
    lug = np.mod(lug_phase, 1.0)
    bar = np.clip((0.46 - np.abs(lug - 0.5)) / 0.13, 0, 1)
    bar = bar ** 0.8

    # the bar edges bite harder than the middle
    bite = np.clip((0.5 - np.abs(lug - 0.5)) * 5.0, 0, 1)

    # per-lug variation so the tread is not a perfect machine repeat
    lugvar = np.repeat(D.fbm(size, 7, 3, 0.5, 2, 7311)[:, 0:1], size, 1)
    bar = np.clip(bar * (0.70 + 0.55 * lugvar), 0, 1)

    grain = D.fbm(size, 26, 3, 0.5, 2, 7001)
    wobble = np.repeat((D.fbm(size, 4, 3, 0.5, 2, 7111)[:, 0:1] - 0.5) * 0.06, size, 1)
    fp = np.clip((0.40 - np.abs(u - 0.5 - wobble)) / 0.085, 0, 1) ** 0.7

    # base flattening under the whole tyre, plus the deeper lug prints
    a = fp * (0.42 + 0.58 * bar * (0.7 + 0.3 * bite))
    a *= (0.72 + 0.50 * grain)
    # tracks fade in and out along their length
    fade = np.repeat(D.fbm(size, 3, 3, 0.5, 2, 7211)[:, 0:1], size, 1)
    a *= 0.62 + 0.55 * fade
    a = np.clip(D.blur_wrap(a, 0.8), 0, 1)
    D.save(D.outpath("Decals", "tyre_track_256.png"), a)


# ======================================================================================
# mud splats
# ======================================================================================
def mud_splat(index, size=256, seed=0):
    rng = np.random.default_rng(seed)
    S = size * 2
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    cx = cy = S / 2

    # main body: a radial field pushed around by noise so the outline is ragged
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (S * 0.5)
    ang = np.arctan2(yy - cy, xx - cx)
    lobes = np.zeros_like(r)
    for k in range(1, 7):
        lobes += np.sin(ang * k + rng.random() * 6.28) * (0.09 / k) * rng.uniform(0.5, 1.5)
    n = D.fbm(S, 7, 4, 0.55, 2, seed + 11)
    rr = r - lobes - (n - 0.5) * 0.30
    body = np.clip((0.46 - rr) / 0.06, 0, 1)

    # thrown droplets and a couple of drag streaks
    drops = np.zeros((S, S))
    im = Image.new("L", (S, S), 0)
    dr = ImageDraw.Draw(im)
    for _ in range(rng.integers(14, 22)):
        a0 = rng.random() * 6.283
        dist = rng.uniform(0.42, 0.86) * S * 0.5
        px, py = cx + np.cos(a0) * dist, cy + np.sin(a0) * dist
        rad = rng.uniform(2.5, 12.0) * (1.0 - dist / (S * 0.62))
        rad = max(2.0, rad)
        el = rng.uniform(1.0, 2.4)
        dr.ellipse([px - rad * el, py - rad, px + rad * el, py + rad], fill=255)
    # thrown teardrops: a chain of shrinking discs, so they taper instead of
    # reading as drawn sticks
    for _ in range(rng.integers(3, 6)):
        a0 = rng.random() * 6.283
        r0, r1 = S * rng.uniform(0.20, 0.28), S * rng.uniform(0.34, 0.47)
        w0 = rng.uniform(7, 13)
        for s in np.linspace(0, 1, 26):
            rad = w0 * (1.0 - s) ** 1.4
            if rad < 0.8:
                break
            dist = r0 + (r1 - r0) * s
            px, py = cx + np.cos(a0) * dist, cy + np.sin(a0) * dist
            dr.ellipse([px - rad, py - rad, px + rad, py + rad], fill=255)
    drops = np.asarray(im, dtype=np.float64) / 255.0
    drops = np.clip(drops * (0.5 + 0.9 * D.fbm(S, 12, 3, 0.5, 2, seed + 31)), 0, 1)

    a = np.clip(np.maximum(body, drops * 0.92), 0, 1)
    a *= (0.70 + 0.46 * D.fbm(S, 20, 3, 0.5, 2, seed + 57))
    a = np.clip(a * 1.25, 0, 1)

    # colour: wet centre is darker than the drying rim
    wet = D.blur(a, 6.0)
    col = D.mix(D.mix("dirt", "wood_dark", 0.55), D.mix("dirt", "wood_dark", 0.86),
                np.clip(wet * 1.2, 0, 1))
    col *= (0.88 + 0.24 * D.fbm(S, 16, 3, 0.5, 2, seed + 71))[..., None]

    img = np.dstack([np.clip(col, 0, 1), a])
    Image.fromarray(D.to8(img), "RGBA").resize((size, size), Image.LANCZOS).save(
        D.outpath("Decals", "mud_splat_%02d_256.png" % index), optimize=True)


# ======================================================================================
def shadow_blob(size=128):
    """Cheap character shadow. RGB is a blue-green shadow tint (ART_BIBLE section 4:
    shadows are blue-tinted and transparent, never black); A is the falloff.
    The edge is deliberately irregular so it does not read as a disc."""
    S = size * 4
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    c = (S - 1) / 2
    r = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / (S * 0.5)
    ang = np.arctan2(yy - c, xx - c)

    wob = np.zeros_like(r)
    rng = np.random.default_rng(9)
    for k in range(2, 7):
        wob += np.sin(ang * k + rng.random() * 6.283) * (0.055 / (k - 1))
    rr = r * (1.0 + wob) * 1.06

    # solid core out to ~45% of the radius, then a long soft falloff
    a = np.clip((1.0 - rr) / 0.58, 0, 1) ** 1.35
    a = a * 0.88
    a = np.asarray(Image.fromarray(D.to8(a), "L").filter(ImageFilter.GaussianBlur(S / 40)),
                   dtype=np.float64) / 255.0

    col = D.shade(D.mix("amb_ground", "split_shadow", 0.40), 0.45)
    rgb = np.ones((S, S, 3)) * col[None, None, :]
    img = np.dstack([rgb, a])
    Image.fromarray(D.to8(img), "RGBA").resize((size, size), Image.LANCZOS).save(
        D.outpath("Decals", "shadow_blob_128.png"), optimize=True)


# ======================================================================================
def old_mow_pattern(size=1024):
    """Last year's winning picture - a crown - still faintly ghosted into the apron
    lawn. Greyscale: 0 = plain apron turf, 1 = fully regrown-but-visible old cut.
    Deliberately low contrast and patchy; the shader should scale this by ~0.12-0.18."""
    SSx = 2
    S = size * SSx
    im = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(im)

    # ---- crown geometry, drawn big and chunky (ART_BIBLE section 7: min feature 6 m)
    cx = S * 0.5
    base_y = S * 0.76
    top_y = S * 0.24
    half = S * 0.36

    band_top = base_y - S * 0.085
    # five points with dipped valleys between them
    pts = []
    n_pt = 5
    xs = np.linspace(cx - half, cx + half, n_pt)
    pts.append((cx - half, band_top))
    for i, x in enumerate(xs):
        peak = top_y + (S * 0.055 if i in (0, n_pt - 1) else 0) + (S * 0.03 if i in (1, 3) else 0)
        if i > 0:
            vx = (xs[i - 1] + x) / 2
            pts.append((vx, band_top - S * 0.055))
        pts.append((x, peak))
    pts.append((cx + half, band_top))
    pts = D.wobble_poly(pts, amp=S * 0.006, seed=4, freq=0.7)
    d.polygon(pts, fill=255)

    # ---- the band
    d.polygon(D.wobble_poly([(cx - half * 1.06, band_top), (cx + half * 1.06, band_top),
                             (cx + half * 1.00, base_y), (cx - half * 1.00, base_y)],
                            amp=S * 0.005, seed=7), fill=255)
    # ---- jewels on the band (holes, so the crown is not one flat lump)
    for i, t in enumerate([0.22, 0.5, 0.78]):
        jx = cx - half + 2 * half * t
        jy = (band_top + base_y) / 2
        jr = S * (0.030 if i == 1 else 0.022)
        d.ellipse([jx - jr, jy - jr, jx + jr, jy + jr], fill=0)
    # ---- balls on the points
    for i, x in enumerate(xs):
        peak = top_y + (S * 0.055 if i in (0, n_pt - 1) else 0) + (S * 0.03 if i in (1, 3) else 0)
        br = S * (0.030 if i in (0, 2, 4) else 0.024)
        d.ellipse([x - br, peak - br * 1.7, x + br, peak + br * 0.3], fill=255)

    shape = np.asarray(im, dtype=np.float64) / 255.0
    shape = np.asarray(Image.fromarray(D.to8(shape), "L").resize((size, size), Image.LANCZOS),
                       dtype=np.float64) / 255.0

    # ---- render it as MOWN STRIPES, not a painted blob -------------------------------
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    ang = np.radians(24.0)
    proj = xx * np.cos(ang) + yy * np.sin(ang)
    # stripes wander a touch - a mower is not a plotter
    proj = proj + (D.fbm(size, 4, 3, 0.5, 2, 8601) - 0.5) * (size / 26.0)
    stripe = 0.5 + 0.5 * np.sin(proj * 2 * np.pi / (size / 9.0))
    stripe = stripe ** 0.85

    # ---- a year of regrowth: patchy, blurred, chewed at the edges --------------------
    regrow = D.fbm(size, 5, 5, 0.55, 2, 8101)
    regrow = D.warp(regrow, D.perlin(size, 9, 8201), D.perlin(size, 9, 8301), 34)
    regrow = D.normalise(regrow, 0.05, 1.0)
    fine = D.fbm(size, 26, 3, 0.5, 2, 8401)

    soft = D.blur(shape, 5.0)
    # nibble the silhouette so the old cut boundary is ragged, not vector-clean
    soft = np.clip((soft - 0.5 + (D.fbm(size, 18, 3, 0.5, 2, 8701) - 0.5) * 0.55) * 3.2 + 0.5, 0, 1)
    soft = D.blur(soft, 2.5)

    # the ghost is mostly the STRIPES, not the fill: interior gets 0.30 of the
    # signal, the striping gets 0.70, and regrowth eats most of it back
    m = soft * (0.30 + 0.70 * stripe)
    m *= (0.18 + 0.82 * regrow)
    m *= (0.80 + 0.34 * fine)

    # a slightly stronger line right on last year's cut edge - the bit that survives
    outline = np.clip(1.0 - np.abs(D.blur(shape, 7.0) - 0.5) * 6.0, 0, 1)
    m = np.clip(m + outline * regrow * 0.30, 0, 1)

    m = D.blur(m, 1.2)
    m = np.clip(D.normalise(m, 0.0, 1.0) ** 1.15, 0, 1)
    print("  old_mow mean %.3f  max %.3f" % (m.mean(), m.max()))
    D.save(D.outpath("Decals", "old_mow_pattern_1024.png"), m)


if __name__ == "__main__":
    print("chalk...");   chalk_line(); chalk_corner(); chalk_dash()
    print("tyre...");    tyre_track()
    print("mud...")
    for i, s in enumerate([31337, 90210, 55501], start=1):
        mud_splat(i, seed=s)
    print("shadow...");  shadow_blob()
    print("old mow...");  old_mow_pattern()
    print("done")
