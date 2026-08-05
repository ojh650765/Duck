"""
DUCK MOW - Mower/

Collisions, the horn, drifting on grass, the boost, and suspension knocks.

The bonk is the one that carries the comedy: a wooden/metal impact followed by
a short pitched 'boing' whose frequency wobbles around a falling centre.  It is
deliberately NOT a glissando - a slide whistle reads as a cartoon reference,
a wobbling struck body reads as a toy set, which is what the art bible asks for.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403


# --------------------------------------------------------------------------
# bonks
# --------------------------------------------------------------------------

BONKS = [
    dict(seed=5001, wood=(155.0, 249.0, 402.0), metal=(742.0, 1281.0, 2065.0),
         boing=196.0, wob=17.0, drop=0.84, dur=0.46, metal_gain=0.78),
    dict(seed=5002, wood=(133.0, 208.0, 346.0), metal=(618.0, 1104.0, 1735.0),
         boing=165.0, wob=13.5, drop=0.80, dur=0.50, metal_gain=0.62),
    dict(seed=5003, wood=(182.0, 291.0, 470.0), metal=(880.0, 1490.0, 2380.0),
         boing=233.0, wob=21.0, drop=0.87, dur=0.42, metal_gain=0.95),
]


def make_bonk(v):
    n = ns(v["dur"])
    r = rng(v["seed"])

    # --- the impact: a wooden body with a metal panel on top --------------
    bend = 1.0 + 0.06 * np.exp(-tt(n) / 0.010)
    wood = modal(n, list(v["wood"]), [0.075, 0.048, 0.030],
                 [1.0, 0.60, 0.34], attack=0.0004, bend=bend, r=r)
    metal = modal(n, list(v["metal"]), [0.110, 0.070, 0.042],
                  [1.0, 0.55, 0.28], attack=0.0003, r=r)
    hit = strike(n, r, 400.0, 8000.0, 0.0018)

    impact = (wood * 1.0 + metal * v["metal_gain"] + hit * 0.85)
    impact *= env_ad(n, 0.0005, 0.070, curve=3.0)

    # --- the boing: a struck spring, wobbling about a falling centre ------
    bn = ns(v["dur"] * 0.82)
    t = tt(bn)
    dec = np.exp(-t / (v["dur"] * 0.30))
    centre = v["boing"] * (1.0 + (v["drop"] - 1.0) * (1.0 - np.exp(-t / 0.16)))
    f = centre * (1.0 + 0.26 * dec * np.sin(2 * np.pi * v["wob"] * t))
    boing = (np.sin(2 * np.pi * phasor(f, bn))
             + 0.34 * np.sin(2 * np.pi * 2 * phasor(f, bn))
             + 0.13 * np.sin(2 * np.pi * 3 * phasor(f, bn)))
    boing *= dec * env_asr(bn, 0.006, 0.06)
    boing = sfilt(boing, "lowpass", 2600.0, 2)

    y = np.zeros(n)
    add(y, 0, impact, 1.0)
    add(y, ns(0.006), boing, 0.52)

    y = sfilt(y, "highpass", 115.0, 2)
    y = satur(y * 0.9, 1.2, 0.5)
    return dc_block(y * env_asr(n, 0.0004, 0.03))


# --------------------------------------------------------------------------
# horn
# --------------------------------------------------------------------------


def make_horn():
    """A small silly parp: a reedy pulse through a nasal formant, with a
    pitch bump on the attack and a deflating droop on release."""
    dur = 0.62
    n = ns(dur)
    r = rng(5101)
    t = tt(n)
    u = t / dur

    # pitch: quick lift into the note, sag at the end - the sound of something
    # small being squeezed
    f = 302.0 * (1.0 + 0.16 * np.exp(-t / 0.020)
                 - 0.115 * np.clip((u - 0.62) / 0.38, 0, 1) ** 1.6)
    f *= 1.0 + 0.014 * np.sin(2 * np.pi * 6.5 * t)   # a nervous little vibrato

    src = bl_pulse(f, n, width=0.22)
    src += 0.35 * bl_saw(f, n)
    src /= np.max(np.abs(src)) + 1e-12

    # reed buzz
    src = satur(src * 1.6, 2.2, 0.7)

    # nasal horn formants - this is what makes it "parp" and not "beep"
    y = sfilt(src, "bandpass", (280.0, 3600.0), 2)
    y = y + reson(src, 880.0, 3.0) * 0.55 + reson(src, 1950.0, 4.0) * 0.40
    y = y + reson(src, 480.0, 2.5) * 0.35
    y = sfilt(y, "lowpass", 5200.0, 2)

    env = env_asr(n, 0.014, 0.055, curve=1.2)
    env *= 1.0 - 0.22 * np.clip((u - 0.55) / 0.45, 0, 1)
    y *= env

    # the puff of air at each end
    air = white(ns(0.05), r) * np.exp(-tt(ns(0.05)) / 0.012)
    add(y, ns(0.002), sfilt(air, "bandpass", (700.0, 5000.0), 2), 0.30)
    phbt = white(ns(0.09), r) * np.exp(-tt(ns(0.09)) / 0.026)
    phbt *= 1.0 + 0.8 * np.sin(2 * np.pi * 42.0 * tt(ns(0.09)))
    add(y, ns(0.535), sfilt(phbt, "bandpass", (320.0, 2400.0), 2), 0.34)

    return dc_block(y)


# --------------------------------------------------------------------------
# drift on grass
# --------------------------------------------------------------------------


def make_drift_loop():
    """
    Grass slide, not tyre screech.  The give-away for a screech is a high-Q
    tonal peak above 1.5 kHz; here the spectrum is broad with a soft shoulder
    around 1 kHz, and the character comes from grass-blade swish grains.
    """
    n = ns(1.0)
    r = rng(5201)

    bed = cfilt(pink(n, r), lambda f: (
        H_peak(f, 1050.0, 0.9, 12.0)
        * H_peak(f, 2700.0, 1.0, 6.0)
        * H_hp(f, 260.0, 2)
        * H_lp(f, 8000.0, 2)
    ))
    bed /= np.max(np.abs(bed)) + 1e-12
    bed *= 0.72 + 0.28 * (0.5 + 0.5 * plfo(n, 2, 0.15)) + 0.18 * smoothnoise(n, 9, rng(5202))

    # blades of grass flicking past
    swish = np.zeros(n)
    for _ in range(46):
        pos = int(r.random() * n)
        gl = int(r.integers(ns(0.008), ns(0.026)))
        fc = 900.0 * (3.5 ** r.random())
        g = white(gl, r) * env_swell(gl, 0.3, 1.4, 2.0)
        g = sfilt(g, "bandpass", (fc * 0.6, min(fc * 2.6, 14000.0)), 2)
        wadd(swish, pos, g / (np.max(np.abs(g)) + 1e-9), 0.35 + 0.5 * r.random())

    # chassis load
    low = cfilt(pink(n, rng(5203)),
                lambda f: H_bp(f, 175.0, 1.0) * H_lp(f, 500.0, 2))
    low /= np.max(np.abs(low)) + 1e-12

    y = bed * 1.0 + swish * 0.55 + low * 0.30
    y = cfilt(y, lambda f: H_hp(f, 140.0, 2) * H_lp(f, 10000.0, 1))
    return dc_block(y)


# --------------------------------------------------------------------------
# boost
# --------------------------------------------------------------------------


def _turbo(n, f_curve, q=9.0, harm=(1.0, 0.45, 0.20)):
    """A turbo whistle: a few harmonics plus band-passed air at the same pitch."""
    y = np.zeros(n)
    ph = phasor(f_curve, n)
    for i, g in enumerate(harm, 1):
        y += g * np.sin(2 * np.pi * i * ph)
    y /= np.max(np.abs(y)) + 1e-12
    return y


def make_boost_start():
    dur = 0.55
    n = ns(dur)
    r = rng(5301)
    t = tt(n)
    u = t / dur

    # low whump as the governor lets go
    add_env = np.zeros(n)
    whump = modal(n, [88.0, 143.0], [0.055, 0.030], [1.0, 0.4], attack=0.0012, r=r)
    add_env += whump * env_ad(n, 0.002, 0.06, 3.0) * 0.55

    # rising air
    fc = 260.0 * (4600.0 / 260.0) ** (u ** 0.75)
    air = bandpass_tv(white(n, r), fc, 1.9)
    air *= env_swell(n, 0.78, 1.7, 1.4)

    # turbo whistle, deliberately overshooting before it settles - the joke is
    # that this mower has been tuned slightly too far
    over = 1.0 + 0.22 * np.clip((u - 0.72) / 0.18, 0, 1) - 0.14 * np.clip((u - 0.90) / 0.10, 0, 1)
    fw = 780.0 * (2700.0 / 780.0) ** (u ** 1.25) * over
    whistle = _turbo(n, fw)
    whistle *= np.clip((u - 0.12) / 0.5, 0, 1) ** 1.5 * env_asr(n, 0.01, 0.03)

    y = add_env + air * 0.85 + whistle * 0.42
    y = sfilt(y, "highpass", 70.0, 2)
    return dc_block(y * env_asr(n, 0.001, 0.012))


def make_boost_loop():
    n = ns(1.0)
    r = rng(5302)

    roar = cfilt(pink(n, r), lambda f: (
        H_peak(f, 520.0, 0.9, 12.0) * H_peak(f, 1800.0, 1.1, 7.0)
        * H_hp(f, 150.0, 2) * H_lp(f, 7500.0, 2)))
    roar /= np.max(np.abs(roar)) + 1e-12
    roar *= 0.80 + 0.20 * plfo(n, 3) + 0.14 * smoothnoise(n, 11, rng(5303))

    # Whistle.  Written as phase = integer_cycles*t + periodic_wobble, which is
    # exactly loop-periodic - safer than integrating a frequency curve and then
    # trying to force the endpoint.
    u = np.arange(n) / n
    ph = 2646.0 * u + 0.9 * np.sin(2 * np.pi * 7 * u)
    whistle = (np.sin(2 * np.pi * ph) + 0.40 * np.sin(2 * np.pi * 2 * ph)
               + 0.15 * np.sin(2 * np.pi * 3 * ph))
    whistle /= np.max(np.abs(whistle)) + 1e-12
    whistle *= 0.75 + 0.25 * plfo(n, 12)

    # flutter: the comedy over-tune, a fast chuffing on top
    flutter = cfilt(white(n, rng(5304)),
                    lambda f: H_bp(f, 3200.0, 1.4) * H_lp(f, 9000.0, 1))
    flutter /= np.max(np.abs(flutter)) + 1e-12
    flutter *= (0.5 + 0.5 * plfo(n, 13, shape="tri")) ** 2

    y = roar * 1.0 + whistle * 0.30 + flutter * 0.22
    y = cfilt(y, lambda f: H_hp(f, 120.0, 2) * H_lp(f, 11000.0, 1))
    return dc_block(y)


def make_boost_end():
    dur = 0.62
    n = ns(dur)
    r = rng(5305)
    t = tt(n)
    u = t / dur

    # whistle spins down
    fw = 2650.0 * (620.0 / 2650.0) ** (u ** 0.8)
    whistle = _turbo(n, fw) * np.exp(-t / 0.16)

    # roar drops away
    fc = 2400.0 * (420.0 / 2400.0) ** u
    roar = bandpass_tv(white(n, r), fc, 1.6) * np.exp(-t / 0.13)

    # wastegate: the "pssht" is the punchline
    wl = ns(0.22)
    ps = white(wl, r) * env_ad(wl, 0.0018, 0.045, curve=2.6)
    ps = sfilt(ps, "bandpass", (1400.0, 9000.0), 2)
    ps += sfilt(white(wl, r) * env_ad(wl, 0.003, 0.030, 3.0), "bandpass", (400.0, 1600.0), 2) * 0.5

    # ...and a small deflating boing underneath it
    dn = ns(0.28)
    dt_ = tt(dn)
    fd = 150.0 * (1.0 - 0.42 * (1 - np.exp(-dt_ / 0.10))) * (1 + 0.18 * np.exp(-dt_ / 0.08) * np.sin(2 * np.pi * 15.0 * dt_))
    deflate = np.sin(2 * np.pi * phasor(fd, dn)) + 0.3 * np.sin(4 * np.pi * phasor(fd, dn))
    deflate *= np.exp(-dt_ / 0.09) * env_asr(dn, 0.004, 0.05)

    y = whistle * 0.40 + roar * 0.75
    add(y, ns(0.22), ps, 0.85)
    add(y, ns(0.24), sfilt(deflate, "lowpass", 2200.0, 2), 0.32)

    y = sfilt(y, "highpass", 90.0, 2)
    return dc_block(y * env_asr(n, 0.001, 0.02))


# --------------------------------------------------------------------------
# suspension
# --------------------------------------------------------------------------

BUMPS = [
    dict(seed=5401, f=(96.0, 158.0, 261.0), tau=0.048, creak=760.0, dur=0.24),
    dict(seed=5402, f=(112.0, 179.0, 302.0), tau=0.038, creak=980.0, dur=0.21),
    dict(seed=5403, f=(84.0, 137.0, 226.0), tau=0.058, creak=640.0, dur=0.27),
]


def make_bump(v):
    n = ns(v["dur"])
    r = rng(v["seed"])
    bend = 1.0 + 0.05 * np.exp(-tt(n) / 0.014)
    body = modal(n, list(v["f"]), [v["tau"], v["tau"] * 0.6, v["tau"] * 0.35],
                 [1.0, 0.48, 0.22], attack=0.0016, bend=bend, r=r)
    body *= env_ad(n, 0.0018, v["tau"] * 1.4, curve=2.6)

    # rubber/tyre compress: soft, dark, no click
    pomf = white(n, r) * env_ad(n, 0.004, 0.022, curve=3.0)
    pomf = sfilt(pomf, "lowpass", 620.0, 2)

    # a small spring creak
    cn = ns(0.09)
    cr = white(cn, r) * env_ad(cn, 0.006, 0.030, 2.0)
    cr = reson(sfilt(cr, "bandpass", (v["creak"] * 0.7, v["creak"] * 2.0), 2),
               v["creak"], 6.0)

    y = body * 1.0 + pomf * 0.55
    add(y, ns(0.012), cr / (np.max(np.abs(cr)) + 1e-9), 0.16)
    y = sfilt(y, "highpass", 55.0, 2)
    y = sfilt(y, "lowpass", 3200.0, 2)   # soft: nothing sharp on top
    return dc_block(y * env_asr(n, 0.0012, 0.02))


def render():
    for i, v in enumerate(BONKS, 1):
        write(out("Mower", f"bonk_{i:02d}.wav"), make_bonk(v), fade_out=0.005)
    write(out("Mower", "horn.wav"), make_horn(), fade_out=0.008)
    write(out("Mower", "drift_loop.wav"), make_drift_loop())
    write(out("Mower", "boost_start.wav"), make_boost_start(), fade_out=0.006)
    write(out("Mower", "boost_loop.wav"), make_boost_loop())
    write(out("Mower", "boost_end.wav"), make_boost_end(), fade_out=0.010)
    for i, v in enumerate(BUMPS, 1):
        write(out("Mower", f"suspension_bump_{i:02d}.wav"), make_bump(v), fade_out=0.005)


if __name__ == "__main__":
    render()
    print("mower rendered")
