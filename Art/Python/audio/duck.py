"""
DUCK MOW - Duck/

The duck's whole personality is in four quacks, so these get the most care.

A quack is a hard-edged glottal buzz (a duck's syrinx makes a very narrow,
very buzzy pulse - width 0.12-0.18, not a soft 0.3) pushed through a bill that
OPENS and then CLOSES over about 200 ms.  That formant movement is the entire
sound: F1 sweeps up then down, F2 falls the whole way.  Get that and it is a
quack; hold the formants still and it is a kazoo.

Emotion is carried by three things and nothing else:
  * how many quacks and how fast they come,
  * whether the pitch contour ends up or down,
  * how much roughness/jitter (a panicked animal has an unstable voice).
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
import critter as C


def bill_quack(dur, f0_start, f0_end, r, *, open_amt=1.0, harsh=0.0,
               bright=1.0, level=1.0, snap=0.35, attack=0.0035):
    """
    One quack.

    open_amt : how far the bill opens - scales the F1 excursion, which is what
               makes a quack sound big rather than pinched.
    harsh    : roughness + noise, for annoyance and panic.
    snap     : the bill-edge click on the attack.
    """
    n = ns(dur)

    # pitch: a fast overshoot at the top of the attack, then a settled fall
    f0 = C.curve([(0, f0_start * 1.16), (0.06, f0_start),
                  (0.45, (f0_start + f0_end) * 0.5), (1, f0_end)], n)

    # bill opens then closes: F1 up-and-back, F2 monotonically down
    F1 = C.curve([(0, 640), (0.18, 640 + 430 * open_amt), (0.55, 900),
                  (1, 700 - 90 * open_amt)], n)
    F2 = C.curve([(0, 2250), (0.25, 1980), (0.6, 1720), (1, 1420)], n)
    F3 = C.curve([(0, 2950 * bright), (0.5, 2780 * bright), (1, 2600 * bright)], n)
    F4 = C.curve([(0, 3850 * bright), (1, 3550 * bright)], n)

    env = env_asr(n, attack, dur * 0.30, curve=1.0)
    # a duck's quack has a little pressure hump early on
    env = env * C.curve([(0, 0.55), (0.10, 1.0), (0.45, 0.86), (1, 0.55)], n)

    src = C.glottal(n, f0, r, jitter=0.014 + 0.030 * harsh,
                    shimmer=0.08 + 0.14 * harsh, width=0.145,
                    rough=0.12 + 0.42 * harsh)
    y = C.formants(src, [F1, F2, F3, F4],
                   qs=[3.6, 3.2, 2.6, 2.2], gains=[1.0, 0.78, 0.40, 0.20])

    nb = C.breath(n, r, 700.0, 7000.0)
    y = y + (0.12 + 0.20 * harsh) * C.formants(
        nb, [F1, F2, F3], qs=[3.6, 3.2, 2.6], gains=[1.0, 0.7, 0.35])

    y = y * env
    y = satur(y * (1.7 + 0.6 * harsh), 1.6, 0.6)

    # the bill itself: a tiny woody snap on contact
    if snap > 0:
        sn = ns(0.012)
        clk = modal(sn, [1450.0, 2680.0], [0.0035, 0.0018], [1.0, 0.5],
                    attack=0.0002, r=r)
        clk = clk + strike(sn, r, 1800.0, 9000.0, 0.0006) * 0.6
        add(y, 0, clk * env_ad(sn, 0.0003, 0.004, 3.0), snap * 0.45)

    y = C.radiate(y, hp=240.0, lp=9000.0)
    return y / (np.max(np.abs(y)) + 1e-12) * level


def make_happy():
    """Two bright quacks, the second lifting - a duck pleased with itself."""
    n = ns(0.58)
    r = rng(9001)
    y = np.zeros(n)
    add(y, ns(0.000), bill_quack(0.185, 330, 268, r, open_amt=1.05,
                                 bright=1.06, harsh=0.05), 1.00)
    add(y, ns(0.215), bill_quack(0.210, 352, 322, r, open_amt=1.15,
                                 bright=1.10, harsh=0.05), 0.94)
    return dc_block(sfilt(y, "highpass", 220.0, 2))


def make_annoyed():
    """One long flat quack that sags at the end.  Lower, nasal, a bit rough."""
    n = ns(0.52)
    r = rng(9002)
    y = np.zeros(n)
    q = bill_quack(0.38, 252, 196, r, open_amt=0.62, bright=0.88,
                   harsh=0.45, attack=0.006)
    add(y, ns(0.010), q, 1.0)
    # a grumbled second syllable trailing off under it
    add(y, ns(0.330), bill_quack(0.140, 205, 172, r, open_amt=0.45,
                                 bright=0.82, harsh=0.55, snap=0.15), 0.42)
    y = sfilt(y, "highpass", 200.0, 2)
    # nasal: pull out the 900-1400 Hz shoulder
    y = y + reson(y, 620.0, 3.0) * 0.35
    return dc_block(y)


def make_panic():
    """Four short quacks, accelerating and climbing, voice coming apart."""
    n = ns(0.72)
    r = rng(9003)
    y = np.zeros(n)
    starts = (0.000, 0.145, 0.272, 0.382)
    pitches = ((372, 330), (404, 356), (438, 392), (476, 430))
    for i, (t, (a, b)) in enumerate(zip(starts, pitches)):
        q = bill_quack(0.115 - 0.012 * i, a, b, r, open_amt=0.80,
                       bright=1.18, harsh=0.55 + 0.10 * i,
                       snap=0.45, attack=0.0022)
        add(y, ns(t), q, 1.0 - 0.06 * i)
    # a last frightened intake of air
    hn = ns(0.16)
    gasp = bandpass_tv(white(hn, r), np.linspace(700.0, 2100.0, hn), 2.4)
    gasp *= env_swell(hn, 0.6, 1.8, 1.5)
    add(y, ns(0.500), gasp, 0.26)
    return dc_block(sfilt(y, "highpass", 250.0, 2))


def make_proud():
    """One long quack that RISES and is held - a duck taking a bow."""
    n = ns(0.78)
    r = rng(9004)
    y = np.zeros(n)

    q = bill_quack(0.60, 292, 336, r, open_amt=1.25, bright=1.02,
                   harsh=0.08, attack=0.008)
    # a slow proud vibrato over the held part
    vib = 1.0 + 0.055 * np.clip((tt(ns(0.60)) - 0.20) / 0.18, 0, 1) \
        * np.sin(2 * np.pi * 5.6 * tt(ns(0.60)))
    q = q * (0.85 + 0.15 * vib)
    add(y, ns(0.006), q, 1.0)

    # chest: a little body under it so it sounds like it came from somewhere
    chest = modal(n, [148.0, 232.0], [0.09, 0.05], [1.0, 0.4],
                  attack=0.010, r=r)
    chest *= env_asr(n, 0.02, 0.25, 1.2)
    add(y, ns(0.010), sfilt(chest, "lowpass", 700.0, 2), 0.22)

    # and a small satisfied bill clatter to finish
    for i, t in enumerate((0.640, 0.672, 0.702)):
        sn = ns(0.020)
        clk = modal(sn, [1180.0 + 90 * i, 2340.0], [0.005, 0.0025], [1.0, 0.45],
                    attack=0.0003, r=r)
        add(y, ns(t), clk * env_ad(sn, 0.0004, 0.005, 3.0), 0.22 - 0.04 * i)

    return dc_block(sfilt(y, "highpass", 150.0, 2))


def render():
    write(out("Duck", "quack_happy.wav"), make_happy(), fade_out=0.010)
    write(out("Duck", "quack_annoyed.wav"), make_annoyed(), fade_out=0.012)
    write(out("Duck", "quack_panic.wav"), make_panic(), fade_out=0.010)
    write(out("Duck", "quack_proud.wav"), make_proud(), fade_out=0.014)


if __name__ == "__main__":
    render()
    print("duck rendered")
