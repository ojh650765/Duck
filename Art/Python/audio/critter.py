"""
DUCK MOW - shared animal-voice synthesis.

Used by Duck/, Judges/ and Crowd/.  The model is the classic source-filter one:

    a buzzy glottal pulse train (with jitter and optional roughness)
        -> a bank of moving band-pass formants
        -> a radiation tilt

Everything a listener reads as "which animal is that" lives in three places:
the pitch contour, where the first two formants sit and how they move, and how
much of the signal is noise rather than buzz.  Those are the knobs here.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403


def curve(points, n):
    """Piecewise-linear control curve from [(0..1 position, value), ...]."""
    xs = np.array([p[0] for p in points], dtype=float)
    ys = np.array([p[1] for p in points], dtype=float)
    return np.interp(np.linspace(0.0, 1.0, n), xs, ys)


def glottal(n, f0, r, jitter=0.012, shimmer=0.05, width=0.28, rough=0.0,
            sub=0.0):
    """
    The buzz.  `jitter` is slow pitch wander (a perfectly steady pitch is the
    number-one tell of a synthetic voice), `shimmer` is per-cycle amplitude
    variation, `rough` adds a sub-audio growl by modulating the pulse width.
    """
    f0 = np.broadcast_to(np.asarray(f0, dtype=float), (n,)).copy()
    if jitter > 0:
        f0 = f0 * (1.0 + jitter * smoothnoise(n, max(int(n / SR * 22), 2), r))
    w = width
    if rough > 0:
        w = width * (1.0 + rough * 0.55 * np.sin(2 * np.pi * 31.0 * tt(n)))
        w = np.clip(w, 0.06, 0.6)
    if np.ndim(w) == 0:
        x = bl_pulse(f0, n, width=float(w))
    else:
        # width modulation: two pulse trains differenced gives a variable duty
        ph = phasor(f0, n)
        x = np.zeros(n)
        k = min(int(SR * 0.45 / max(float(np.max(f0)), 1.0)), 90)
        for h in range(1, k + 1):
            x += (np.sin(np.pi * h * w) / (np.pi * h)) * np.cos(2 * np.pi * h * ph)
        x *= 4.0
    if shimmer > 0:
        x = x * (1.0 + shimmer * smoothnoise(n, max(int(n / SR * 60), 3), rng(int(r.integers(1 << 30)))))
    if sub > 0:
        x = x + sub * np.sin(2 * np.pi * phasor(f0 * 0.5, n))
    return x / (np.max(np.abs(x)) + 1e-12)


def formants(x, freqs, qs=None, gains=None):
    """
    Bank of (possibly moving) band-pass formants summed in parallel.
    `freqs` entries may be scalars or per-sample arrays.
    """
    qs = qs or [4.0] * len(freqs)
    gains = gains or [1.0, 0.6, 0.35, 0.2][: len(freqs)]
    y = np.zeros(len(x))
    for f, q, g in zip(freqs, qs, gains):
        y += g * bandpass_tv(x, f, q)
    return y


def breath(n, r, lo=700.0, hi=5000.0, tilt=-2.0):
    b = white(n, r)
    b = sfilt(b, "bandpass", (lo, min(hi, SR * 0.45)), 2)
    return b / (np.max(np.abs(b)) + 1e-12)


def radiate(x, hp=180.0, lp=8000.0, tilt=1.5):
    """Mouth radiation: a gentle +dB/oct lift, then band limits."""
    y = sfilt(x, "highpass", hp, 2)
    y = sfilt(y, "lowpass", lp, 2)
    d = np.diff(y, prepend=y[0])
    return y + tilt * 0.25 * d * SR / 8000.0


def critter(dur, f0_pts, formant_pts, r, *, jitter=0.014, shimmer=0.06,
            width=0.28, rough=0.0, sub=0.0, noise=0.10, noise_band=(800, 5000),
            env=None, attack=0.010, release=0.06, qs=None, gains=None,
            hp=180.0, lp=8000.0, drive=1.2):
    """
    One animal utterance.

    f0_pts       : [(pos, Hz), ...] pitch contour
    formant_pts  : list of [(pos, Hz), ...], one per formant
    """
    n = ns(dur)
    f0 = curve(f0_pts, n)
    src = glottal(n, f0, r, jitter=jitter, shimmer=shimmer, width=width,
                  rough=rough, sub=sub)
    fs = [curve(p, n) for p in formant_pts]
    y = formants(src, fs, qs, gains)

    if noise > 0:
        nb = breath(n, r, *noise_band)
        # noise is shaped by the same formants: it comes out of the same mouth
        y = y + noise * formants(nb, fs, qs, gains)

    e = env if env is not None else env_asr(n, attack, release, curve=1.1)
    y = y * e
    y = satur(y * drive, 1.5, 0.55)
    y = radiate(y, hp, lp)
    return y / (np.max(np.abs(y)) + 1e-12)


# --------------------------------------------------------------------------
# ready-made species, used by the crowd and the judges
# --------------------------------------------------------------------------


def goat(dur, r, f0=245.0, up=1.0, tremolo=23.0, depth=0.75):
    """A bleat: the giveaway is a hard 20-26 Hz tremolo, not the vowel."""
    n = ns(dur)
    e = env_asr(n, 0.018, 0.09, 1.0)
    trem = 1.0 - depth * 0.5 * (1.0 + np.sin(2 * np.pi * tremolo * tt(n)))
    e = e * (0.25 + 0.75 * trem)
    return critter(
        dur,
        [(0, f0 * 1.02), (0.12, f0), (0.75, f0 * (1.0 + 0.06 * up)), (1.0, f0 * (1.0 - 0.10 + 0.16 * up))],
        [[(0, 700), (1, 760 + 60 * up)],
         [(0, 1320), (1, 1420)],
         [(0, 2550), (1, 2700)]],
        r, jitter=0.020, shimmer=0.10, width=0.24, rough=0.15,
        noise=0.14, env=e, qs=[3.4, 3.0, 2.4], gains=[1.0, 0.55, 0.28], drive=1.5,
    )


def badger(dur, r, f0=120.0, chuffs=3, up=1.0):
    """A boisterous chuff: low, breathy, several bursts."""
    n = ns(dur)
    y = np.zeros(n)
    step = dur / (chuffs + 0.6)
    for i in range(chuffs):
        cd = step * (0.72 + 0.16 * r.random())
        f = f0 * (1.0 + 0.10 * i * up)
        c = critter(
            cd,
            [(0, f * 1.15), (0.25, f), (1, f * 0.88)],
            [[(0, 470), (1, 400)], [(0, 1080), (1, 940)], [(0, 2300), (1, 2050)]],
            r, jitter=0.030, shimmer=0.14, width=0.34, rough=0.35,
            noise=0.55, noise_band=(300, 3600),
            attack=0.006, release=0.05, qs=[2.6, 2.4, 2.0],
            gains=[1.0, 0.62, 0.30], hp=110.0, lp=6000.0, drive=1.7,
        )
        add(y, ns(i * step), c, 1.0 - 0.14 * i)
    return y / (np.max(np.abs(y)) + 1e-12)


def heron(dur, r, f0=88.0, up=1.0):
    """A dry aloof croak: very low buzz rate, rough, narrow band."""
    n = ns(dur)
    e = env_asr(n, 0.008, 0.05, 1.3)
    e = e * (0.55 + 0.45 * (0.5 + 0.5 * np.sin(2 * np.pi * 14.0 * tt(n) - 1.2)))
    return critter(
        dur,
        [(0, f0 * 1.10), (0.2, f0), (1, f0 * (0.86 + 0.30 * up))],
        [[(0, 620), (1, 700 + 120 * up)],
         [(0, 1450), (1, 1560)],
         [(0, 2450), (1, 2600)]],
        r, jitter=0.045, shimmer=0.18, width=0.14, rough=0.55,
        noise=0.22, noise_band=(500, 4000), env=e,
        qs=[2.2, 2.0, 1.8], gains=[1.0, 0.5, 0.22], hp=200.0, lp=6500.0, drive=1.9,
    )


def goose(dur, r, f0=250.0):
    """Honk."""
    return critter(
        dur,
        [(0, f0 * 0.86), (0.10, f0 * 1.06), (0.6, f0), (1, f0 * 0.80)],
        [[(0, 560), (0.3, 780), (1, 640)],
         [(0, 1150), (1, 1320)],
         [(0, 2400), (1, 2500)]],
        r, jitter=0.018, shimmer=0.08, width=0.20, rough=0.10,
        noise=0.12, attack=0.012, release=0.05,
        qs=[3.0, 2.8, 2.2], gains=[1.0, 0.6, 0.25], drive=1.6,
    )


def sheep(dur, r, f0=190.0):
    return goat(dur, r, f0=f0, up=0.3, tremolo=18.0, depth=0.6)


def cow(dur, r, f0=105.0):
    return critter(
        dur,
        [(0, f0 * 0.94), (0.25, f0), (0.7, f0 * 0.97), (1, f0 * 0.78)],
        [[(0, 380), (0.4, 470), (1, 360)],
         [(0, 880), (1, 800)],
         [(0, 2200), (1, 2100)]],
        r, jitter=0.012, shimmer=0.05, width=0.30, rough=0.08,
        noise=0.16, attack=0.05, release=0.15,
        qs=[2.6, 2.4, 1.9], gains=[1.0, 0.5, 0.18], hp=90.0, lp=5000.0, drive=1.4,
    )


def quack(dur, r, f0=310.0, f0_end=None, bright=1.0, harsh=0.0):
    """Generic duck quack - the Duck/ folder builds its variants on this."""
    f1 = f0_end if f0_end is not None else f0 * 0.80
    return critter(
        dur,
        [(0, f0 * 1.12), (0.08, f0), (0.55, f0 * 0.96), (1, f1)],
        [[(0, 760), (0.15, 940), (0.6, 820), (1, 700)],
         [(0, 2050), (0.2, 1780), (1, 1500)],
         [(0, 2900 * bright), (1, 2650 * bright)],
         [(0, 3700 * bright), (1, 3500 * bright)]],
        r, jitter=0.016 + 0.02 * harsh, shimmer=0.09 + 0.10 * harsh,
        width=0.16, rough=0.10 + 0.35 * harsh,
        noise=0.13 + 0.15 * harsh, noise_band=(700, 6000),
        attack=0.004, release=0.035,
        qs=[3.6, 3.2, 2.6, 2.2], gains=[1.0, 0.72, 0.38, 0.18],
        hp=220.0, lp=8500.0, drive=1.7,
    )
