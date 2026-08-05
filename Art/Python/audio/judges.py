"""
DUCK MOW - Judges/

Three short non-verbal stings per judge, "low" for a bad score and "high" for a
good one.  They must be tellable apart with the eyes shut, so each animal owns a
different part of the design space:

  Mildred the goat   - 245 Hz buzz, hard 23 Hz bleat tremolo, mid formants.
                       Severe: the LOW version falls and stops abruptly.
  Boris the badger   - 120 Hz, mostly breath, several chuffs, low formants.
                       Generous: the HIGH version is four fast rising chuffs.
  Priscilla the heron - 88 Hz croak, very narrow pulse, rough, dry, no tail.
                       Hard to please: even her HIGH version barely lifts.

The tremolo rate, the chuff count and the pulse width are the three things
carrying "which animal"; pitch direction carries "what did they think".
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
import critter as C


# --------------------------------------------------------------------------
# Mildred - goat
# --------------------------------------------------------------------------


def goat_low():
    """Dismissive: short, drops a fourth, chopped off at the end."""
    r = rng(9101)
    n = ns(0.62)
    y = np.zeros(n)
    e = env_asr(ns(0.50), 0.014, 0.05, 1.0)
    trem = 0.28 + 0.72 * (0.5 - 0.5 * np.cos(2 * np.pi * 24.0 * tt(ns(0.50))))
    v = C.critter(
        0.50,
        [(0, 258), (0.10, 248), (0.55, 236), (1, 182)],
        [[(0, 700), (0.5, 680), (1, 590)],
         [(0, 1320), (1, 1180)],
         [(0, 2520), (1, 2350)]],
        r, jitter=0.022, shimmer=0.12, width=0.24, rough=0.22,
        noise=0.16, env=e * trem, qs=[3.4, 3.0, 2.4],
        gains=[1.0, 0.55, 0.26], drive=1.6,
    )
    add(y, ns(0.005), v, 1.0)
    # she is already chewing again
    for i, t in enumerate((0.500, 0.548)):
        cn = ns(0.045)
        ch = white(cn, r) * env_ad(cn, 0.002, 0.012, 2.6)
        ch = sfilt(ch, "bandpass", (450.0, 2600.0), 2)
        add(y, ns(t), ch, 0.22 - 0.06 * i)
    return dc_block(sfilt(y, "highpass", 190.0, 2))


def goat_high():
    """Grudgingly impressed: longer, lifts at the end, two bleats."""
    r = rng(9102)
    n = ns(0.95)
    y = np.zeros(n)

    e1 = env_asr(ns(0.46), 0.016, 0.06, 1.0)
    tr1 = 0.30 + 0.70 * (0.5 - 0.5 * np.cos(2 * np.pi * 22.0 * tt(ns(0.46))))
    v1 = C.critter(
        0.46,
        [(0, 246), (0.15, 252), (1, 268)],
        [[(0, 690), (1, 780)], [(0, 1300), (1, 1440)], [(0, 2500), (1, 2680)]],
        r, jitter=0.020, shimmer=0.10, width=0.23, rough=0.18,
        noise=0.14, env=e1 * tr1, qs=[3.4, 3.0, 2.4],
        gains=[1.0, 0.55, 0.28], drive=1.6)
    add(y, 0, v1, 1.0)

    e2 = env_asr(ns(0.36), 0.014, 0.07, 1.0)
    tr2 = 0.30 + 0.70 * (0.5 - 0.5 * np.cos(2 * np.pi * 25.0 * tt(ns(0.36))))
    v2 = C.critter(
        0.36,
        [(0, 262), (0.2, 274), (1, 300)],
        [[(0, 720), (1, 830)], [(0, 1360), (1, 1520)], [(0, 2560), (1, 2760)]],
        r, jitter=0.020, shimmer=0.10, width=0.22, rough=0.16,
        noise=0.13, env=e2 * tr2, qs=[3.4, 3.0, 2.4],
        gains=[1.0, 0.55, 0.28], drive=1.6)
    add(y, ns(0.545), v2, 0.86)
    return dc_block(sfilt(y, "highpass", 190.0, 2))


# --------------------------------------------------------------------------
# Boris - badger
# --------------------------------------------------------------------------


def badger_low():
    """A single disappointed chuff that sinks into a grumble."""
    r = rng(9201)
    n = ns(0.72)
    y = np.zeros(n)
    v = C.critter(
        0.30,
        [(0, 138), (0.2, 124), (1, 98)],
        [[(0, 500), (1, 390)], [(0, 1120), (1, 900)], [(0, 2350), (1, 2000)]],
        r, jitter=0.032, shimmer=0.16, width=0.36, rough=0.40,
        noise=0.62, noise_band=(260, 3200), attack=0.008, release=0.07,
        qs=[2.6, 2.4, 2.0], gains=[1.0, 0.62, 0.28],
        hp=105.0, lp=5200.0, drive=1.7)
    add(y, ns(0.010), v, 1.0)

    grumble = C.critter(
        0.34,
        [(0, 96), (1, 82)],
        [[(0, 400), (1, 350)], [(0, 880), (1, 800)]],
        r, jitter=0.045, shimmer=0.20, width=0.40, rough=0.55,
        noise=0.40, noise_band=(200, 2200), attack=0.03, release=0.12,
        qs=[2.3, 2.0], gains=[1.0, 0.5], hp=95.0, lp=3600.0, drive=1.5)
    add(y, ns(0.320), grumble, 0.48)
    return dc_block(sfilt(y, "highpass", 90.0, 2))


def badger_high():
    """Four fast rising chuffs and a bark - Boris likes everything."""
    r = rng(9202)
    n = ns(1.05)
    y = np.zeros(n)
    times = (0.00, 0.145, 0.275, 0.395)
    for i, t in enumerate(times):
        f = 118.0 * (1.0 + 0.11 * i)
        c = C.critter(
            0.135,
            [(0, f * 1.18), (0.2, f), (1, f * 0.94)],
            [[(0, 480 + 40 * i), (1, 430 + 40 * i)],
             [(0, 1080 + 90 * i), (1, 960 + 90 * i)],
             [(0, 2300), (1, 2100)]],
            r, jitter=0.030, shimmer=0.15, width=0.34, rough=0.34,
            noise=0.58, noise_band=(280, 3800), attack=0.005, release=0.045,
            qs=[2.6, 2.4, 2.0], gains=[1.0, 0.62, 0.30],
            hp=110.0, lp=6200.0, drive=1.8)
        add(y, ns(t), c, 1.0 - 0.05 * i)

    bark = C.critter(
        0.26,
        [(0, 170), (0.12, 158), (1, 128)],
        [[(0, 620), (0.3, 700), (1, 520)],
         [(0, 1300), (1, 1080)], [(0, 2600), (1, 2300)]],
        r, jitter=0.028, shimmer=0.14, width=0.26, rough=0.30,
        noise=0.40, noise_band=(400, 4500), attack=0.004, release=0.06,
        qs=[2.8, 2.5, 2.0], gains=[1.0, 0.6, 0.28],
        hp=120.0, lp=6500.0, drive=1.9)
    add(y, ns(0.560), bark, 0.95)

    # he is applauding before the card is even up
    for i, t in enumerate((0.86, 0.925)):
        cn = ns(0.06)
        cl = white(cn, r) * env_ad(cn, 0.0006, 0.010, 3.0)
        cl = sfilt(cl, "bandpass", (700.0, 5200.0), 2)
        add(y, ns(t), cl / (np.max(np.abs(cl)) + 1e-9), 0.34 - 0.08 * i)
    return dc_block(sfilt(y, "highpass", 90.0, 2))


# --------------------------------------------------------------------------
# Priscilla - heron
# --------------------------------------------------------------------------


def heron_low():
    """A flat, dry croak.  No inflection whatsoever.  That is the insult."""
    r = rng(9301)
    n = ns(0.55)
    y = np.zeros(n)
    e = env_asr(ns(0.30), 0.006, 0.035, 1.4)
    rasp = 0.45 + 0.55 * (0.5 + 0.5 * np.sin(2 * np.pi * 15.0 * tt(ns(0.30)) - 1.1))
    v = C.critter(
        0.30,
        [(0, 96), (0.15, 88), (1, 80)],
        [[(0, 610), (1, 580)], [(0, 1440), (1, 1400)], [(0, 2440), (1, 2400)]],
        r, jitter=0.050, shimmer=0.20, width=0.13, rough=0.60,
        noise=0.22, noise_band=(500, 4200), env=e * rasp,
        qs=[2.2, 2.0, 1.8], gains=[1.0, 0.5, 0.22],
        hp=210.0, lp=6200.0, drive=1.9)
    add(y, ns(0.008), v, 1.0)

    # one slow blink's worth of silence, then a short dismissive click
    cn = ns(0.03)
    tk = modal(cn, [820.0, 1640.0], [0.006, 0.003], [1.0, 0.4], attack=0.0004, r=r)
    add(y, ns(0.430), tk * env_ad(cn, 0.0005, 0.007, 3.0), 0.30)
    return dc_block(sfilt(y, "highpass", 200.0, 2))


def heron_high():
    """Two croaks, the second lifting a little.  For Priscilla this is rapture."""
    r = rng(9302)
    n = ns(0.82)
    y = np.zeros(n)

    e1 = env_asr(ns(0.24), 0.006, 0.03, 1.4)
    ra1 = 0.45 + 0.55 * (0.5 + 0.5 * np.sin(2 * np.pi * 16.0 * tt(ns(0.24)) - 1.1))
    v1 = C.critter(
        0.24, [(0, 94), (1, 90)],
        [[(0, 630), (1, 660)], [(0, 1460), (1, 1520)], [(0, 2460), (1, 2520)]],
        r, jitter=0.048, shimmer=0.19, width=0.13, rough=0.58,
        noise=0.22, noise_band=(500, 4200), env=e1 * ra1,
        qs=[2.2, 2.0, 1.8], gains=[1.0, 0.5, 0.22],
        hp=210.0, lp=6400.0, drive=1.9)
    add(y, 0, v1, 1.0)

    e2 = env_asr(ns(0.34), 0.006, 0.045, 1.4)
    ra2 = 0.45 + 0.55 * (0.5 + 0.5 * np.sin(2 * np.pi * 13.0 * tt(ns(0.34)) - 1.1))
    v2 = C.critter(
        0.34, [(0, 92), (0.3, 98), (1, 112)],
        [[(0, 650), (1, 790)], [(0, 1480), (1, 1650)], [(0, 2480), (1, 2650)]],
        r, jitter=0.045, shimmer=0.18, width=0.14, rough=0.52,
        noise=0.24, noise_band=(500, 4600), env=e2 * ra2,
        qs=[2.2, 2.0, 1.8], gains=[1.0, 0.5, 0.22],
        hp=210.0, lp=6600.0, drive=1.9)
    add(y, ns(0.330), v2, 0.92)

    # a single beak clack, which from her counts as applause
    cn = ns(0.05)
    tk = modal(cn, [1180.0, 2260.0, 3400.0], [0.008, 0.004, 0.002],
               [1.0, 0.5, 0.25], attack=0.0003, r=r)
    tk += strike(cn, r, 1600.0, 9000.0, 0.0007) * 0.5
    add(y, ns(0.720), tk * env_ad(cn, 0.0004, 0.010, 3.0), 0.34)
    return dc_block(sfilt(y, "highpass", 200.0, 2))


def render():
    write(out("Judges", "judge_goat_low.wav"), goat_low(), fade_out=0.012)
    write(out("Judges", "judge_goat_high.wav"), goat_high(), fade_out=0.012)
    write(out("Judges", "judge_badger_low.wav"), badger_low(), fade_out=0.015)
    write(out("Judges", "judge_badger_high.wav"), badger_high(), fade_out=0.012)
    write(out("Judges", "judge_heron_low.wav"), heron_low(), fade_out=0.012)
    write(out("Judges", "judge_heron_high.wav"), heron_high(), fade_out=0.012)


if __name__ == "__main__":
    render()
    print("judges rendered")
