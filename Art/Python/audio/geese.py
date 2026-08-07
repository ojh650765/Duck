"""
DUCK MOW - Geese/  (stage two, the Goose Rally arena)

Nine birds, on screen at once, calling every 2-5 s and beating their wings up to
seven times a second each.  That density is the whole design brief: every clip
here is authored for what it sounds like on the FIFTIETH play in ten seconds,
not for what it sounds like alone.

Three rules follow from that, and they are why this file does not just call
`critter.goose()` with a longer duration:

  1. **A goose is not a duck.**  The game already has four quacks and they must
     not be confusable.  The duck sits at 250-480 Hz through a bill that opens
     and closes (F1 640 -> 1070 -> 700).  The goose sits an octave lower
     (155-235 Hz), holds a much flatter, lower vowel (F1 ~430-540), carries
     three to four times the breath noise, and has a **nasal anti-resonance**
     notched out around 950-1050 Hz.  That notch is the single most
     goose-specific thing in the file: a honk is a nasal sound, and a nasal
     tract is a pole *and a zero*, not just a formant.  Remove the notch and the
     honk immediately reads as a big rude duck.

  2. **Variants must differ in pitch centre AND length**, not in seed.  Three
     renders of the same gesture with different random numbers still read as one
     sound repeated, because the ear tracks contour and duration, not noise
     realisations.  So: 0.44 s at 190 Hz rising-then-sagging, 0.30 s at 232 Hz
     clipped off short, 0.56 s at 158 Hz in two syllables.

  3. **The wing beats are the hard part.**  Up to ~63 of them a second across
     the flock.  Anything with a tone in it turns into a chord; anything with a
     tail longer than its own inter-onset time turns into noise.  So each one is
     a 0.10-0.18 s three-part transient - a low air whump whose band sweeps
     DOWN as the air column decelerates, a mid canvas flap, and a 6 ms leathery
     snap - highpassed at 105 Hz so nine birds cannot pile up in the sub, and
     with the whole thing over before the next stroke of the same wing begins.

Everything here is a ONE-SHOT.  Nothing in this folder loops, so the loop-seam
gates in analyze.py do not apply to any of it.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
import critter as C


# --------------------------------------------------------------------------
# the voice
# --------------------------------------------------------------------------


def goose_voice(dur, f0_pts, r, *, air=0.34, rough=0.42, bright=1.0, neck=1.0,
                nasal_hz=1000.0, nasal_q=2.0, attack=0.004, release=0.10,
                env=None, open_amt=1.0, drive=1.9, sub=0.16):
    """
    One goose vocalisation.

    The source is a narrow, hard glottal pulse with a lot of jitter and an
    octave-down component (`sub`) - a goose's syrinx is a bigger, looser, less
    tidy oscillator than a duck's, and the sub is what puts weight under it
    without dropping the fundamental into the engine's band.

    `air` is the breath fraction.  Geese are LOUD in the sense of moving a lot
    of air, and the noise is shaped by the same formants as the buzz because it
    comes out of the same neck.

    `neck` scales the whole formant set.  It is the second axis the three honk
    variants differ on: a lower pitch centre alone is heard as "the same bird
    further away", whereas a lower pitch centre AND a longer vocal tract is
    heard as a different, bigger bird - which is what nine of them need.
    """
    n = ns(dur)
    f0 = C.curve(f0_pts, n)

    src = C.glottal(n, f0, r, jitter=0.028, shimmer=0.15, width=0.19,
                    rough=rough, sub=sub)

    # A long neck: F1 low and fairly still, F2 in the nasal/buzz region, F3/F4
    # broad and weak.  Compare duck.py, where F1 sweeps 430 Hz and F2 falls 830.
    F1 = C.curve([(0, 400), (0.14, 420 + 130 * open_amt), (0.6, 470),
                  (1, 440)], n) * neck
    F2 = C.curve([(0, 1420), (0.3, 1560 + 120 * open_amt), (1, 1490)], n) * bright * neck
    F3 = C.curve([(0, 2350), (1, 2480)], n) * bright * neck
    F4 = C.curve([(0, 3450), (1, 3300)], n) * bright

    qs = [2.6, 2.1, 1.8, 1.5]
    gains = [1.0, 0.66, 0.30, 0.14]
    y = C.formants(src, [F1, F2, F3, F4], qs=qs, gains=gains)

    if air > 0:
        nb = C.breath(n, r, 350.0, 7000.0)
        y = y + air * C.formants(nb, [F1, F2, F3, F4], qs=qs, gains=gains)

    e = env if env is not None else env_asr(n, attack, release, curve=1.0)
    y = y * e
    y = satur(y * drive, 1.6, 0.62)

    # The nasal zero.  A honk is made in the nose as much as the throat, and a
    # nasal tract puts an anti-resonance just above 1 kHz.  This is the line
    # that stops it sounding like a duck with a cold.
    y = sfilt(y, "notch", nasal_hz, 2, nasal_q)
    y = sfilt(y, "notch", nasal_hz * 2.35, 2, 2.6) * 1.0

    y = C.radiate(y, hp=135.0, lp=7600.0)
    return y / (np.max(np.abs(y)) + 1e-12)


def air_puff(dur, r, lo=250.0, hi=2600.0, peak_at=0.28, gain=1.0):
    """The intake before a call - a goose audibly loads up before it shouts."""
    n = ns(dur)
    x = white(n, r)
    x = bandpass_tv(x, np.linspace(lo, hi, n), 1.5)
    x = x * env_swell(n, peak_at, 1.5, 2.0)
    return x / (np.max(np.abs(x)) + 1e-12) * gain


# --------------------------------------------------------------------------
# honks - three variants, fired every 2-5 s from up to nine birds
# --------------------------------------------------------------------------


def make_honk_1():
    """
    The standard call: 0.44 s, centre 190 Hz.  Snaps up onto the note, holds,
    and sags at the end - a bird stating its intention rather than reacting.
    """
    r = rng(9501)
    n = ns(0.44)
    y = np.zeros(n)

    dur = 0.40
    m = ns(dur)
    e = env_asr(m, 0.0035, 0.13, 1.0)
    # a pressure hump early on, as in duck.py - the lungs are not a valve.  It
    # peaks at 4 % of the note, not 9 %: at 9 % the measured 10->90 % attack was
    # 41 ms, which is a duck's attack, and the brief asked for a hard one.
    e = e * C.curve([(0, 0.86), (0.04, 1.0), (0.42, 0.88), (1, 0.55)], m)
    v = goose_voice(
        dur,
        [(0, 168), (0.06, 205), (0.30, 192), (0.75, 186), (1, 160)],
        r, air=0.34, rough=0.42, open_amt=1.0, env=e)
    add(y, ns(0.004), v, 1.0)

    # The intake, mixed UNDER the onset rather than in front of it.  In front of
    # it, it is a 17 ms ramp on the leading edge and the honk stops being an
    # event; under it, it is the air in the sound.
    add(y, 0, air_puff(0.05, r, 300.0, 2200.0, 0.12), 0.13)
    return dc_block(sfilt(y, "highpass", 120.0, 2))


def make_honk_2():
    """
    The short one: 0.30 s, centre 232 Hz.  Higher, tighter, cut off almost
    where it starts - the bird is already moving.  This is the variant that has
    to survive being the one that happens to fire twice in a row.
    """
    r = rng(9502)
    n = ns(0.30)
    y = np.zeros(n)

    dur = 0.25
    m = ns(dur)
    e = env_asr(m, 0.0022, 0.055, 1.2)
    e = e * C.curve([(0, 0.88), (0.03, 1.0), (0.55, 0.80), (1, 0.42)], m)
    v = goose_voice(
        dur,
        [(0, 214), (0.05, 248), (0.45, 234), (1, 206)],
        r, air=0.38, rough=0.48, bright=1.06, neck=1.10, open_amt=0.72,
        nasal_hz=1040.0, env=e)
    add(y, ns(0.003), v, 1.0)

    add(y, 0, air_puff(0.035, r, 400.0, 2900.0, 0.10), 0.11)
    return dc_block(sfilt(y, "highpass", 130.0, 2))


def make_honk_3():
    """
    The long one: 0.56 s, centre 158 Hz, in two syllables - a low grunt and
    then the honk proper, which sags a long way.  The lowest and rudest of the
    three; this is the bird that is genuinely annoyed.
    """
    r = rng(9503)
    n = ns(0.56)
    y = np.zeros(n)

    # syllable one: a short throat grunt, almost no nasality yet
    g = ns(0.085)
    eg = env_asr(g, 0.005, 0.035, 1.1)
    v0 = goose_voice(
        0.085, [(0, 150), (0.4, 162), (1, 146)],
        r, air=0.40, rough=0.55, neck=0.88, open_amt=0.45, nasal_hz=880.0,
        nasal_q=1.4, env=eg)
    add(y, ns(0.004), v0, 0.80)

    # syllable two: the honk
    dur = 0.42
    m = ns(dur)
    e = env_asr(m, 0.005, 0.17, 1.0)
    e = e * C.curve([(0, 0.58), (0.10, 1.0), (0.50, 0.84), (1, 0.48)], m)
    v = goose_voice(
        dur,
        [(0, 142), (0.07, 176), (0.35, 165), (0.70, 156), (1, 128)],
        r, air=0.32, rough=0.40, bright=0.94, neck=0.88, open_amt=1.15,
        nasal_hz=940.0, env=e)
    add(y, ns(0.120), v, 1.0)

    add(y, 0, air_puff(0.05, r, 260.0, 1900.0, 0.12), 0.12)
    return dc_block(sfilt(y, "highpass", 110.0, 2))


# --------------------------------------------------------------------------
# wing beats - ONE downstroke, up to ~7/s/bird, up to nine birds
# --------------------------------------------------------------------------


def wing_beat(dur, r, *, whump_hi=250.0, whump_lo=105.0, snap_at=0.030,
              snap_lo=1900.0, snap_hi=6200.0, snap=0.55, flap=0.55,
              whump=0.85, tau=0.040, seed_shift=0.0):
    """
    One downstroke.  Three parts, no tone anywhere:

      * the WHUMP - a slug of air pushed down and then left behind.  Its band
        sweeps DOWNWARD (whump_hi -> whump_lo) because the air column
        decelerates after the stroke; a static band reads as a drum.  It is the
        SHORTEST part, deliberately: it is the only part with energy below
        300 Hz, and low energy with a tail is exactly what nine birds cannot
        afford.
      * the FLAP - mid-band canvas, amplitude-scattered so it is cloth and not
        a filter sweep.  This is what carries the length of the stroke, because
        1 kHz material can be layered nine deep and still be counted.
      * the SNAP - 6 ms of leather at the bottom of the stroke, where the
        primaries load up.  It is what makes the sound READ as a wing rather
        than as a soft thud.

    Deliberately dry: no reverb, no resonators above Q 1.6, and the whole thing
    decays inside its own duration so consecutive strokes cannot overlap.
    """
    n = ns(dur)
    y = np.zeros(n)

    # --- whump.  env_ad's decay is scaled by `curve`, so the time constant is
    # tau/curve.  The first version ran the whump out to 0.45 x the clip at
    # curve 1.8 to make the file length honest; measurement said that put 52 %
    # of the energy in 160-320 Hz and dropped the centroid to 679 Hz, i.e. it
    # bought the duration by making exactly the mud the brief warns about.  The
    # duration now comes from the flap instead and the whump is a 13 ms thump.
    w = white(n, r)
    fc = np.linspace(whump_hi, whump_lo, n)
    w = bandpass_tv(w, fc, 1.15)
    w = w * env_ad(n, 0.0040, tau * 0.55, 2.6)
    y += whump * w / (np.max(np.abs(w)) + 1e-12)

    # --- flap: mid canvas, gated by its own coarse noise so it is not a shhh
    fl = white(n, r)
    fl = bandpass_tv(fl, np.linspace(1400.0, 620.0, n), 0.9)
    grain = 0.45 + 0.55 * np.abs(C.curve(
        [(0, 1.0), (0.25, 0.55), (0.5, 0.85), (0.8, 0.35), (1, 0.15)], n))
    fl = fl * grain * env_ad(n, 0.0035, tau, 1.7)
    y += flap * fl / (np.max(np.abs(fl)) + 1e-12)

    # --- snap
    sn_n = min(ns(0.007), n)
    sp = strike(sn_n, r, snap_lo, snap_hi, 0.0016)
    sp = sp * env_ad(sn_n, 0.0004, 0.0035, 3.0)
    add(y, ns(snap_at), sp / (np.max(np.abs(sp)) + 1e-12), snap)

    # Nothing below 105 Hz: nine birds beating together must not sum in the sub,
    # and there is nothing musical down there to lose.
    y = sfilt(y, "highpass", 105.0, 2)
    y = sfilt(y, "lowpass", 9000.0, 2)
    return dc_block(y)


def make_wing_1():
    """Mid stroke, 0.14 s.  The default."""
    return wing_beat(0.14, rng(9511), whump_hi=250.0, whump_lo=105.0,
                     snap_at=0.028, snap=0.55, flap=0.80, whump=0.80,
                     tau=0.063)


def make_wing_2():
    """Shorter and higher, 0.105 s - a wing at the top of its rate."""
    return wing_beat(0.105, rng(9512), whump_hi=305.0, whump_lo=135.0,
                     snap_at=0.019, snap_lo=2300.0, snap_hi=7000.0,
                     snap=0.62, flap=0.72, whump=0.72, tau=0.047)


def make_wing_3():
    """Longer and heavier, 0.18 s - a big slow stroke on take-off."""
    return wing_beat(0.18, rng(9513), whump_hi=205.0, whump_lo=92.0,
                     snap_at=0.042, snap_lo=1500.0, snap_hi=5200.0,
                     snap=0.44, flap=0.86, whump=0.92, tau=0.081)


# --------------------------------------------------------------------------
# hiss - the threat display
# --------------------------------------------------------------------------


def make_hiss():
    """
    0.72 s of shaped air with a rising sibilance and no pitch at all.

    A goose hiss is a throat, not a voice: it starts low and wet around 700 Hz
    and climbs into a 3-4 kHz sibilant as the bird pushes harder.  There is a
    small tremble on it (a real one at ~9 Hz, from the bird, not an LFO tell)
    and a hard air-burst on the front so it has a transient and does not join
    the shhh failure mode the analysis crest column exists to catch.
    """
    r = rng(9521)
    dur = 0.72
    n = ns(dur)

    # the body: a band centre climbing 780 -> 3400 Hz, widening as it goes
    x = white(n, r)
    fc = C.curve([(0, 780), (0.25, 1350), (0.62, 2600), (1, 3400)], n)
    body = bandpass_tv(x, fc, 1.25)
    # a second, wider layer above it - sibilance is broad, not a whistle
    hi = sfilt(white(n, rng(9522)), "bandpass", (2200.0, 8200.0), 2)
    hi = hi * C.curve([(0, 0.10), (0.45, 0.45), (1, 1.0)], n)
    # and the wet low throat, which fades as the sibilance takes over
    lo = sfilt(white(n, rng(9523)), "bandpass", (280.0, 900.0), 2)
    lo = lo * C.curve([(0, 1.0), (0.35, 0.62), (1, 0.18)], n)

    y = (body / (np.max(np.abs(body)) + 1e-12)
         + 0.62 * hi / (np.max(np.abs(hi)) + 1e-12)
         + 0.40 * lo / (np.max(np.abs(lo)) + 1e-12))

    # pressure envelope: quick onset, long push, tails off as the air runs out
    e = C.curve([(0, 0.0), (0.035, 1.0), (0.30, 0.82), (0.72, 0.95),
                 (1, 0.0)], n)
    tremble = 1.0 + 0.13 * np.sin(2 * np.pi * 9.0 * tt(n) - 0.7) \
        * C.curve([(0, 0.2), (1, 1.0)], n)
    y = y * e * tremble

    # the front: the first burst of air escaping, which is a transient
    burst = air_puff(0.045, r, 500.0, 5200.0, 0.16)
    add(y, 0, burst, 0.85)

    y = sfilt(y, "highpass", 260.0, 2)
    return dc_block(y)


# --------------------------------------------------------------------------
# squawk - hit by a lawnmower.  Comic, not distressing.
# --------------------------------------------------------------------------


def make_squawk():
    """
    0.58 s.  Sharp, cracked, falling - the undignified noise.

    The joke is carried by a REGISTER BREAK: the call starts up at 395 Hz
    (higher than any honk here, and higher than the duck), the voice cracks 90
    ms in - the pitch jumps and the pulse width collapses for two cycles, which
    is what a real voice break is - and then the whole thing slides down to
    118 Hz and wheezes out.  Falling pitch plus a break plus a deflating
    exhale reads as slapstick; the same fall with a smooth contour and a long
    reverb tail would read as an animal being hurt, which is not this game.
    """
    r = rng(9531)
    n = ns(0.58)
    y = np.zeros(n)

    # --- the shriek, and the crack in it
    d1 = 0.13
    m1 = ns(d1)
    e1 = env_asr(m1, 0.0018, 0.030, 1.3)
    v1 = goose_voice(
        d1, [(0, 360), (0.05, 405), (0.6, 380), (1, 330)],
        r, air=0.42, rough=0.55, bright=1.12, open_amt=0.5,
        nasal_hz=1150.0, env=e1, attack=0.0018, drive=2.2)
    add(y, 0, v1, 1.0)

    # the break itself: a short, badly-behaved burst an octave adrift
    d2 = 0.055
    m2 = ns(d2)
    e2 = env_ad(m2, 0.0012, 0.022, 3.0)
    v2 = goose_voice(
        d2, [(0, 232), (0.5, 268), (1, 214)],
        r, air=0.55, rough=0.80, bright=0.9, open_amt=0.3,
        nasal_hz=860.0, nasal_q=1.5, env=e2, drive=2.4, sub=0.34)
    add(y, ns(0.108), v2, 0.86)

    # --- the fall
    d3 = 0.28
    m3 = ns(d3)
    e3 = env_asr(m3, 0.006, 0.12, 1.0)
    e3 = e3 * C.curve([(0, 1.0), (0.45, 0.72), (1, 0.30)], m3)
    v3 = goose_voice(
        d3, [(0, 300), (0.18, 236), (0.55, 172), (1, 118)],
        r, air=0.46, rough=0.62, bright=0.88, open_amt=0.85,
        nasal_hz=900.0, env=e3, drive=2.0, sub=0.30)
    add(y, ns(0.158), v3, 0.92)

    # --- the wheeze: the last of the air, with the bird no longer driving it
    wh = air_puff(0.17, r, 900.0, 2400.0, 0.55, gain=1.0)
    wh = wh * C.curve([(0, 0.9), (0.5, 0.55), (1, 0.0)], ns(0.17))
    add(y, ns(0.400), wh, 0.20)

    # one flustered feather rattle on the way out
    for i, t in enumerate((0.455, 0.492, 0.524)):
        add(y, ns(t), wing_beat(0.045, rng(9532 + i), whump_hi=420.0,
                                whump_lo=210.0, snap_at=0.006,
                                snap_lo=2400.0, snap_hi=7000.0,
                                snap=0.5, flap=0.4, tau=0.014),
            0.17 - 0.04 * i)

    return dc_block(sfilt(y, "highpass", 150.0, 2))


# --------------------------------------------------------------------------


def render():
    write(out("Geese", "goose_honk_1.wav"), make_honk_1(), fade_out=0.012)
    write(out("Geese", "goose_honk_2.wav"), make_honk_2(), fade_out=0.010)
    write(out("Geese", "goose_honk_3.wav"), make_honk_3(), fade_out=0.014)
    write(out("Geese", "goose_wingbeat_1.wav"), make_wing_1(), fade_out=0.006)
    write(out("Geese", "goose_wingbeat_2.wav"), make_wing_2(), fade_out=0.005)
    write(out("Geese", "goose_wingbeat_3.wav"), make_wing_3(), fade_out=0.008)
    write(out("Geese", "goose_hiss.wav"), make_hiss(), fade_out=0.030)
    write(out("Geese", "goose_squawk.wav"), make_squawk(), fade_out=0.016)


if __name__ == "__main__":
    render()
    print("geese rendered")
