"""
DUCK MOW - Music/

A small county-fair string band: banjo, fiddle, pizzicato upright, squeezebox,
and light hand percussion.  Written as note lists through the sequencer in
instruments.py - real tunes with real chord changes, not arpeggiators.

Two things matter technically:

  * `music_round_loop` and `music_round_urgent_layer` are both 16 bars at
    128 BPM = exactly 30.000 s = 1,323,000 samples, built on the same grid, so
    they line up sample-for-sample and the urgent layer can be faded in at any
    moment.
  * The round mix has the **60-160 Hz band carved out** - a 2-pole high-pass at
    130 Hz plus a broad -9 dB bell at 105 Hz, on top of an arrangement whose
    every bass note is voiced at 196 Hz or above - so the music never fights
    the engine.  `verify_carve()` measures what is left in the band.

The urgent layer is deliberately *uniform* in intensity rather than building
across its 30 s: it gets faded in during the last 15 s of a 75 s round, so it
will be entered at an arbitrary phase and must be tense wherever you join it.
"""

from __future__ import annotations

import numpy as np
from dsp import *  # noqa: F403
from instruments import Seq, CHORD, voicing, m2f
import instruments as I


# --------------------------------------------------------------------------
# shared helpers
# --------------------------------------------------------------------------

ROLL_PATTERNS = [
    [0, 1, 2, 3, 0, 2, 1, 3],
    [2, 1, 0, 3, 1, 2, 0, 1],
    [0, 2, 1, 2, 3, 1, 2, 0],
    [1, 0, 2, 3, 2, 0, 1, 2],
    [0, 1, 3, 2, 0, 1, 2, 3],
    [2, 0, 1, 3, 0, 2, 3, 1],
]


def banjo_bar(seq, bar, chord, drone, pat_idx, vel, p, lo=62, hi=79,
              grace=None, beats_per_bar=4):
    """
    One bar of a banjo roll.  The pool is the chord voicing plus the fixed
    5th-string drone, exactly as a real banjo works - the drone is what makes
    a roll sound like a banjo instead of an arpeggiator.
    """
    pool = voicing(chord, lo, hi)
    while len(pool) < 3:
        pool.append(pool[-1] + 12)
    notes = pool + [drone]
    pat = ROLL_PATTERNS[pat_idx % len(ROLL_PATTERNS)]
    for i in range(beats_per_bar * 2):
        b = bar * beats_per_bar + i * 0.5
        m = notes[pat[i % len(pat)] % len(notes)]
        v = vel * (1.0 if i % 4 == 0 else (0.72 if i % 2 else 0.86))
        seq.note("banjo", b, 0.5, m, v, p + 0.05 * ((i % 3) - 1))
    if grace is not None:
        gb, gm = grace
        seq.note("banjo", bar * beats_per_bar + gb, 0.25, gm, vel * 0.55, p)


def master(y, *, carve=False, rev=None, hp=48.0, air=1.0, drive=1.15,
           loop=True):
    """Bus processing: bass management, the engine carve, room, soft limit."""
    def H(f):
        h = H_hp(f, hp, 2) * H_lp(f, 15000.0, 1)
        h = h * H_peak(f, 250.0, 1.1, -3.0)     # tidy the low mids
        h = h * H_peak(f, 3200.0, 0.9, 3.0 * air)
        if carve:
            # THE ENGINE LIVES HERE.  A 2-pole high-pass at 130 Hz plus a
            # broad -9 dB bell at 105 Hz: about -15 dB averaged across
            # 60-160 Hz, while 250 Hz and up (where every bass note in this
            # arrangement actually sits) only loses a few dB.
            h = h * H_hp(f, 130.0, 2) * H_peak(f, 105.0, 1.0, -9.0)
        return h

    # Circular filtering everywhere: on loops it is what keeps the seam exact,
    # and on the one-shots the head and tail are near silence so the wrap is
    # inaudible.
    y = cfilt(y, H)
    if rev is not None:
        ir, amt = rev
        y = (creverb(y, ir) * amt + y * (1.0 - 0.25 * amt)) if loop else \
            (oreverb(y, ir, tail=False) * amt + y * (1.0 - 0.25 * amt))
    y = satur(y * 0.85, drive, 0.45)
    return dc_block(y)


# --------------------------------------------------------------------------
# MENU - 12 bars, 120 BPM, key of G.  Relaxed, jaunty, nothing at stake.
# --------------------------------------------------------------------------

MENU_CHORDS = ["G", "G", "C", "G", "Em", "C", "D", "D", "C", "G", "D", "G"]

# (beat, duration in beats, midi)
MENU_MELODY = [
    (0.0, 1.0, 62), (1.0, 1.0, 67), (2.0, 1.5, 71), (3.5, 0.5, 69),
    (4.0, 1.5, 67), (5.5, 0.5, 71), (6.0, 2.0, 74),
    (8.0, 1.0, 72), (9.0, 1.0, 71), (10.0, 1.0, 69), (11.0, 1.0, 67),
    (12.0, 2.0, 71), (14.0, 2.0, 67),
    (16.0, 1.0, 64), (17.0, 1.0, 67), (18.0, 1.0, 71), (19.0, 1.0, 76),
    (20.0, 1.5, 74), (21.5, 0.5, 72), (22.0, 2.0, 69),
    (24.0, 1.0, 66), (25.0, 1.0, 69), (26.0, 1.5, 74), (27.5, 0.5, 72),
    (28.0, 2.0, 71), (30.0, 2.0, 69),
    (32.0, 1.0, 67), (33.0, 1.0, 69), (34.0, 1.0, 71), (35.0, 1.0, 72),
    (36.0, 2.0, 74), (38.0, 1.0, 71), (39.0, 1.0, 67),
    (40.0, 1.0, 69), (41.0, 1.0, 66), (42.0, 2.0, 69),
    (44.0, 3.0, 67), (47.5, 0.5, 62),          # the pickup wraps into bar 0
]

# root / fifth for the oompah bass, all voiced at 196 Hz or above
MENU_BASS = {"G": (55, 62), "C": (60, 67), "D": (62, 57), "Em": (59, 64)}


def make_menu():
    s = Seq(120, 48, loop=True, seed=11)
    r = s.r

    for bar, ch in enumerate(MENU_CHORDS):
        b0 = bar * 4
        # squeezebox holds the harmony, low and soft
        s.chord("melodeon", b0, 3.85, [m + 12 for m in CHORD[ch]],
                vel=0.30, spread=0.35, gain=0.52, strum=0.010)
        # bass: root on 1, fifth on 3, with a passing note into the change
        root, fifth = MENU_BASS[ch]
        s.note("bass", b0, 1.6, root, 0.80, 0.0, 0.95)
        s.note("bass", b0 + 2, 1.6, fifth, 0.66, 0.0, 0.92)
        if bar in (3, 7, 11):
            nxt = MENU_BASS[MENU_CHORDS[(bar + 1) % 12]][0]
            s.note("bass", b0 + 3.5, 0.5, nxt - 2, 0.52, 0.0, 0.70)
        # banjo roll, panned left
        grace = None
        if bar in (2, 5, 9):
            grace = (3.75, CHORD[ch][1] + 12)
        banjo_bar(s, bar, ch, drone=67, pat_idx=bar, vel=0.58, p=-0.42,
                  lo=62, hi=79, grace=grace)
        # percussion: brush on 2 and 4, shaker eighths, one stomp per bar
        for beat in (1, 3):
            s.hit("brush", b0 + beat, 0.40 + 0.06 * (beat == 3), 0.30, 0.55)
        for e in range(8):
            if e % 2 == 1:
                s.hit("shaker", b0 + e * 0.5, 0.16, 0.45, 0.40)
        s.hit("stomp", b0 + 2, 0.30, 0.0, 0.32)

    # fiddle carries the tune, right of centre
    for b, d, m in MENU_MELODY:
        vel = 0.72 + (0.10 if (b % 4) == 0 else 0.0) + (0.06 if d >= 2 else 0.0)
        s.note("fiddle", b, d, m, min(vel, 0.92), 0.30, 0.95)

    ir = ir_room(1.1, rng(1101), decay=0.34, bright=4200.0, predelay=0.016)
    return master(s.buf, carve=False, rev=(ir, 0.24), air=1.0, drive=1.45)


# --------------------------------------------------------------------------
# ROUND - 16 bars, 128 BPM, key of D.  Same band, more drive.
# --------------------------------------------------------------------------

ROUND_CHORDS = ["D", "D", "G", "D", "Bm", "G", "A", "A",
                "D", "G", "D", "Bm", "G", "A", "D", "A"]

ROUND_MELODY = [
    (0.0, .5, 74), (0.5, .5, 78), (1.0, 1.0, 81), (2.0, .5, 78), (2.5, .5, 74), (3.0, 1.0, 76),
    (4.0, 1.5, 78), (5.5, .5, 76), (6.0, 1.0, 74), (7.0, 1.0, 71),
    (8.0, .5, 71), (8.5, .5, 74), (9.0, 1.0, 79), (10.0, .5, 78), (10.5, .5, 76), (11.0, 1.0, 74),
    (12.0, 2.0, 78), (14.0, 1.0, 74), (15.0, 1.0, 73),
    (16.0, .5, 71), (16.5, .5, 74), (17.0, 1.0, 78), (18.0, 1.0, 76), (19.0, 1.0, 74),
    (20.0, 1.0, 71), (21.0, .5, 74), (21.5, .5, 76), (22.0, 2.0, 79),
    (24.0, .5, 73), (24.5, .5, 76), (25.0, 1.0, 81), (26.0, 1.0, 78), (27.0, 1.0, 76),
    (28.0, 1.5, 73), (29.5, .5, 71), (30.0, 2.0, 69),
    (32.0, .5, 74), (32.5, .5, 78), (33.0, 1.0, 81), (34.0, 1.0, 79), (35.0, 1.0, 78),
    (36.0, 1.0, 76), (37.0, 1.0, 74), (38.0, .5, 71), (38.5, .5, 74), (39.0, 1.0, 79),
    (40.0, 1.5, 78), (41.5, .5, 76), (42.0, 2.0, 74),
    (44.0, .5, 71), (44.5, .5, 73), (45.0, 1.0, 74), (46.0, 1.0, 78), (47.0, 1.0, 76),
    (48.0, 1.0, 79), (49.0, .5, 78), (49.5, .5, 76), (50.0, 1.0, 74), (51.0, 1.0, 71),
    (52.0, .5, 73), (52.5, .5, 76), (53.0, 1.0, 78), (54.0, 2.0, 81),
    (56.0, 1.0, 78), (57.0, 1.0, 74), (58.0, .5, 76), (58.5, .5, 78), (59.0, 1.0, 79),
    (60.0, 1.0, 78), (61.0, 1.0, 76), (62.0, 1.0, 73), (63.0, .5, 71), (63.5, .5, 74),
]

ROUND_BASS = {"D": (62, 57), "G": (55, 62), "A": (57, 64), "Bm": (59, 66)}


def make_round():
    s = Seq(128, 64, loop=True, seed=22)

    for bar, ch in enumerate(ROUND_CHORDS):
        b0 = bar * 4
        s.chord("melodeon", b0, 3.9, [m + 12 for m in CHORD[ch]],
                vel=0.26, spread=0.40, gain=0.42, strum=0.008)

        root, fifth = ROUND_BASS[ch]
        # driving four-to-the-bar with a push into every other bar
        s.note("bass", b0 + 0, 0.9, root, 0.85, 0.0, 1.0)
        s.note("bass", b0 + 1, 0.9, fifth, 0.62, 0.0, 0.9)
        s.note("bass", b0 + 2, 0.9, root, 0.78, 0.0, 0.95)
        if bar % 2 == 1:
            s.note("bass", b0 + 3, 0.5, fifth, 0.58, 0.0, 0.85)
            nxt = ROUND_BASS[ROUND_CHORDS[(bar + 1) % 16]][0]
            s.note("bass", b0 + 3.5, 0.5, nxt - 2, 0.55, 0.0, 0.80)
        else:
            s.note("bass", b0 + 3, 0.9, fifth, 0.60, 0.0, 0.9)

        grace = (3.75, CHORD[ch][2] + 12) if bar in (1, 5, 9, 13) else None
        banjo_bar(s, bar, ch, drone=69, pat_idx=bar + 2, vel=0.62, p=-0.40,
                  lo=62, hi=81, grace=grace)

        for beat in (1, 3):
            s.hit("brush", b0 + beat, 0.52, 0.32, 0.62)
        for e in range(8):
            s.hit("shaker", b0 + e * 0.5, 0.20 if e % 2 else 0.13, 0.48, 0.46)
        s.hit("stomp", b0 + 0, 0.44, 0.0, 0.46)
        s.hit("stomp", b0 + 2, 0.34, 0.0, 0.42)
        if bar % 4 == 3:
            s.hit("woodblock", b0 + 3.5, 0.34, -0.22, 0.42)

    for b, d, m in ROUND_MELODY:
        vel = 0.74 + (0.12 if (b % 4) == 0 else 0.0) + (0.05 if d >= 1.5 else 0.0)
        s.note("fiddle", b, d, m, min(vel, 0.95), 0.28, 1.0)

    ir = ir_room(0.9, rng(2201), decay=0.26, bright=4000.0, predelay=0.013)
    return master(s.buf, carve=True, rev=(ir, 0.20), air=1.1, drive=1.75)


# --------------------------------------------------------------------------
# URGENT LAYER - same grid, added on top for the last 15 s
# --------------------------------------------------------------------------


def make_urgent():
    s = Seq(128, 64, loop=True, seed=33)
    r = s.r

    for bar, ch in enumerate(ROUND_CHORDS):
        b0 = bar * 4
        tri_ = CHORD[ch]

        # 1. fiddle tremolo double-stop on the 3rd and 5th: the tension bed
        for e in range(16):
            b = b0 + e * 0.25
            v = 0.34 if e % 4 == 0 else 0.24
            s.note("fiddle", b, 0.28, tri_[1] + 12, v, -0.34, 0.62, bow=0.34, vib=0.004)
            s.note("fiddle", b, 0.28, tri_[2] + 12, v * 0.8, 0.34, 0.58, bow=0.34, vib=0.004)

        # 2. a ratcheting four-note figure that climbs within every bar
        fig = [tri_[0] + 24, tri_[1] + 24, tri_[2] + 24, tri_[0] + 36]
        for i, m in enumerate(fig):
            s.note("banjo", b0 + 0.5 + i * 0.5, 0.5, m, 0.42 + 0.05 * i,
                   0.15 * ((i % 2) * 2 - 1), 0.58)

        # 3. a hard pulse on every beat, at 215 Hz - clear of the engine band
        for beat in range(4):
            s.hit("stomp", b0 + beat, 0.62 if beat % 2 == 0 else 0.44, 0.0, 0.70)
        # 4. sixteenth tambourine/shaker drive
        for e in range(16):
            s.hit("shaker", b0 + e * 0.25, 0.30 if e % 4 == 0 else 0.17, 0.55, 0.55)
        s.hit("tamb", b0 + 2, 0.34, -0.5, 0.42)
        # 5. an off-beat woodblock tick, pitched up a little each bar so it
        #    never settles into a comfortable groove
        s.hit("woodblock", b0 + 1.75, 0.40, -0.55, 0.50,
              f=1100.0 + 55.0 * (bar % 8))
        s.hit("woodblock", b0 + 3.25, 0.34, 0.55, 0.46,
              f=1320.0 + 55.0 * (bar % 8))

    ir = ir_room(0.7, rng(3301), decay=0.20, bright=4500.0, predelay=0.010)
    return master(s.buf, carve=True, rev=(ir, 0.16), air=1.25, drive=1.75)


# --------------------------------------------------------------------------
# REVEAL - 6 s one-shot.  IV -> V -> I, the camera lifts, there it is.
# --------------------------------------------------------------------------


def make_reveal():
    s = Seq(100, 10, loop=False, seed=44)
    r = s.r
    sp = s.spb

    # a bowed swell lifting into the arrival on beat 4
    s.raw(0.0, I.swell(4 * sp, 0.55, r, lo=500.0, hi=9000.0, peak=0.95), 0.55, 0.0)

    # bar 1: C (IV)
    s.chord("melodeon", 0.0, 2.0, [72, 76, 79], vel=0.42, spread=0.4, gain=0.70, strum=0.012)
    s.note("bass", 0.0, 1.8, 60, 0.72, 0.0, 0.85)
    s.note("fiddle", 0.0, 1.0, 67, 0.62, 0.25, 0.85)
    s.note("fiddle", 1.0, 1.0, 71, 0.68, 0.25, 0.88)

    # bar 2: D (V), with a suspension that resolves on the way up
    s.chord("melodeon", 2.0, 2.0, [74, 78, 81], vel=0.48, spread=0.4, gain=0.74, strum=0.012)
    s.note("bass", 2.0, 1.8, 62, 0.76, 0.0, 0.88)
    s.note("fiddle", 2.0, 1.0, 74, 0.76, 0.25, 0.92)
    s.note("fiddle", 3.0, 0.5, 76, 0.72, 0.25, 0.90)
    s.note("fiddle", 3.5, 0.5, 78, 0.78, 0.25, 0.92)

    # beat 4: G (I) arrives, held, with an added 9th for warmth
    s.chord("melodeon", 4.0, 5.6, [67, 71, 74, 79], vel=0.62, spread=0.45,
            gain=0.95, strum=0.015)
    s.chord("melodeon", 4.0, 5.6, [81], vel=0.30, spread=0.0, gain=0.45)
    s.note("bass", 4.0, 2.4, 55, 0.88, 0.0, 1.0)
    s.note("bass", 6.0, 2.0, 62, 0.62, 0.0, 0.85)
    s.note("fiddle", 4.0, 4.2, 79, 0.90, 0.22, 1.0)
    s.note("fiddle", 4.0, 4.2, 74, 0.52, -0.28, 0.70)

    # banjo rolls under the held chord
    for bar, ch in ((4.0, "G"), (6.0, "C"), (8.0, "G")):
        pool = voicing(ch, 67, 84)
        for i in range(4):
            s.note("banjo", bar + i * 0.5, 0.5, pool[i % len(pool)],
                   0.50 - 0.04 * i, -0.35, 0.70)

    # sparkle on the arrival
    for i, (b, m, g) in enumerate([(4.0, 91, 0.85), (4.25, 95, 0.60),
                                   (4.5, 98, 0.45), (5.0, 91, 0.35),
                                   (6.0, 95, 0.30)]):
        s.note("glock", b, 1.0, m, g, 0.15 * (i % 3 - 1), 0.55)

    s.hit("tamb", 4.0, 0.55, 0.0, 0.60)
    s.hit("stomp", 4.0, 0.65, 0.0, 0.55)
    s.hit("brush", 6.0, 0.35, 0.3, 0.40)
    s.hit("brush", 8.0, 0.28, -0.3, 0.35)

    ir = ir_room(1.7, rng(4401), decay=0.55, bright=4800.0, predelay=0.020)
    y = master(s.buf, carve=False, rev=(ir, 0.30), air=1.15, loop=False)
    n = y.shape[-1]
    return y * env_asr(n, 0.002, 0.55, curve=1.3)


# --------------------------------------------------------------------------
# JUDGING BED - 8 s loop, 4 bars at 120 BPM.  B minor, sparse, plucky, tense.
# --------------------------------------------------------------------------


def make_judging():
    s = Seq(120, 16, loop=True, seed=55)

    # a pizzicato ostinato that never quite resolves
    ostinato = [(0.0, 59), (1.5, 59), (2.5, 66), (3.5, 62)]
    bars = [0, 0, 1, 2]     # bar 3 shifts down a step: the rug moves
    shifts = [0, 0, -2, -1]
    for bar in range(4):
        b0 = bar * 4
        sh = shifts[bar]
        for b, m in ostinato:
            s.note("bass", b0 + b, 1.2, m + sh, 0.62 if b == 0 else 0.46,
                   0.0, 0.78)
        # a low drone with a minor-second rub that only appears in the last bar
        s.note("melodeon", b0, 3.9, 59 + 12, 0.24, -0.25, 0.50)
        if bar == 3:
            s.note("melodeon", b0 + 2, 1.9, 60 + 12, 0.20, 0.30, 0.44)
        # a single muted banjo note per bar, high and dry
        pick = [78, 74, 81, 76][bar]
        s.note("banjo", b0 + 2.75, 0.5, pick, 0.60, 0.42, 0.92)
        if bar % 2 == 1:
            s.note("banjo", b0 + 3.5, 0.25, pick - 3, 0.42, 0.42, 0.70)
        # clock
        s.hit("woodblock", b0 + 1.0, 0.36, -0.5, 0.55, f=1450.0)
        s.hit("woodblock", b0 + 3.0, 0.28, -0.5, 0.46, f=1450.0)
        s.hit("shaker", b0 + 2.0, 0.20, 0.5, 0.42)

    ir = ir_room(1.3, rng(5501), decay=0.40, bright=3400.0, predelay=0.018)
    return master(s.buf, carve=False, rev=(ir, 0.30), air=1.15, drive=1.30)


# --------------------------------------------------------------------------
# VERDICT FANFARES - 3 s each
# --------------------------------------------------------------------------


def make_fanfare_good():
    s = Seq(126, 6.3, loop=False, seed=66)
    r = s.r

    # pickup triplet
    for i, m in enumerate((62, 67, 71)):
        s.note("banjo", -0.5 + i * 0.166, 0.3, m, 0.55 + 0.08 * i, -0.3, 0.7)
        s.note("fiddle", -0.5 + i * 0.166, 0.2, m, 0.45 + 0.08 * i, 0.3, 0.6)

    stabs = [(0.0, "G", 0.6, 0.95), (1.0, "C", 0.5, 0.80), (1.5, "D", 0.5, 0.85)]
    for b, ch, d, g in stabs:
        s.chord("melodeon", b, d, [m + 12 for m in CHORD[ch]], vel=0.62,
                spread=0.4, gain=g, strum=0.010)
        s.chord("banjo", b, d, voicing(ch, 67, 84), vel=0.60, spread=0.35,
                gain=0.75, strum=0.014)
        s.note("bass", b, d, {"G": 55, "C": 60, "D": 62}[ch], 0.85, 0.0, 1.0)
        s.hit("stomp", b, 0.55, 0.0, 0.50)
        s.hit("tamb", b, 0.40, 0.0, 0.42)

    # the landing
    s.chord("melodeon", 2.0, 4.0, [67, 71, 74, 79], vel=0.70, spread=0.45,
            gain=1.0, strum=0.015)
    s.note("bass", 2.0, 2.2, 55, 0.90, 0.0, 1.0)
    s.note("fiddle", 0.0, 1.0, 74, 0.72, 0.25, 0.85)
    s.note("fiddle", 1.0, 0.5, 76, 0.70, 0.25, 0.82)
    s.note("fiddle", 1.5, 0.5, 78, 0.76, 0.25, 0.85)
    s.note("fiddle", 2.0, 3.4, 79, 0.92, 0.22, 1.0)
    for i, (b, m) in enumerate([(2.0, 91), (2.25, 95), (2.6, 98)]):
        s.note("glock", b, 1.0, m, 0.75 - 0.15 * i, 0.2 * (i - 1), 0.55)
    for i in range(5):
        s.hit("shaker", 2.0 + i * 0.25, 0.30 - 0.04 * i, 0.5, 0.40)
    s.hit("stomp", 2.0, 0.75, 0.0, 0.60)
    s.hit("tamb", 2.0, 0.60, 0.0, 0.55)

    ir = ir_room(1.2, rng(6601), decay=0.36, bright=4600.0, predelay=0.014)
    y = master(s.buf, carve=False, rev=(ir, 0.26), air=1.1, loop=False)
    return y * env_asr(y.shape[-1], 0.002, 0.42, curve=1.2)


def make_fanfare_bad():
    """
    Deflating, not cruel: the band tries a chord, thinks better of it, and
    sinks a semitone at a time while the squeezebox slowly runs out of air.
    """
    s = Seq(96, 4.8, loop=False, seed=77)
    r = s.r

    # a hopeful start that immediately goes wrong
    s.chord("melodeon", 0.0, 0.8, [67, 71, 74], vel=0.55, spread=0.4,
            gain=0.85, strum=0.012)
    s.note("bass", 0.0, 0.8, 55, 0.78, 0.0, 0.95)
    s.hit("stomp", 0.0, 0.50, 0.0, 0.45)

    # the sag: G -> Gm -> F#dim-ish, each a bit slower and flatter
    sag = [(0.85, [67, 70, 74], 0.72, 55), (1.55, [66, 69, 73], 0.62, 54),
           (2.30, [65, 68, 72], 0.52, 53)]
    for b, ms, g, bs in sag:
        s.chord("melodeon", b, 0.75, ms, vel=0.46, spread=0.35, gain=g,
                strum=0.014)
        s.note("bass", b, 0.7, bs + 12, 0.60, 0.0, 0.80)
        s.hit("woodblock", b, 0.30, -0.4, 0.35, f=920.0 - 60 * (b > 1.5))

    # the last note, with the squeezebox bellows giving out - the pitch droop
    # is done on the oscillator, not with a pitch-shifted sample
    n = ns(1.45)
    t = tt(n)
    droop = m2f(60) * (1.0 - 0.085 * np.clip((t - 0.20) / 0.9, 0, 1) ** 1.4)
    reed = bl_saw(droop, n) * 0.5 + bl_saw(droop * 1.005, n) * 0.5
    reed += 0.20 * bl_square(droop * 2.0, n)
    reed = sfilt(reed, "lowpass", 2200.0, 2)
    reed = reed + reson(reed, 420.0, 2.0) * 0.3
    reed *= env_asr(n, 0.05, 0.55, 1.4) * (1.0 + 0.05 * np.sin(2 * np.pi * 3.6 * t))
    reed /= np.max(np.abs(reed)) + 1e-12
    s.raw(3.05, reed, 0.85, 0.0)

    lown = ns(1.3)
    lowd = m2f(53) * (1.0 - 0.07 * np.clip((tt(lown) - 0.2) / 0.8, 0, 1) ** 1.4)
    low = tri(lowd, lown, nharm=8) * np.exp(-tt(lown) / 0.55)
    s.raw(3.05, sfilt(low, "lowpass", 900.0, 2) / (np.max(np.abs(low)) + 1e-12),
          0.42, 0.0)

    # one last unimpressed woodblock
    s.hit("woodblock", 3.05, 0.34, 0.0, 0.38, f=760.0)

    ir = ir_room(1.0, rng(7701), decay=0.30, bright=3000.0, predelay=0.014)
    y = master(s.buf, carve=False, rev=(ir, 0.24), air=0.7, drive=1.05, loop=False)
    return y * env_asr(y.shape[-1], 0.002, 0.30, curve=1.2)


# --------------------------------------------------------------------------
# verification + render
# --------------------------------------------------------------------------


def verify_carve(y, label=""):
    """Measure how much of the mix sits in the engine's 60-160 Hz band."""
    mono = y.mean(axis=0) if y.ndim == 2 else y
    n = len(mono)
    X = np.abs(np.fft.rfft(mono * np.hanning(n))) ** 2
    f = np.fft.rfftfreq(n, 1 / SR)
    band = X[(f >= 60) & (f < 160)].sum()
    ref = X[(f >= 200) & (f < 800)].sum()
    tot = X.sum()
    return {
        "label": label,
        "band_frac_pct": 100.0 * band / (tot + 1e-20),
        "band_vs_200_800_db": lin2db(np.sqrt(band / (ref + 1e-20))),
    }


def render():
    info = []
    y = make_menu()
    write(out("Music", "music_menu_loop.wav"), y)
    info.append(verify_carve(y, "menu"))

    rl = make_round()
    write(out("Music", "music_round_loop.wav"), rl)
    info.append(verify_carve(rl, "round"))

    ur = make_urgent()
    write(out("Music", "music_round_urgent_layer.wav"), ur)
    info.append(verify_carve(ur, "urgent"))
    assert rl.shape == ur.shape, "round and urgent must be identical length"

    write(out("Music", "music_reveal.wav"), make_reveal(), fade_out=0.05)
    write(out("Music", "music_judging_bed_loop.wav"), make_judging())
    write(out("Music", "fanfare_good.wav"), make_fanfare_good(), fade_out=0.04)
    write(out("Music", "fanfare_bad.wav"), make_fanfare_bad(), fade_out=0.05)
    return info


if __name__ == "__main__":
    for d in render():
        print(f"{d['label']:8s} 60-160 Hz = {d['band_frac_pct']:6.3f}% of total, "
              f"{d['band_vs_200_800_db']:+6.1f} dB vs 200-800 Hz")
