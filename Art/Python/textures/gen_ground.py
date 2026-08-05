"""DUCK MOW - Ground/ tiling terrain textures.

Tiling scale: 512 px = 4 m (128 px/m), so a URP tiling of (worldSize/4) is correct.
Palette: everything is derived from ART_BIBLE 'dirt' #B99A6B, 'wood_warm' #9A6B41,
'wood_dark' #6E4A2C and 'fence_white' #F1EDE0. No new hues are invented - the pale
stone tone is dirt tinted 45% toward fence_white, the damp tone is dirt mixed 55%
toward wood_dark.
"""
import numpy as np
import duckart as D


SIZE = 512
PX_PER_M = 128.0


# --------------------------------------------------------------------------------------
def stone_layer(size, cells, seed, coverage=0.55, rad=0.42, height=1.0, warp_amt=0.0):
    """Returns (mask, height, tone) for a scatter of dome pebbles on a periodic grid.
    warp_amt (in px) distorts the cell distance field so stones are irregular lumps
    rather than perfect discs."""
    d, cid = D.worley(size, cells, seed=seed, n=1, jitter=1.0)
    f1 = d[0]
    if warp_amt > 0:
        wf = max(4, int(cells * 1.6))
        wx = D.perlin(size, wf, seed + 5501)
        wy = D.perlin(size, wf, seed + 6607)
        f1 = D.warp(f1, wx, wy, warp_amt)
        cid = D.warp(cid, wx, wy, warp_amt)
    r_rand = np.mod(cid * 41.71, 1.0)
    present = (np.mod(cid * 13.37, 1.0) < coverage).astype(np.float64)
    r = rad * (0.55 + 0.75 * r_rand)
    t = np.clip(1.0 - f1 / np.maximum(r, 1e-4), 0, 1)
    h = np.sqrt(np.clip(t * (2 - t), 0, 1)) * present * height
    mask = np.clip(t * 4.0, 0, 1) * present
    tone = np.mod(cid * 7.13, 1.0)
    return mask, h, tone


def contrast(a, k):
    return np.clip((a - 0.5) * k + 0.5, 0, 1)


# --------------------------------------------------------------------------------------
def dirt_path(size=SIZE):
    """Compacted warm dirt lane. Two soft cart ruts run along +V so the texture can
    be laid along a path and tiled forwards forever."""
    base = D.hexcol("dirt")
    damp = D.mix("dirt", "wood_dark", 0.55)
    pale = D.mix("dirt", "fence_white", 0.45)

    # --- large scale earth variation --------------------------------------------------
    n_big = D.fbm(size, 3, 5, 0.55, 2, 210)
    wx = D.perlin(size, 6, 311); wy = D.perlin(size, 6, 313)
    n_big = D.warp(n_big, wx, wy, 26)
    n_mid = D.fbm(size, 10, 4, 0.5, 2, 907)
    n_fine = D.fbm(size, 40, 3, 0.5, 2, 1301)

    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    u = xx / size

    # --- cart ruts: two shallow troughs, wobbling along their length -----------------
    rut = np.zeros((size, size))
    for centre, sd in ((0.28, 55), (0.715, 77)):
        wob = (D.perlin(size, 4, sd)[:, :1] - 0.5) * 0.055          # per-row wobble
        du = np.abs(((u - centre - wob + 0.5) % 1.0) - 0.5)
        rut = np.maximum(rut, np.exp(-(du / 0.055) ** 2))
    rut *= 0.55 + 0.45 * D.fbm(size, 8, 3, 0.5, 2, 333)             # ruts fade in/out

    # --- directional drag streaks along the rut direction ----------------------------
    streak = D.fbm(size, 64, 3, 0.5, 2, 640)
    streak = D.blur_wrap(streak, (5.0, 0.7))
    streak = D.normalise(streak, 0.3, 0.7)

    # --- pebbles half-buried in the surface ------------------------------------------
    sm, sh, st = stone_layer(size, 30, 2024, coverage=0.30, rad=0.46, warp_amt=3.0)
    sm2, sh2, st2 = stone_layer(size, 15, 4048, coverage=0.14, rad=0.40, warp_amt=5.0)
    # only the crown of each pebble breaks the surface - that is what stops them
    # reading as pale discs stuck on top
    crown = lambda m, hh: (np.clip((hh - 0.42) / 0.35, 0, 1) * m)
    stone_mask = np.clip(crown(sm, sh) + crown(sm2, sh2), 0, 1)
    stone_h = np.maximum(sh * 0.8, sh2)
    stone_tone = np.where(sh2 > sh, st2, st)

    # --- height ----------------------------------------------------------------------
    h = (0.50 + 0.30 * n_big + 0.14 * n_mid + 0.08 * n_fine
         - 0.34 * rut + 0.13 * stone_h * stone_mask + 0.05 * (streak - 0.5))
    h = D.normalise(h, 0, 1)

    # --- albedo ----------------------------------------------------------------------
    # ruts are compacted and damp; the shoulders are dry and dusty
    dampness = np.clip(0.85 * rut + 0.75 * (0.62 - n_big), 0, 1) * 0.80
    dust = np.clip((n_big - 0.55) * 2.6, 0, 1) * (1 - rut * 0.8)
    v = (0.5 + 0.75 * (n_mid - 0.5) + 0.55 * (n_fine - 0.5) + 0.7 * (streak - 0.5))
    col = D.mix(base, pale, np.clip(v * 0.5 + 0.10, 0, 1))
    col = col * (1 - dampness[..., None]) + D.hexcol(damp)[None, None, :] * dampness[..., None]
    col = col * (1 - (dust * 0.55)[..., None]) + \
        D.mix("dirt", "fence_white", 0.62)[None, None, :] * (dust * 0.55)[..., None]

    stone_col = D.mix(D.mix("dirt", "wood_warm", 0.35), pale, stone_tone)
    stone_col *= (0.80 + 0.34 * stone_h)[..., None]
    sm3 = (stone_mask * 0.85)[..., None]
    col = col * (1 - sm3) + stone_col * sm3

    # bake a whisper of curvature light so it reads without a strong normal
    ao = D.blur_wrap(h, 3.0)
    col *= (0.86 + 0.28 * np.clip((h - ao) * 2.2 + 0.5, 0, 1))[..., None]
    col = np.clip(col, 0, 1)

    D.save(D.outpath("Ground", "dirt_path_albedo_512.png"), col)
    D.save(D.outpath("Ground", "dirt_path_normal_512.png"),
           D.height_to_normal(D.blur_wrap(h, 0.9), strength=0.55))


# --------------------------------------------------------------------------------------
def gravel(size=SIZE):
    """Fair-lane gravel. Stones are dirt tinted toward fence_white / shaded toward
    wood_dark - no grey (ART_BIBLE section 8.8)."""
    sm_a, sh_a, st_a = stone_layer(size, 42, 6101, coverage=0.80, rad=0.52, warp_amt=2.2)
    sm_b, sh_b, st_b = stone_layer(size, 26, 6203, coverage=0.45, rad=0.48, warp_amt=3.4)
    sm_c, sh_c, st_c = stone_layer(size, 68, 6307, coverage=0.70, rad=0.54, warp_amt=1.4)

    ha, hb, hc = sh_a * 1.00, sh_b * 1.30, sh_c * 0.58
    h = np.maximum(np.maximum(ha, hb), hc)
    top = np.stack([ha, hb, hc], 0).argmax(0)
    tone = np.choose(top, [st_a, st_b, st_c])
    mask = np.choose(top, [sm_a, sm_b, sm_c])

    fill = D.fbm(size, 60, 3, 0.5, 2, 6400)
    # large-scale sparseness so the gravel is not a uniform porridge
    density = D.fbm(size, 4, 3, 0.55, 2, 6511)
    mask = np.clip(mask * (0.45 + 1.25 * density), 0, 1)
    h = h * mask

    bed = D.mix("dirt", "wood_dark", 0.52) * (0.80 + 0.36 * fill)[..., None]

    h = h * 0.9 + 0.10 * fill
    h = D.normalise(h, 0, 1)

    pale = D.mix("dirt", "fence_white", 0.55)
    warm = D.mix("dirt", "wood_warm", 0.62)
    # three tone families so the gravel has genuine colour variety, not one beige
    t = np.clip(tone * 1.1, 0, 1)
    stone_col = np.where((t < 0.34)[..., None],
                         D.mix(warm, "wood_dark", 0.30),
                         np.where((t < 0.70)[..., None], D.mix("dirt", pale, 0.45), pale))
    stone_col = stone_col * (0.70 + 0.50 * np.clip(h, 0, 1))[..., None]

    m = np.clip(mask, 0, 1)[..., None]
    col = bed * (1 - m) + stone_col * m

    ao = D.blur_wrap(h, 2.2)
    col *= (0.80 + 0.34 * np.clip((h - ao) * 2.0 + 0.5, 0, 1))[..., None]
    # crevice darkening between stones
    col *= (0.74 + 0.26 * np.clip(D.blur_wrap(h, 1.4) * 1.5, 0, 1))[..., None]
    col = np.clip(col, 0, 1)

    D.save(D.outpath("Ground", "gravel_albedo_512.png"), col)
    D.save(D.outpath("Ground", "gravel_normal_512.png"),
           D.height_to_normal(D.blur_wrap(h, 0.9), strength=0.28))


# --------------------------------------------------------------------------------------
def apron_grass_detail(size=SIZE):
    """Greyscale (R) fine grass detail for the short apron lawn. Mean ~0.5 so a
    shader can use it as (tex - 0.5) * amount. Blades lean, clump, and there are
    a few faint mower swirls."""
    rng = np.random.default_rng(88)
    acc = np.zeros((size, size))

    # many short anisotropic blade streaks, in a handful of lean directions
    for k in range(7):
        ang = np.radians(rng.uniform(58, 122))
        n = D.fbm(size, 96, 2, 0.5, 2, 700 + k * 53)
        sx, sy = abs(np.cos(ang)) * 0.55, abs(np.sin(ang)) * 3.4
        acc += D.blur_wrap(n, (sy, sx)) * (0.7 + 0.3 * rng.random())
    acc = D.normalise(acc, 0, 1)

    # clumping: low-frequency density so it isn't uniform fuzz
    clump = D.fbm(size, 5, 4, 0.55, 2, 1717)
    wx = D.perlin(size, 8, 191); wy = D.perlin(size, 8, 193)
    clump = D.warp(clump, wx, wy, 22)
    clump2 = D.fbm(size, 14, 3, 0.5, 2, 1811)
    acc = acc * (0.40 + 1.10 * clump) * (0.72 + 0.56 * clump2)

    # sparse individual bright blades on top
    d, cid = D.worley(size, 90, seed=2211, n=1, jitter=1.0)
    blade = np.clip(1.0 - d[0] / 0.30, 0, 1) * (np.mod(cid * 5.9, 1.0) < 0.30)
    blade = D.blur_wrap(blade, (2.1, 0.5))
    acc += blade * 0.45

    # a couple of very faint groomed sweeps (the apron is mown too)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    sweep = np.sin((xx * 0.86 + yy * 0.51) * 2 * np.pi / size * 4
                   + D.fbm(size, 3, 2, 0.5, 2, 91) * 3.0) * 0.5 + 0.5
    acc = acc * 0.90 + sweep * 0.10

    acc = D.normalise(acc, 0.06, 0.94)
    acc = acc + (0.5 - acc.mean())
    D.save(D.outpath("Ground", "apron_grass_detail_512.png"), np.clip(acc, 0, 1))


# --------------------------------------------------------------------------------------
def soil_scuff(size=SIZE):
    """Greyscale wear mask: 1 = bare scuffed soil, 0 = untouched turf. Blobby,
    trodden, with feathered edges and a few scattered scrapes. Mean is kept low
    (~0.22) so it is used sparingly where it is projected."""
    big = D.fbm(size, 4, 5, 0.58, 2, 3131)
    wx = D.fbm(size, 6, 3, 0.5, 2, 3201)
    wy = D.fbm(size, 6, 3, 0.5, 2, 3301)
    big = D.warp(big, wx, wy, 34)

    # wide, soft ramp - wear fades out over ~0.4 m rather than cutting off
    patch = np.clip((big - 0.42) / 0.42, 0, 1)
    patch = patch ** 1.35

    # eaten edges: mid-frequency noise nibbles the boundary only where it is soft
    edge = D.fbm(size, 22, 4, 0.5, 2, 3407)
    softness = 1.0 - np.abs(patch * 2 - 1)
    patch = np.clip(patch * (1.0 + 0.85 * (edge - 0.5) * softness), 0, 1)

    # second, smaller patch scale so wear exists at two sizes
    small = D.fbm(size, 11, 4, 0.55, 2, 3719)
    small = D.warp(small, D.perlin(size, 14, 3803), D.perlin(size, 14, 3821), 12)
    small = np.clip((small - 0.60) / 0.28, 0, 1) ** 1.4 * 0.72

    grain = D.fbm(size, 48, 3, 0.5, 2, 3607)
    out = np.clip(np.maximum(patch, small) * (0.82 + 0.30 * grain), 0, 1)
    out = D.blur_wrap(out, 1.4)
    out = np.clip(out * 1.30, 0, 1)
    print("  soil_scuff mean %.3f" % out.mean())
    D.save(D.outpath("Ground", "soil_scuff_512.png"), out)


if __name__ == "__main__":
    print("dirt path...");  dirt_path()
    print("gravel...");     gravel()
    print("apron grass..."); apron_grass_detail()
    print("soil scuff...");  soil_scuff()
    print("done")
