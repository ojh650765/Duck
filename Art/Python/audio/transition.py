"""
DUCK MOW - Transition/

The eight cues that cover a cut.  A transition in this game is a foliage wipe,
a tournament banner sliding across the lens, or a gate opening onto the next
lawn - never a film-trailer whoosh - so the folder is built to three rules:

*   **The whooshes are objects, not sweeps.**  Every one of them is a moving
    band with a *doppler* multiplier on top of it (the band opens as the thing
    approaches and the whole spectrum falls as it goes past), plus the debris
    that object would actually make - leaf crackle for the hedge, canvas slaps
    and a rope creak for the banner.  A band sweep on its own reads as a synth
    filter, which is the one thing a wipe must not sound like.
*   **`transition_riser` peaks on its own last sample.**  It is scheduled
    backwards from the cut (`PlayScheduled(cutTime - 1.6)`), so it has no tail
    at all and no decay after the peak; `transition_impact` is what lands on
    the frame the riser was pointing at.
*   **Two sizes of sting.**  `transition_stamp_small` is the band playing three
    notes on the way past; `transition_fanfare_big` is the same gesture with
    the cornet section and the crowd behind it, for the last stage entrance and
    the run into judging.  They are deliberately the same shape so escalating
    from one to the other reads as *more*, not as *different*.

Everything here is stereo except `transition_gate_creak`, which is diegetic and
gets placed at the gate's transform.  Nothing in this folder loops.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
from instruments import Seq, voicing, m2f
import critter as C
import crowd as CR


# --------------------------------------------------------------------------
# shared helpers
# --------------------------------------------------------------------------


def sweep_pan(mono, p0, p1, curve=1.0):
    """
    Constant-power pan that *moves* across the clip.

    `pan()` places a sound; this one flies it past the listener.  No delay is
    involved - a Haas-style moving delay would smear the leaf crackle and put a
    copy of the loudest part of a one-shot at its own head.
    """
    n = mono.shape[-1]
    u = np.linspace(0.0, 1.0, n) ** curve
    p = np.clip(p0 + (p1 - p0) * u, -1.0, 1.0)
    a = (p + 1.0) * 0.25 * np.pi
    return np.stack([mono * np.cos(a), mono * np.sin(a)])


def doppler(n, centre=0.45, width=0.09, drop=0.30):
    """
    The pitch multiplier of something passing the listener: flat on approach,
    an S-curve through the pass-by, flat and `drop` lower afterwards.  It is
    what separates "an object went past" from "a filter opened".
    """
    u = np.linspace(0.0, 1.0, n)
    s = 1.0 / (1.0 + np.exp(-(u - centre) / max(width, 1e-4)))
    return 1.0 + drop * 0.5 - drop * s


def proximity(n, centre=0.45, width=0.24):
    """How close the thing is, 0..1.  Drives band width and debris density."""
    u = np.linspace(0.0, 1.0, n)
    return np.exp(-((u - centre) / max(width, 1e-4)) ** 2)


def brass(f, dur, vel, r, bite=1.0):
    """
    A cornet.  The band does not own one until the final stage entrance, which
    is exactly why the big fanfare gets one and nothing else in the game does.

    Two things make brass read as brass rather than as a saw with a filter on
    it: the lip **scoop** (the pitch arrives about 2 % flat and pulls up over
    ~25 ms) and the fact that a brass instrument gets *brighter as it gets
    louder* - so the waveshaper drive is driven by the note's own envelope
    rather than being a fixed amount of distortion.
    """
    n = ns(dur + 0.14)
    t = tt(n)
    scoop = 1.0 - 0.021 * np.exp(-t / 0.024)
    vib = 1.0 + 0.0045 * np.clip((t - 0.20) / 0.22, 0, 1) * np.sin(2 * np.pi * 5.1 * t)
    src = bl_saw(f * scoop * vib, n) + 0.28 * bl_square(f * scoop * vib, n)
    src /= np.max(np.abs(src)) + 1e-12

    env = env_asr(n, 0.024 + 0.020 * (1.0 - vel), 0.075, curve=1.15)
    env *= 1.0 + 0.035 * np.sin(2 * np.pi * 4.4 * t + 0.5)
    # brighter when louder: the drive tracks the envelope
    src = satur(src * (1.0 + 2.4 * vel * bite * env), 1.9, 0.85)

    # the bell: a broad lift 800-3000 Hz with a hard roll-off above it
    y = (sfilt(src, "lowpass", 1700.0 + 3400.0 * vel, 2)
         + reson(src, 860.0, 2.2) * 0.34
         + reson(src, 1240.0, 2.6) * 0.26
         + reson(src, 2150.0, 2.4) * 0.20
         + reson(src, 3050.0, 2.0) * 0.11)

    # the air going through it
    breath = sfilt(white(n, r), "bandpass", (1400.0, 7000.0), 2)
    y = y + 0.055 * breath / (np.max(np.abs(breath)) + 1e-12)

    y = sfilt(y, "highpass", 180.0, 2)
    y *= env
    return y * vel / (np.max(np.abs(y)) + 1e-12)


def brass_note(s, beat, dur_beats, midi, vel=0.8, p=0.0, gain=1.0, **kw):
    """Place one cornet note on a `Seq` grid (brass is not in INSTRUMENTS)."""
    s.raw(beat, brass(m2f(midi), dur_beats * s.spb, vel, s.r, **kw), gain, p)


def brass_chord(s, beat, dur_beats, midis, vel=0.8, spread=0.35, gain=1.0,
                strum=0.0, **kw):
    for i, m in enumerate(midis):
        pp = spread * (i / max(len(midis) - 1, 1) * 2 - 1)
        brass_note(s, beat + strum * i, dur_beats - strum * i, m, vel, pp,
                   gain, **kw)


# --------------------------------------------------------------------------
# 1. LEAF SWEEP - 1.1 s.  A hedge wipes across the frame.
# --------------------------------------------------------------------------


def make_leaf_sweep():
    """
    Dry, close, and *left to right*.  The band centre opens from 520 Hz to
    about 2.6 kHz as the foliage arrives and the doppler multiplier pulls the
    whole thing down a fourth on the way out; the crackle transients are
    weighted by proximity, so the leaves are loudest and densest at the pass-by
    instead of being scattered evenly, which is what a random sprinkle sounds
    like.  No reverb at all - this is happening 30 cm from the lens.
    """
    dur = 1.10
    n = ns(dur)
    r = rng(11001)

    prox = proximity(n, 0.45, 0.26)
    dop = doppler(n, 0.45, 0.085, 0.34)
    fc = (430.0 + 1500.0 * prox) * dop

    body = bandpass_tv(white(n, r), fc, 1.15)
    rustle = bandpass_tv(white(n, r), fc * 2.10, 1.20) * 0.30   # the dry "sss"
    stalk = bandpass_tv(white(n, r), fc * 0.40, 1.7) * 0.55     # stems bending
    y = body + rustle + stalk
    y *= env_swell(n, 0.45, 1.7, 1.55)

    # leaf crackle: individual blades snapping past, densest at the pass-by
    for _ in range(17):
        t = float(np.clip(r.normal(0.45, 0.16), 0.02, 0.95))
        w = float(np.interp(t, [0, 0.45, 1.0], [0.30, 1.0, 0.35]))
        gn = ns(0.006 + 0.010 * r.random())
        g = white(gn, r) * expdec(gn, 0.0016 + 0.0022 * r.random())
        g = sfilt(g, "bandpass", (1200.0 + 1300.0 * r.random(),
                                  3800.0 + 2600.0 * r.random()), 2)
        add(y, ns(t * dur), g / (np.max(np.abs(g)) + 1e-12),
            (0.10 + 0.16 * r.random()) * w)

    y = sfilt(y, "highpass", 180.0, 2)
    y = sfilt(y, "lowpass", 9000.0, 2)
    st = sweep_pan(y, -0.88, 0.88, curve=0.85)
    return dc_block(st * env_asr(n, 0.006, 0.10))


# --------------------------------------------------------------------------
# 2. BANNER WHOOSH - 0.9 s.  The tournament banner slides across the lens.
# --------------------------------------------------------------------------


def make_banner_whoosh():
    """
    The same gesture as the leaf sweep an octave and a half down, and going the
    other way, so a scene that uses both does not read as the same wipe twice.
    Canvas rather than foliage: a brown-noise weight under the band, two cloth
    slaps as the fabric takes the air, and one short rope creak.
    """
    dur = 0.90
    n = ns(dur)
    r = rng(11101)

    prox = proximity(n, 0.44, 0.28)
    dop = doppler(n, 0.44, 0.095, 0.30)
    fc = (135.0 + 520.0 * prox) * dop

    body = bandpass_tv(white(n, r), fc, 0.85)
    weight = bandpass_tv(brown(n, r), fc * 0.50, 1.1) * 1.05   # the sheet's mass
    top = bandpass_tv(white(n, r), fc * 3.2, 0.9) * 0.16       # weave, kept low
    y = body + weight + top
    y *= env_swell(n, 0.44, 1.5, 1.45)

    # cloth slaps: the canvas snapping taut, low and round
    for t, g in ((0.235, 0.85), (0.400, 1.0), (0.585, 0.55)):
        gn = ns(0.055)
        s = white(gn, r) * env_ad(gn, 0.0018, 0.012, curve=2.6)
        s = sfilt(s, "bandpass", (115.0, 900.0), 2)
        s += sfilt(white(gn, r) * env_ad(gn, 0.0012, 0.005, 3.0),
                   "bandpass", (1200.0, 4200.0), 2) * 0.30
        add(y, ns(t), s / (np.max(np.abs(s)) + 1e-12), 0.34 * g)

    # one rope creak over the pulley
    cn = ns(0.16)
    exc = np.zeros(cn)
    tt_ = 0.0
    while tt_ < 0.11:
        k = ns(tt_)
        if 0 <= k < cn - 4:
            exc[k] += 0.4 + 0.6 * r.random()
        tt_ += 1.0 / (48.0 + 90.0 * (tt_ / 0.11)) * (0.7 + 0.6 * r.random())
    creak = reson(exc, 410.0, 11.0) + reson(exc, 780.0, 13.0) * 0.5
    creak *= env_swell(cn, 0.4, 1.4, 1.6)
    add(y, ns(0.150), creak / (np.max(np.abs(creak)) + 1e-12), 0.14)

    y = sfilt(y, "highpass", 78.0, 2)
    y = sfilt(y, "lowpass", 6000.0, 2)
    y = satur(y * 0.92, 1.25, 0.45)
    st = sweep_pan(y, 0.90, -0.90, curve=0.9)
    return dc_block(st * env_asr(n, 0.005, 0.09))


# --------------------------------------------------------------------------
# 3. RISER - 1.6 s.  Peaks on its own last sample.
# --------------------------------------------------------------------------


def make_riser():
    """
    Scheduled so it *lands* on the cut, which is why the level curve is
    strictly monotonic and there is no decay whatsoever after the peak - the
    file simply stops at full amplitude (a 4 ms out is all that stands between
    that and a DAC click).

    Three layers, all pointing the same way: a noise band climbing 260 Hz ->
    7 kHz, a reed tone rising a minor tenth with the interval opening as it
    goes, and a flutter whose rate accelerates from 5 Hz to 22 Hz - the
    accelerating rate is what makes 1.6 s feel like it is running out of time.
    The two noise channels are independently seeded rather than delayed, so
    nothing from the end of the clip appears at its start.
    """
    dur = 1.60
    n = ns(dur)
    r = rng(11201)
    u = np.linspace(0.0, 1.0, n)

    fc = 260.0 * (7000.0 / 260.0) ** (u ** 1.35)

    def hiss(seed):
        h = bandpass_tv(white(n, rng(seed)), fc, 1.3)
        h += 0.45 * bandpass_tv(white(n, rng(seed + 1)), fc * 0.42, 2.1)
        return h / (np.max(np.abs(h)) + 1e-12)

    hl, hr = hiss(11202), hiss(11204)

    # the tone: 98 Hz -> ~590 Hz, accelerating, opening up as it climbs
    f0 = 98.0 * 2.0 ** (2.6 * u ** 1.7)
    tone = bl_saw(f0, n) * 0.70 + bl_square(f0 * 1.5, n) * 0.22
    tone = bandpass_tv(tone, f0 * 2.4, 1.0) + sfilt(tone, "lowpass", 2600.0, 2) * 0.6
    tone /= np.max(np.abs(tone)) + 1e-12

    # something heavy underneath, arriving late
    rumble = sfilt(brown(n, r), "lowpass", 220.0, 2)
    rumble /= np.max(np.abs(rumble)) + 1e-12

    ph = 2.0 * np.pi * np.cumsum(5.0 + 17.0 * u ** 1.6) / SR
    trem = 1.0 - 0.30 * (1.0 - u) ** 0.6 * (0.5 + 0.5 * np.sin(ph))

    env = u ** 1.5
    y = np.stack([hl, hr]) * 0.60 + np.stack([tone, tone]) * 0.55
    y += np.stack([rumble, rumble]) * 0.42 * (u ** 2.2)
    y *= env * trem

    y = sfilt(y, "highpass", 62.0, 2)
    y = sfilt(y, "lowpass", 13000.0, 2)
    return dc_block(y)


# --------------------------------------------------------------------------
# 4. IMPACT - 1.4 s.  The downbeat the riser was pointing at.
# --------------------------------------------------------------------------


def make_impact():
    """
    A marquee pole hit with a mallet, not a braam.  The body is a wooden plank
    (78 Hz, inharmonic, bending down 6 % over its first 8 ms), the bloom is a
    tin tray somebody hit at the same moment, and the tail is a small canvas
    room rather than a hall.  The sub layer drops 96 -> 52 Hz in 120 ms and is
    kept at a third of the plank's level: enough for the cut to land in the
    chest, not enough for it to become the sound.
    """
    dur = 1.40
    n = ns(dur)
    r = rng(11301)

    bend = 1.0 + 0.062 * np.exp(-tt(n) / 0.008)
    plank = modal(n, [78.0, 121.0, 194.0, 302.0, 447.0],
                  [0.52, 0.34, 0.21, 0.115, 0.062],
                  [1.0, 0.66, 0.40, 0.24, 0.13],
                  attack=0.0006, bend=bend, r=r)

    # the bright bloom: a struck tin tray, inharmonic, ringing on under the
    # plank rather than stopping with it - a hit that ends in the same 40 ms it
    # started in is a click with a low end on it
    tray = modal(n, [1240.0, 1873.0, 2540.0, 3410.0, 4620.0],
                 [0.55, 0.40, 0.28, 0.19, 0.12],
                 [1.0, 0.62, 0.44, 0.28, 0.16], attack=0.0008, r=r)
    crack = strike(n, r, 900.0, 11000.0, 0.0035)

    # sub: a short drop, deliberately modest
    sub = np.sin(2 * np.pi * phasor(
        96.0 * (1.0 - 0.46 * np.clip(tt(n) / 0.12, 0, 1) ** 0.8), n))
    sub *= expdec(n, 0.20) * (1.0 - np.exp(-tt(n) / 0.0018))

    hit = plank * 1.0 + tray * 0.26 + crack * 0.42 + sub * 0.28
    hit *= env_ad(n, 0.0006, 0.62, curve=1.7)
    hit = satur(hit * 0.95, 1.35, 0.45)

    # a canvas awning, not a cathedral - but a long enough one that the clip is
    # still doing something at 1.2 s.  Two decorrelated rooms, not one panned.
    wet = np.stack([
        oreverb(hit, ir_room(1.15, rng(11302), decay=0.42, bright=2500.0,
                             predelay=0.011), tail=False),
        oreverb(hit, ir_room(1.15, rng(11303), decay=0.42, bright=2300.0,
                             predelay=0.015), tail=False),
    ])
    y = np.stack([hit, hit]) * 0.90 + wet * 0.45

    y = sfilt(y, "highpass", 46.0, 2)
    y = sfilt(y, "lowpass", 11000.0, 2)
    return dc_block(y * env_asr(n, 0.0006, 0.30))


# --------------------------------------------------------------------------
# 5. STAMP SMALL - 0.7 s.  The early-transition sting.
# --------------------------------------------------------------------------


def make_stamp_small():
    """
    Three plucked notes up a G triad (D - F# - A) with a woodblock on the first
    and a shaker on the last.  Same shape as the big fanfare - pickup, rise,
    arrival - played by two people instead of a band, so escalating to
    `transition_fanfare_big` reads as the same gesture getting bigger.
    """
    s = Seq(150, 1.75, loop=False, seed=111)
    r = s.r

    s.note("bass", 0.0, 0.9, 50, 0.70, 0.0, 0.85)          # D3 pizz underneath
    for b, m, v, p in ((0.000, 74, 0.72, -0.22),
                       (0.375, 78, 0.76, 0.06),
                       (0.750, 81, 0.88, 0.26)):
        s.note("banjo", b, 0.6, m, v, p, 0.95)
    s.note("fiddle", 0.75, 0.85, 86, 0.52, 0.30, 0.45)     # a thin lift on top

    s.hit("woodblock", 0.0, 0.34, -0.45, 0.42, f=1260.0)
    s.hit("shaker", 0.75, 0.30, 0.45, 0.40)
    s.hit("tamb", 0.75, 0.26, 0.0, 0.30)

    y = sfilt(s.buf, "highpass", 190.0, 2)
    ir = ir_room(0.40, rng(11401), decay=0.13, bright=3800.0, predelay=0.009)
    y = y + oreverb(y, ir, tail=False) * 0.22
    y = satur(y * 0.9, 1.15, 0.40)
    return dc_block(y * env_asr(y.shape[-1], 0.0015, 0.06))


# --------------------------------------------------------------------------
# 6. FANFARE BIG - 2.2 s.  Final stage entrance / the run into judging.
# --------------------------------------------------------------------------


def make_fanfare_big():
    """
    The escalated sibling of `transition_stamp_small`: same pickup-rise-arrival
    shape, same G, but the village finally got a cornet section and the stands
    come in underneath it.  IV -> V -> I over four beats at 132 BPM, the brass
    strumming a touch so the section is not perfectly together (it never is),
    and a handful of `crowd.shout` voices lifting into the arrival so the cue
    hands over to the crowd rather than stopping.
    """
    s = Seq(132, 4.84, loop=False, seed=122)
    r = s.r

    # pickup, on the way in
    brass_note(s, -0.50, 0.45, 62, 0.52, -0.20, 0.70)
    brass_note(s, -0.25, 0.40, 67, 0.62, 0.20, 0.78)
    s.note("banjo", -0.50, 0.4, 62, 0.50, -0.35, 0.60)
    s.note("banjo", -0.25, 0.4, 67, 0.55, -0.35, 0.62)

    # the call, then the two stabs climbing to the arrival
    brass_chord(s, 0.0, 0.85, [71, 74, 79], vel=0.78, spread=0.40, gain=0.92,
                strum=0.014)
    for b, ch, ms in ((1.0, "C", [72, 76, 79]), (1.5, "D", [74, 78, 81])):
        brass_chord(s, b, 0.48, ms, vel=0.74, spread=0.38, gain=0.86,
                    strum=0.012)
        s.chord("melodeon", b, 0.48, ms, vel=0.52, spread=0.35, gain=0.55,
                strum=0.010)
        s.chord("banjo", b, 0.48, voicing(ch, 67, 84), vel=0.58, spread=0.32,
                gain=0.62, strum=0.014)
        s.note("bass", b, 0.46, {"C": 60, "D": 62}[ch], 0.82, 0.0, 0.95)
        s.hit("stomp", b, 0.58, 0.0, 0.50)
        s.hit("tamb", b, 0.42, 0.0, 0.40)

    s.note("bass", 0.0, 0.82, 55, 0.85, 0.0, 1.0)
    s.hit("stomp", 0.0, 0.62, 0.0, 0.52)
    s.hit("tamb", 0.0, 0.48, 0.0, 0.44)

    # the arrival
    brass_chord(s, 2.0, 2.75, [67, 71, 74, 79], vel=0.92, spread=0.45,
                gain=1.0, strum=0.016)
    s.chord("melodeon", 2.0, 2.75, [79, 83, 86], vel=0.55, spread=0.40,
            gain=0.55, strum=0.012)
    s.note("bass", 2.0, 1.6, 55, 0.92, 0.0, 1.0)
    s.note("fiddle", 2.0, 2.4, 86, 0.72, 0.24, 0.62)
    for i, (b, m) in enumerate(((2.0, 91), (2.22, 95), (2.55, 98))):
        s.note("glock", b, 1.0, m, 0.72 - 0.14 * i, 0.22 * (i - 1), 0.52)
    for i in range(6):
        s.hit("shaker", 2.0 + i * 0.25, 0.28 - 0.03 * i, 0.5, 0.36)
    s.hit("stomp", 2.0, 0.80, 0.0, 0.62)
    s.hit("tamb", 2.0, 0.66, 0.0, 0.58)

    # the stands, coming in under the arrival
    for i in range(7):
        d = 0.55 + 0.55 * r.random()
        kind = ("whoo", "yeah", "aah")[int(r.integers(0, 3))]
        v = CR.shout(d, r, f0=155.0 + 190.0 * r.random(), kind=kind)
        add(s.buf, ns(0.86 + abs(r.normal(0.05, 0.07))),
            pan(v, (r.random() * 2 - 1) * 0.9), 0.13 + 0.10 * r.random())
    for _ in range(14):
        t = 0.95 + 1.05 * r.random() ** 0.7
        add(s.buf, ns(t), pan(CR.clap(r, big=True),
                              (r.random() * 2 - 1) * 0.95),
            0.10 + 0.10 * r.random())

    y = sfilt(s.buf, "highpass", 130.0, 2)
    y = sfilt(y, "lowpass", 13000.0, 2)
    ir = ir_room(1.0, rng(11501), decay=0.30, bright=4400.0, predelay=0.014)
    y = y + oreverb(y, ir, tail=False) * 0.26
    y = satur(y * 0.88, 1.45, 0.45)
    return dc_block(y * env_asr(y.shape[-1], 0.002, 0.16))


# --------------------------------------------------------------------------
# 7. CROWD SWELL - 2.0 s.  Murmur -> cheer -> settle.
# --------------------------------------------------------------------------


def make_crowd_swell():
    """
    Built entirely out of `crowd.py`'s voices - `murmur_grain` for the
    anonymous bed, `shout` for the individuals, `clap` for the hands - because
    a second crowd model would be a second crowd, and the stands have to be the
    same animals either side of a cut.

    The shape is the point: the murmur is already running at sample 0 (a crowd
    does not start), the shouts arrive raggedly across 0.45-1.0 s, the claps
    peak at 1.1 s, and by 2.0 s it is back to a murmur so the clip can be
    crossfaded straight into `crowd_ambient_loop`.
    """
    dur = 2.00
    n = ns(dur)
    r = rng(11601)
    u = np.linspace(0.0, 1.0, n)

    # the anonymous bed, vowel-coloured, present from the first sample
    # Two shallow, overlapping vowel bells rather than one deep one at 540 Hz:
    # the second has to sit low enough (1.1 kHz, not 1.5) that its skirt fills
    # the 640-1280 Hz octave, which is where a third of the energy of every
    # other clip in Crowd/ lives.  A bed with a hole there reads as a crowd
    # heard down a telephone.
    bed = decorrelate(n, 11602, lambda f: (
        H_peak(f, 540.0, 0.7, 8.0)
        * H_peak(f, 1150.0, 0.8, 9.0)
        * H_peak(f, 2600.0, 1.0, 4.0)
        * H_hp(f, 200.0, 2)
        * H_lp(f, 5200.0, 2)
    ))
    bed /= np.max(np.abs(bed)) + 1e-12
    bed_env = np.interp(u, [0.0, 0.22, 0.50, 0.72, 1.0],
                        [0.34, 0.55, 1.00, 0.80, 0.40])
    y = bed * bed_env * 0.45

    # indistinct voices getting louder and more animated through the peak
    for _ in range(20):
        d = 0.20 + 0.45 * r.random()
        t = float(np.clip(r.normal(0.62, 0.30), 0.0, dur - d))
        w = float(np.interp(t / dur, [0, 0.55, 1.0], [0.45, 1.0, 0.35]))
        g = CR.murmur_grain(d, r, bright=0.85 + 0.55 * r.random())
        add(y, ns(t), pan(g, (r.random() * 2 - 1) * 0.85),
            (0.13 + 0.16 * r.random()) * w)

    # the cheer itself: individual voices arriving raggedly
    for _ in range(11):
        d = 0.50 + 0.60 * r.random()
        kind = ("whoo", "yeah", "aah")[int(r.integers(0, 3))]
        v = CR.shout(d, r, f0=150.0 + 210.0 * r.random(), kind=kind)
        add(y, ns(0.42 + abs(r.normal(0.10, 0.16))),
            pan(v, (r.random() * 2 - 1) * 0.9), 0.32 + 0.34 * r.random())

    # Hands, peaking a little after the voices - and carried at roughly the
    # level `_cheer` gives them, because the claps are the only thing in a
    # crowd with any energy above 2.5 kHz.  Quiet claps are what makes a
    # synthesised crowd sound like it is coming down a telephone line.
    for _ in range(34):
        t = float(np.clip(r.normal(1.10, 0.34), 0.30, dur - 0.09))
        w = float(np.interp(t, [0.3, 1.1, dur], [0.30, 1.0, 0.30]))
        add(y, ns(t), pan(CR.clap(r), (r.random() * 2 - 1) * 0.95),
            (0.26 + 0.42 * r.random()) * w)

    # a couple of animals that could not help themselves
    add(y, ns(0.66), pan(sfilt(C.goose(0.28, rng(11603), f0=245), "lowpass",
                               3600.0, 2), -0.55), 0.26)
    add(y, ns(1.02), pan(sfilt(C.sheep(0.42, rng(11604), f0=195), "lowpass",
                               3200.0, 2), 0.58), 0.20)

    # A bright, restrained room.  A darker or wetter one piles another 12
    # percentage points of the total energy into 320-640 Hz on its own, and a
    # crowd whose energy is three quarters in one octave sounds like a crowd
    # behind a door - the rest of Crowd/ splits that octave with 640-1280 Hz
    # roughly 50/30, and this clip has to cut to and from those.
    ir = ir_room(0.75, rng(11605), decay=0.22, bright=3800.0, predelay=0.011)
    y = oreverb(y, ir, tail=False) * 0.26 + y * 0.88
    y = cfilt(y, lambda f: (H_peak(f, 470.0, 0.9, -3.5)
                            * H_peak(f, 1450.0, 0.75, 4.5)
                            * H_hp(f, 175.0, 2)
                            * H_lp(f, 7500.0, 2)))
    return dc_block(y * env_asr(n, 0.008, 0.14))


# --------------------------------------------------------------------------
# 8. GATE CREAK - 1.0 s.  Mono, positional: it plays at the gate.
# --------------------------------------------------------------------------


def make_gate_creak():
    """
    A creak is stick-slip, so it is built the same way `windmill_creak` is: a
    train of micro-impulses whose rate rises as the hinge loads up, run through
    the beam's resonances.  This one is a five-bar field gate rather than a
    windmill sail, so the resonances are lower and wider, the whole thing is
    over in 0.7 s, and the latch drops at 0.78 s - which is the frame the gate
    is fully open on, and the reason the clip is a single asset and not two.
    """
    dur = 1.00
    n = ns(dur)
    r = rng(11701)
    exc = np.zeros(n)

    def stickslip(t0, length, rate0, rate1, level):
        t = t0
        while t < t0 + length:
            v = (t - t0) / length
            rate = rate0 * (rate1 / rate0) ** v
            amp = level * (0.4 + 0.6 * r.random()) * np.sin(np.pi * np.clip(v, 0, 1)) ** 0.55
            k = ns(t)
            if 0 <= k < n - 4:
                exc[k] += amp
            t += (1.0 / rate) * (0.72 + 0.56 * r.random())

    stickslip(0.030, 0.40, 22.0, 64.0, 1.00)     # the swing taking the weight
    stickslip(0.430, 0.27, 74.0, 30.0, 0.62)     # it slows as it opens out

    y = (reson(exc, 186.0, 8.0) * 1.00
         + reson(exc, 372.0, 11.0) * 0.58
         + reson(exc, 690.0, 13.0) * 0.30
         + reson(exc, 1420.0, 10.0) * 0.13)
    groan = sfilt(reson(exc, 82.0, 5.0), "lowpass", 240.0, 2) * 0.85

    # the latch: an iron drop bar landing in a wooden keeper
    ln = ns(0.22)
    latch = modal(ln, [720.0, 1180.0, 2310.0, 386.0],
                  [0.020, 0.012, 0.006, 0.030],
                  [1.0, 0.52, 0.26, 0.60], attack=0.0004, r=r)
    latch += strike(ln, r, 900.0, 9000.0, 0.0012) * 0.55
    latch += modal(ln, [148.0, 233.0], [0.028, 0.016], [1.0, 0.45],
                   attack=0.0006, r=r) * 0.42      # the post taking the knock
    latch *= env_ad(ln, 0.0004, 0.032, curve=2.8)
    latch /= np.max(np.abs(latch)) + 1e-12

    y = y + groan * 0.55
    y /= np.max(np.abs(y)) + 1e-12
    add(y, ns(0.780), latch, 0.95)

    y = sfilt(y, "highpass", 68.0, 2)
    y = sfilt(y, "lowpass", 6800.0, 2)
    y = satur(y * 0.9, 1.25, 0.45)
    return dc_block(y * env_asr(n, 0.003, 0.08))


# --------------------------------------------------------------------------


def render():
    write(out("Transition", "transition_leaf_sweep.wav"),
          make_leaf_sweep(), fade_out=0.012)
    write(out("Transition", "transition_banner_whoosh.wav"),
          make_banner_whoosh(), fade_out=0.012)
    # 4 ms out only - the riser must still be at full level when the cut lands
    write(out("Transition", "transition_riser.wav"),
          make_riser(), fade_out=0.004)
    write(out("Transition", "transition_impact.wav"),
          make_impact(), fade_out=0.030)
    write(out("Transition", "transition_stamp_small.wav"),
          make_stamp_small(), fade_out=0.010)
    write(out("Transition", "transition_fanfare_big.wav"),
          make_fanfare_big(), fade_out=0.040)
    write(out("Transition", "transition_crowd_swell.wav"),
          make_crowd_swell(), fade_out=0.060)
    write(out("Transition", "transition_gate_creak.wav"),
          make_gate_creak(), fade_out=0.015)


if __name__ == "__main__":
    render()
    print("transition rendered")
