"""DUCK MOW - UI/ panels, buttons, bars, frames, vignette.

9-slice borders are reported by each function and collected in SLICES.
"""
import numpy as np
from PIL import Image, ImageFilter
import duckart as D
import uikit as U

SLICES = {}


# ======================================================================================
def _card(w, h, base, ink, accent, name, pad=13, radius=22, seed=1, corner_pins=True,
          rule=True):
    rgb, a = U.blank(w, h)
    x0, y0, x1, y1 = pad, pad - 3, w - pad, h - pad - 3

    shape, pts = U.mask_rrect(w, h, x0, y0, x1, y1, radius, wob=1.1, seed=seed)

    # --- soft drop shadow -------------------------------------------------------------
    sh = U.soft_shadow(shape, dx=0, dy=6, blur=5.5, opacity=0.34)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    # --- card stock -------------------------------------------------------------------
    g = U.vgrad(w, h)
    body = D.mix(D.tint(base, 0.10), D.mix(base, "wood_warm", 0.14), g ** 1.2)
    body = body * U.grain(w, h, seed=seed * 3, strength=0.055, freq=52)[..., None]
    # very slight vignetting into the corners, like light falling across card stock
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    rad = np.sqrt(((xx / w - 0.5) * 1.1) ** 2 + ((yy / h - 0.5) * 1.1) ** 2)
    body *= (1.0 - np.clip(rad - 0.34, 0, 1) * 0.16)[..., None]
    rgb, a = U.over(rgb, a, body, shape)

    # --- painted outline --------------------------------------------------------------
    mo = U.Mask(w, h)
    mo.stroke(pts, 3.4)
    outline = mo.resolve()
    rgb, a = U.over(rgb, a, D.hexcol(ink), outline * 0.95)

    # --- hand-ruled double line, the way a fair sign is bordered ----------------------
    if rule:
        mi = U.Mask(w, h)
        ip = D.wobble_poly(U.rrect(x0 + 9, y0 + 9, x1 - 9, y1 - 9, radius - 8), 0.9, seed + 5)
        mi.stroke(ip, 3.0)
        rgb, a = U.over(rgb, a, D.hexcol(accent), mi.resolve() * 0.92)
        mi2 = U.Mask(w, h)
        ip2 = D.wobble_poly(U.rrect(x0 + 15, y0 + 15, x1 - 15, y1 - 15, radius - 13), 0.8, seed + 6)
        mi2.stroke(ip2, 1.3)
        rgb, a = U.over(rgb, a, D.hexcol(accent), mi2.resolve() * 0.62)

    # --- brass pins in the corners ----------------------------------------------------
    if corner_pins:
        mp = U.Mask(w, h)
        for px, py in [(x0 + 25, y0 + 25), (x1 - 25, y0 + 25), (x0 + 25, y1 - 25), (x1 - 25, y1 - 25)]:
            mp.ellipse(px, py, 3.6, 3.6)
        pin = mp.resolve()
        pin_col = D.mix(U.BRASS, "sun", 0.25)
        rgb, a = U.over(rgb, a, pin_col, pin * 0.95)
        mp2 = U.Mask(w, h)
        for px, py in [(x0 + 25, y0 + 25), (x1 - 25, y0 + 25), (x0 + 25, y1 - 25), (x1 - 25, y1 - 25)]:
            mp2.ellipse(px - 1.0, py - 1.0, 1.4, 1.4)
        rgb, a = U.over(rgb, a, D.tint(U.BRASS, 0.55), mp2.resolve() * 0.9)

    b = pad + radius + 22
    SLICES[name] = (b, b, b, b + 4)
    U.save_ui(name, rgb, a)
    return b


def panel_card(w=256, h=256):
    _card(w, h, U.CREAM, U.INK, U.RED, "panel_card_256.png", seed=1)


def panel_card_dark(w=256, h=256):
    _card(w, h, D.mix("wood_warm", "wood_dark", 0.55), D.shade("wood_dark", 0.45),
          D.mix("tent_cream", "brass", 0.55), "panel_card_dark_256.png", seed=9)


# ======================================================================================
def _button(w, h, name, pressed=False, seed=3):
    rgb, a = U.blank(w, h)
    pad = 12
    drop = 7 if not pressed else 2
    x0, y0 = pad, pad - 4
    x1, y1 = w - pad, h - pad - 8 + (0 if not pressed else 4)
    radius = (y1 - y0) * 0.34

    shape, pts = U.mask_rrect(w, h, x0, y0, x1, y1, radius, wob=0.9, seed=seed)

    # the "back plate" gives the button physical thickness (a painted wooden token)
    plate, ppts = U.mask_rrect(w, h, x0, y0 + drop, x1, y1 + drop, radius, wob=0.9, seed=seed)
    sh = U.soft_shadow(plate, dx=0, dy=4, blur=4.5, opacity=0.33)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)
    rgb, a = U.over(rgb, a, D.shade(U.RED, 0.55), plate)

    face = D.mix(U.RED, "mower_red", 0.35) if not pressed else D.shade(D.mix(U.RED, "mower_red", 0.35), 0.30)
    g = U.vgrad(w, h)
    body = D.mix(D.tint(face, 0.20), D.shade(face, 0.18), g ** 1.1)
    if pressed:
        body = D.mix(D.shade(face, 0.10), D.tint(face, 0.06), g ** 1.1)
    body = body * U.grain(w, h, seed=seed * 5, strength=0.045, freq=44)[..., None]
    rgb, a = U.over(rgb, a, body, shape)

    # cream inner panel - the label area on a fairground button
    inner, ipts = U.mask_rrect(w, h, x0 + 7, y0 + 6, x1 - 7, y1 - 7, radius - 5, wob=0.8, seed=seed + 2)
    icol = D.mix(U.CREAM, "duck_cream", 0.4)
    if pressed:
        icol = D.shade(icol, 0.13)
    icol = np.ones((h, w, 3)) * icol[None, None, :]
    icol = icol * U.bevel(inner, light=(-0.4, -0.75), width=2.0,
                          up=0.10 if not pressed else 0.03,
                          down=0.16 if not pressed else 0.22)[..., None]
    icol = icol * U.grain(w, h, seed=seed * 11, strength=0.05, freq=40)[..., None]
    rgb, a = U.over(rgb, a, icol, inner)

    mo = U.Mask(w, h)
    mo.stroke(pts, 2.4)
    mo.stroke(ipts, 1.5)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * (0.88 if not pressed else 0.94))

    # top highlight sliver, only on the raised state
    if not pressed:
        hl = U.Mask(w, h)
        hl.stroke(U.rrect(x0 + 5, y0 + 4, x1 - 5, y1 - 4, radius - 3), 2.2)
        hlm = hl.resolve() * np.clip(1.0 - U.vgrad(w, h) * 2.4, 0, 1)
        rgb, a = U.over(rgb, a, D.tint(U.RED, 0.55), hlm * 0.45)

    b_h = int(pad + radius + 6)
    b_v = int(pad + radius * 0.9)
    SLICES[name] = (b_h, b_v + 2, b_h, b_v + 10)
    U.save_ui(name, rgb, a)


def button(w=256, h=128):
    _button(w, h, "button_256.png", pressed=False)


def button_pressed(w=256, h=128):
    _button(w, h, "button_pressed_256.png", pressed=True)


# ======================================================================================
def progress_bar(w=256, h=48):
    # ---- background trough -----------------------------------------------------------
    rgb, a = U.blank(w, h)
    pad = 8
    x0, y0, x1, y1 = pad, pad, w - pad, h - pad
    r = (y1 - y0) * 0.5
    shape, pts = U.mask_rrect(w, h, x0, y0, x1, y1, r, wob=0.5, seed=21)
    sh = U.soft_shadow(shape, dy=4, blur=4.0, opacity=0.30)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    trough = D.mix("wood_warm", "wood_dark", 0.62)
    body = np.ones((h, w, 3)) * trough[None, None, :]
    body *= (0.80 + 0.34 * U.vgrad(w, h) ** 0.7)[..., None]     # lit from below inside
    body *= U.grain(w, h, seed=41, strength=0.05, freq=40)[..., None]
    rgb, a = U.over(rgb, a, body, shape)

    mo = U.Mask(w, h); mo.stroke(pts, 2.4)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * 0.9)
    SLICES["progress_bar_bg_256.png"] = (int(pad + r), int(pad + r * 0.7),
                                         int(pad + r), int(pad + r * 0.7) + 4)
    U.save_ui("progress_bar_bg_256.png", rgb, a)

    # ---- fill ------------------------------------------------------------------------
    rgb, a = U.blank(w, h)
    fx0, fy0, fx1, fy1 = pad + 3, pad + 3, w - pad - 3, h - pad - 3
    fr = (fy1 - fy0) * 0.5
    fshape, fpts = U.mask_rrect(w, h, fx0, fy0, fx1, fy1, fr, wob=0.4, seed=22)

    g = U.vgrad(w, h)
    fill = D.mix(D.tint("cut_tip", 0.22), D.mix("cut_base", "stripe_dark", 0.5), g ** 0.9)
    # mow-stripe candy banding, the same language as the lawn
    st = U.stripes(w, h, period=15.0, angle=68.0, duty=0.5, softness=2.2)
    fill = fill * (0.94 + 0.11 * st)[..., None]
    fill *= U.grain(w, h, seed=43, strength=0.035, freq=40)[..., None]
    rgb, a = U.over(rgb, a, fill, fshape)

    # glossy top sliver
    hl = U.Mask(w, h)
    hl.poly(U.rrect(fx0 + 3, fy0 + 2, fx1 - 3, fy0 + (fy1 - fy0) * 0.42, fr * 0.55))
    rgb, a = U.over(rgb, a, D.tint("cut_tip", 0.62), hl.resolve() * 0.30 * fshape)

    mo = U.Mask(w, h); mo.stroke(fpts, 1.7)
    rgb, a = U.over(rgb, a, D.shade("cut_base", 0.45), mo.resolve() * 0.75)
    SLICES["progress_bar_fill_256.png"] = (int(pad + fr), int(pad + fr * 0.7),
                                           int(pad + fr), int(pad + fr * 0.7))
    U.save_ui("progress_bar_fill_256.png", rgb, a)


# ======================================================================================
def boost_gauge(w=256, h=72):
    """Gauge HOUSING with a transparent window: put progress_bar_fill (or any fill
    image) behind it. Brass rim, eight segment dividers, a little flame plate."""
    rgb, a = U.blank(w, h)
    pad = 7
    x0, y0, x1, y1 = pad, pad, w - pad, h - pad - 6
    r = (y1 - y0) * 0.42

    outer, opts = U.mask_rrect(w, h, x0, y0, x1, y1, r, wob=0.5, seed=31)
    win_x0 = x0 + 30
    window, wpts = U.mask_rrect(w, h, win_x0, y0 + 8, x1 - 9, y1 - 8, r * 0.52, wob=0.4, seed=32)

    ring = np.clip(outer - window, 0, 1)
    sh = U.soft_shadow(outer, dy=5, blur=4.5, opacity=0.34)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    g = U.vgrad(w, h)
    housing = D.mix(D.tint(U.BRASS, 0.34), D.shade(U.BRASS, 0.34), g ** 0.85)
    housing = housing * U.bevel(outer, light=(-0.3, -0.85), width=2.4, up=0.20, down=0.24)[..., None]
    housing *= U.grain(w, h, seed=51, strength=0.05, freq=44)[..., None]
    rgb, a = U.over(rgb, a, housing, ring)

    # segment dividers across the window
    seg = U.Mask(w, h)
    for i in range(1, 8):
        sx = win_x0 + (x1 - 9 - win_x0) * i / 8.0
        seg.rect(sx - 0.9, y0 + 8, sx + 0.9, y1 - 8)
    segm = seg.resolve() * window
    rgb, a = U.over(rgb, a, D.shade(U.BRASS, 0.55), segm * 0.85)

    # inner shadow inside the window so the fill sits *in* the gauge
    ish = np.asarray(Image.fromarray(D.to8(1 - window), "L")
                     .filter(ImageFilter.GaussianBlur(2.6)), dtype=np.float64) / 255.0
    rgb, a = U.over(rgb, a, D.shade(U.BRASS, 0.6), np.clip(ish - (1 - window), 0, 1) * window * 0.55)

    # the little boost plate on the left: a chevron badge
    fl = U.Mask(w, h)
    cx, cy = x0 + 16, (y0 + y1) / 2
    for k, off in enumerate((-7.5, 0.5, 8.5)):
        fl.poly([(cx - 7, cy + off - 4.0), (cx + 1.5, cy + off), (cx - 7, cy + off + 4.0),
                 (cx - 3.5, cy + off)])
    rgb, a = U.over(rgb, a, D.mix("duck_orange", "sun", 0.30), fl.resolve() * 0.95)

    mo = U.Mask(w, h)
    mo.stroke(opts, 2.3)
    mo.stroke(wpts, 1.6)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * 0.85)

    SLICES["boost_gauge_256.png"] = (int(win_x0 + 8), int(pad + 12), int(pad + 14), int(pad + 16))
    U.save_ui("boost_gauge_256.png", rgb, a)


# ======================================================================================
def minimap_frame(w=256, h=256):
    """Timber frame with a transparent window and painted corner brackets."""
    rgb, a = U.blank(w, h)
    pad = 10
    x0, y0, x1, y1 = pad, pad - 2, w - pad, h - pad - 6
    r = 16
    outer, opts = U.mask_rrect(w, h, x0, y0, x1, y1, r, wob=1.0, seed=61)
    t = 17
    window, wpts = U.mask_rrect(w, h, x0 + t, y0 + t, x1 - t, y1 - t, r * 0.55, wob=0.8, seed=62)
    ring = np.clip(outer - window, 0, 1)

    sh = U.soft_shadow(outer, dy=6, blur=5.5, opacity=0.34)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)

    # timber with a wood grain that runs round the frame
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    edge_t = np.minimum(np.minimum(xx - x0, x1 - xx), np.minimum(yy - y0, y1 - yy))
    grainf = np.sin(edge_t * 1.15 + D.fbm(max(w, h), 7, 3, 0.5, 2, 63)[:h, :w] * 5.0)
    timber = D.mix(D.tint(U.TIMBER, 0.16), D.shade(U.TIMBER, 0.22), 0.5 + 0.5 * grainf)
    timber = timber * U.bevel(ring, light=(-0.45, -0.7), width=2.6, up=0.18, down=0.22)[..., None]
    timber *= U.grain(w, h, seed=64, strength=0.05, freq=46)[..., None]
    rgb, a = U.over(rgb, a, timber, ring)

    # cream inlay line
    ml = U.Mask(w, h)
    ml.stroke(D.wobble_poly(U.rrect(x0 + 6, y0 + 6, x1 - 6, y1 - 6, r - 5), 0.8, 65), 1.6)
    rgb, a = U.over(rgb, a, D.hexcol(U.CREAM), ml.resolve() * ring * 0.7)

    # corner brackets, red painted
    mb = U.Mask(w, h)
    L = 26
    for (sx, sy, dx, dy) in [(x0 + 3, y0 + 3, 1, 1), (x1 - 3, y0 + 3, -1, 1),
                             (x0 + 3, y1 - 3, 1, -1), (x1 - 3, y1 - 3, -1, -1)]:
        mb.poly(D.wobble_poly([(sx, sy), (sx + dx * L, sy), (sx + dx * L, sy + dy * 5),
                               (sx + dx * 5, sy + dy * 5), (sx + dx * 5, sy + dy * L),
                               (sx, sy + dy * L)], 0.7, int(sx + sy)))
    rgb, a = U.over(rgb, a, D.hexcol(U.RED), mb.resolve() * 0.92)

    mo = U.Mask(w, h)
    mo.stroke(opts, 2.4)
    mo.stroke(wpts, 2.0)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), mo.resolve() * 0.88)

    b = pad + t + 8
    SLICES["minimap_frame_256.png"] = (b, b, b, b + 4)
    U.save_ui("minimap_frame_256.png", rgb, a)


# ======================================================================================
def vignette(w=512, h=512):
    """Screen-space vignette overlay. RGB is the ART_BIBLE split-tone shadow hue so it
    darkens without going grey; A is the falloff. Alpha-blend over the frame."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    x = (xx / (w - 1) - 0.5) * 2
    y = (yy / (h - 1) - 0.5) * 2
    # superellipse so 16:9 stretching keeps the corners heavier than the edges
    r = (np.abs(x) ** 2.4 + np.abs(y) ** 2.4) ** (1 / 2.4)
    a = np.clip((r - 0.52) / 0.62, 0, 1) ** 1.65 * 0.60

    # break the perfect radial symmetry very slightly so it is not a maths gradient
    n = D.fbm(max(w, h), 3, 3, 0.55, 2, 71)[:h, :w]
    a *= (0.86 + 0.28 * n)
    a = D.blur(a, 6.0)

    col = D.mix("split_shadow", "cut_edge_shadow", 0.62)
    col = D.shade(col, 0.30)
    rgb = np.ones((h, w, 3)) * col[None, None, :]
    U.save_ui("vignette_soft_512.png", rgb, np.clip(a, 0, 1))


if __name__ == "__main__":
    panel_card(); panel_card_dark()
    button(); button_pressed()
    progress_bar(); boost_gauge(); minimap_frame(); vignette()
    for k, v in SLICES.items():
        print("  %-30s border L,B,R,T = %s" % (k, v))
    print("done")
