"""DUCK MOW - Sky/ skybox panorama and billboard cloud sheet.

sky_gradient_1024x512 is a 2:1 latitude-longitude panorama for Skybox/Panoramic.
Row 0 = zenith (+Y), row 256 = horizon, rows below = the ground haze that the
fence and hills sit in front of. The sun is placed to agree with ART_BIBLE
section 4 (directional light rotation 46 deg elevation, -38 deg yaw).

Colours are strictly ART_BIBLE section 3 'Sky & distance': zenith #4E9BD4,
horizon #CFE7F2, sun #FFF3D0, hills #7FA8A0, haze #C6DCE4.
"""
import numpy as np
from PIL import Image
import duckart as D


SUN_ELEV = 46.0
SUN_YAW = -38.0


# ======================================================================================
def sky_gradient(W=1024, H=512):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    u = (xx + 0.5) / W
    v = (yy + 0.5) / H
    elev = (0.5 - v) * np.pi            # +pi/2 zenith .. -pi/2 nadir
    az = (u - 0.5) * 2 * np.pi

    # ---- base vertical gradient ------------------------------------------------------
    # not linear: sky darkens fast near the zenith and holds pale near the horizon,
    # which is what makes a painted sky read as deep rather than as a ramp
    t = np.clip(np.sin(np.clip(elev, 0, None)), 0, 1)
    t = t ** 0.62
    col = D.mix("sky_horizon", "sky_zenith", t)

    # a second, cooler pass just above the horizon stops the pale band looking milky
    band = np.exp(-((v - 0.50) / 0.16) ** 2) * (v < 0.5)
    col = col * (1 - (band * 0.22)[..., None]) + \
        D.mix("sky_horizon", "haze", 0.55)[None, None, :] * (band * 0.22)[..., None]

    # ---- sun and its warm scatter ----------------------------------------------------
    se = np.radians(SUN_ELEV)
    sa = np.radians(SUN_YAW)
    sx, sy, sz = np.cos(se) * np.sin(sa), np.sin(se), np.cos(se) * np.cos(sa)
    dx = np.cos(elev) * np.sin(az)
    dy = np.sin(elev)
    dz = np.cos(elev) * np.cos(az)
    cosang = np.clip(dx * sx + dy * sy + dz * sz, -1, 1)
    ang = np.arccos(cosang)

    scatter = np.exp(-(ang / 0.62) ** 1.5) * 0.85         # broad warm halo
    scatter += np.exp(-(ang / 0.16) ** 2) * 0.75          # tight glow
    core = np.exp(-(ang / 0.030) ** 2)                    # soft disc, no hard rim
    # warm light spills along the horizon on the sun's side
    horizon_warm = np.exp(-((v - 0.5) / 0.10) ** 2) * np.clip(np.cos(az - sa), 0, 1) ** 2 * 0.5

    warm = np.clip(scatter + horizon_warm, 0, 1.6)
    col = col * (1 - np.clip(warm, 0, 0.92)[..., None]) + \
        D.hexcol("sun")[None, None, :] * np.clip(warm, 0, 0.92)[..., None]
    col = col + D.hexcol("sun")[None, None, :] * (core * 0.55)[..., None]

    # ---- high cloud: a soft cirrus band, stretched flat by perspective ---------------
    # sample a periodic-in-u noise, squashed vertically toward the horizon
    NB = 1024
    n1 = D.fbm(NB, 5, 5, 0.55, 2, 2201)
    n2 = D.fbm(NB, 11, 4, 0.5, 2, 2311)
    wx = D.perlin(NB, 7, 2401); wy = D.perlin(NB, 7, 2411)
    n1 = D.warp(n1, wx, wy, 60)

    # map each pixel to the noise via a flat-plane projection: a cloud deck at a
    # fixed height, so bands compress toward the horizon like real cirrus
    e = np.clip(elev, np.radians(3.0), np.pi / 2)
    plane_r = 1.0 / np.tan(e)
    px = (np.sin(az) * plane_r) * 0.42
    py = (np.cos(az) * plane_r) * 0.42
    sxi = np.mod(px * NB, NB)
    syi = np.mod(py * NB, NB)
    c1 = D.bilinear_wrap(n1, sxi, syi)
    c2 = D.bilinear_wrap(n2, sxi * 2.3 + 90, syi * 2.3 + 40)

    cloud = np.clip((c1 * 0.70 + c2 * 0.30 - 0.455) / 0.20, 0, 1) ** 1.15
    # fade out at the zenith and right at the horizon so the deck has a middle
    cloud *= np.clip((v - 0.03) / 0.20, 0, 1) * np.clip((0.480 - v) / 0.09, 0, 1)
    # large-scale gaps so the deck is drifting weather, not a ring painted round the dome
    gap = 0.5 + 0.5 * (np.sin(az * 1.0 + 0.7) * 0.55 + np.sin(az * 2.0 + 2.3) * 0.30
                       + np.sin(az * 3.0 - 1.1) * 0.15)
    cloud *= np.clip(gap * 1.45 - 0.16, 0, 1)
    cloud *= 0.66

    cloud_col = D.mix("sky_horizon", "sun", 0.35)
    lit = np.clip(np.cos(az - sa), 0, 1) ** 1.5
    cloud_col = cloud_col[None, None, :] * (0.95 + 0.14 * lit)[..., None]
    col = col * (1 - cloud[..., None]) + cloud_col * cloud[..., None]

    # ---- painterly breakup: very low amplitude, keeps the sky from looking vector ----
    brush = D.fbm(NB, 4, 4, 0.55, 2, 2601)
    b = D.bilinear_wrap(brush, np.mod(u * NB * 1.0, NB), np.mod(v * NB * 2.0, NB))
    col *= (0.965 + 0.07 * b)[..., None]

    # large-scale colour temperature drift: the half of the dome away from the sun
    # cools slightly toward the zenith blue. Keeps the sky from reading as airbrush.
    cool = np.clip(-np.cos(az - sa), 0, 1) ** 1.4 * np.clip((0.5 - v) / 0.5, 0, 1) * 0.16
    col = col * (1 - cool[..., None]) + D.mix("sky_zenith", "haze", 0.30)[None, None, :] * cool[..., None]

    # ---- below the horizon: haze into distant-hill green ----------------------------
    below = np.clip((v - 0.5) / 0.22, 0, 1)
    ground = D.mix("haze", "hills", np.clip(below * 1.25, 0, 1) ** 0.8)
    col = col * (1 - below[..., None] ** 0.8) + ground * (below[..., None] ** 0.8)

    # ---- dither with the blue-noise texture to kill 8-bit banding --------------------
    try:
        bn = D.load(D.outpath("Noise", "noise_blue_256.png"))
        bn = np.tile(bn, (H // 256 + 1, W // 256 + 1))[:H, :W]
        col = col + (bn[..., None] - 0.5) * (1.2 / 255.0)
    except Exception:
        pass

    D.save(D.outpath("Sky", "sky_gradient_1024x512.png"), np.clip(col, 0, 1))
    print("  sun placed at u=%.3f v=%.3f" % (((SUN_YAW / 360.0) % 1.0 + 0.5) % 1.0,
                                             0.5 - SUN_ELEV / 180.0))


# ======================================================================================
def _one_cloud(S, seed, style):
    """Chunky storybook cumulus: a union of spheres with a flattened base, shaded
    as one implicit surface so it reads as a solid object, not a smudge."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    x = xx / S; y = yy / S

    lobes = []
    if style == 0:          # wide, three big lobes
        base = [(0.30, 0.62, 0.175), (0.52, 0.52, 0.215), (0.72, 0.62, 0.155),
                (0.42, 0.60, 0.15), (0.62, 0.58, 0.145)]
    elif style == 1:        # tall, cauliflower stack
        base = [(0.46, 0.66, 0.185), (0.53, 0.46, 0.165), (0.40, 0.50, 0.135),
                (0.63, 0.58, 0.145), (0.34, 0.66, 0.125)]
    elif style == 2:        # long low bank
        base = [(0.24, 0.60, 0.135), (0.40, 0.56, 0.165), (0.58, 0.55, 0.170),
                (0.75, 0.60, 0.140), (0.49, 0.63, 0.150)]
    else:                   # small round puff
        base = [(0.50, 0.55, 0.205), (0.36, 0.62, 0.135), (0.65, 0.62, 0.130)]
    for cxp, cyp, r in base:
        lobes.append((cxp + rng.uniform(-.02, .02), cyp + rng.uniform(-.02, .02),
                      r * rng.uniform(0.92, 1.10)))

    # implicit height field: max of hemispherical lobes
    h = np.zeros((S, S))
    for cxp, cyp, r in lobes:
        d2 = ((x - cxp) ** 2 + (y - cyp) ** 2) / (r * r)
        h = np.maximum(h, np.sqrt(np.clip(1 - d2, 0, 1)) * r)

    # flat-ish base: clip everything below the base line, softly
    base_y = max(cy for _, cy, r in lobes) + 0.055
    flat = np.clip((base_y - y) / 0.055, 0, 1)
    h = h * flat

    # bumpy silhouette, low frequency only - chunky, never wispy
    bump = D.fbm(S, 6, 3, 0.5, 2, seed + 41) - 0.5
    h = h + bump * 0.030 * (h > 0.001)
    h = D.blur(h, S / 140.0)

    # tight alpha ramp - a storybook cloud has an edge, not a haze
    a = np.clip((h - 0.006) / 0.013, 0, 1)
    a = a * a * (3 - 2 * a)

    # ---- shading ---------------------------------------------------------------------
    # normals come from a well-smoothed height so the lobes shade as rounded masses
    # and the silhouette does not blow out into a rim-light fringe
    hs = D.blur(h, S / 42.0)
    g = S * 0.020
    gx = np.clip(np.gradient(hs, axis=1) * g, -1.6, 1.6)
    gy = np.clip(np.gradient(hs, axis=0) * g, -1.6, 1.6)
    l = np.sqrt(gx * gx + gy * gy + 1.0)
    nx, ny, nzn = -gx / l, gy / l, 1.0 / l
    L = np.array([-0.46, 0.66, 0.60]); L /= np.linalg.norm(L)   # sun upper-left
    ndl = np.clip(nx * L[0] + ny * L[1] + nzn * L[2], 0, 1)

    lit = D.mix((1.0, 1.0, 1.0), "sun", 0.42)          # warm sunlit white
    mid = D.mix("sky_horizon", "haze", 0.45)
    shadow = D.mix(D.mix("haze", "sky_zenith", 0.42), "split_shadow", 0.30)

    ramp = np.clip((ndl - 0.38) / 0.42, 0, 1)
    ramp = ramp * ramp * (3 - 2 * ramp)                # smoothstep, banded look
    col = D.mix(shadow, mid, np.clip(ramp * 2.0, 0, 1))
    col = col * (1 - np.clip(ramp * 2 - 1, 0, 1)[..., None]) + \
        lit[None, None, :] * np.clip(ramp * 2 - 1, 0, 1)[..., None]

    # the underside sits in its own shadow, and picks up a green bounce off the lawn
    depth = np.clip((y - (base_y - 0.20)) / 0.20, 0, 1) ** 1.4
    col = col * (1 - (depth * 0.34)[..., None]) + shadow[None, None, :] * (depth * 0.34)[..., None]
    bounce = depth * np.clip(1 - ndl, 0, 1) * 0.15
    col = col * (1 - bounce[..., None]) + \
        D.mix("amb_equator", "sun", 0.45)[None, None, :] * bounce[..., None]

    return np.clip(col, 0, 1), np.clip(a, 0, 1)


def cloud_puff(size=512):
    """2x2 sheet, four cumulus variations, each in a 256 cell."""
    cell = size // 2
    SS = cell * 2
    sheet = np.zeros((size, size, 4))
    for k, seed in enumerate([1201, 1303, 1409, 1511]):
        col, a = _one_cloud(SS, seed, k)
        img = np.dstack([col, a])
        img = np.asarray(Image.fromarray(D.to8(img), "RGBA").resize((cell, cell), Image.LANCZOS),
                         dtype=np.float64) / 255.0
        r, c = divmod(k, 2)
        sheet[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = img
    D.save(D.outpath("Sky", "cloud_puff_512.png"), sheet)


if __name__ == "__main__":
    print("sky gradient..."); sky_gradient()
    print("cloud puffs...");  cloud_puff()
    print("done")
