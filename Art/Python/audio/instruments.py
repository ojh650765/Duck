"""
DUCK MOW - the county-fair band.

Five synthesised instruments plus percussion, and a tiny step sequencer that
renders note lists into a stereo buffer.  Notes that run past the end of a loop
are wrap-added, so a loop's decay tails land back on its own downbeat - that,
plus circular reverb, is what makes the music loops seamless without a
crossfade.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403


def m2f(midi):
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


# --------------------------------------------------------------------------
# instruments.  All take (freq, dur_seconds, vel 0..1, rng) -> mono array.
# --------------------------------------------------------------------------


def banjo(f, dur, vel, r):
    """
    Five-string banjo: Karplus-Strong with a bright, hard pick and a short
    body resonance.  Banjos are a membrane over a pot, so there is a strong
    300-400 Hz head resonance and very little sustain below it.
    """
    n = ns(min(dur + 0.55, 1.6))
    damp = 0.30 + 0.10 * (1.0 - vel)
    s = karplus(f, n, r, damp=damp, decay=0.9955, bright=1.5, pick=0.30)
    s *= np.exp(-tt(n) / (0.45 + 0.35 * vel))
    # the head: resonant, and it is where the "plink" lives
    y = s + reson(s, 340.0, 2.2) * 0.35 + reson(s, 780.0, 3.0) * 0.18
    y = sfilt(y, "highpass", 190.0, 2)
    y = sfilt(y, "lowpass", 8500.0, 2)
    y *= env_asr(n, 0.0008, 0.05, 1.0)
    return y * vel / (np.max(np.abs(y)) + 1e-12) * 1.0


def fiddle(f, dur, vel, r, bow=0.22, vib=0.010):
    """
    Bowed fiddle: a band-limited saw with a slow-onset vibrato, bow noise, and
    a three-peak body.  The delayed vibrato is what stops long notes sounding
    like an organ.
    """
    n = ns(dur + 0.10)
    t = tt(n)
    vibe = 1.0 + vib * np.clip((t - 0.14) / 0.18, 0, 1) * np.sin(2 * np.pi * 5.4 * t)
    # a little bow scrape at the start pulls the pitch flat for ~25 ms
    onset = 1.0 - 0.012 * np.exp(-t / 0.025)
    src = bl_saw(f * vibe * onset, n)
    src = src + 0.30 * bl_square(f * vibe * onset, n)
    src /= np.max(np.abs(src)) + 1e-12

    scrape = white(n, r) * np.exp(-t / 0.05)
    scrape = sfilt(scrape, "bandpass", (1200.0, 7000.0), 2)
    src = src + bow * 0.6 * scrape / (np.max(np.abs(scrape)) + 1e-12)
    # continuous bow hiss riding on the note
    hiss = sfilt(white(n, r), "bandpass", (2000.0, 9000.0), 2)
    src = src + bow * 0.10 * hiss / (np.max(np.abs(hiss)) + 1e-12)

    y = (sfilt(src, "lowpass", 4200.0 + 2500.0 * vel, 2)
         + reson(src, 300.0, 2.0) * 0.30
         + reson(src, 800.0, 2.4) * 0.24
         + reson(src, 2450.0, 2.0) * 0.16)
    y = sfilt(y, "highpass", 170.0, 2)

    env = env_asr(n, 0.035 + 0.02 * (1 - vel), 0.075, curve=1.1)
    env *= 1.0 + 0.05 * np.sin(2 * np.pi * 4.7 * t)   # bow pressure wobble
    y *= env
    return y * vel / (np.max(np.abs(y)) + 1e-12)


def bass(f, dur, vel, r):
    """
    Pizzicato upright.  Deliberately voiced high (196 Hz and above) so the
    round-loop mix leaves the 60-160 Hz engine band empty.
    """
    n = ns(min(dur + 0.35, 1.4))
    t = tt(n)
    body = tri(f, n, nharm=10) * 1.0 + np.sin(2 * np.pi * f * t) * 0.55
    body += 0.22 * bl_saw(f, n, nharm=8)
    dec = np.exp(-t / (0.20 + 0.22 * vel))
    y = body * dec
    # finger release: a short click and the string slapping the board
    k = ns(0.010)
    clk = white(k, r) * np.exp(-tt(k) / 0.0018)
    add(y, 0, sfilt(clk, "bandpass", (500.0, 3500.0), 2), 0.35)
    y = sfilt(y, "lowpass", 2400.0, 2)
    y = sfilt(y, "highpass", 130.0, 2)
    y *= env_asr(n, 0.0015, 0.04, 1.0)
    return y * vel / (np.max(np.abs(y)) + 1e-12)


def melodeon(f, dur, vel, r, reedy=1.0):
    """
    Squeezebox: two detuned reeds through a wooden box.  Slow attack, slight
    breathiness, gentle tremolo - the warm pad that holds the harmony.
    """
    n = ns(dur + 0.18)
    t = tt(n)
    d = 0.0035 * reedy
    a = bl_saw(f * (1.0 - d), n)
    b = bl_saw(f * (1.0 + d), n)
    c = bl_square(f * 2.0, n) * 0.18
    src = (a + b) * 0.5 + c
    src /= np.max(np.abs(src)) + 1e-12

    air = sfilt(white(n, r), "bandpass", (900.0, 6000.0), 2)
    src = src + 0.06 * air / (np.max(np.abs(air)) + 1e-12)

    y = (sfilt(src, "lowpass", 2600.0, 2)
         + reson(src, 420.0, 2.0) * 0.28
         + reson(src, 1250.0, 2.2) * 0.18)
    y = sfilt(y, "highpass", 160.0, 2)
    env = env_asr(n, 0.045, 0.10, curve=1.2)
    env *= 1.0 + 0.045 * np.sin(2 * np.pi * 4.1 * t + 0.7)
    y *= env
    return y * vel / (np.max(np.abs(y)) + 1e-12)


def glock(f, dur, vel, r):
    """A little metal sparkle for the reveal and the good fanfare."""
    n = ns(min(dur + 0.9, 1.6))
    y = modal(n, [f, f * 2.76, f * 5.40, f * 8.93],
              [0.55, 0.28, 0.14, 0.07], [1.0, 0.42, 0.20, 0.09],
              attack=0.0004, r=r)
    y += strike(n, r, 3000.0, 12000.0, 0.0007) * 0.30
    y *= env_ad(n, 0.0006, 0.45, curve=2.2)
    return y * vel


# --------------------------------------------------------------------------
# percussion
# --------------------------------------------------------------------------


def brush(vel, r, bright=1.0):
    """A brush on a snare head: soft, no crack."""
    n = ns(0.11)
    y = white(n, r) * env_ad(n, 0.0025, 0.022, curve=2.6)
    y = sfilt(y, "bandpass", (1600.0 * bright, 7000.0 * bright), 2)
    y += sfilt(white(n, r) * env_ad(n, 0.004, 0.012, 3.0),
               "bandpass", (300.0, 900.0), 2) * 0.25
    return y / (np.max(np.abs(y)) + 1e-12) * vel


def shaker(vel, r):
    n = ns(0.055)
    y = white(n, r) * env_ad(n, 0.0018, 0.010, curve=3.0)
    y = sfilt(y, "bandpass", (4200.0, 12000.0), 2)
    return y / (np.max(np.abs(y)) + 1e-12) * vel


def tamb(vel, r):
    n = ns(0.22)
    y = np.zeros(n)
    for _ in range(16):
        p = int(abs(r.normal(0, 0.006)) * SR)
        gl = ns(0.06)
        f = 5000.0 + 4500.0 * r.random()
        g = np.sin(2 * np.pi * f * tt(gl)) * np.exp(-tt(gl) / (0.012 + 0.02 * r.random()))
        add(y, p, g, 0.4 + 0.6 * r.random())
    y = sfilt(y, "highpass", 3600.0, 2)
    add(y, 0, brush(0.4, r, bright=1.4), 0.5)   # the skin under the jingles
    return y / (np.max(np.abs(y)) + 1e-12) * vel


def stomp(vel, r):
    """A boot on a board.  Sits at ~215 Hz, deliberately clear of the engine."""
    n = ns(0.14)
    y = modal(n, [215.0, 348.0, 561.0], [0.030, 0.018, 0.010],
              [1.0, 0.45, 0.20], attack=0.0006,
              bend=1.0 + 0.06 * np.exp(-tt(n) / 0.006), r=r)
    y += strike(n, r, 400.0, 4000.0, 0.0018) * 0.45
    y *= env_ad(n, 0.0006, 0.035, curve=2.8)
    y = sfilt(y, "highpass", 165.0, 2)
    return y / (np.max(np.abs(y)) + 1e-12) * vel


def woodblock(vel, r, f=1180.0):
    n = ns(0.10)
    y = modal(n, [f, f * 2.32, f * 3.81], [0.016, 0.008, 0.004],
              [1.0, 0.4, 0.18], attack=0.0003, r=r)
    y += strike(n, r, 1400.0, 8000.0, 0.0008) * 0.4
    y *= env_ad(n, 0.0004, 0.020, curve=3.0)
    return y / (np.max(np.abs(y)) + 1e-12) * vel


def swell(dur, vel, r, lo=600.0, hi=9000.0, peak=0.85):
    """A bowed-cymbal style rise, used to lift into the reveal."""
    n = ns(dur)
    y = bandpass_tv(white(n, r), np.linspace(lo, hi, n), 0.9)
    y = y * env_swell(n, peak, 2.4, 1.2)
    return y / (np.max(np.abs(y)) + 1e-12) * vel


# --------------------------------------------------------------------------
# sequencer
# --------------------------------------------------------------------------

INSTRUMENTS = {
    "banjo": banjo,
    "fiddle": fiddle,
    "bass": bass,
    "melodeon": melodeon,
    "glock": glock,
}

PERC = {
    "brush": brush,
    "shaker": shaker,
    "tamb": tamb,
    "stomp": stomp,
    "woodblock": woodblock,
}


class Seq:
    """
    A tiny step sequencer.

    `bpm` and `bars` define the grid; `n` is fixed so the buffer length is an
    exact multiple of the beat, which is what lets the urgent layer line up
    sample-for-sample with the round loop.
    """

    def __init__(self, bpm, beats, loop=True, seed=1):
        self.bpm = bpm
        self.spb = 60.0 / bpm
        self.beats = beats
        self.n = ns(beats * self.spb)
        self.loop = loop
        self.buf = np.zeros((2, self.n))
        self.r = rng(seed)

    def _place(self, pos, mono, gain, p):
        st = pan(mono, p)
        if self.loop:
            wadd(self.buf, pos, st, gain)
        else:
            add(self.buf, pos, st, gain)

    def note(self, inst, beat, dur_beats, midi, vel=0.8, p=0.0, gain=1.0,
             **kw):
        f = m2f(midi)
        s = INSTRUMENTS[inst](f, dur_beats * self.spb, vel, self.r, **kw)
        self._place(ns(beat * self.spb), s, gain, p)

    def hit(self, kind, beat, vel=0.8, p=0.0, gain=1.0, **kw):
        s = PERC[kind](vel, self.r, **kw)
        self._place(ns(beat * self.spb), s, gain, p)

    def raw(self, beat, mono, gain=1.0, p=0.0):
        self._place(ns(beat * self.spb), mono, gain, p)

    def chord(self, inst, beat, dur_beats, midis, vel=0.8, spread=0.0,
              gain=1.0, strum=0.0, **kw):
        for i, m in enumerate(midis):
            pp = spread * (i / max(len(midis) - 1, 1) * 2 - 1)
            self.note(inst, beat + strum * i, dur_beats - strum * i, m,
                      vel, pp, gain, **kw)


# --------------------------------------------------------------------------
# chord vocabulary
# --------------------------------------------------------------------------

CHORD = {
    "G": [55, 59, 62], "C": [60, 64, 67], "D": [62, 66, 69],
    "Em": [64, 67, 71], "Am": [57, 60, 64], "Bm": [59, 62, 66],
    "A": [57, 61, 64], "F#m": [54, 57, 61], "Gm": [55, 58, 62],
    "Dm": [62, 65, 69], "Bb": [58, 62, 65], "E": [64, 68, 71],
}


def voicing(name, lo=67, hi=84):
    """Spread a chord across a register (used for the banjo rolls)."""
    base = CHORD[name]
    out = []
    for m in base:
        while m < lo:
            m += 12
        while m > hi:
            m -= 12
        out.append(m)
    return sorted(set(out))
