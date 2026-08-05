"""DUCK MOW - UI/ prize rosettes for the five ranks.

S is a three-tier gold rosette with a starburst and three tails; D is a small,
crooked, half-frayed scrap with one bent pin. They are drawn on the same skeleton
so they read as the same family of prize, just further apart in generosity.

Palette: gold = brass #C9A55A pushed toward sun #FFF3D0; ranks A/C use tent_red
#D8534E and tent_cream #F5EAD6; B uses wood_warm #9A6B41 with brass; D is
tent_cream knocked back toward dirt #B99A6B, which is the bible's own "left out in
the weather" direction.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import duckart as D
import uikit as U
import letters as L

SSR = 4


# --------------------------------------------------------------------------------------
def _aa(v, k=1.6):
    return np.clip(v / k + 0.5, 0, 1)


def _polar(S, cx, cy):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    dx, dy = xx - cx, yy - cy
    return np.sqrt(dx * dx + dy * dy), np.arctan2(-dy, dx)


def pleat_ring(S, cx, cy, R, r_in, n, col_lit, col_dark, phase=0.0,
               scallop=0.075, crease=0.45, seed=0):
    """One tier of pleated ribbon. Returns (mask, rgb)."""
    rad, th = _polar(S, cx, cy)
    wob = 1.0 + 0.012 * np.sin(th * 3 + seed) + 0.010 * np.sin(th * 5 - seed)
    Rout = R * wob * (1.0 - scallop * 0.5 * (1.0 - np.cos(n * th + phase)))
    mask = _aa(Rout - rad, 1.8) * _aa(rad - r_in, 1.8)

    # pleat shading: light on the crown of each pleat, dark in the fold
    f = 0.5 + 0.5 * np.cos(n * th + phase)
    fold = f ** 0.8
    # a hard crease line right in each valley
    valley = np.clip(1.0 - np.abs(((n * th + phase) % (2 * np.pi)) - np.pi) / 0.30, 0, 1)

    t = np.clip((rad - r_in) / max(R - r_in, 1e-3), 0, 1)
    col = D.mix(col_dark, col_lit, fold)
    # the ribbon lifts away from the centre, so the inner edge sits in shade
    col = D.mix(D.shade(col_dark, 0.30), col, np.clip(t * 1.7, 0, 1) ** 0.7)
    # and catches light right on the outer rim
    col = D.mix(col, D.tint(col_lit, 0.35), np.clip((t - 0.78) / 0.22, 0, 1) * 0.5)
    col = D.mix(col, D.shade(col_dark, 0.42), valley * crease)
    return mask, col


def tail_poly(cx, cy, ang_deg, length, width, curve, notch, seed=0):
    n = 18
    pl, pr = [], []
    for i in range(n):
        t = i / (n - 1)
        ang = np.radians(ang_deg + curve * t * t)
        px = cx + np.sin(ang) * length * t
        py = cy + np.cos(ang) * length * t
        w = width * (0.82 + 0.42 * t)
        nx, ny = np.cos(ang), -np.sin(ang)
        pl.append((px - nx * w / 2, py - ny * w / 2))
        pr.append((px + nx * w / 2, py + ny * w / 2))
    ang = np.radians(ang_deg + curve)
    tipx = cx + np.sin(ang) * (length - notch)
    tipy = cy + np.cos(ang) * (length - notch)
    pts = pl + [(tipx, tipy)] + pr[::-1]
    return D.wobble_poly(pts, amp=length * 0.014, seed=seed, freq=2.0)


# --------------------------------------------------------------------------------------
def make_rosette(name, spec, size=256):
    S = size * SSR
    rgb = np.zeros((S, S, 3))
    a = np.zeros((S, S))

    tilt = np.radians(spec.get("tilt", 0.0))
    cx = S * (0.5 + spec.get("cx_off", 0.0))
    cy = S * spec.get("cy", 0.40)

    def sc(v):
        return v * SSR

    # ---- tails (behind everything) ---------------------------------------------------
    tim = Image.new("L", (S, S), 0)
    td = ImageDraw.Draw(tim)
    tpolys = []
    for k, (ang, ln, wd, cv, nt) in enumerate(spec["tails"]):
        p = tail_poly(cx, cy + sc(6), ang + spec.get("tilt", 0), sc(ln), sc(wd), cv, sc(nt),
                      seed=k * 7 + 1)
        tpolys.append(p)
        td.polygon(p, fill=255)
    tail_mask = np.asarray(tim, dtype=np.float64) / 255.0
    cover = np.zeros((S, S))          # everything drawn in front of the tails

    yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
    tcol_lit, tcol_dark = D.hexcol(spec["tail_lit"]), D.hexcol(spec["tail_dark"])
    folds = 0.5 + 0.5 * np.sin((xx * 0.55 + yy * 0.22) * (2 * np.pi / sc(17.0)))
    tcol = D.mix(D.shade(tcol_dark, 0.10), tcol_lit, folds ** 1.15 * 0.85)
    tcol = tcol * (0.88 + 0.22 * np.clip(1.0 - (yy - cy) / (S * 0.55), 0, 1))[..., None]
    rgb, a = U.over(rgb, a, tcol, tail_mask)

    # ---- starburst -------------------------------------------------------------------
    if spec.get("burst"):
        n, R, r0 = spec["burst"]
        rad, th = _polar(S, cx, cy)
        spike = np.abs(((th * n + np.pi) % (2 * np.pi)) - np.pi) / np.pi
        Rs = sc(r0) + (sc(R) - sc(r0)) * (1.0 - spike) ** 0.75
        bm = _aa(Rs - rad, 2.0)
        bcol = D.mix(D.mix("brass", "sun", 0.30), D.shade("brass", 0.30),
                     np.clip(rad / sc(R), 0, 1))
        rgb, a = U.over(rgb, a, bcol, bm)

    # ---- pleated tiers ---------------------------------------------------------------
    for k, ring in enumerate(spec["rings"]):
        R, r_in, n, lit, dark, ph = ring
        m, c = pleat_ring(S, cx, cy, sc(R), sc(r_in), n, lit, dark,
                          phase=ph + tilt * n, seed=k + 1)
        if spec.get("torn") and k == 0:
            # D's outer tier has a bite taken out of it
            rad, th = _polar(S, cx, cy)
            bite = np.clip(1.0 - np.abs(((th - 1.05 + np.pi) % (2 * np.pi)) - np.pi) / 0.42, 0, 1)
            m = m * (1.0 - bite * np.clip((rad - sc(r_in) * 1.05) / (sc(R) * 0.5), 0, 1))
        # each tier casts a small shadow on the one below
        if k > 0:
            pass
        rgb, a = U.over(rgb, a, c, m)
        cover = np.maximum(cover, m)
        if k + 1 < len(spec["rings"]):
            nxt = spec["rings"][k + 1]
            rad, _ = _polar(S, cx, cy)
            inner_sh = _aa(sc(nxt[0]) * 1.16 - rad, 6.0) * _aa(rad - sc(nxt[0]), 6.0)
            rgb, a = U.over(rgb, a, D.shade(D.hexcol(dark), 0.55), inner_sh * m * 0.38)

    # ---- medallion -------------------------------------------------------------------
    rad, th = _polar(S, cx, cy)
    MR = sc(spec["med_r"])
    med = _aa(MR - rad, 1.8)
    rim = _aa(MR - rad, 1.8) * _aa(rad - MR * 0.80, 1.8)

    face = D.mix(D.tint(spec["med_face"], 0.18), D.mix(spec["med_face"], "wood_warm", 0.22),
                 np.clip((yy - (cy - MR)) / (2 * MR), 0, 1) ** 1.1)
    face = face * U.grain(S, S, seed=17, strength=0.05, freq=90)[..., None]
    rgb, a = U.over(rgb, a, face, med)
    rimcol = D.mix(D.tint(spec["med_rim"], 0.35), D.shade(spec["med_rim"], 0.30),
                   np.clip((yy - (cy - MR)) / (2 * MR), 0, 1))
    rgb, a = U.over(rgb, a, rimcol, rim)
    cover = np.maximum(cover, med)

    # ---- letter ----------------------------------------------------------------------
    lim = Image.new("L", (S, S), 0)
    ld = ImageDraw.Draw(lim)
    gs = MR * 1.12
    L.draw_glyph(ld, spec["letter"], cx - gs * 0.40, cy - gs * 0.52, gs,
                 max(2, MR * 0.185), 255, wobble=0.006, seed=5)
    letter = np.asarray(lim, dtype=np.float64) / 255.0
    rgb, a = U.over(rgb, a, D.hexcol(spec["letter_col"]), letter * med)

    # ---- ink outlines ----------------------------------------------------------------
    ink = Image.new("L", (S, S), 0)
    idr = ImageDraw.Draw(ink)
    for p in tpolys:
        idr.line(list(p) + [p[0]], fill=255, width=int(sc(2.0)), joint="curve")
    inkm = np.asarray(ink, dtype=np.float64) / 255.0
    # the tail outline must not be drawn where a tier or the medallion covers it
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), inkm * 0.78 * (1 - np.clip(cover, 0, 1)))

    rimline = _aa(MR - rad, 1.4) * _aa(rad - (MR - sc(1.6)), 1.4)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), rimline * 0.7)

    # ---- sparkles for S ---------------------------------------------------------------
    for (sx, sy, ss) in spec.get("sparkles", []):
        sm = Image.new("L", (S, S), 0)
        sd = ImageDraw.Draw(sm)
        px, py, r = sc(sx), sc(sy), sc(ss)
        sd.polygon([(px, py - r), (px + r * .20, py - r * .20), (px + r, py),
                    (px + r * .20, py + r * .20), (px, py + r),
                    (px - r * .20, py + r * .20), (px - r, py),
                    (px - r * .20, py - r * .20)], fill=255)
        smm = np.asarray(sm, dtype=np.float64) / 255.0
        rgb, a = U.over(rgb, a, D.tint("sun", 0.45), smm * 0.95)

    # ---- resolve, then drop shadow ----------------------------------------------------
    img = Image.fromarray(D.to8(np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])), "RGBA") \
        .resize((size, size), Image.LANCZOS)
    arr = np.asarray(img, dtype=np.float64) / 255.0

    orgb, oa = U.blank(size, size)
    sh = U.soft_shadow(arr[..., 3], dy=5, blur=4.5, opacity=0.36)
    orgb, oa = U.over(orgb, oa, U.SHADOW, sh)
    orgb, oa = U.over(orgb, oa, arr[..., :3], arr[..., 3])
    U.save_ui(name, orgb, oa)


# --------------------------------------------------------------------------------------
GOLD = D.mix("brass", "sun", 0.58)
GOLD_D = D.mix("brass", "wood_warm", 0.35)
CREAM = D.hexcol("tent_cream")
CREAM_D = D.mix("tent_cream", "wood_warm", 0.30)
RED = D.hexcol("tent_red")
RED_D = D.shade("tent_red", 0.34)
TIMBER = D.hexcol("wood_warm")
TIMBER_D = D.shade("wood_warm", 0.32)
FADED = D.mix("tent_cream", "dirt", 0.55)
FADED_D = D.mix(D.shade("dirt", 0.30), "wood_dark", 0.30)


SPECS = {
    "rosette_S_256.png": dict(
        letter="S", cy=0.345, tilt=-3.0,
        burst=(18, 92, 56),
        rings=[(78, 48, 22, GOLD, GOLD_D, 0.0),
               (60, 36, 18, CREAM, CREAM_D, 0.35),
               (45, 19, 15, GOLD, GOLD_D, 0.8)],
        med_r=26, med_face=CREAM, med_rim=GOLD, letter_col="wood_dark",
        tails=[(-21, 128, 30, 15, 14), (0, 142, 32, 0, 16), (21, 128, 30, -15, 14)],
        tail_lit=GOLD, tail_dark=GOLD_D,
        sparkles=[(212, 40, 12), (40, 58, 9), (30, 132, 7)],
    ),
    "rosette_A_256.png": dict(
        letter="A", cy=0.355, tilt=2.0,
        rings=[(68, 42, 18, RED, RED_D, 0.0),
               (50, 17, 14, CREAM, CREAM_D, 0.4)],
        med_r=23, med_face=CREAM, med_rim=GOLD, letter_col="tent_red",
        tails=[(-17, 118, 27, 13, 12), (17, 118, 27, -13, 12)],
        tail_lit=RED, tail_dark=RED_D,
        sparkles=[(206, 52, 8)],
    ),
    "rosette_B_256.png": dict(
        letter="B", cy=0.365, tilt=-2.0,
        rings=[(60, 36, 16, D.mix(TIMBER, "brass", 0.50), TIMBER_D, 0.0),
               (43, 15, 12, CREAM, CREAM_D, 0.5)],
        med_r=20, med_face=CREAM, med_rim=D.mix("brass", "wood_warm", 0.4),
        letter_col="wood_dark",
        tails=[(-16, 100, 24, 12, 11), (16, 100, 24, -12, 11)],
        tail_lit=D.mix(TIMBER, "brass", 0.40), tail_dark=TIMBER_D,
    ),
    "rosette_C_256.png": dict(
        letter="C", cy=0.375, tilt=3.0,
        rings=[(48, 13, 13, CREAM, CREAM_D, 0.0)],
        med_r=18, med_face=D.mix(CREAM, "dirt", 0.18), med_rim=RED,
        letter_col="wood_dark",
        tails=[(9, 84, 21, -9, 10)],
        tail_lit=CREAM, tail_dark=CREAM_D,
    ),
    "rosette_D_256.png": dict(
        letter="D", cy=0.395, cx_off=-0.035, tilt=-12.0, torn=True,
        rings=[(36, 10, 9, FADED, FADED_D, 0.2)],
        med_r=14, med_face=D.mix(FADED, "dirt", 0.35), med_rim=D.mix("dirt", "wood_dark", 0.45),
        letter_col="wood_dark",
        tails=[(16, 62, 17, -28, 8)],
        tail_lit=FADED, tail_dark=FADED_D,
    ),
}


if __name__ == "__main__":
    for name, spec in SPECS.items():
        print("  " + name)
        make_rosette(name, spec)
    print("done")
