"""
Numeric verification for every rendered clip.

We cannot listen to these files, so every claim about them has to be measured.
`python analyze.py` walks Assets/Audio and prints a table; `report()` returns
the raw numbers so render scripts can assert on them.

Measured per clip:
  * duration, channels, file size
  * peak dBFS  (must land on -1.5) and RMS dBFS (the practical loudness)
  * crest factor - separates "has a transient" from "flat noise wall"
  * f0 estimated by autocorrelation over the loudest window
  * harmonicity - how much of the spectrum sits on multiples of f0
  * spectral centroid + octave-band energy split
  * attack time (10% -> 90% of the RMS envelope peak)
  * loop seam metrics for anything registered as a loop
"""

from __future__ import annotations

import os
import sys
import glob
import numpy as np
from scipy.io import wavfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dsp
from dsp import SR, lin2db

AUDIO_ROOT = dsp.AUDIO_ROOT

# clips that must loop seamlessly
LOOPS = {
    "engine_idle_loop", "engine_mid_loop", "engine_high_loop",
    "blade_loop", "blade_cut_grass_loop",
    "drift_loop", "boost_loop",
    "crowd_ambient_loop", "applause_loop",
    "birds_loop", "wind_grass_loop", "pond_loop",
    "music_menu_loop", "music_round_loop", "music_round_urgent_layer",
    "music_judging_bed_loop",
}


def load(path):
    sr, data = wavfile.read(path)
    assert sr == dsp.OUTPUT_SR, f"{path}: sample rate {sr} != {dsp.OUTPUT_SR}"
    assert data.dtype == np.int16, f"{path}: dtype {data.dtype} != int16"
    x = data.astype(np.float64) / 32768.0
    if x.ndim == 2:
        x = x.T  # (2, N)
    return x


def env_rms(mono, win=1024, hop=256):
    n = len(mono)
    starts = np.arange(0, max(n - win, 1), hop)
    return np.array([np.sqrt(np.mean(mono[s:s + win] ** 2)) for s in starts]), hop


def est_f0(mono, fmin=18.0, fmax=4000.0):
    """
    Autocorrelation f0 over the loudest window.

    Picks the first strong *local peak* after the AC's first zero crossing (a
    plain argmax just returns the shortest allowed lag for any low-passed
    signal), then does an octave-error pass: if a peak near half the lag is
    almost as strong, prefer it.
    """
    n = len(mono)
    w = min(32768, n)
    if n > w:
        e, hop = env_rms(mono, 2048, 1024)
        s = min(int(np.argmax(e)) * hop, n - w)
    else:
        s = 0
    seg = mono[s:s + w] * np.hanning(w)
    seg = seg - seg.mean()
    if np.std(seg) < 1e-7:
        return 0.0, 0.0
    ac = np.correlate(seg, seg, "full")[w - 1:]
    ac = ac / (ac[0] + 1e-12)
    # normalise out the triangular window bias
    taper = np.maximum(np.arange(w, 0, -1) / w, 1e-3)
    ac = ac / taper

    lo = max(int(SR / fmax), 2)
    hi = min(int(SR / fmin), w // 2 - 2)
    if hi <= lo + 4:
        return 0.0, 0.0
    z = np.where(ac[lo:hi] < 0)[0]
    start = lo + (int(z[0]) if len(z) else 0)
    if start >= hi - 3:
        return 0.0, 0.0

    seg_ac = ac[start:hi]
    idx = np.where((seg_ac[1:-1] > seg_ac[:-2]) & (seg_ac[1:-1] >= seg_ac[2:]))[0] + 1
    if len(idx) == 0:
        return 0.0, 0.0
    best = idx[int(np.argmax(seg_ac[idx]))]
    val = seg_ac[best]
    # octave correction: an earlier peak worth >=0.85 of the best wins
    strong = idx[seg_ac[idx] >= 0.85 * val]
    best = int(strong[0])
    k = start + best
    return SR / k, float(np.clip(ac[k], -1, 1))


def harmonicity(mono, f0, nh=12):
    """Fraction of spectral energy within +-3% of the first nh multiples of f0."""
    if f0 <= 0:
        return 0.0
    n = len(mono)
    X = np.abs(np.fft.rfft(mono * np.hanning(n))) ** 2
    f = np.fft.rfftfreq(n, 1 / SR)
    tot = X.sum() + 1e-20
    m = np.zeros_like(f, dtype=bool)
    for h in range(1, nh + 1):
        fh = f0 * h
        if fh > SR * 0.45:
            break
        m |= np.abs(f - fh) < max(fh * 0.03, 3.0)
    return float(X[m].sum() / tot)


def spectrum(mono):
    n = len(mono)
    X = np.abs(np.fft.rfft(mono * np.hanning(n))) ** 2
    f = np.fft.rfftfreq(n, 1 / SR)
    return f, X


def centroid(mono):
    f, X = spectrum(mono)
    return float((f * X).sum() / (X.sum() + 1e-20))


def band_energy(mono, lo, hi):
    """Fraction of total energy in [lo, hi) Hz."""
    f, X = spectrum(mono)
    return float(X[(f >= lo) & (f < hi)].sum() / (X.sum() + 1e-20))


def octave_split(mono):
    edges = [20, 80, 160, 320, 640, 1280, 2560, 5120, 10240, 22050]
    return [band_energy(mono, a, b) for a, b in zip(edges[:-1], edges[1:])]


def spectral_peak(mono, fmin=30.0):
    f, X = spectrum(mono)
    m = f >= fmin
    return float(f[m][np.argmax(X[m])])


def attack_time(mono):
    e, hop = env_rms(mono, 512, 128)
    if e.max() < 1e-9:
        return 0.0
    p = e.max()
    i90 = int(np.argmax(e >= 0.9 * p))
    above10 = np.where(e >= 0.1 * p)[0]
    i10 = int(above10[0]) if len(above10) else 0
    return max(i90 - i10, 0) * hop / SR


def measure(path):
    x = load(path)
    stereo = x.ndim == 2
    mono = x.mean(axis=0) if stereo else x
    n = mono.shape[-1]
    name = os.path.splitext(os.path.basename(path))[0]

    f0, conf = est_f0(mono)
    peak = float(np.max(np.abs(x)))
    rms = float(np.sqrt(np.mean(mono ** 2)))

    m = {
        "name": name,
        "path": path,
        "dur": n / SR,
        "ch": 2 if stereo else 1,
        "bytes": os.path.getsize(path),
        "peak_dbfs": lin2db(peak),
        "rms_dbfs": lin2db(rms),
        "crest_db": lin2db(peak / (rms + 1e-12)),
        "f0": f0,
        "f0_conf": conf,
        "harm": harmonicity(mono, f0),
        "centroid": centroid(mono),
        "spec_peak": spectral_peak(mono),
        "attack_s": attack_time(mono),
        "oct": octave_split(mono),
        "eng_band": band_energy(mono, 60, 160),
        "is_loop": name in LOOPS,
    }
    if stereo:
        c = np.corrcoef(x[0], x[1])[0, 1]
        m["lr_corr"] = float(c)
    if m["is_loop"]:
        m.update(dsp.loop_discontinuity(x))
    return m


def all_clips():
    return sorted(glob.glob(os.path.join(AUDIO_ROOT, "**", "*.wav"), recursive=True))


def report(paths=None, verbose=True):
    paths = paths or all_clips()
    rows = [measure(p) for p in paths]
    if verbose:
        hdr = (f"{'clip':<28}{'s':>6}{'ch':>3}{'peak':>7}{'rms':>7}{'crest':>7}"
               f"{'f0':>8}{'harm':>6}{'cent':>7}{'atk_ms':>8}  seam")
        print(hdr)
        print("-" * len(hdr))
        cat = None
        for m in rows:
            c = os.path.basename(os.path.dirname(m["path"]))
            if c != cat:
                cat = c
                print(f"[{cat}]")
            seam = ""
            if m["is_loop"]:
                seam = (f"step={m['step_dbfs']:6.1f}dBFS p{m['step_pct']:4.0f} "
                        f"lvl={m['seam_db']:+5.2f}dB p{m['seam_pct']:3.0f}")
            print(f"{m['name']:<28}{m['dur']:6.2f}{m['ch']:3d}"
                  f"{m['peak_dbfs']:7.2f}{m['rms_dbfs']:7.1f}{m['crest_db']:7.1f}"
                  f"{m['f0']:8.1f}{m['harm']:6.2f}{m['centroid']:7.0f}"
                  f"{m['attack_s']*1000:8.1f}  {seam}")
        tot = sum(m["bytes"] for m in rows)
        print("-" * len(hdr))
        print(f"{len(rows)} clips, {tot/1024/1024:.2f} MB uncompressed")
    return rows


def check(rows=None):
    """Hard gates.  Prints anything that fails."""
    rows = rows or report(verbose=False)
    bad = []
    for m in rows:
        if abs(m["peak_dbfs"] - dsp.TARGET_DBFS) > 0.35:
            bad.append(f"{m['name']}: peak {m['peak_dbfs']:.2f} dBFS (want -1.5)")
        if m["peak_dbfs"] > -0.5:
            bad.append(f"{m['name']}: too hot")
        if m["is_loop"]:
            if m["step_pct"] > 99.9:
                bad.append(f"{m['name']}: seam step is the largest in the clip")
            if m["step_dbfs"] > m["rms_dbfs"] + 6.0:
                bad.append(f"{m['name']}: seam step {m['step_dbfs']:.1f} dBFS "
                           f"is above the clip RMS - audible click")
            if not (2.0 <= m["seam_pct"] <= 98.0):
                bad.append(f"{m['name']}: seam window level is an outlier "
                           f"(p{m['seam_pct']:.0f}, {m['seam_db']:+.1f} dB) - "
                           f"dip or bump at the loop point")
    for b in bad:
        print("FAIL", b)
    if not bad:
        print("all gates pass")
    return bad


if __name__ == "__main__":
    args = sys.argv[1:]
    sel = None
    if args:
        sel = [p for p in all_clips()
               if any(a.lower() in p.lower() for a in args)]
    rows = report(sel)
    print()
    check(rows)
