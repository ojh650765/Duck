"""
DUCK MOW - UI/

Everything here is a struck wooden or wooden-metal object, never an oscillator
with an envelope on it.  The rule for this folder: if it could have been made
by tapping something on the county-fair scoreboard, it is right.

`countdown_beep` / `countdown_go` are marimba-class bars (partials near 1, 4,
9.8 with a warm sub) rather than sines, so they are bright and readable over
the engine without being shrill.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403


def wood_bar(n, f0, r, decay=0.20, bright=1.0, warm=0.35, mallet=0.35,
             partials=(1.0, 4.0, 9.8), tune_blip=0.012):
    """
    A struck wooden bar.  The tuned partials are marimba-like (1 : 4 : 9.8),
    the sub gives it body, and the mallet noise is the attack transient that
    stops it reading as a synth beep.
    """
    decs = [decay, decay * 0.40, decay * 0.18]
    gains = [1.0, 0.42 * bright, 0.20 * bright]
    # a tiny upward pitch blip on the strike: real bars go sharp under the mallet
    bend = 1.0 + tune_blip * np.exp(-tt(n) / 0.004)
    bar = modal(n, [f0 * p for p in partials], decs, gains,
                attack=0.0004, bend=bend, r=r)

    sub = np.sin(2 * np.pi * f0 * 0.5 * tt(n)) * np.exp(-tt(n) / (decay * 0.55))
    sub *= 1.0 - np.exp(-tt(n) / 0.0012)

    knock = strike(n, r, 900.0, 6500.0, 0.0013)
    thud = modal(n, [f0 * 0.33, f0 * 0.51], [0.012, 0.008], [1.0, 0.5],
                 attack=0.0006, r=r)

    y = bar + sub * warm + knock * mallet + thud * 0.25 * mallet
    return y * env_ad(n, 0.0006, decay * 1.5, curve=2.6)


# --------------------------------------------------------------------------
# countdown
# --------------------------------------------------------------------------


def make_countdown_beep():
    n = ns(0.40)
    r = rng(6001)
    # A5.  High enough to sit above the engine, low enough to stay warm.
    y = wood_bar(n, 880.0, r, decay=0.19, bright=0.95, warm=0.42, mallet=0.40)
    y += wood_bar(n, 1320.0, r, decay=0.10, bright=0.6, warm=0.0, mallet=0.0) * 0.22
    y = sfilt(y, "highpass", 150.0, 2)
    y = sfilt(y, "lowpass", 11000.0, 2)
    return dc_block(y)


def make_countdown_go():
    """GO: the same instrument, a bright open chord, plus a shaker accent."""
    n = ns(0.75)
    r = rng(6002)
    y = np.zeros(n)
    # D major triad up an octave-ish - open, festive, not a fanfare yet
    for f, g, d in ((587.33, 1.00, 0.30), (739.99, 0.72, 0.26),
                    (880.00, 0.62, 0.24), (1174.66, 0.48, 0.20)):
        add(y, ns(0.000 + 0.004 * r.random()),
            wood_bar(n, f, r, decay=d, bright=1.1, warm=0.30, mallet=0.30), g)

    # tambourine jingle: many tiny metal discs
    jn = ns(0.30)
    j = np.zeros(jn)
    for _ in range(26):
        p = int(abs(r.normal(0, 0.010)) * SR)
        gl = ns(0.05)
        f = 5200.0 + 4200.0 * r.random()
        g = np.sin(2 * np.pi * f * tt(gl)) * np.exp(-tt(gl) / (0.010 + 0.02 * r.random()))
        add(j, p, g, 0.4 + 0.6 * r.random())
    j = sfilt(j, "highpass", 3800.0, 2)
    add(y, ns(0.002), j / (np.max(np.abs(j)) + 1e-9), 0.30)

    y = sfilt(y, "highpass", 170.0, 2)
    return dc_block(y * env_asr(n, 0.0004, 0.05))


def make_klaxon():
    """
    Time-up horn.  A two-tone fairground klaxon through a limited-bandwidth PA:
    reedy source, a fifth apart, slight detune wobble, one slap echo, and just
    enough distortion to sound like it is coming out of a tin trumpet on a pole.
    """
    dur = 1.30
    n = ns(dur)
    r = rng(6003)
    t = tt(n)
    u = t / dur

    def reed(f0, det):
        f = f0 * (1.0 + 0.006 * np.sin(2 * np.pi * 5.2 * t + det)
                  + 0.020 * np.exp(-t / 0.05))
        # the sag at the end: the air is running out
        f = f * (1.0 - 0.055 * np.clip((u - 0.72) / 0.28, 0, 1) ** 1.5)
        s = bl_pulse(f, n, width=0.30) + 0.5 * bl_saw(f, n)
        return s / (np.max(np.abs(s)) + 1e-12)

    src = reed(415.3, 0.0) * 1.0 + reed(622.25, 1.7) * 0.82
    src = satur(src * 1.5, 2.4, 0.85)

    # horn body
    y = sfilt(src, "bandpass", (300.0, 4200.0), 2)
    y += reson(src, 1020.0, 3.5) * 0.55
    y += reson(src, 2180.0, 4.0) * 0.42
    y += reson(src, 640.0, 2.5) * 0.30

    env = env_asr(n, 0.055, 0.13, curve=1.0)
    env *= 1.0 - 0.18 * np.clip((u - 0.60) / 0.40, 0, 1)
    env *= 1.0 + 0.06 * np.sin(2 * np.pi * 4.6 * t)
    y *= env

    # PA slap: one short reflection off the grandstand
    slap = np.zeros(n)
    add(slap, ns(0.062), sfilt(y, "bandpass", (450.0, 3000.0), 2), 0.26)
    add(slap, ns(0.128), sfilt(y, "bandpass", (450.0, 2200.0), 2), 0.13)
    y = y + slap

    y = sfilt(y, "highpass", 230.0, 2)      # small speaker: no bass at all
    y = sfilt(y, "lowpass", 6500.0, 2)
    y = satur(y * 0.85, 1.6, 0.5)
    return dc_block(y * env_asr(n, 0.002, 0.05))


# --------------------------------------------------------------------------
# small wooden UI
# --------------------------------------------------------------------------


def make_click():
    n = ns(0.11)
    r = rng(6101)
    y = modal(n, [1180.0, 2640.0, 4310.0], [0.020, 0.010, 0.005],
              [1.0, 0.45, 0.20], attack=0.0003, r=r)
    y += strike(n, r, 1500.0, 9000.0, 0.0009) * 0.55
    y += modal(n, [420.0], [0.010], [1.0], attack=0.0005, r=r) * 0.30
    y *= env_ad(n, 0.0004, 0.022, curve=3.0)
    return dc_block(sfilt(y, "highpass", 260.0, 2))


def make_hover():
    n = ns(0.075)
    r = rng(6102)
    y = modal(n, [1980.0, 4180.0], [0.009, 0.005], [1.0, 0.35],
              attack=0.0004, r=r)
    y += strike(n, r, 2200.0, 8000.0, 0.0006) * 0.35
    y *= env_ad(n, 0.0007, 0.011, curve=3.0)
    y = sfilt(y, "highpass", 600.0, 2)
    y = sfilt(y, "lowpass", 7000.0, 2)      # soft: no top-end spit
    return dc_block(y)


def _two_note(f1, f2, gap, seed, decay=0.13):
    n = ns(gap + decay * 3.2)
    r = rng(seed)
    y = np.zeros(n)
    add(y, 0, wood_bar(n, f1, r, decay=decay, bright=0.8, warm=0.30, mallet=0.30), 1.0)
    add(y, ns(gap), wood_bar(n, f2, r, decay=decay * 1.25, bright=0.85,
                             warm=0.32, mallet=0.28), 0.92)
    return dc_block(sfilt(y, "highpass", 200.0, 2))


def make_confirm():
    return _two_note(698.46, 1046.50, 0.075, 6103)     # F5 -> C6, an open fifth up


def make_back():
    return _two_note(880.00, 587.33, 0.070, 6104)      # A5 -> D5, down


def make_score_tick():
    """
    Played up to 30x a second, so: 28 ms long, one narrow band, no low end at
    all (low energy is what accumulates into mud when you retrigger fast), and
    a decay short enough that consecutive ticks never overlap.
    """
    n = ns(0.028)
    r = rng(6105)
    y = modal(n, [1720.0, 3520.0], [0.0055, 0.0028], [1.0, 0.38],
              attack=0.00025, r=r)
    y += strike(n, r, 2400.0, 9500.0, 0.0005) * 0.40
    y *= env_ad(n, 0.0003, 0.0075, curve=3.2)
    y = sfilt(y, "highpass", 950.0, 2)      # nothing below 950 Hz to pile up
    y = sfilt(y, "lowpass", 9000.0, 2)
    return dc_block(y)


# --------------------------------------------------------------------------
# scorecards
# --------------------------------------------------------------------------


def make_card_flip():
    n = ns(0.32)
    r = rng(6201)
    y = np.zeros(n)

    # the card sweeps through the air: bandpass climbing as the edge turns
    sn = ns(0.13)
    fc = 620.0 * (3600.0 / 620.0) ** (tt(sn) / 0.13) ** 0.8
    sw = bandpass_tv(white(sn, r), fc, 2.2) * env_swell(sn, 0.42, 1.5, 1.8)
    add(y, ns(0.004), sw, 0.85)

    # two stiff card flutters
    for i, p in enumerate((0.030, 0.072)):
        fl = ns(0.03)
        g = white(fl, r) * env_ad(fl, 0.0012, 0.008, 3.0)
        g = sfilt(g, "bandpass", (900.0, 5200.0), 2)
        add(y, ns(p), g, 0.5 - 0.15 * i)

    # it lands against the wooden frame
    tap = modal(n, [1340.0, 2810.0, 640.0], [0.014, 0.007, 0.018],
                [1.0, 0.4, 0.5], attack=0.0004, r=r)
    tap += strike(n, r, 1200.0, 8000.0, 0.0010) * 0.5
    add(y, ns(0.150), tap * env_ad(n, 0.0004, 0.020, 3.0), 0.72)

    y = sfilt(y, "highpass", 320.0, 2)
    return dc_block(y * env_asr(n, 0.001, 0.05))


def make_card_raise():
    """Card sliding up out of the holder, then locking with a soft clack."""
    n = ns(0.50)
    r = rng(6202)
    y = np.zeros(n)

    sn = ns(0.30)
    fc = 380.0 * (1900.0 / 380.0) ** (tt(sn) / 0.30) ** 1.2
    slide = bandpass_tv(white(sn, r), fc, 1.7) * env_swell(sn, 0.62, 1.3, 1.6)
    # card stock scraping against wood: a low rasp under the hiss
    rasp = bandpass_tv(white(sn, r), fc * 0.30, 3.0) * env_swell(sn, 0.55, 1.4, 1.6)
    add(y, ns(0.005), slide, 0.75)
    add(y, ns(0.005), rasp, 0.40)

    clack = modal(n, [880.0, 1720.0, 372.0], [0.020, 0.010, 0.026],
                  [1.0, 0.45, 0.55], attack=0.0005, r=r)
    clack += strike(n, r, 800.0, 6000.0, 0.0012) * 0.45
    add(y, ns(0.315), clack * env_ad(n, 0.0006, 0.028, 3.0), 0.85)

    y = sfilt(y, "highpass", 240.0, 2)
    return dc_block(y * env_asr(n, 0.001, 0.06))


def make_stamp():
    """The rank letter slamming down: wood, ink, and a bit of the tent."""
    n = ns(0.55)
    r = rng(6203)
    y = np.zeros(n)

    # a short whoosh of the arm coming down
    wn = ns(0.06)
    wh = bandpass_tv(white(wn, r), np.linspace(900.0, 260.0, wn), 1.4)
    wh *= env_swell(wn, 0.8, 1.6, 1.1)
    add(y, 0, wh, 0.30)

    imp = ns(0.40)
    body = modal(imp, [98.0, 152.0, 247.0, 389.0], [0.075, 0.048, 0.028, 0.016],
                 [1.0, 0.70, 0.42, 0.24], attack=0.0004,
                 bend=1.0 + 0.07 * np.exp(-tt(imp) / 0.008), r=r)
    crack = strike(imp, r, 700.0, 11000.0, 0.0016)
    ink = sfilt(white(imp, r) * env_ad(imp, 0.0015, 0.014, 3.0),
                "bandpass", (1100.0, 4200.0), 2)
    hit = body * 1.0 + crack * 0.60 + ink * 0.35
    hit *= env_ad(imp, 0.0005, 0.085, curve=2.4)
    add(y, ns(0.062), hit, 1.0)

    # awning/tent tail so the verdict lands in a place
    y = y + oreverb(y, ir_room(0.16, r, decay=0.055, bright=2200.0,
                               predelay=0.006), tail=False) * 0.20
    y = sfilt(y, "highpass", 60.0, 2)
    return dc_block(y * env_asr(n, 0.0006, 0.06))


def render():
    write(out("UI", "countdown_beep.wav"), make_countdown_beep(), fade_out=0.006)
    write(out("UI", "countdown_go.wav"), make_countdown_go(), fade_out=0.008)
    write(out("UI", "klaxon.wav"), make_klaxon(), fade_out=0.010)
    write(out("UI", "ui_click.wav"), make_click(), fade_out=0.003)
    write(out("UI", "ui_hover.wav"), make_hover(), fade_out=0.003)
    write(out("UI", "ui_confirm.wav"), make_confirm(), fade_out=0.005)
    write(out("UI", "ui_back.wav"), make_back(), fade_out=0.005)
    write(out("UI", "score_tick.wav"), make_score_tick(), fade_out=0.002)
    write(out("UI", "card_flip.wav"), make_card_flip(), fade_out=0.006)
    write(out("UI", "card_raise.wav"), make_card_raise(), fade_out=0.006)
    write(out("UI", "stamp.wav"), make_stamp(), fade_out=0.010)


if __name__ == "__main__":
    render()
    print("ui rendered")
