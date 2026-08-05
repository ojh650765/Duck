"""DUCK MOW - UI/ banner, timer ring, scorecard, score icons."""
import numpy as np
from PIL import Image, ImageFilter
import duckart as D
import uikit as U
import letters as L

SLICES = {}


# ======================================================================================
def banner_ribbon(w=512, h=256):
    """Subject-announcement ribbon: a sagging cream sign panel carried on a red
    ribbon with swallowtail tails. 9-sliceable horizontally only - keep the vertical
    border at the full panel height and stretch across."""
    rgb, a = U.blank(w, h)
    cy = h * 0.46
    band_x0, band_x1 = 100, w - 100
    band_hh = 52
    sag = 13.0

    def edge(x, off):
        t = (np.asarray(x, dtype=np.float64) - band_x0) / (band_x1 - band_x0)
        return cy + off + sag * np.sin(np.clip(t, 0, 1) * np.pi)

    xs = np.linspace(band_x0, band_x1, 60)
    band_pts = ([(x, edge(x, -band_hh)) for x in xs] +
                [(x, edge(x, band_hh)) for x in xs[::-1]])

    # ---- tails ------------------------------------------------------------------------
    tails = []
    for side in (-1, 1):
        ix = band_x0 + 6 if side < 0 else band_x1 - 6
        ox = 12 if side < 0 else w - 12
        inner_top = edge(ix, -band_hh - 4)
        inner_bot = edge(ix, band_hh + 4)
        drop = 16
        tails.append(D.wobble_poly(
            [(ix, inner_top), (ox, inner_top - drop + 26), (ox - side * 30, cy + 4),
             (ox, inner_bot + drop + 4), (ix, inner_bot)], amp=1.4, seed=int(ix)))

    mt = U.Mask(w, h)
    for t in tails:
        mt.poly(t)
    tail_mask = mt.resolve()

    mb = U.Mask(w, h)
    mb.poly(D.wobble_poly(band_pts, amp=1.2, seed=5, freq=2.0))
    band_mask = mb.resolve()

    whole = np.clip(tail_mask + band_mask, 0, 1)
    sh = U.soft_shadow(whole, dy=7, blur=6.0, opacity=0.36)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    # ---- tail cloth -------------------------------------------------------------------
    g = U.vgrad(w, h)
    tail_col = D.mix(D.tint(U.RED, 0.16), D.shade(U.RED, 0.30), g ** 0.9)
    # cloth folds running out along each tail
    fold = U.stripes(w, h, period=17.0, angle=8.0, duty=0.5, softness=6.0)
    tail_col = tail_col * (0.90 + 0.16 * fold)[..., None]
    tail_col *= U.grain(w, h, seed=77, strength=0.05, freq=46)[..., None]
    rgb, a = U.over(rgb, a, tail_col, tail_mask)

    # the tails pass *behind* the panel, so darken where they meet it
    tuck = np.asarray(Image.fromarray(D.to8(band_mask), "L")
                      .filter(ImageFilter.GaussianBlur(7.0)), dtype=np.float64) / 255.0
    rgb, a = U.over(rgb, a, D.shade(U.RED, 0.55), np.clip(tuck - band_mask, 0, 1) * tail_mask * 0.85)

    # ---- the sign panel ---------------------------------------------------------------
    face = D.mix(D.tint(U.CREAM, 0.14), D.mix(U.CREAM, "wood_warm", 0.16), g ** 1.1)
    face *= U.grain(w, h, seed=79, strength=0.055, freq=50)[..., None]
    rgb, a = U.over(rgb, a, face, band_mask)

    # red top and bottom rails on the panel
    rail = U.Mask(w, h)
    rail.poly([(x, edge(x, -band_hh)) for x in xs] +
              [(x, edge(x, -band_hh + 13)) for x in xs[::-1]])
    rail.poly([(x, edge(x, band_hh - 13)) for x in xs] +
              [(x, edge(x, band_hh)) for x in xs[::-1]])
    rgb, a = U.over(rgb, a, D.hexcol(U.RED), rail.resolve() * band_mask * 0.95)

    # a hand-ruled cream keyline just inside the rails
    key = U.Mask(w, h)
    key.stroke([(x, edge(x, -band_hh + 17)) for x in xs], 2.0, closed=False)
    key.stroke([(x, edge(x, band_hh - 17)) for x in xs], 2.0, closed=False)
    rgb, a = U.over(rgb, a, D.tint(U.CREAM, 0.5), key.resolve() * band_mask * 0.55)

    # ---- ink outline over everything ---------------------------------------------------
    mo = U.Mask(w, h)
    mo.stroke(D.wobble_poly(band_pts, amp=1.2, seed=5, freq=2.0), 3.0)
    for t in tails:
        mo.stroke(t, 3.0)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * 0.90)

    # brass eyelets where the panel is laced to the ribbon
    me = U.Mask(w, h)
    for ex in (band_x0 + 16, band_x1 - 16):
        me.ellipse(ex, edge(ex, 0), 5.0, 5.0)
    ring = me.resolve()
    me2 = U.Mask(w, h)
    for ex in (band_x0 + 16, band_x1 - 16):
        me2.ellipse(ex, edge(ex, 0), 2.2, 2.2)
    hole = me2.resolve()
    rgb, a = U.over(rgb, a, D.mix(U.BRASS, "sun", 0.3), np.clip(ring - hole, 0, 1) * 0.95)
    rgb, a = U.over(rgb, a, D.shade(U.TIMBER, 0.5), hole * 0.85)

    SLICES["banner_ribbon_512.png"] = (band_x0 + 40, 0, w - band_x1 + 40, 0)
    U.save_ui("banner_ribbon_512.png", rgb, a)


# ======================================================================================
def timer_ring(w=256, h=256):
    """Radial-fill ring for the round timer. Use as the Filled/Radial360 image;
    the tick marks are baked into the band so they sweep away with the fill."""
    rgb, a = U.blank(w, h)
    cx, cy = w / 2, h / 2 + 1
    R, r = 112.0, 84.0

    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    dx, dy = xx - cx, yy - cy
    rad = np.sqrt(dx * dx + dy * dy)
    th = np.arctan2(-dy, dx)

    def aa(v, k=1.2):
        return np.clip(v / k + 0.5, 0, 1)

    band = aa(R - rad) * aa(rad - r)

    sh = U.soft_shadow(band, dy=5, blur=5.0, opacity=0.34)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    # radial shading: bright outer bevel, warm core, darker inner lip
    t = np.clip((rad - r) / (R - r), 0, 1)
    col = D.mix(D.mix("duck_orange", "sun", 0.55), D.mix("duck_orange", "duck_orange_dark", 0.5),
                np.clip((t - 0.15) / 0.85, 0, 1) ** 0.9)
    col = D.mix(col, D.tint("sun", 0.35), np.clip(1 - np.abs(t - 0.30) / 0.22, 0, 1) * 0.45)
    col *= U.grain(w, h, seed=91, strength=0.04, freq=44)[..., None]
    rgb, a = U.over(rgb, a, col, band)

    # ---- tick marks: 12 minor notches cut into the outer edge, 4 major full-depth ----
    ticks = np.zeros((h, w))
    for i in range(12):
        ang = np.pi / 2 - i * (2 * np.pi / 12)
        dth = np.abs(((th - ang + np.pi) % (2 * np.pi)) - np.pi)
        major = (i % 3 == 0)
        wdt = 0.052 if major else 0.030
        depth = r + (R - r) * (0.00 if major else 0.52)
        m = np.clip((wdt - dth) * 60, 0, 1) * aa(R - rad) * aa(rad - depth)
        ticks = np.maximum(ticks, m)
    rgb, a = U.over(rgb, a, D.shade("duck_orange_dark", 0.45), ticks * band * 0.85)

    # ---- ink rims ---------------------------------------------------------------------
    rim = np.clip(np.clip((rad - (R - 2.6)) / 1.2 + 0.5, 0, 1) * aa(R - rad), 0, 1)
    rim2 = np.clip(np.clip(((r + 2.6) - rad) / 1.2 + 0.5, 0, 1) * aa(rad - r), 0, 1)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), np.clip(rim + rim2, 0, 1) * 0.85)

    U.save_ui("timer_ring_256.png", rgb, a)


# ======================================================================================
def scorecard(w=192, h=256):
    """The card a judge holds up. Blank cream face with a big clear number well;
    render the digits on top at runtime."""
    rgb, a = U.blank(w, h)
    pad = 12
    x0, y0, x1, y1 = pad, pad - 2, w - pad, h - pad - 8

    pts = D.wobble_poly(U.rrect(x0, y0, x1, y1, 9), 1.3, seed=3, freq=1.0)
    m = U.Mask(w, h); m.poly(pts)
    shape = m.resolve()

    sh = U.soft_shadow(shape, dy=6, blur=5.5, opacity=0.36)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    g = U.vgrad(w, h)
    face = D.mix(D.tint("duck_cream", 0.18), D.mix("duck_cream", "duck_shadow_cream", 0.45),
                 g ** 1.25)
    face *= U.grain(w, h, seed=13, strength=0.07, freq=54)[..., None]
    # thumb-worn bottom corner
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    thumb = np.exp(-(((xx - x1 + 16) / 26.0) ** 2 + ((yy - y1 + 14) / 22.0) ** 2)) * 0.16
    face *= (1 - thumb)[..., None]
    rgb, a = U.over(rgb, a, face, shape)

    # red header and footer bands - a judging card, not a notepad
    mh = U.Mask(w, h)
    mh.poly(D.wobble_poly([(x0, y0), (x1, y0), (x1, y0 + 24), (x0, y0 + 24)], 1.0, 4))
    mh.poly(D.wobble_poly([(x0, y1 - 15), (x1, y1 - 15), (x1, y1), (x0, y1)], 1.0, 6))
    rgb, a = U.over(rgb, a, D.hexcol(U.RED), mh.resolve() * shape * 0.95)

    # the number well: a ruled box in the middle
    mw = U.Mask(w, h)
    mw.stroke(D.wobble_poly(U.rrect(x0 + 16, y0 + 40, x1 - 16, y1 - 30, 8), 1.0, 8), 2.0)
    rgb, a = U.over(rgb, a, D.mix(U.INK, U.RED, 0.35), mw.resolve() * shape * 0.55)

    # three little judging pips in the header
    mp = U.Mask(w, h)
    for i in range(3):
        mp.ellipse(w / 2 + (i - 1) * 18, y0 + 12, 3.6, 3.6)
    rgb, a = U.over(rgb, a, D.tint(U.CREAM, 0.35), mp.resolve() * shape * 0.9)

    mo = U.Mask(w, h); mo.stroke(pts, 2.8)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * 0.9)

    SLICES["scorecard_blank_256.png"] = (28, 30, 28, 42)
    U.save_ui("scorecard_blank_256.png", rgb, a)


# ======================================================================================
def _icon_base(size, draw_fn, name, ink_w=5.0, seed=0):
    """Common icon treatment: painted shape, ink outline, small drop shadow."""
    w = h = size
    rgb, a = U.blank(w, h)
    fill_mask, ink_mask, colour = draw_fn(w, h)

    whole = np.clip(fill_mask + ink_mask, 0, 1)
    sh = U.soft_shadow(whole, dy=4, blur=3.5, opacity=0.34)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    col = colour * U.grain(w, h, seed=seed + 3, strength=0.05, freq=34)[..., None] \
        if np.ndim(colour) == 3 else colour
    rgb, a = U.over(rgb, a, col, fill_mask)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), ink_mask * 0.92)
    U.save_ui(name, rgb, a)


def icon_speed(size=128):
    def f(w, h):
        m = U.Mask(w, h); mi = U.Mask(w, h)
        # two right-pointing chevrons with trailing speed lines behind them
        def chev(x, s, seed):
            return D.wobble_poly([(x * w, 0.17 * h), ((x + 0.20) * w, 0.50 * h),
                                  (x * w, 0.83 * h), ((x - 0.10) * w, 0.83 * h),
                                  ((x + 0.10) * w, 0.50 * h), ((x - 0.10) * w, 0.17 * h)],
                                 1.5, seed)
        for x, sd in ((0.66, 1), (0.44, 2)):
            c = chev(x, 1.0, sd)
            m.poly(c); mi.stroke(c, 4.0)
        for yy, ln in [(0.27, 0.20), (0.50, 0.27), (0.73, 0.16)]:
            mi.stroke([(0.05 * w, yy * h), ((0.05 + ln) * w, yy * h)], 5.4, closed=False)
        g = U.vgrad(w, h)
        col = D.mix(D.tint("mower_red", 0.25), D.shade(U.RED, 0.15), g)
        return m.resolve(), mi.resolve(), col
    _icon_base(size, f, "icon_speed_128.png", seed=1)


def icon_accuracy(size=128):
    def f(w, h):
        m = U.Mask(w, h); mi = U.Mask(w, h)
        cx, cy = 0.47 * w, 0.53 * h
        m.ellipse(cx, cy, 0.36 * w, 0.36 * h)
        mi.stroke(L.arc(cx, cy, 0.36 * w, 0.36 * h, 0, 360, 60), 4.2)
        mi.stroke(L.arc(cx, cy, 0.23 * w, 0.23 * h, 0, 360, 48), 4.0)
        mi.ellipse(cx, cy, 0.085 * w, 0.085 * h)
        # a chalk-stroke tick landing dead centre
        mi.stroke([(0.60 * w, 0.13 * h), (0.52 * w, 0.44 * h), (cx, cy)], 4.6, closed=False)
        ring = m.resolve()
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
        rr = np.sqrt(((xx - cx) / (0.36 * w)) ** 2 + ((yy - cy) / (0.36 * h)) ** 2)
        col = D.mix(D.tint(U.CREAM, 0.25), D.hexcol(U.RED), np.clip(1 - np.abs(rr - 0.45) * 2.4, 0, 1))
        col = D.mix(col, D.hexcol(U.RED), np.clip(1 - rr / 0.20, 0, 1))
        return ring, mi.resolve(), col
    _icon_base(size, f, "icon_accuracy_128.png", seed=2)


def icon_coverage(size=128):
    def f(w, h):
        m = U.Mask(w, h); mi = U.Mask(w, h)
        pts = D.wobble_poly(U.rrect(0.13 * w, 0.16 * h, 0.87 * w, 0.84 * h, 0.10 * w), 1.6, 3)
        m.poly(pts); mi.stroke(pts, 4.4)
        # a mown corner: stripes filling two thirds of the plot
        shape = m.resolve()
        st = U.stripes(w, h, period=0.115 * w, angle=90, duty=0.5, softness=1.6)
        cut = np.clip(((0.66 * w) - np.mgrid[0:h, 0:w][1].astype(np.float64)) / 6.0 + 0.5, 0, 1)
        base = D.mix("uncut_base", "uncut_tip", 0.35)
        mown = D.mix("cut_base", "stripe_light", st)
        col = D.mix(base, mown, cut)
        # the mower's cut edge, drawn dark like the lawn shader does
        edge = np.clip(1 - np.abs(cut - 0.5) * 5.0, 0, 1)
        col = D.mix(col, "cut_edge_shadow", edge * 0.55)
        return shape, mi.resolve(), col
    _icon_base(size, f, "icon_coverage_128.png", seed=3)


def icon_style(size=128):
    def f(w, h):
        m = U.Mask(w, h); mi = U.Mask(w, h)
        cx, cy = 0.48 * w, 0.50 * h
        pts = []
        for i in range(10):
            ang = np.pi / 2 + i * np.pi / 5
            rr = (0.40 if i % 2 == 0 else 0.175) * w
            pts.append((cx + rr * np.cos(ang), cy - rr * np.sin(ang)))
        pts = D.wobble_poly(pts, 1.8, 4)
        m.poly(pts); mi.stroke(pts, 4.2)
        # two little sparkles, because style is showing off
        for (sx, sy, s) in [(0.87 * w, 0.19 * h, 0.085 * w), (0.15 * w, 0.81 * h, 0.062 * w)]:
            spk = [(sx, sy - s), (sx + s * 0.22, sy - s * 0.22), (sx + s, sy),
                   (sx + s * 0.22, sy + s * 0.22), (sx, sy + s),
                   (sx - s * 0.22, sy + s * 0.22), (sx - s, sy),
                   (sx - s * 0.22, sy - s * 0.22)]
            m.poly(spk); mi.stroke(spk, 2.6)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
        rr = np.sqrt(((xx - cx) / (0.4 * w)) ** 2 + ((yy - cy) / (0.4 * h)) ** 2)
        col = D.mix(D.tint("sun", 0.25), D.mix(U.BRASS, "duck_orange", 0.45), np.clip(rr, 0, 1) ** 0.8)
        return m.resolve(), mi.resolve(), col
    _icon_base(size, f, "icon_style_128.png", seed=4)


if __name__ == "__main__":
    banner_ribbon(); timer_ring(); scorecard()
    icon_speed(); icon_accuracy(); icon_coverage(); icon_style()
    for k, v in SLICES.items():
        print("  %-30s border L,B,R,T = %s" % (k, v))
    print("done")
