"""
DUCK MOW - audio synthesis core.

Everything in the game's soundtrack is synthesised from scratch with numpy/scipy.
This module holds the primitives every clip script uses.

Design rules that matter:

*   **Loops are periodic by construction.**  Filtering a loop buffer is done with
    an FFT multiply (`cfilt`), which is a *circular* convolution - the filter
    tail wraps into the head automatically, so the loop is seamless to the
    sample with no crossfade dip.  Grains/notes that run past the end are
    wrap-added (`wadd`).  Modulators use `plfo`, which only allows an integer
    number of cycles per loop.
*   **One-shots use causal filters** (`sfilt`, sos) so nothing wraps.
*   Peak normalisation is the last thing that happens to any clip, to -1.5 dBFS.

44.1 kHz throughout.  Mono for SFX, stereo for music/ambience.
"""

from __future__ import annotations

import os
import numpy as np
from scipy import signal

SR = 44100
TARGET_DBFS = -1.5

# --------------------------------------------------------------------------
# basics
# --------------------------------------------------------------------------


def ns(seconds: float) -> int:
    """Sample count for a duration (rounded)."""
    return int(round(seconds * SR))


def tt(n: int) -> np.ndarray:
    """Time vector of n samples, seconds."""
    return np.arange(n, dtype=np.float64) / SR


def db2lin(db: float) -> float:
    return 10.0 ** (db / 20.0)


def lin2db(x: float) -> float:
    return 20.0 * np.log10(max(float(x), 1e-12))


def rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


# --------------------------------------------------------------------------
# noise
# --------------------------------------------------------------------------


def white(n: int, r: np.random.Generator) -> np.ndarray:
    return r.standard_normal(n)


def pink(n: int, r: np.random.Generator) -> np.ndarray:
    """Circular pink noise: 1/sqrt(f) magnitude shaping in the FFT domain."""
    x = white(n, r)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    shape = np.ones_like(f)
    shape[1:] = 1.0 / np.sqrt(f[1:] / 20.0)
    shape[0] = 0.0
    shape = np.minimum(shape, 40.0)
    y = np.fft.irfft(X * shape, n)
    return y / (np.std(y) + 1e-12)


def brown(n: int, r: np.random.Generator) -> np.ndarray:
    """Circular brown/red noise (1/f magnitude), DC removed."""
    x = white(n, r)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    shape = np.zeros_like(f)
    shape[1:] = 20.0 / f[1:]
    shape = np.minimum(shape, 60.0)
    y = np.fft.irfft(X * shape, n)
    return y / (np.std(y) + 1e-12)


# --------------------------------------------------------------------------
# frequency-domain filter responses (analog prototypes evaluated on the bins)
# --------------------------------------------------------------------------


def _s(f: np.ndarray, fc: float) -> np.ndarray:
    return 1j * (f / max(fc, 1e-6))


def H_lp(f, fc, order=2):
    return 1.0 / (1.0 + _s(f, fc)) ** order


def H_hp(f, fc, order=2):
    s = _s(f, fc)
    return (s / (1.0 + s)) ** order


def H_res(f, f0, q):
    """Two-pole resonator, peak gain ~= q at f0, unity at DC."""
    x = f / max(f0, 1e-6)
    return 1.0 / (1.0 - x * x + 1j * x / max(q, 1e-6))


def H_bp(f, f0, q):
    """Band-pass with unity peak gain at f0."""
    x = f / max(f0, 1e-6)
    return (1j * x / max(q, 1e-6)) / (1.0 - x * x + 1j * x / max(q, 1e-6))


def H_peak(f, f0, q, gain_db):
    """
    Peaking/bell EQ (use a negative gain to carve a band out).

    Analog prototype (s^2 + (A/Q)s + 1) / (s^2 + (1/(AQ))s + 1), whose gain at
    f0 is A**2 - so A must be sqrt of the requested gain for `gain_db` to mean
    what it says.
    """
    g = db2lin(gain_db * 0.5)
    x = f / max(f0, 1e-6)
    num = 1.0 - x * x + 1j * x * g / max(q, 1e-6)
    den = 1.0 - x * x + 1j * x / (g * max(q, 1e-6))
    return num / den


def H_tilt(f, pivot=800.0, db_per_oct=-3.0):
    """Smooth spectral tilt, 0 dB at the pivot."""
    with np.errstate(divide="ignore"):
        oct_ = np.log2(np.maximum(f, 1e-3) / pivot)
    return db2lin(db_per_oct * oct_)


def cfilt(x: np.ndarray, hfun) -> np.ndarray:
    """Circular (seamless) filtering.  `hfun(f)` returns a complex response."""
    n = x.shape[-1]
    f = np.fft.rfftfreq(n, 1.0 / SR)
    H = hfun(f)
    if np.ndim(x) == 1:
        return np.fft.irfft(np.fft.rfft(x) * H, n)
    return np.stack([np.fft.irfft(np.fft.rfft(c) * H, n) for c in x])


# --------------------------------------------------------------------------
# causal filters for one-shots
# --------------------------------------------------------------------------


def sfilt(x, btype, cutoff, order=2, q=None):
    """Causal biquad chain.  cutoff is Hz (scalar) or (lo, hi) for band types."""
    ny = SR * 0.5
    if btype == "peakres":
        b, a = signal.iirpeak(cutoff / ny, q or 4.0)
        sos = np.array([[b[0], b[1], b[2], a[0], a[1], a[2]]])
    elif btype == "notch":
        b, a = signal.iirnotch(cutoff / ny, q or 4.0)
        sos = np.array([[b[0], b[1], b[2], a[0], a[1], a[2]]])
    else:
        wn = (
            np.clip(np.asarray(cutoff, dtype=float) / ny, 1e-5, 0.99999)
            if np.ndim(cutoff)
            else np.clip(cutoff / ny, 1e-5, 0.99999)
        )
        sos = signal.butter(order, wn, btype=btype, output="sos")
    return signal.sosfilt(sos, x)


def reson(x, f0, q, gain=1.0):
    """Causal two-pole resonator ping filter (constant skirt gain)."""
    b, a = signal.iirpeak(np.clip(f0, 20, SR * 0.49) / (SR * 0.5), q)
    return gain * signal.lfilter(b, a, x)


def svf(x, fc, q):
    """
    Time-varying state-variable filter (Zavalishin TPT topology).

    `fc` may be a scalar or a per-sample array - this is what lets the duck
    quacks and animal calls sweep their formants.  Returns (lp, bp, hp).
    """
    n = len(x)
    fc = np.broadcast_to(np.asarray(fc, dtype=float), (n,))
    fc = np.clip(fc, 20.0, SR * 0.45)
    g = np.tan(np.pi * fc / SR)
    k = 1.0 / max(q, 1e-3)
    a1 = 1.0 / (1.0 + g * (g + k))
    a2 = g * a1
    a3 = g * a2

    lp = np.empty(n)
    bp = np.empty(n)
    hp = np.empty(n)
    ic1 = 0.0
    ic2 = 0.0
    for i in range(n):
        v3 = x[i] - ic2
        v1 = a1[i] * ic1 + a2[i] * v3
        v2 = ic2 + a2[i] * ic1 + a3[i] * v3
        ic1 = 2.0 * v1 - ic1
        ic2 = 2.0 * v2 - ic2
        lp[i] = v2
        bp[i] = v1
        hp[i] = x[i] - k * v1 - v2
    return lp, bp, hp


def _bpf_coeffs(f0, q):
    """RBJ band-pass biquad, constant 0 dB peak gain."""
    w0 = 2 * np.pi * np.clip(f0, 15.0, SR * 0.47) / SR
    alpha = np.sin(w0) / (2.0 * max(q, 1e-3))
    a0 = 1.0 + alpha
    b = np.array([alpha, 0.0, -alpha]) / a0
    a = np.array([1.0, -2.0 * np.cos(w0) / a0, (1.0 - alpha) / a0])
    return b, a


def bandpass_tv(x, fc, q, block=128):
    """
    Time-varying band-pass with unity peak gain.

    Coefficients are recomputed once per 128-sample block (~3 ms) with the
    filter state carried across the boundary.  Formants never move faster than
    that, and it is ~40x quicker than a per-sample state-variable loop, which
    matters when a crowd bed contains a few hundred voices.
    """
    n = len(x)
    fc_arr = np.broadcast_to(np.asarray(fc, dtype=float), (n,))
    if fc_arr.std() < 1e-6:
        b, a = _bpf_coeffs(float(fc_arr[0]), q)
        return signal.lfilter(b, a, x)

    out = np.empty(n)
    zi = np.zeros(2)
    for s in range(0, n, block):
        e = min(s + block, n)
        b, a = _bpf_coeffs(float(fc_arr[s:e].mean()), q)
        out[s:e], zi = signal.lfilter(b, a, x[s:e], zi=zi)
    return out


# --------------------------------------------------------------------------
# envelopes
# --------------------------------------------------------------------------


def env_ad(n, attack, decay, curve=3.0, hold=0.0):
    """Attack / hold / exponential decay envelope."""
    a = max(ns(attack), 1)
    h = ns(hold)
    e = np.zeros(n)
    a = min(a, n)
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 1.4
    rest = n - a
    if rest <= 0:
        return e
    hh = min(h, rest)
    e[a : a + hh] = 1.0
    rest = n - a - hh
    if rest <= 0:
        return e
    d = tt(rest)
    e[a + hh :] = np.exp(-curve * d / max(decay, 1e-4))
    return e


def env_asr(n, attack, release, curve=1.0):
    """Attack, plateau, smooth release across the whole buffer."""
    e = np.ones(n)
    a = min(max(ns(attack), 1), n)
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** curve
    r = min(max(ns(release), 1), n - a) if n > a else 0
    if r > 0:
        e[n - r :] *= np.cos(np.linspace(0, np.pi / 2, r)) ** curve
    return e


def env_swell(n, peak_at=0.35, attack_curve=1.6, decay_curve=2.2):
    """Single hump: fast-ish in, slower out.  For crowd swells and whooshes."""
    p = np.clip(peak_at, 0.02, 0.98)
    k = int(n * p)
    e = np.empty(n)
    e[:k] = np.linspace(0, 1, k) ** attack_curve
    e[k:] = np.linspace(1, 0, n - k) ** decay_curve
    return e


def expdec(n, tau):
    return np.exp(-tt(n) / max(tau, 1e-5))


def plfo(n, cycles, phase=0.0, shape="sin"):
    """
    Periodic LFO with an **integer** number of cycles over the buffer, so it is
    automatically loop-safe.  Returns values in [-1, 1].
    """
    c = max(int(round(cycles)), 0)
    ph = 2 * np.pi * (c * np.arange(n) / n + phase)
    if shape == "sin":
        return np.sin(ph)
    if shape == "tri":
        return signal.sawtooth(ph, 0.5)
    if shape == "saw":
        return signal.sawtooth(ph)
    raise ValueError(shape)


def smoothnoise(n, cycles, r, order=2):
    """Loop-safe smooth random modulation: circular-lowpassed noise, [-1,1]."""
    x = white(n, r)
    fc = max(cycles, 1) * SR / n
    y = cfilt(x, lambda f: H_lp(f, fc, order))
    y -= y.mean()
    m = np.max(np.abs(y)) + 1e-12
    return y / m


# --------------------------------------------------------------------------
# oscillators
# --------------------------------------------------------------------------


def phasor(freq, n):
    """Phase ramp (cycles) from an instantaneous frequency (scalar or array)."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    return np.cumsum(f) / SR


def bl_saw(freq, n, nharm=None, phase0=0.0):
    """Band-limited sawtooth by additive synthesis (handles glides)."""
    ph = phasor(freq, n) + phase0
    fmax = float(np.max(np.broadcast_to(np.asarray(freq, float), (n,))))
    k = nharm or max(1, int(SR * 0.45 / max(fmax, 1.0)))
    k = min(k, 200)
    out = np.zeros(n)
    for h in range(1, k + 1):
        out += np.sin(2 * np.pi * h * ph) / h
    return out * (2.0 / np.pi)


def bl_square(freq, n, nharm=None, phase0=0.0):
    ph = phasor(freq, n) + phase0
    fmax = float(np.max(np.broadcast_to(np.asarray(freq, float), (n,))))
    k = nharm or max(1, int(SR * 0.45 / max(fmax, 1.0)))
    k = min(k, 200)
    out = np.zeros(n)
    for h in range(1, k + 1, 2):
        out += np.sin(2 * np.pi * h * ph) / h
    return out * (4.0 / np.pi)


def bl_pulse(freq, n, width=0.25, nharm=None):
    """Band-limited pulse - the buzzy glottal source for animal voices."""
    ph = phasor(freq, n)
    fmax = float(np.max(np.broadcast_to(np.asarray(freq, float), (n,))))
    k = nharm or max(1, int(SR * 0.45 / max(fmax, 1.0)))
    k = min(k, 160)
    out = np.zeros(n)
    for h in range(1, k + 1):
        out += (np.sin(np.pi * h * width) / (np.pi * h)) * np.cos(2 * np.pi * h * ph)
    return out * 4.0


def sine(freq, n, phase0=0.0):
    return np.sin(2 * np.pi * (phasor(freq, n) + phase0))


def tri(freq, n, nharm=12):
    ph = phasor(freq, n)
    out = np.zeros(n)
    sign = 1.0
    for i, h in enumerate(range(1, 2 * nharm, 2)):
        out += sign * np.sin(2 * np.pi * h * ph) / (h * h)
        sign = -sign
    return out * (8.0 / np.pi**2)


# --------------------------------------------------------------------------
# struck bodies (wood, metal, plastic) - used by bonks, pings, UI, percussion
# --------------------------------------------------------------------------


def modal(n, freqs, decays, gains=None, attack=0.0006, bend=None, r=None):
    """
    Sum of exponentially decaying partials - a struck object.

    freqs   : partial frequencies (inharmonic ratios read as metal/wood, exact
              integer ratios read as a tuned pitch)
    decays  : per-partial time constant, seconds (high partials should die
              first, that is what makes it sound like a real body)
    bend    : optional per-sample multiplier on all frequencies (a small
              downward bend on impact is what gives a hit its "weight")
    """
    gains = gains if gains is not None else [1.0 / (i + 1) for i in range(len(freqs))]
    t = tt(n)
    y = np.zeros(n)
    atk = 1.0 - np.exp(-t / max(attack, 1e-6))
    for f, d, g in zip(freqs, decays, gains):
        fr = f * bend if bend is not None else f
        ph = 2 * np.pi * phasor(fr, n)
        if r is not None:
            ph = ph + r.random() * 2 * np.pi
        y += g * np.sin(ph) * np.exp(-t / max(d, 1e-5)) * atk
    return y / (np.max(np.abs(y)) + 1e-12)


def strike(n, r, lo=600.0, hi=6000.0, tau=0.0022):
    """The contact transient - the scrape of two things touching."""
    x = white(n, r) * np.exp(-tt(n) / max(tau, 1e-5))
    return sfilt(x, "bandpass", (lo, min(hi, SR * 0.45)), 2)


def karplus(freq, n, r, damp=0.42, decay=0.9965, bright=1.0, pick=0.35):
    """
    Karplus-Strong plucked string - the banjo / plucked-bass voice.

    Written as the closed recursion

        y[j] = decay * ((1-damp) * y[j-L] + damp * y[j-L-1])

    which can be evaluated a whole delay-line length at a time with numpy
    instead of sample by sample; a 30 s banjo part has a few hundred notes in
    it and the per-sample version is 50x slower.

    `damp` is the loop one-zero coefficient (higher = duller, faster high-end
    decay).  Its group delay is ~damp samples, so the integer delay is chosen
    as round(SR/f - damp) to keep the string in tune.
    """
    L = max(int(round(SR / max(freq, 20.0) - damp)), 2)
    exc = white(L, r)
    exc = sfilt(exc, "highpass", 180.0 * bright, 1)
    exc = sfilt(exc, "lowpass", 1500.0 * bright + 1100.0, 1)
    exc /= np.max(np.abs(exc)) + 1e-12

    buf = np.zeros(n + L + 2)
    buf[1 : L + 1] = exc
    j = L + 1
    while j < n + 1:
        m = min(L, n + 1 - j)
        buf[j : j + m] = decay * ((1.0 - damp) * buf[j - L : j - L + m]
                                  + damp * buf[j - L - 1 : j - L - 1 + m])
        j += m
    out = buf[1 : n + 1].copy()

    if pick > 0:
        k = min(ns(0.004), n)
        out[:k] += pick * np.exp(-tt(k) / 0.0009) * np.sin(2 * np.pi * freq * 3 * tt(k))
    return out


# --------------------------------------------------------------------------
# placement / mixing helpers
# --------------------------------------------------------------------------


def wadd(buf: np.ndarray, pos: int, grain: np.ndarray, gain: float = 1.0):
    """
    Wrap-add a grain into a loop buffer at sample `pos`.  Anything past the end
    wraps to the start, which is exactly what makes grain-based loops seamless.
    Works on mono (1-D) and stereo (2, N) buffers.
    """
    n = buf.shape[-1]
    g = grain * gain
    m = g.shape[-1]
    pos = int(pos) % n
    if m >= n:
        # fold the whole thing
        for off in range(0, m, n):
            chunk = g[..., off : off + n]
            idx = (np.arange(chunk.shape[-1]) + pos) % n
            np.add.at(buf, (Ellipsis, idx), chunk)
        return
    end = pos + m
    if end <= n:
        buf[..., pos:end] += g
    else:
        k = n - pos
        buf[..., pos:] += g[..., :k]
        buf[..., : end - n] += g[..., k:]


def add(buf: np.ndarray, pos: int, grain: np.ndarray, gain: float = 1.0):
    """Non-wrapping add (one-shots); silently truncates at the end."""
    n = buf.shape[-1]
    pos = int(pos)
    if pos >= n:
        return
    if pos < 0:
        grain = grain[..., -pos:]
        pos = 0
    m = min(grain.shape[-1], n - pos)
    if m > 0:
        buf[..., pos : pos + m] += grain[..., :m] * gain


def pan(mono: np.ndarray, p: float) -> np.ndarray:
    """Constant-power pan.  p = -1 hard left .. +1 hard right."""
    a = (np.clip(p, -1, 1) + 1) * 0.25 * np.pi
    return np.stack([mono * np.cos(a), mono * np.sin(a)])


def widen(mono: np.ndarray, r: np.random.Generator, ms=9.0, amount=0.55):
    """
    Cheap decorrelation for beds: a short circular all-pass-ish delay on one
    side.  Stays mono-compatible because the delay is short and level-matched.
    """
    n = len(mono)
    d = ns(ms / 1000.0)
    right = np.roll(mono, d)
    left = np.roll(mono, -d // 2)
    return np.stack(
        [mono * (1 - amount) + left * amount, mono * (1 - amount) + right * amount]
    )


def decorrelate(n, r, hfun, seeds=(0, 1)):
    """Two independent circular-filtered noise channels: a wide, seamless bed."""
    a = cfilt(pink(n, rng(int(r) + seeds[0])), hfun)
    b = cfilt(pink(n, rng(int(r) + seeds[1])), hfun)
    return np.stack([a, b])


# --------------------------------------------------------------------------
# saturation / dynamics
# --------------------------------------------------------------------------


def satur(x, drive=1.0, mix=1.0):
    """Soft asymmetric-ish saturation; adds harmonics without hard clipping."""
    y = np.tanh(x * drive) / np.tanh(drive) if drive > 0 else x
    return x * (1 - mix) + y * mix


def follow(x, tau_a=0.004, tau_r=0.06):
    """Simple envelope follower (used to duck/modulate layers)."""
    ax = np.abs(x)
    aa = np.exp(-1.0 / (SR * tau_a))
    ar = np.exp(-1.0 / (SR * tau_r))
    out = np.empty_like(ax)
    y = 0.0
    for i in range(len(ax)):
        c = aa if ax[i] > y else ar
        y = c * y + (1 - c) * ax[i]
        out[i] = y
    return out


def follow_fast(x, tau=0.02):
    """Vectorised approximate envelope follower (circular, loop-safe)."""
    n = len(x)
    fc = 1.0 / (2 * np.pi * tau)
    return np.abs(cfilt(np.abs(x), lambda f: H_lp(f, fc, 1)))


# --------------------------------------------------------------------------
# reverb / space
# --------------------------------------------------------------------------


def ir_room(seconds, r, decay=0.6, bright=2600.0, predelay=0.008, n=None):
    """Synthetic decaying-noise impulse response with a dark tail."""
    m = ns(seconds)
    x = white(m, r) * np.exp(-tt(m) / max(decay, 1e-3))
    x = sfilt(x, "lowpass", bright, 2)
    x = sfilt(x, "highpass", 180.0, 2)
    pd = ns(predelay)
    x = np.concatenate([np.zeros(pd), x])[:m]
    x[0] += 1.0  # dry impulse so convolution keeps the direct sound
    return x / (np.sqrt(np.sum(x**2)) + 1e-9) * 1.2


def creverb(x, ir):
    """Circular convolution reverb - tail wraps, so loops stay seamless."""
    n = x.shape[-1]
    irp = np.zeros(n)
    m = min(len(ir), n)
    irp[:m] = ir[:m]
    IR = np.fft.rfft(irp)
    if np.ndim(x) == 1:
        return np.fft.irfft(np.fft.rfft(x) * IR, n)
    return np.stack([np.fft.irfft(np.fft.rfft(c) * IR, n) for c in x])


def oreverb(x, ir, tail=True):
    """Linear convolution reverb for one-shots."""
    if np.ndim(x) == 1:
        y = signal.fftconvolve(x, ir)
        return y if tail else y[: len(x)]
    return np.stack([oreverb(c, ir, tail) for c in x])


# --------------------------------------------------------------------------
# loop conditioning
# --------------------------------------------------------------------------


def dc_block(x):
    return x - np.mean(x, axis=-1, keepdims=True)


def wrap_xfade(x, loop_len, tail, power=True):
    """
    Fallback seamless-loop maker for material that could not be made periodic by
    construction: render `loop_len + tail` samples, then fold the tail back over
    the head with a **complementary** crossfade (equal-power by default) so the
    energy stays constant - no dip at the seam.
    """
    L = int(loop_len)
    T = int(tail)
    assert x.shape[-1] >= L + T
    y = np.array(x[..., :L], dtype=float)
    u = np.linspace(0, 1, T, endpoint=False)
    if power:
        fi, fo = np.sqrt(u), np.sqrt(1 - u)
    else:
        fi, fo = u, 1 - u
    y[..., :T] = y[..., :T] * fi + x[..., L : L + T] * fo
    return y


def loop_discontinuity(x):
    """
    Numeric seam verification.  Returns a dict:

      step_pct   - the wrap-around sample step |x[0]-x[-1]| expressed as a
                   PERCENTILE of the clip's own interior sample-to-sample steps.
                   A genuinely seamless loop scores well under 99: the join is
                   indistinguishable from any other sample boundary.  A butt
                   join scores 100.
      step_dbfs  - the same step in absolute dBFS (how loud the click would be).
      seam_db    - RMS of a 1024-sample window straddling the wrap versus the
                   median interior window, in dB.  ~0 dB proves there is no
                   amplitude dip (the classic fade-both-ends mistake) and no
                   bump at the seam.
    """
    if np.ndim(x) > 1:
        mono = x.mean(axis=0)
        step = float(np.max(np.abs(x[:, 0] - x[:, -1])))
        d = np.abs(np.diff(x, axis=-1)).max(axis=0)
    else:
        mono = x
        step = float(abs(x[0] - x[-1]))
        d = np.abs(np.diff(x))

    pct = float((d < step).mean() * 100.0)

    w = 1024
    seam_db = 0.0
    seam_pct = 50.0
    if len(mono) >= 8 * w:
        seam = np.concatenate([mono[-w // 2 :], mono[: w // 2]])
        seam_rms = np.sqrt(np.mean(seam**2))
        starts = np.arange(0, len(mono) - w, w // 2)
        inner = np.array([np.sqrt(np.mean(mono[s : s + w] ** 2)) for s in starts])
        seam_db = lin2db(seam_rms / (np.median(inner) + 1e-12))
        # percentile matters more than the median ratio for impulsive material
        # (applause, granular shredding), where interior windows vary hugely
        # on their own and a +-5 dB median ratio is completely normal.
        seam_pct = float((inner < seam_rms).mean() * 100.0)

    return {
        "step_pct": pct,
        "step_dbfs": lin2db(step),
        "seam_db": seam_db,
        "seam_pct": seam_pct,
    }


# --------------------------------------------------------------------------
# output
# --------------------------------------------------------------------------


def normalize(x, dbfs=TARGET_DBFS):
    peak = np.max(np.abs(x))
    if peak < 1e-9:
        return x
    return x * (db2lin(dbfs) / peak)


# Everything is synthesised at 44.1 kHz.  Set this lower (e.g. 22050) to write
# smaller files; `write` resamples with an FFT, which is circular and therefore
# keeps loops sample-exact seamless at the new rate.
OUTPUT_SR = SR


def write(path, x, dbfs=TARGET_DBFS, fade_in=0.0, fade_out=0.0, normalise=True):
    """
    Write 16-bit PCM.  Mono if x is 1-D, stereo if (2, N).
    `fade_in`/`fade_out` are for ONE-SHOTS only - never use them on a loop.
    """
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 2 and x.shape[0] != 2:
        raise ValueError("stereo buffers must be shaped (2, N)")
    x = dc_block(x)
    if fade_in > 0:
        k = min(ns(fade_in), x.shape[-1])
        x[..., :k] *= np.sin(np.linspace(0, np.pi / 2, k)) ** 2
    if fade_out > 0:
        k = min(ns(fade_out), x.shape[-1])
        x[..., -k:] *= np.cos(np.linspace(0, np.pi / 2, k)) ** 2
    if OUTPUT_SR != SR:
        m = int(round(x.shape[-1] * OUTPUT_SR / SR))
        # FFT resampling is circular, so a seamless loop stays seamless; the
        # one-shots start and end near silence so the wrap costs nothing.
        x = signal.resample(x, m, axis=-1)
    if normalise:
        x = normalize(x, dbfs)
    if np.max(np.abs(x)) > 0.9999:
        raise RuntimeError(f"{path}: would clip")

    data = np.clip(x, -1.0, 1.0)
    ints = np.round(data * 32767.0).astype("<i2")
    if ints.ndim == 2:
        ints = ints.T.copy()  # interleave
    os.makedirs(os.path.dirname(path), exist_ok=True)
    from scipy.io import wavfile

    wavfile.write(path, OUTPUT_SR, ints)
    return path


AUDIO_ROOT = r"C:\Duck\Assets\Audio"


def out(category: str, name: str) -> str:
    return os.path.join(AUDIO_ROOT, category, name)
