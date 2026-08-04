"""
DUCK MOW - Engine/

A single-cylinder small engine, built as three RPM layers Unity crossfades and
pitch-shifts at runtime, plus a start and a stop one-shot.

How it is made
--------------
Each layer is a *firing event train*, not an oscillator.  A combustion grain
(sharp pressure rise, short decay, a puff of combustion noise) is wrap-added
into the loop buffer once per revolution, with:

  * a half-period secondary event at ~0.45 gain -> reinforces the EVEN
    harmonics, which is what makes it read as a lumpy putter rather than a saw;
  * per-event timing jitter and gain jitter -> it never locks into a synth
    drone;
  * a 4-event "cycle lump" pattern + a slow periodic wobble -> the uneven idle.

The train is then run through a fixed exhaust/cavity resonator set with a
circular FFT filter, so the filter tail wraps into the head and the loop is
seamless by construction - no crossfade, so no dip at the seam.

Layer bases: see BASES below.  They are geometrically spaced (ratio ~1.84) so a
constant-power crossfade at the geometric midpoints only ever needs +-30% pitch,
which is as close to the requested +-25% as three layers spanning 28->95 Hz can
physically get.  See AUDIO_SPEC.md for the mapping table.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403

# (name, fundamental Hz, character)
BASES = {"idle": 28.0, "mid": 52.0, "high": 95.0}

LOOP_SECONDS = 2.0


# --------------------------------------------------------------------------
# building blocks
# --------------------------------------------------------------------------


def firing_grain(f0, r, bright=1.0, rasp=0.0, size=1.0):
    """One combustion event: pressure pulse + combustion noise."""
    dur = min(0.85 / f0, 0.026) * size
    n = max(ns(dur), 24)
    t = tt(n)
    # Pressure pulse: fast rise, short decay.  The rise time sets the top
    # corner (too slow and the whole engine goes dull); the decay sets the
    # low corner.  These values put the two corners at roughly 1.5 kHz and
    # 130 Hz, which leaves real content for the pipe resonators to work on.
    rise = 0.00009 + 0.00009 / max(bright, 0.3)
    fall = 0.00095 + 0.00075 / max(bright, 0.3)
    p = (1.0 - np.exp(-t / rise)) * np.exp(-t / fall)
    p /= p.max() + 1e-12
    # combustion turbulence
    nz = white(n, r) * np.exp(-t / (0.0022 + 0.0014 * rasp))
    nz = sfilt(nz, "bandpass", (260.0, 2200.0 + 3000.0 * bright), 2)
    nz /= np.max(np.abs(nz)) + 1e-12
    g = p + nz * (0.42 + 0.45 * rasp)
    return g / (np.max(np.abs(g)) + 1e-12)


def event_train(n, f0, r, jitter=0.018, amp_jit=0.10, bright=1.0, rasp=0.0,
                even_gain=0.45, lump=0.16, wobble_cycles=3, wobble=0.10):
    """
    Wrap-added firing events over exactly N = f0 * dur revolutions, so the
    schedule itself is loop-periodic.
    """
    period = SR / f0
    count = int(round(n / period))
    assert abs(count * period - n) < 1e-6, "loop must contain a whole number of firings"

    buf = np.zeros(n)
    main = firing_grain(f0, r, bright, rasp)
    # the secondary (intake/valve) event is duller and shorter
    sec = firing_grain(f0, r, bright * 0.5, rasp * 0.4, size=0.7)

    # slow periodic wobble - integer cycles so it wraps
    wob = plfo(count, wobble_cycles)

    for i in range(count):
        # 4-stroke style lumpiness: a repeating 4-event weight pattern
        lump_w = 1.0 + lump * [0.0, -0.55, 0.22, -0.28][i % 4]
        g = lump_w * (1.0 + wobble * wob[i]) * (1.0 + amp_jit * r.standard_normal())
        g = max(g, 0.05)
        dt = jitter * period * r.standard_normal()
        wadd(buf, int(round(i * period + dt)), main, g)

        if even_gain > 0:
            g2 = even_gain * g * (1.0 + amp_jit * 1.4 * r.standard_normal())
            dt2 = jitter * 1.6 * period * r.standard_normal()
            wadd(buf, int(round(i * period + period * 0.5 + dt2)), sec, max(g2, 0.02))
    return buf


def exhaust(x, lp=2600.0, extra=None, form=(16.0, 11.0, 8.0), tilt=-1.6):
    """
    Exhaust / cavity formant set, applied circularly (so the loop stays exact).

    The formant CENTRES are absolute - a real pipe does not change length with
    RPM, and keeping them fixed is what makes the three layers read as the same
    machine.  Only their gains open up with RPM, which is how a small engine
    actually behaves when you put a load on it.
    """
    g1, g2, g3 = form

    def H(f):
        # Bell EQs, not cascaded 2-pole resonators: a resonator is a low-pass
        # underneath, and three of them stacked buries everything above 1 kHz.
        h = (
            H_peak(f, 168.0, 1.5, g1)
            * H_peak(f, 425.0, 1.3, g2)
            * H_peak(f, 1120.0, 1.1, g3)
            * H_peak(f, 700.0, 1.6, -7.0)   # small scoop keeps it from honking
            * H_lp(f, lp, 2)
            * H_hp(f, 34.0, 2)
            * H_tilt(f, 400.0, tilt)
        )
        if extra is not None:
            h = h * extra(f)
        return h

    return cfilt(x, H)


def mech_noise(n, train_env, r, lo=280.0, hi=3200.0, seed_off=0):
    """
    Mechanical/valvetrain hiss.  Amplitude-modulated by the firing envelope so
    it breathes with the engine instead of sitting under it as a noise wall.
    """
    nz = pink(n, rng(int(r.integers(1 << 30)) + seed_off))
    nz = cfilt(nz, lambda f: H_bp(f, np.sqrt(lo * hi), 0.75) * H_lp(f, hi, 1))
    e = train_env / (np.max(train_env) + 1e-12)
    return nz * (0.30 + 0.70 * e)


# --------------------------------------------------------------------------
# loops
# --------------------------------------------------------------------------


def make_layer(kind):
    f0 = BASES[kind]
    n = ns(LOOP_SECONDS)
    assert abs(f0 * LOOP_SECONDS - round(f0 * LOOP_SECONDS)) < 1e-9

    cfg = {
        "idle": dict(seed=101, jitter=0.022, amp_jit=0.13, bright=0.55, rasp=0.05,
                     even=0.50, lump=0.24, wobble=0.14, wobble_cycles=3,
                     lp=1900.0, noise=0.20, drive=0.9, nlo=240.0, nhi=2200.0,
                     body=0.42, form=(17.0, 10.0, 6.0), tilt=-1.9),
        "mid": dict(seed=202, jitter=0.014, amp_jit=0.09, bright=0.95, rasp=0.20,
                    even=0.42, lump=0.15, wobble=0.09, wobble_cycles=4,
                    lp=2700.0, noise=0.26, drive=1.35, nlo=300.0, nhi=3400.0,
                    body=0.34, form=(12.0, 11.0, 9.0), tilt=-1.4),
        "high": dict(seed=303, jitter=0.010, amp_jit=0.075, bright=1.35, rasp=0.55,
                     even=0.30, lump=0.10, wobble=0.06, wobble_cycles=6,
                     lp=4200.0, noise=0.42, drive=1.9, nlo=420.0, nhi=5600.0,
                     body=0.22, form=(4.0, 11.0, 14.0), tilt=-0.7),
    }[kind]

    r = rng(cfg["seed"])
    train = event_train(
        n, f0, r,
        jitter=cfg["jitter"], amp_jit=cfg["amp_jit"], bright=cfg["bright"],
        rasp=cfg["rasp"], even_gain=cfg["even"], lump=cfg["lump"],
        wobble_cycles=cfg["wobble_cycles"], wobble=cfg["wobble"],
    )

    extra = None
    if kind == "high":
        # a narrow edge resonance is where the "opened up" rasp lives
        extra = lambda f: H_peak(f, 2350.0, 2.6, 12.0) * H_peak(f, 3300.0, 2.0, 7.0)
    elif kind == "mid":
        extra = lambda f: H_peak(f, 1750.0, 1.8, 6.0)

    core = exhaust(train, lp=cfg["lp"], extra=extra,
                   form=cfg["form"], tilt=cfg["tilt"])

    # low body: the chest thump, kept modest so it does not eat the headroom
    # that the audible 200 Hz - 2 kHz part of the engine needs.
    body = cfilt(train, lambda f: H_res(f, 74.0, 1.4) * H_lp(f, 260.0, 2) * H_hp(f, 38.0, 2))

    env = follow_fast(np.abs(train), tau=0.010)
    mech = mech_noise(n, env, r, cfg["nlo"], cfg["nhi"], seed_off=7)

    y = core / (np.max(np.abs(core)) + 1e-12)
    y = y + cfg["body"] * body / (np.max(np.abs(body)) + 1e-12)
    y = y + cfg["noise"] * mech / (np.max(np.abs(mech)) + 1e-12)

    # gentle saturation glues the layers and adds the last of the even harmonics
    y = satur(y * 0.72, drive=cfg["drive"], mix=0.8)

    # final polish: kill anything the WebGL mixer will only turn into mud,
    # and shave the very top so it can never buzz.
    y = cfilt(y, lambda f: H_hp(f, 32.0, 2) * H_lp(f, cfg["lp"] * 1.9, 2)
              * H_peak(f, 3800.0, 1.0, -6.0))
    return dc_block(y)


# --------------------------------------------------------------------------
# one-shots
# --------------------------------------------------------------------------


def _sched(rate_pts, t_end):
    """Pulse times from a piecewise-linear rate curve [(t, hz), ...]."""
    ts = np.array([p[0] for p in rate_pts])
    hz = np.array([p[1] for p in rate_pts])
    out, t = [], rate_pts[0][0]
    while t < t_end:
        out.append(t)
        t += 1.0 / max(float(np.interp(t, ts, hz)), 1.0)
    return out


def _cord_pull(dur, r, f_lo=350.0, f_hi=2000.0, gain=1.0):
    """Recoil starter: a rasping zip, rising then cut off."""
    n = ns(dur)
    t = tt(n)
    u = t / dur
    nz = white(n, r)
    fc = f_lo * (f_hi / f_lo) ** u
    zip_ = bandpass_tv(nz, fc, 3.0)
    # ratchet teeth
    ratchet = np.zeros(n)
    k = 0.0
    while k < dur:
        i = ns(k)
        if i < n - 40:
            ratchet[i:i + 40] += np.exp(-tt(40) / 0.0009) * (0.6 + 0.4 * r.random())
        k += 0.011 * (1.0 - 0.5 * (k / dur))
    ratchet = sfilt(ratchet, "bandpass", (900.0, 5200.0), 2)
    y = zip_ * 1.0 + ratchet * 0.5
    return y * env_asr(n, 0.012, 0.03) * (0.35 + 0.65 * u) * gain


def _burst(times, f_amp, r, n, bright=0.8, rasp=0.2, lp=2400.0, grain_f0=34.0):
    """Render a list of firing times into a one-shot buffer (causal filters)."""
    buf = np.zeros(n)
    for t in times:
        g = firing_grain(grain_f0, r, bright, rasp)
        add(buf, ns(t), g, f_amp(t))
    y = sfilt(buf, "bandpass", (55.0, lp), 2)
    y += sfilt(buf, "lowpass", 190.0, 2) * 0.9
    return y


def make_start():
    dur = 1.15
    n = ns(dur)
    r = rng(511)
    y = np.zeros(n)

    # --- pull 1: catches for three coughs then dies -----------------------
    add(y, ns(0.00), _cord_pull(0.115, r, 300, 1700), 0.55)
    t1 = _sched([(0.10, 11.0), (0.30, 5.0)], 0.30)
    y += _burst(t1, lambda t: 0.55 * np.exp(-(t - 0.10) / 0.13), r, n,
                bright=0.5, rasp=0.15, lp=2000.0)

    # --- pull 2: closer, four coughs, still dies --------------------------
    add(y, ns(0.34), _cord_pull(0.105, r, 340, 1900), 0.6)
    t2 = _sched([(0.44, 15.0), (0.66, 6.5)], 0.66)
    y += _burst(t2, lambda t: 0.7 * np.exp(-(t - 0.44) / 0.16), r, n,
                bright=0.6, rasp=0.2, lp=2200.0)

    # --- pull 3: it fires ------------------------------------------------
    add(y, ns(0.68), _cord_pull(0.095, r, 380, 2200), 0.65)
    t3 = _sched([(0.76, 13.0), (0.86, 24.0), (0.96, 31.0), (1.15, 28.0)], dur)
    y += _burst(t3, lambda t: 0.95 * min(1.0, 0.35 + (t - 0.76) * 3.2), r, n,
                bright=0.85, rasp=0.3, lp=2900.0)

    # exhaust body + a little room
    y = sfilt(y, "highpass", 38.0, 2)
    y += reson(y, 172.0, 2.3) * 0.30 + reson(y, 430.0, 2.0) * 0.18
    y = satur(y * 0.8, 1.3, 0.75)
    y *= env_asr(n, 0.004, 0.012)
    return dc_block(y)


def make_stop():
    dur = 0.92
    n = ns(dur)
    r = rng(733)
    y = np.zeros(n)

    # rate falls away, amplitude with it
    ts = _sched([(0.0, 28.0), (0.18, 22.0), (0.36, 12.0), (0.52, 6.0)], 0.52)
    y += _burst(ts, lambda t: 0.95 * np.exp(-t / 0.30), r, n,
                bright=0.6, rasp=0.25, lp=2300.0)

    # the wheeze: air bleeding out, pitch falling
    wn = ns(0.42)
    fc = 1500.0 * (240.0 / 1500.0) ** (tt(wn) / 0.42)
    wheeze = bandpass_tv(white(wn, r), fc, 4.5) * np.exp(-tt(wn) / 0.20)
    add(y, ns(0.06), wheeze, 0.30)

    # one last cough
    add(y, ns(0.70), firing_grain(24.0, r, 0.45, 0.35), 0.42)
    puff = white(ns(0.10), r) * np.exp(-tt(ns(0.10)) / 0.030)
    add(y, ns(0.705), sfilt(puff, "bandpass", (180.0, 1400.0), 2), 0.30)

    y = sfilt(y, "highpass", 38.0, 2)
    y += reson(y, 172.0, 2.3) * 0.28 + reson(y, 430.0, 2.0) * 0.15
    y = satur(y * 0.8, 1.2, 0.7)
    y *= env_asr(n, 0.003, 0.030)
    return dc_block(y)


# --------------------------------------------------------------------------
# render
# --------------------------------------------------------------------------

# Target loudness relationship between the three layers, in dB.
#
# All three are set EQUAL.  The three files have different crest factors, so
# peak-normalising each to -1.5 dBFS (which the spec requires) leaves them at
# different RMS levels; the trims below undo that, which means a constant-power
# crossfade between two layers produces no level step at all.  The "engine gets
# louder with RPM" part of the sound belongs on an RPM -> bus-volume curve in
# Unity, not baked into the layers, so it can be tuned without re-rendering.
LAYER_VOICING_DB = {"idle": 0.0, "mid": 0.0, "high": 0.0}


def render():
    paths = {}
    rms = {}
    for kind in ("idle", "mid", "high"):
        y = make_layer(kind)
        p = out("Engine", f"engine_{kind}_loop.wav")
        write(p, y)
        paths[kind] = p
        yn = normalize(y)
        rms[kind] = lin2db(np.sqrt(np.mean(yn ** 2)))

    write(out("Engine", "engine_start.wav"), make_start(), fade_out=0.004)
    write(out("Engine", "engine_stop.wav"), make_stop(), fade_out=0.010)

    # Report the trim each layer needs so that, at -1.5 dBFS peak, all three
    # sit at the intended relative loudness.
    trims = {}
    for k in rms:
        trims[k] = LAYER_VOICING_DB[k] - (rms[k] - rms["high"])
    return {"rms_dbfs": rms, "recommended_trim_db": trims}


if __name__ == "__main__":
    info = render()
    for k, v in info["rms_dbfs"].items():
        print(f"{k:5s} rms {v:6.2f} dBFS   unity trim {info['recommended_trim_db'][k]:+.2f} dB")
