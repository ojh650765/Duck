"""
DUCK MOW - Blade/

The cutting deck.  Three ideas:

  * `blade_loop` is the deck itself: resonant filtered air with a hard chop at
    the blade-pass rate (4x the engine order, i.e. 4 * 52 Hz = 208 Hz at the
    mid layer's base RPM).  Gated on whenever the blade is spinning.
  * `blade_cut_grass_loop` is the reward layer, added only over UNCUT grass.
    It is granular: several wet shred bursts fired in the wake of every single
    blade pass, so it locks to the deck loop rhythmically instead of sitting
    on top of it as unrelated noise.
  * clutch clunks and stone pings are modal hits.

Both loops are 1.5 s and hold a whole number of blade passes (208 * 1.5 = 312),
so the chop itself is exactly periodic; noise is circular-filtered and grains
are wrap-added, so the loops are seamless with no crossfade.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403

LOOP = 1.5
ENGINE_ORDER = 52.0          # the mid engine layer's fundamental
BLADE_MULT = 4.0             # "4x the engine order"
CHOP = ENGINE_ORDER * BLADE_MULT   # 208 Hz


def chop_shape(n, f, sharp=6.0, cycles=None):
    """Periodic blade-pass shape: a smooth 0..1 bump once per pass."""
    c = cycles if cycles is not None else int(round(f * n / SR))
    ph = 2 * np.pi * c * np.arange(n) / n
    return ((np.cos(ph) * 0.5 + 0.5) ** sharp)


def make_blade_loop():
    n = ns(LOOP)
    r = rng(4001)
    passes = int(round(CHOP * LOOP))
    assert abs(passes - CHOP * LOOP) < 1e-9

    # --- the whine: air through the deck, two resonant peaks ---------------
    air = pink(n, r)
    whine = cfilt(air, lambda f: (
        H_peak(f, 1880.0, 5.0, 30.0)
        * H_peak(f, 3150.0, 4.0, 18.0)
        * H_peak(f, 950.0, 1.4, 8.0)
        * H_hp(f, 380.0, 2)
        * H_lp(f, 7000.0, 2)
    ))
    whine /= np.max(np.abs(whine)) + 1e-12

    # slow wobble so the resonance is alive, integer cycles => loop safe
    whine *= 1.0 + 0.16 * plfo(n, 3, 0.0) + 0.09 * smoothnoise(n, 7, rng(4002))

    # --- the chop: hard amplitude gate at the blade-pass rate --------------
    chop = chop_shape(n, CHOP, sharp=3.0, cycles=passes)
    # a second, weaker pass halfway round (two-blade deck, one blade duller)
    chop2 = chop_shape(n, CHOP, sharp=4.0, cycles=passes)
    chop2 = np.roll(chop2, int(round(n / passes / 2)))
    gate = 0.42 + 0.58 * (chop + 0.55 * chop2) / 1.55

    # --- tonal blade-pass component ---------------------------------------
    tone = np.zeros(n)
    for h, g in ((1, 1.0), (2, 0.45), (3, 0.22), (4, 0.10)):
        tone += g * np.sin(2 * np.pi * h * passes * np.arange(n) / n + h * 0.7)
    tone = cfilt(tone, lambda f: H_lp(f, 1600.0, 1) * H_hp(f, 120.0, 1))
    tone /= np.max(np.abs(tone)) + 1e-12

    # --- deck rumble ------------------------------------------------------
    rumble = cfilt(pink(n, rng(4003)),
                   lambda f: H_bp(f, 165.0, 1.1) * H_lp(f, 420.0, 2))
    rumble /= np.max(np.abs(rumble)) + 1e-12
    rumble *= 0.7 + 0.3 * chop

    y = whine * gate * 0.85 + tone * 0.30 + rumble * 0.34
    y = satur(y * 0.8, 1.1, 0.6)
    y = cfilt(y, lambda f: H_hp(f, 90.0, 2) * H_lp(f, 9000.0, 2))
    return dc_block(y)


def make_cut_grass_loop():
    """
    Wet shredding.  This is the audio reward for mowing uncut grass, so it gets
    three components that all fire in the wake of a blade pass:
      1. a bright shred burst (the cut itself),
      2. a juicy mid-low 'chuff' (the mass of grass being thrown),
      3. a sprayed high fizz (clippings hitting the deck).
    """
    n = ns(LOOP)
    r = rng(4101)
    passes = int(round(CHOP * LOOP))
    period = n / passes

    shred = np.zeros(n)
    chuff = np.zeros(n)
    fizz = np.zeros(n)

    for p in range(passes):
        base = p * period
        # 2-4 grass strikes per blade pass, packed into the first 60% of it
        for _ in range(int(r.integers(2, 5))):
            off = r.random() * period * 0.6
            g = 0.55 + 0.45 * r.random()

            # 1. shred: short band-passed burst, centre wanders
            gl = int(r.integers(ns(0.004), ns(0.012)))
            fc = 1400.0 * (4.0 ** r.random())
            b = white(gl, r) * np.exp(-tt(gl) / (0.0016 + 0.0022 * r.random()))
            b = sfilt(b, "bandpass", (fc * 0.55, min(fc * 2.3, 15000.0)), 2)
            wadd(shred, int(base + off), b / (np.max(np.abs(b)) + 1e-9), g)

            # 2. chuff: the wet body, one per strike but quieter and lower
            if r.random() < 0.55:
                cl = ns(0.020)
                c = white(cl, r) * np.exp(-tt(cl) / 0.0055)
                c = sfilt(c, "bandpass", (180.0, 900.0), 2)
                wadd(chuff, int(base + off + ns(0.001)),
                     c / (np.max(np.abs(c)) + 1e-9), g * 0.9)

        # 3. fizz: a fine spray of clippings, denser than the strikes
        for _ in range(3):
            off = r.random() * period
            fl = ns(0.003)
            b = white(fl, r) * np.exp(-tt(fl) / 0.0009)
            b = sfilt(b, "highpass", 4200.0, 2)
            wadd(fizz, int(base + off), b / (np.max(np.abs(b)) + 1e-9),
                 0.25 + 0.35 * r.random())

    # gentle spectral shaping, all circular
    shred = cfilt(shred, lambda f: H_peak(f, 2400.0, 1.0, 8.0)
                  * H_peak(f, 800.0, 1.2, 5.0) * H_lp(f, 11000.0, 2)
                  * H_hp(f, 300.0, 2))
    chuff = cfilt(chuff, lambda f: H_peak(f, 420.0, 1.3, 10.0) * H_lp(f, 1400.0, 2)
                  * H_hp(f, 130.0, 2))
    fizz = cfilt(fizz, lambda f: H_hp(f, 3800.0, 2) * H_lp(f, 13000.0, 1))

    for a in (shred, chuff, fizz):
        a /= np.max(np.abs(a)) + 1e-12

    y = shred * 1.0 + chuff * 0.62 + fizz * 0.30

    # a slow density swell so 75 seconds of it never sits still
    y *= 1.0 + 0.13 * plfo(n, 2) + 0.10 * smoothnoise(n, 5, rng(4102))
    y = satur(y * 0.85, 1.2, 0.5)
    return dc_block(y)


def _clutch_clunk(r, size=1.0):
    n = ns(0.16)
    m = modal(
        n,
        [148.0 * size, 322.0 * size, 705.0 * size, 1290.0 * size],
        [0.055, 0.035, 0.020, 0.011],
        [1.0, 0.62, 0.40, 0.22],
        attack=0.0004, r=r,
    )
    s = strike(n, r, 900.0, 7000.0, 0.0016) * 0.55
    return (m + s) * env_ad(n, 0.0006, 0.045, curve=3.5)


def make_engage():
    n = ns(0.42)
    r = rng(4201)
    y = np.zeros(n)
    add(y, ns(0.012), _clutch_clunk(r, 1.0), 1.0)
    add(y, ns(0.055), _clutch_clunk(r, 1.32), 0.42)

    # belt takes up: a slip-squeal that rises and settles
    sl = ns(0.30)
    fc = 620.0 * (1880.0 / 620.0) ** (tt(sl) / 0.30) ** 0.7
    slip = bandpass_tv(white(sl, r), fc, 7.0)
    slip *= env_ad(sl, 0.020, 0.10, curve=2.2)
    add(y, ns(0.035), slip, 0.55)

    # deck spins up
    sp = ns(0.32)
    spin = np.sin(2 * np.pi * phasor(np.linspace(70.0, 205.0, sp), sp))
    spin += 0.4 * np.sin(2 * np.pi * 2 * phasor(np.linspace(70.0, 205.0, sp), sp))
    spin *= env_asr(sp, 0.10, 0.05) * 0.5
    add(y, ns(0.09), sfilt(spin, "lowpass", 1400.0, 2), 0.6)

    y = sfilt(y, "highpass", 70.0, 2)
    return dc_block(y * env_asr(n, 0.0005, 0.02))


def make_disengage():
    n = ns(0.42)
    r = rng(4202)
    y = np.zeros(n)
    add(y, ns(0.010), _clutch_clunk(r, 1.15), 0.9)

    # deck winds down
    sp = ns(0.34)
    f = np.linspace(205.0, 58.0, sp)
    spin = np.sin(2 * np.pi * phasor(f, sp)) + 0.35 * np.sin(2 * np.pi * 2 * phasor(f, sp))
    spin *= np.exp(-tt(sp) / 0.16)
    add(y, ns(0.020), sfilt(spin, "lowpass", 1200.0, 2), 0.75)

    # whine falling away
    wl = ns(0.30)
    fc = 1750.0 * (430.0 / 1750.0) ** (tt(wl) / 0.30)
    wh = bandpass_tv(white(wl, r), fc, 6.0) * np.exp(-tt(wl) / 0.11)
    add(y, ns(0.015), wh, 0.42)

    # settling thunk
    add(y, ns(0.29), _clutch_clunk(r, 0.78), 0.34)

    y = sfilt(y, "highpass", 70.0, 2)
    return dc_block(y * env_asr(n, 0.0005, 0.02))


# stone-on-deck: inharmonic sheet-metal modes, different plate spot each time
PING_VARIANTS = [
    dict(seed=4301, base=1720.0, ratios=(1.00, 1.74, 2.41, 3.36, 4.62), tau=0.075),
    dict(seed=4302, base=2280.0, ratios=(1.00, 1.58, 2.66, 3.11, 5.05), tau=0.055),
    dict(seed=4303, base=1330.0, ratios=(1.00, 1.93, 2.28, 3.72, 4.31), tau=0.095),
    dict(seed=4304, base=2860.0, ratios=(1.00, 1.66, 2.85, 3.49, 4.90), tau=0.042),
]


def make_ping(v):
    n = ns(0.25)
    r = rng(v["seed"])
    freqs = [v["base"] * x for x in v["ratios"]]
    decays = [v["tau"] * (0.9 ** i) * (0.75 + 0.5 * r.random()) for i in range(len(freqs))]
    gains = [1.0, 0.70, 0.52, 0.34, 0.20]
    # a small downward bend as the plate relaxes = a real impact, not a bell
    bend = 1.0 + 0.035 * np.exp(-tt(n) / 0.012)
    m = modal(n, freqs, decays, gains, attack=0.00025, bend=bend, r=r)

    # the stone itself: a dry low tonk plus the contact click
    tonk = modal(n, [176.0, 268.0], [0.030, 0.018], [1.0, 0.5], attack=0.0003, r=r)
    click = strike(n, r, 1800.0, 12000.0, 0.0011)

    y = m * 1.0 + tonk * 0.45 + click * 0.55
    y *= env_ad(n, 0.0004, v["tau"] * 1.6, curve=3.0)
    y = sfilt(y, "highpass", 130.0, 2)
    return dc_block(y)


def render():
    write(out("Blade", "blade_loop.wav"), make_blade_loop())
    write(out("Blade", "blade_cut_grass_loop.wav"), make_cut_grass_loop())
    write(out("Blade", "blade_engage.wav"), make_engage(), fade_out=0.006)
    write(out("Blade", "blade_disengage.wav"), make_disengage(), fade_out=0.006)
    for i, v in enumerate(PING_VARIANTS, 1):
        write(out("Blade", f"debris_ping_{i:02d}.wav"), make_ping(v), fade_out=0.004)


if __name__ == "__main__":
    render()
    print("blade rendered")
