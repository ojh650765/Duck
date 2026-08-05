"""
DUCK MOW - Crowd/

The crowd is made of animals, so the bed is built in two parts: an anonymous
distant murmur (which is what a crowd actually is at 40 m - broadband, vowel-
coloured, no intelligible detail) and a handful of individual bleats, honks and
quacks poking through it.  The individuals are the joke, so they are placed
sparsely and panned wide; the murmur is what makes them believable.

The 8 s bed and the 3 s applause loop are seamless by construction: the murmur
is circular-filtered noise, and every grain is wrap-added.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
import critter as C


# --------------------------------------------------------------------------
# lightweight voices (the bed needs hundreds of them, so no full formant synth)
# --------------------------------------------------------------------------


def murmur_grain(dur, r, f0=None, bright=1.0):
    """One indistinct distant voice: two moving formants over a mixed source."""
    n = ns(dur)
    f0 = f0 or (110.0 + 190.0 * r.random())
    ph = 2 * np.pi * f0 * tt(n) * (1.0 + 0.03 * smoothnoise(n, 3, r))
    buzz = np.zeros(n)
    for h in range(1, 13):
        buzz += np.sin(h * ph + r.random() * 6.28) / h
    src = buzz / 2.0 + white(n, r) * 0.55

    f1 = (420.0 + 420.0 * r.random()) * (1.0 + 0.25 * smoothnoise(n, 3, r))
    f2 = (1150.0 + 900.0 * r.random()) * bright * (1.0 + 0.18 * smoothnoise(n, 4, r))
    y = bandpass_tv(src, f1, 2.4) + 0.55 * bandpass_tv(src, f2, 2.2)
    y *= env_swell(n, 0.25 + 0.3 * r.random(), 1.5, 1.8)
    return y / (np.max(np.abs(y)) + 1e-12)


def shout(dur, r, f0=None, kind="whoo"):
    """A single cheering voice.  Cheap, but with a real vowel and contour."""
    n = ns(dur)
    f0 = f0 or (170.0 + 220.0 * r.random())
    if kind == "whoo":
        pitch = f0 * np.interp(np.linspace(0, 1, n), [0, .25, 1], [.82, 1.06, .96])
        f1 = np.interp(np.linspace(0, 1, n), [0, 1], [380.0, 460.0])
        f2 = np.interp(np.linspace(0, 1, n), [0, 1], [820.0, 1000.0])
    elif kind == "yeah":
        pitch = f0 * np.interp(np.linspace(0, 1, n), [0, .2, 1], [1.10, 1.0, .84])
        f1 = np.interp(np.linspace(0, 1, n), [0, .3, 1], [700.0, 620.0, 500.0])
        f2 = np.interp(np.linspace(0, 1, n), [0, .3, 1], [1200.0, 1700.0, 1450.0])
    else:  # "aah"
        pitch = f0 * np.interp(np.linspace(0, 1, n), [0, 1], [1.0, 0.93])
        f1 = np.full(n, 720.0)
        f2 = np.full(n, 1200.0)

    ph = 2 * np.pi * phasor(pitch * (1 + 0.02 * smoothnoise(n, 5, r)), n)
    src = np.zeros(n)
    for h in range(1, 17):
        src += np.sin(h * ph) / h
    src = src / 2.5 + white(n, r) * 0.30

    y = (bandpass_tv(src, f1, 3.2)
         + 0.62 * bandpass_tv(src, f2, 2.8)
         + 0.22 * bandpass_tv(src, 2600.0, 2.0))
    y *= env_swell(n, 0.18 + 0.2 * r.random(), 1.4, 2.0)
    return y / (np.max(np.abs(y)) + 1e-12)


def clap(r, big=False):
    """One pair of hands (or hooves).  Body peak + bright slap + tiny tail."""
    n = ns(0.055 if not big else 0.075)
    body = white(n, r) * np.exp(-tt(n) / (0.0035 + 0.004 * r.random()))
    f = 900.0 + 1400.0 * r.random()
    y = bandpass_tv(body, f, 1.6) * 1.0
    y += sfilt(body, "bandpass", (2600.0, 8000.0), 2) * (0.55 + 0.4 * r.random())
    y += sfilt(body, "bandpass", (260.0, 700.0), 2) * 0.35
    y *= env_ad(n, 0.0004, 0.010, curve=3.0)
    return y / (np.max(np.abs(y)) + 1e-12)


# --------------------------------------------------------------------------
# the ambient bed
# --------------------------------------------------------------------------


def make_ambient_loop():
    dur = 8.0
    n = ns(dur)
    r = rng(7001)

    # 1. anonymous murmur: two decorrelated channels of vowel-coloured noise
    bed = decorrelate(n, 7100, lambda f: (
        H_peak(f, 520.0, 0.9, 16.0)
        * H_peak(f, 1400.0, 1.1, 10.0)
        * H_peak(f, 2900.0, 1.2, -6.0)
        * H_hp(f, 190.0, 2)
        * H_lp(f, 4200.0, 2)
    ))
    bed /= np.max(np.abs(bed)) + 1e-12
    # it breathes: crowds surge and settle
    swell = 0.62 + 0.22 * (0.5 + 0.5 * plfo(n, 2)) + 0.22 * smoothnoise(n, 5, rng(7101))
    bed = bed * swell

    # 2. individual indistinct voices, sparse and wide
    voices = np.zeros((2, n))
    for _ in range(34):
        d = 0.18 + 0.5 * r.random()
        g = murmur_grain(d, r, bright=0.8 + 0.5 * r.random())
        p = (r.random() * 2 - 1) * 0.85
        wadd(voices, int(r.random() * n), pan(g, p), 0.16 + 0.22 * r.random())

    # 3. the animals.  Sparse - roughly one every 1.4 s, never two at once.
    critters = np.zeros((2, n))
    plan = [
        (0.55, "sheep", 0.30), (1.90, "goose", 0.26), (3.05, "quack", 0.22),
        (4.10, "goat", 0.24), (5.20, "cow", 0.20), (6.05, "goose", 0.18),
        (6.95, "quack", 0.20), (7.60, "sheep", 0.16),
    ]
    for t, kind, g in plan:
        rr = rng(int(t * 1000) + 7200)
        if kind == "sheep":
            v = C.sheep(0.45 + 0.2 * rr.random(), rr, f0=175 + 40 * rr.random())
        elif kind == "goat":
            v = C.goat(0.42, rr, f0=230 + 40 * rr.random())
        elif kind == "goose":
            v = C.goose(0.34, rr, f0=225 + 60 * rr.random())
        elif kind == "cow":
            v = C.cow(0.70, rr, f0=98 + 18 * rr.random())
        else:
            v = C.quack(0.24, rr, f0=290 + 50 * rr.random())
        # distance: dull them down and push them back
        v = sfilt(v, "lowpass", 2600.0, 2)
        v = sfilt(v, "highpass", 220.0, 2)
        wadd(critters, ns(t), pan(v, (rr.random() * 2 - 1) * 0.9), g)

    y = bed * 1.0 + voices * 0.9 + critters * 0.9

    # the fairground behind it all: a little air and a long soft tail
    y = creverb(y, ir_room(0.9, rng(7300), decay=0.28, bright=2400.0,
                           predelay=0.012)) * 0.55 + y * 0.6
    y = cfilt(y, lambda f: H_hp(f, 150.0, 2) * H_lp(f, 5200.0, 2))
    return dc_block(y)


# --------------------------------------------------------------------------
# reactions
# --------------------------------------------------------------------------


def _cheer(dur, seed, voices, animals, clap_rate, peak_at, spread, bright):
    n = ns(dur)
    r = rng(seed)
    y = np.zeros((2, n))

    env = env_swell(n, peak_at, 1.5, 2.1)

    # a wall of voices arriving slightly raggedly - a crowd is never in sync
    for i in range(voices):
        d = 0.5 + 0.9 * r.random()
        kind = ("whoo", "yeah", "aah")[int(r.integers(0, 3))]
        v = shout(d, r, f0=(150.0 + spread * r.random()), kind=kind)
        onset = abs(r.normal(0.0, 0.085)) + 0.012 * r.random()
        add(y, ns(onset), pan(v, (r.random() * 2 - 1) * 0.9), 0.5 + 0.5 * r.random())

    # clapping
    claps = int(clap_rate * dur)
    for _ in range(claps):
        t = r.random() ** 0.7 * dur * 0.92
        c = clap(r, big=bright > 1.0)
        add(y, ns(t + 0.10), pan(c, (r.random() * 2 - 1) * 0.95),
            (0.25 + 0.5 * r.random()) * float(np.interp(t, [0, dur * peak_at, dur], [0.3, 1.0, 0.35])))

    # the animals in the stands
    for t, kind, g, p in animals:
        rr = rng(seed + int(t * 977))
        if kind == "goose":
            v = C.goose(0.30, rr, f0=230 + 70 * rr.random())
        elif kind == "sheep":
            v = C.sheep(0.40, rr, f0=180 + 50 * rr.random())
        elif kind == "goat":
            v = C.goat(0.38, rr, f0=240 + 50 * rr.random())
        elif kind == "cow":
            v = C.cow(0.55, rr, f0=100 + 15 * rr.random())
        else:
            v = C.quack(0.22, rr, f0=300 + 60 * rr.random())
        add(y, ns(t), pan(sfilt(v, "lowpass", 4200.0, 2), p), g)

    y = y * env
    y = creverb(y, ir_room(0.7, rng(seed + 5), decay=0.22, bright=2800.0,
                           predelay=0.010)) * 0.42 + y * 0.75
    y = cfilt(y, lambda f: H_hp(f, 170.0, 2) * H_lp(f, 6800.0 * bright, 2))
    return dc_block(y)


def make_cheer_small():
    return _cheer(2.4, 7401, voices=9, clap_rate=14, peak_at=0.26, spread=170,
                  bright=0.95,
                  animals=[(0.16, "goose", 0.42, -0.6), (0.52, "sheep", 0.30, 0.55)])


def make_cheer_big():
    return _cheer(2.6, 7402, voices=22, clap_rate=42, peak_at=0.22, spread=260,
                  bright=1.15,
                  animals=[(0.10, "goose", 0.55, -0.7), (0.28, "goat", 0.42, 0.62),
                           (0.62, "quack", 0.34, -0.25), (0.95, "cow", 0.30, 0.35),
                           (1.35, "goose", 0.34, 0.8)])


def make_gasp():
    """A collective inhale: the sound is air rushing IN, so the band opens up
    rather than closing down, and there is almost no voicing at the front."""
    dur = 1.5
    n = ns(dur)
    r = rng(7501)
    y = np.zeros((2, n))

    for i in range(14):
        d = 0.30 + 0.22 * r.random()
        gn = ns(d)
        fc = np.interp(np.linspace(0, 1, gn), [0, 1],
                       [380.0 + 200.0 * r.random(), 1500.0 + 900.0 * r.random()])
        g = bandpass_tv(white(gn, r), fc, 2.2)
        # a thin voiced edge on some of them
        if r.random() < 0.5:
            f0 = 200.0 + 200.0 * r.random()
            v = np.sin(2 * np.pi * phasor(np.linspace(f0, f0 * 1.15, gn), gn))
            g = g + 0.25 * bandpass_tv(v, 900.0, 3.0)
        g *= env_swell(gn, 0.55, 1.9, 1.5)
        g /= np.max(np.abs(g)) + 1e-12
        add(y, ns(abs(r.normal(0.02, 0.045))), pan(g, (r.random() * 2 - 1) * 0.9),
            0.4 + 0.6 * r.random())

    # one animal that could not help itself
    add(y, ns(0.30), pan(C.goose(0.22, rng(7502), f0=330), -0.5), 0.30)
    # ...and the hush afterwards
    hush = decorrelate(n, 7503, lambda f: H_bp(f, 900.0, 0.7) * H_lp(f, 3000.0, 2))
    hush /= np.max(np.abs(hush)) + 1e-12
    y += hush * np.interp(np.linspace(0, 1, n), [0, 0.35, 0.6, 1.0], [0.0, 0.10, 0.16, 0.05])

    y = creverb(y, ir_room(0.6, rng(7504), decay=0.20, bright=2600.0)) * 0.35 + y * 0.8
    y = cfilt(y, lambda f: H_hp(f, 200.0, 2) * H_lp(f, 6000.0, 2))
    return dc_block(y * env_asr(n, 0.004, 0.16))


def make_aww():
    """Descending, warm, sympathetic.  Vowel travels from 'ah' toward 'oo'."""
    dur = 1.6
    n = ns(dur)
    r = rng(7601)
    y = np.zeros((2, n))

    for i in range(15):
        d = 0.75 + 0.5 * r.random()
        gn = ns(d)
        f0 = 155.0 + 175.0 * r.random()
        pitch = f0 * np.interp(np.linspace(0, 1, gn), [0, 0.2, 1], [1.06, 1.0, 0.80])
        ph = 2 * np.pi * phasor(pitch * (1 + 0.018 * smoothnoise(gn, 4, r)), gn)
        src = np.zeros(gn)
        for h in range(1, 15):
            src += np.sin(h * ph) / h
        src = src / 2.4 + white(gn, r) * 0.22
        f1 = np.interp(np.linspace(0, 1, gn), [0, 1], [740.0, 470.0])
        f2 = np.interp(np.linspace(0, 1, gn), [0, 1], [1180.0, 780.0])
        g = bandpass_tv(src, f1, 3.0) + 0.6 * bandpass_tv(src, f2, 2.6)
        g *= env_swell(gn, 0.20, 1.3, 1.9)
        g /= np.max(np.abs(g)) + 1e-12
        add(y, ns(abs(r.normal(0.03, 0.06))), pan(g, (r.random() * 2 - 1) * 0.85),
            0.5 + 0.5 * r.random())

    add(y, ns(0.22), pan(C.sheep(0.55, rng(7602), f0=200), 0.55), 0.26)
    add(y, ns(0.75), pan(C.cow(0.60, rng(7603), f0=96), -0.5), 0.22)

    y = creverb(y, ir_room(0.7, rng(7604), decay=0.24, bright=2400.0)) * 0.38 + y * 0.78
    y = cfilt(y, lambda f: H_hp(f, 160.0, 2) * H_lp(f, 5200.0, 2))
    return dc_block(y * env_asr(n, 0.010, 0.20))


def make_laugh():
    """Ha-ha-ha at ~5.5 Hz, with a goose and a goat losing it in the stands."""
    dur = 1.6
    n = ns(dur)
    r = rng(7701)
    y = np.zeros((2, n))

    for i in range(13):
        f0 = 150.0 + 200.0 * r.random()
        rate = 4.6 + 2.4 * r.random()
        nsyl = int(2 + r.integers(0, 4))
        t0 = abs(r.normal(0.04, 0.07))
        p = (r.random() * 2 - 1) * 0.9
        for s in range(nsyl):
            sd = 0.075 + 0.05 * r.random()
            gn = ns(sd)
            pitch = f0 * (1.0 - 0.06 * s) * np.interp(np.linspace(0, 1, gn), [0, 1], [1.12, 0.92])
            ph = 2 * np.pi * phasor(pitch, gn)
            src = np.zeros(gn)
            for h in range(1, 13):
                src += np.sin(h * ph) / h
            src = src / 2.2 + white(gn, r) * 0.35
            g = (bandpass_tv(src, 640.0 + 200.0 * r.random(), 3.0)
                 + 0.55 * bandpass_tv(src, 1250.0 + 300.0 * r.random(), 2.6))
            g *= env_ad(gn, 0.006, sd * 0.35, curve=2.4)
            g /= np.max(np.abs(g)) + 1e-12
            add(y, ns(t0 + s / rate), pan(g, p + 0.05 * (r.random() - 0.5)),
                (0.5 + 0.5 * r.random()) * (1.0 - 0.13 * s))

    add(y, ns(0.18), pan(C.goose(0.26, rng(7702), f0=300), -0.62), 0.36)
    add(y, ns(0.46), pan(C.goat(0.36, rng(7703), f0=265), 0.60), 0.30)
    add(y, ns(0.92), pan(C.goose(0.22, rng(7704), f0=245), 0.30), 0.26)

    y = creverb(y, ir_room(0.6, rng(7705), decay=0.20, bright=3000.0)) * 0.35 + y * 0.8
    y = cfilt(y, lambda f: H_hp(f, 180.0, 2) * H_lp(f, 6500.0, 2))
    return dc_block(y * env_asr(n, 0.004, 0.18))


def make_applause_loop():
    """
    3 s seamless.  Claps are placed with a jittered grid rather than uniformly
    at random: real applause has a weak collective pulse, and a pure Poisson
    scatter sounds like rain.
    """
    dur = 3.0
    n = ns(dur)
    r = rng(7801)
    y = np.zeros((2, n))

    rate = 9.0                       # claps per person per second
    people = 13
    for _ in range(people):
        phase = r.random()
        drift = 0.9 + 0.2 * r.random()
        p = (r.random() * 2 - 1) * 0.95
        level = 0.45 + 0.55 * r.random()
        # Each clapper's rate is snapped so a whole number of claps fits the
        # loop.  Without this the last clap of each person lands short of the
        # end and leaves a hole at the wrap - which is exactly the -5 dB dip
        # the seam check catches.
        k = max(int(round(rate * drift * dur)), 1)
        step = dur / k
        for i in range(k):
            t = ((i + phase) * step + r.normal(0, 0.016)) % dur
            wadd(y, ns(t), pan(clap(r), p + 0.04 * (r.random() - 0.5)),
                 level * (0.7 + 0.6 * r.random()))

    # room + the far side of the crowd
    far = decorrelate(n, 7802, lambda f: H_bp(f, 1600.0, 0.7) * H_lp(f, 5000.0, 2))
    far /= np.max(np.abs(far)) + 1e-12
    y = y + far * 0.10

    y = creverb(y, ir_room(0.55, rng(7803), decay=0.16, bright=3200.0,
                           predelay=0.008)) * 0.30 + y * 0.85
    y = cfilt(y, lambda f: H_hp(f, 200.0, 2) * H_lp(f, 9000.0, 1))
    # slow collective swell so 3 s does not tick
    y = y * (0.88 + 0.12 * plfo(n, 2, 0.2))
    return dc_block(y)


def render():
    write(out("Crowd", "crowd_ambient_loop.wav"), make_ambient_loop())
    write(out("Crowd", "crowd_cheer_small.wav"), make_cheer_small(), fade_out=0.08)
    write(out("Crowd", "crowd_cheer_big.wav"), make_cheer_big(), fade_out=0.08)
    write(out("Crowd", "crowd_gasp.wav"), make_gasp(), fade_out=0.06)
    write(out("Crowd", "crowd_aww.wav"), make_aww(), fade_out=0.08)
    write(out("Crowd", "crowd_laugh.wav"), make_laugh(), fade_out=0.06)
    write(out("Crowd", "applause_loop.wav"), make_applause_loop())


if __name__ == "__main__":
    render()
    print("crowd rendered")
