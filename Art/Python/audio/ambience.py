"""
DUCK MOW - Ambience/

Birds, wind through grass, the pond, and the windmill.

The birds are the piece that most easily gives away a synthesised soundscape,
so they are built as seven *distinct species* with their own pitch contours -
a bird call is almost a pure tone whose frequency moves fast, so the contour is
the whole identity - and spread over 12 s with silence between them.  Nothing
repeats inside the loop.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
import critter as C


# --------------------------------------------------------------------------
# birds
# --------------------------------------------------------------------------


def whistle(dur, pts, r, harm2=0.22, harm3=0.06, vib=0.0, vib_hz=0.0,
            noise=0.02, attack=0.006, release=0.03, res=None):
    """One bird note: a near-pure tone on a fast frequency contour."""
    n = ns(dur)
    f = C.curve(pts, n)
    if vib > 0:
        f = f * (1.0 + vib * np.sin(2 * np.pi * vib_hz * tt(n)))
    ph = phasor(f, n)
    y = (np.sin(2 * np.pi * ph)
         + harm2 * np.sin(4 * np.pi * ph)
         + harm3 * np.sin(6 * np.pi * ph))
    if noise > 0:
        y = y + noise * bandpass_tv(white(n, r), f * 1.4, 3.0)
    y = y * env_asr(n, attack, release, curve=1.3)
    if res:
        y = y + reson(y, res, 3.0) * 0.35
    return y / (np.max(np.abs(y)) + 1e-12)


def bird_two_note(r):
    """A clean descending pair - the 'chiff-chaff'."""
    n = ns(0.42)
    y = np.zeros(n)
    add(y, 0, whistle(0.10, [(0, 3350), (0.3, 3250), (1, 3150)], r,
                      harm2=0.14, attack=0.004, release=0.03), 1.0)
    add(y, ns(0.155), whistle(0.11, [(0, 2760), (0.3, 2680), (1, 2560)], r,
                              harm2=0.16, attack=0.004, release=0.035), 0.85)
    return y


def bird_trill(r):
    """A fast warbling trill."""
    n = ns(0.55)
    base = [(0, 2950), (0.3, 3250), (0.7, 3150), (1, 2850)]
    y = whistle(0.55, base, r, harm2=0.20, vib=0.055, vib_hz=31.0,
                attack=0.020, release=0.06, noise=0.03)
    y = y * (0.55 + 0.45 * (0.5 + 0.5 * np.sin(2 * np.pi * 31.0 * tt(n) - 1.0)))
    return y / (np.max(np.abs(y)) + 1e-12)


def bird_chirps(r):
    """Three or four short rising chirps."""
    n = ns(0.50)
    y = np.zeros(n)
    for i in range(4):
        f0 = 2300 + 200 * i + 150 * r.random()
        c = whistle(0.035, [(0, f0), (1, f0 * 1.85)], r, harm2=0.30,
                    attack=0.003, release=0.012)
        add(y, ns(0.055 + i * 0.105 + 0.012 * r.random()), c, 1.0 - 0.13 * i)
    return y


def bird_warble(r):
    """The melodic one - a real little phrase, five notes with glides."""
    n = ns(1.05)
    y = np.zeros(n)
    phrase = [
        (0.00, 0.13, [(0, 2050), (0.5, 2450), (1, 2350)]),
        (0.16, 0.09, [(0, 2900), (1, 2750)]),
        (0.28, 0.16, [(0, 2350), (0.4, 2100), (1, 2250)]),
        (0.50, 0.11, [(0, 3050), (0.5, 3250), (1, 2950)]),
        (0.65, 0.22, [(0, 2450), (0.3, 2650), (0.7, 2200), (1, 1950)]),
    ]
    for t, d, pts in phrase:
        add(y, ns(t), whistle(d, pts, r, harm2=0.26, harm3=0.08,
                              vib=0.010, vib_hz=17.0, attack=0.005,
                              release=0.03), 0.75 + 0.25 * r.random())
    return y / (np.max(np.abs(y)) + 1e-12)


def bird_caw(r):
    """Something harsh and unimpressed in the hedgerow."""
    n = ns(0.85)
    y = np.zeros(n)
    for i, t in enumerate((0.0, 0.34)):
        d = 0.20 - 0.03 * i
        c = C.critter(
            d, [(0, 900), (0.15, 820), (1, 700)],
            [[(0, 900), (1, 780)], [(0, 1750), (1, 1550)], [(0, 3100), (1, 2900)]],
            r, jitter=0.03, shimmer=0.12, width=0.16, rough=0.45,
            noise=0.35, noise_band=(700, 5000), attack=0.006, release=0.05,
            qs=[3.0, 2.6, 2.0], gains=[1.0, 0.6, 0.25], hp=400.0, lp=7000.0,
            drive=1.8)
        add(y, ns(t), c, 1.0 - 0.2 * i)
    return y / (np.max(np.abs(y)) + 1e-12)


def bird_coo(r):
    """Wood pigeon: low, warm, five soft syllables."""
    n = ns(1.35)
    y = np.zeros(n)
    pattern = [(0.00, 0.16, 1.00), (0.22, 0.26, 1.00), (0.56, 0.14, 0.75),
               (0.76, 0.20, 0.85), (1.03, 0.15, 0.55)]
    for t, d, g in pattern:
        c = whistle(d, [(0, 470), (0.25, 510), (1, 455)], r,
                    harm2=0.45, harm3=0.16, vib=0.006, vib_hz=9.0,
                    attack=0.030, release=0.06, noise=0.02, res=980.0)
        add(y, ns(t), c, g)
    return y / (np.max(np.abs(y)) + 1e-12)


def bird_peep(r):
    n = ns(0.10)
    return whistle(0.055, [(0, 4300), (0.4, 4650), (1, 4400)], r,
                   harm2=0.12, attack=0.004, release=0.02)


BIRD_PLAN = [
    # (time s, builder, gain, pan, distance 0..1)
    (0.35, bird_two_note, 0.85, -0.55, 0.25),
    (1.90, bird_chirps, 0.62, 0.70, 0.45),
    (3.10, bird_warble, 1.00, -0.20, 0.10),
    (5.05, bird_coo, 0.70, 0.62, 0.55),
    (6.85, bird_trill, 0.72, -0.75, 0.30),
    (8.20, bird_peep, 0.45, 0.35, 0.60),
    (8.95, bird_caw, 0.55, 0.85, 0.80),
    (10.40, bird_two_note, 0.60, 0.15, 0.50),
    (11.35, bird_chirps, 0.50, -0.85, 0.65),
]


def make_birds_loop():
    dur = 12.0
    n = ns(dur)
    y = np.zeros((2, n))

    for i, (t, fn, g, p, dist) in enumerate(BIRD_PLAN):
        r = rng(8100 + i * 17)
        v = fn(r)
        # distance: air absorbs the top, and the reflections arrive later
        v = sfilt(v, "lowpass", 9000.0 - 6200.0 * dist, 2)
        wadd(y, ns(t), pan(v, p), g * (1.0 - 0.45 * dist))

    # the reflections off the barn and the hedgerow
    y = creverb(y, ir_room(1.4, rng(8200), decay=0.42, bright=4200.0,
                           predelay=0.022)) * 0.30 + y * 0.85

    # a very quiet layer of air so the gaps are not digital silence
    airbed = decorrelate(n, 8300, lambda f: H_bp(f, 1100.0, 0.55) * H_lp(f, 4500.0, 2))
    airbed /= np.max(np.abs(airbed)) + 1e-12
    y = y + airbed * 0.035 * (1.0 + 0.4 * plfo(n, 3))

    y = cfilt(y, lambda f: H_hp(f, 260.0, 2) * H_lp(f, 12000.0, 1))
    return dc_block(y)


# --------------------------------------------------------------------------
# wind through grass
# --------------------------------------------------------------------------


def make_wind_loop():
    """
    Six seconds that breathe.  Two gust waves at 1 and 3 cycles per loop drive
    BOTH the level and the brightness together, because that is what a gust
    actually does - it is not a volume fade on a static hiss.
    """
    dur = 6.0
    n = ns(dur)

    gust = (0.55 * plfo(n, 1, 0.0) + 0.30 * plfo(n, 3, 0.35)
            + 0.35 * smoothnoise(n, 4, rng(8401)))
    gust = np.clip(gust * 0.5 + 0.5, 0.0, 1.0)

    low = decorrelate(n, 8410, lambda f: H_bp(f, 320.0, 0.6) * H_lp(f, 900.0, 2))
    mid = decorrelate(n, 8420, lambda f: H_bp(f, 1150.0, 0.7) * H_lp(f, 4000.0, 1))
    hi = decorrelate(n, 8430, lambda f: H_bp(f, 4200.0, 0.8) * H_lp(f, 11000.0, 1))
    for a in (low, mid, hi):
        a /= np.max(np.abs(a)) + 1e-12

    # the top band only appears in the gusts: that is the grass, not the air
    y = (low * (0.45 + 0.35 * gust)
         + mid * (0.28 + 0.62 * gust ** 1.3)
         + hi * (0.05 + 0.42 * gust ** 2.2))

    # individual blades ticking against each other at the peak of a gust
    r = rng(8440)
    rustle = np.zeros((2, n))
    for _ in range(150):
        t = r.random()
        w = float(np.interp(t * n, np.arange(n), gust)) ** 2
        if r.random() > w * 0.9 + 0.05:
            continue
        gl = int(r.integers(ns(0.004), ns(0.016)))
        g = white(gl, r) * env_ad(gl, 0.0008, 0.004, curve=2.5)
        g = sfilt(g, "bandpass", (2200.0, 11000.0), 2)
        wadd(rustle, int(t * n), pan(g / (np.max(np.abs(g)) + 1e-9),
                                     (r.random() * 2 - 1) * 0.9),
             0.30 + 0.5 * r.random())
    y = y + rustle * 0.28

    y = cfilt(y, lambda f: H_hp(f, 120.0, 2) * H_lp(f, 12000.0, 1)
              * H_peak(f, 700.0, 1.0, -4.0))
    return dc_block(y)


# --------------------------------------------------------------------------
# pond
# --------------------------------------------------------------------------


def water_drop(r, f_hi=1500.0, f_lo=420.0, dur=0.10):
    """The classic 'plip': a fast downward chirp inside a resonant cavity."""
    n = ns(dur)
    f = f_hi * (f_lo / f_hi) ** (tt(n) / dur) ** 0.35
    y = np.sin(2 * np.pi * phasor(f, n)) * np.exp(-tt(n) / (dur * 0.30))
    y += 0.25 * np.sin(4 * np.pi * phasor(f, n)) * np.exp(-tt(n) / (dur * 0.15))
    tick = white(ns(0.004), r) * np.exp(-tt(ns(0.004)) / 0.0008)
    add(y, 0, sfilt(tick, "bandpass", (1500.0, 9000.0), 2), 0.35)
    return y / (np.max(np.abs(y)) + 1e-12)


def make_pond_loop():
    dur = 5.0
    n = ns(dur)
    r = rng(8501)

    # the surface: quiet, low, always moving
    bed = decorrelate(n, 8510, lambda f: H_bp(f, 620.0, 0.55) * H_lp(f, 2600.0, 2)
                      * H_hp(f, 180.0, 2))
    bed /= np.max(np.abs(bed)) + 1e-12
    bed = bed * (0.45 + 0.30 * (0.5 + 0.5 * plfo(n, 2, 0.1))
                 + 0.35 * smoothnoise(n, 5, rng(8511)))

    y = bed * 0.55

    # laps against the bank
    for i in range(9):
        t = (i + 0.35 * r.random()) * dur / 9.0
        d = 0.22 + 0.16 * r.random()
        ln = ns(d)
        fc = np.interp(np.linspace(0, 1, ln), [0, 1],
                       [700.0 + 400.0 * r.random(), 320.0 + 200.0 * r.random()])
        lap = bandpass_tv(white(ln, r), fc, 1.5) * env_swell(ln, 0.30, 1.6, 2.0)
        lap /= np.max(np.abs(lap)) + 1e-12
        wadd(y, ns(t), pan(lap, (r.random() * 2 - 1) * 0.7), 0.20 + 0.20 * r.random())

    # a few drips
    for t, fh in ((1.15, 1650.0), (2.80, 1280.0), (4.35, 1900.0)):
        wadd(y, ns(t), pan(water_drop(r, fh, fh * 0.28), (r.random() * 2 - 1) * 0.6),
             0.28)

    # and a duck who lives here
    q = C.quack(0.22, rng(8520), f0=285.0)
    q = sfilt(q, "lowpass", 3400.0, 2)
    wadd(y, ns(3.35), pan(q, -0.45), 0.30)

    y = creverb(y, ir_room(0.7, rng(8530), decay=0.26, bright=2600.0)) * 0.28 + y * 0.85
    y = cfilt(y, lambda f: H_hp(f, 170.0, 2) * H_lp(f, 7000.0, 2))
    return dc_block(y)


# --------------------------------------------------------------------------
# windmill
# --------------------------------------------------------------------------


def make_windmill_creak():
    """
    Wood under load.  A creak is stick-slip: a burst of micro-impulses whose
    rate rises and falls, run through the resonances of the beam.  Two creaks
    with a groan under them.
    """
    dur = 2.0
    n = ns(dur)
    r = rng(8601)
    exc = np.zeros(n)

    def creak(t0, length, rate0, rate1, level):
        t = t0
        i = 0
        while t < t0 + length:
            u = (t - t0) / length
            rate = rate0 * (rate1 / rate0) ** u
            amp = level * (0.4 + 0.6 * r.random()) * np.sin(np.pi * np.clip(u, 0, 1)) ** 0.6
            k = ns(t)
            if 0 <= k < n - 4:
                exc[k] += amp
            t += (1.0 / rate) * (0.75 + 0.5 * r.random())
            i += 1

    creak(0.05, 0.62, 34.0, 88.0, 1.0)
    creak(1.05, 0.48, 62.0, 26.0, 0.75)

    y = (reson(exc, 232.0, 9.0) * 1.0
         + reson(exc, 496.0, 12.0) * 0.55
         + reson(exc, 905.0, 14.0) * 0.30
         + reson(exc, 1780.0, 10.0) * 0.14)

    # the beam groaning underneath
    groan = reson(exc, 96.0, 5.0) * 0.9
    groan = sfilt(groan, "lowpass", 260.0, 2)

    # a whisper of the sails passing
    air = bandpass_tv(white(n, r), 1400.0, 0.9)
    air *= 0.5 + 0.5 * np.sin(2 * np.pi * 1.5 * tt(n) - 1.0) ** 2

    y = y * 1.0 + groan * 0.55 + air * 0.10
    y = sfilt(y, "highpass", 70.0, 2)
    y = sfilt(y, "lowpass", 6500.0, 2)
    y = satur(y * 0.9, 1.3, 0.5)
    return dc_block(y * env_asr(n, 0.004, 0.10))


def render():
    write(out("Ambience", "birds_loop.wav"), make_birds_loop())
    write(out("Ambience", "wind_grass_loop.wav"), make_wind_loop())
    write(out("Ambience", "pond_loop.wav"), make_pond_loop())
    write(out("Ambience", "windmill_creak.wav"), make_windmill_creak(), fade_out=0.02)


if __name__ == "__main__":
    render()
    print("ambience rendered")
