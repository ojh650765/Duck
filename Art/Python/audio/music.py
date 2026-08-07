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


def master(y, *, carve=False, voice=False, rev=None, hp=48.0, air=1.0,
           drive=1.15, loop=True):
    """Bus processing: bass management, the engine carve, room, soft limit."""
    def H(f):
        h = H_hp(f, hp, 2) * H_lp(f, 15000.0, 1)
        h = h * H_peak(f, 250.0, 1.1, -3.0)     # tidy the low mids
        h = h * H_peak(f, 3200.0, 0.9, 3.0 * air)
        if voice:
            # THE NARRATOR LIVES HERE.  Two broad, shallow dips rather than one
            # deep notch: 700 Hz is where the squeezebox pad's fundamentals
            # pile up and 1900 Hz is the consonant band, and a bed that is
            # -3 dB in both is one a voice sits on top of without anybody
            # having to ride a fader.  Shallow because this is a tune, not a
            # duck: 6 dB out of the middle of it and the band sounds broken.
            h = h * H_peak(f, 700.0, 0.9, -3.5) * H_peak(f, 1900.0, 0.8, -3.0)
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
# RALLY - 16 bars, 128 BPM, key of D.  Stage two: the goose rally.  Same band,
# same tempo and key as the round loop - it has to read as the same game -
# but with more harmonic motion, a busier bass and a denser kit, because the
# rally is a step up from the calm of mowing: four contestants, a flock of
# geese, and a horn the player is timing against a closing bird.
# --------------------------------------------------------------------------

# A chord every bar instead of every two, and F#m in the mix (borrowed from
# the parallel minor's neighbour) for a brighter, more restless progression
# than the round's.  Still resolves V (A) -> I (D) across the seam, same as
# ROUND_CHORDS, so the loop turns over on a real cadence rather than a splice.
RALLY_CHORDS = ["D", "A", "Bm", "F#m", "G", "D", "Em", "A",
                "Bm", "F#m", "G", "A", "D", "A", "Bm", "A"]

# All still D major diatonic, same register the round tune lives in, but the
# contour reaches a fifth higher at its two peaks (bars 9 and 11 - the chase
# closing in) instead of the round's ceiling.  The last note is the leading
# tone into the loop's first, exactly the trick CUTSCENE_MELODY uses to make
# the wrap read as a cadence.
RALLY_MELODY = [
    (0.0, .5, 74), (0.5, .5, 78), (1.0, .5, 81), (1.5, .5, 78), (2.0, .5, 74), (2.5, .5, 76), (3.0, 1.0, 78),
    (4.0, .5, 76), (4.5, .5, 73), (5.0, .5, 76), (5.5, .5, 78), (6.0, 1.0, 81), (7.0, 1.0, 78),
    (8.0, .5, 74), (8.5, .5, 71), (9.0, .5, 74), (9.5, .5, 78), (10.0, 1.0, 81), (11.0, 1.0, 74),
    (12.0, .5, 73), (12.5, .5, 69), (13.0, .5, 73), (13.5, .5, 76), (14.0, 1.5, 78),
    (16.0, .5, 79), (16.5, .5, 76), (17.0, .5, 79), (17.5, .5, 81), (18.0, 1.0, 83), (19.0, 1.0, 79),
    (20.0, .5, 78), (20.5, .5, 74), (21.0, .5, 78), (21.5, .5, 81), (22.0, 1.5, 83),
    (24.0, .5, 76), (24.5, .5, 71), (25.0, .5, 76), (25.5, .5, 79), (26.0, 1.0, 83), (27.0, 1.0, 79),
    (28.0, .5, 78), (28.5, .5, 74), (29.0, .5, 78), (29.5, .5, 81), (30.0, 1.5, 83),
    (32.0, .5, 74), (32.5, .5, 78), (33.0, .5, 81), (33.5, .5, 83), (34.0, 1.0, 85), (35.0, 1.0, 81),
    (36.0, .5, 78), (36.5, .5, 73), (37.0, .5, 78), (37.5, .5, 81), (38.0, 1.5, 83),
    (40.0, .5, 79), (40.5, .5, 83), (41.0, .5, 85), (41.5, .5, 83), (42.0, 1.0, 81), (43.0, 1.0, 79),
    (44.0, .5, 78), (44.5, .5, 74), (45.0, .5, 78), (45.5, .5, 81), (46.0, 1.5, 83),
    (48.0, .5, 78), (48.5, .5, 74), (49.0, .5, 78), (49.5, .5, 81), (50.0, 1.0, 83), (51.0, 1.0, 78),
    (52.0, .5, 76), (52.5, .5, 73), (53.0, .5, 76), (53.5, .5, 78), (54.0, 1.5, 81),
    (56.0, .5, 74), (56.5, .5, 71), (57.0, .5, 74), (57.5, .5, 78), (58.0, 1.0, 81), (59.0, 1.0, 74),
    (60.0, .5, 73), (60.5, .5, 69), (61.0, .5, 73), (61.5, .5, 76), (62.0, 1.0, 78), (63.0, 1.0, 73),
]

# Root/fifth, both transposed up an octave where the plain chord tone would
# have fallen under 196 Hz (MIDI 55) - F#m's root is MIDI 54 in CHORD, so it
# is voiced here at 66 instead - so the engine carve still finds nothing to
# cut in this arrangement, same rule as ROUND_BASS and CUTSCENE_BASS.
RALLY_BASS = {"D": (62, 57), "A": (57, 64), "Bm": (59, 66),
              "F#m": (66, 61), "G": (55, 62), "Em": (64, 59)}


def make_rally():
    """
    Stage two: the goose rally.

    Built as ROUND is - same carve, same bass register rule, same band - and
    then pushed harder in three places, because "a step up in energy" has to
    be a fact about the arrangement, not a fader:

      * the bass walks in eighths instead of mostly quarters;
      * the banjo alternates a normal roll with the urgent layer's climbing
        16th-note figure, bar by bar, instead of rolling the same pattern
        throughout;
      * the kit runs the urgent layer's sixteenth shaker and a second
        tambourine part under the round's own brush-and-stomp backbone,
        rather than the round's plain eighths.

    It does NOT borrow the urgent layer's fiddle tremolo wholesale - that
    effect is a tension texture with no tune of its own, right for fifteen
    seconds under something else, wrong for seventy-eight seconds on its
    own. The rally needed a tune a player could leave the arena humming.
    """
    s = Seq(128, 64, loop=True, seed=77)

    for bar, ch in enumerate(RALLY_CHORDS):
        b0 = bar * 4
        s.chord("melodeon", b0, 3.9, [m + 12 for m in CHORD[ch]],
                vel=0.25, spread=0.40, gain=0.40, strum=0.008)

        root, fifth = RALLY_BASS[ch]
        # eighth-note walk, not the round's quarters - the busier bass is
        # most of where the extra drive comes from
        s.note("bass", b0 + 0.0, 0.45, root, 0.86, 0.0, 1.0)
        s.note("bass", b0 + 0.5, 0.40, fifth, 0.55, 0.0, 0.75)
        s.note("bass", b0 + 1.0, 0.45, root, 0.80, 0.0, 0.95)
        s.note("bass", b0 + 1.5, 0.40, fifth, 0.50, 0.0, 0.70)
        s.note("bass", b0 + 2.0, 0.45, root, 0.82, 0.0, 0.95)
        s.note("bass", b0 + 2.5, 0.40, fifth, 0.52, 0.0, 0.72)
        s.note("bass", b0 + 3.0, 0.45, root, 0.78, 0.0, 0.90)
        nxt = RALLY_BASS[RALLY_CHORDS[(bar + 1) % 16]][0]
        s.note("bass", b0 + 3.5, 0.45, nxt - 2, 0.58, 0.0, 0.82)

        if bar % 2 == 0:
            banjo_bar(s, bar, ch, drone=69, pat_idx=bar + 3, vel=0.66,
                      p=-0.38, lo=64, hi=83)
        else:
            # the urgent layer's climbing figure, borrowed as a fill rather
            # than run continuously - two bars of tune, one bar of ratchet
            tri_ = CHORD[ch]
            fig = [tri_[0] + 24, tri_[1] + 24, tri_[2] + 24, tri_[0] + 36]
            for i, m in enumerate(fig):
                s.note("banjo", b0 + i * 0.5, 0.5, m, 0.46 + 0.05 * i,
                       0.12 * ((i % 2) * 2 - 1), 0.55)
            pool = voicing(ch, 64, 81)
            for i in range(4):
                s.note("banjo", b0 + 2.0 + i * 0.5, 0.5,
                       pool[i % len(pool)], 0.40, -0.30, 0.50)

        for beat in (1, 3):
            s.hit("brush", b0 + beat, 0.42, 0.30, 0.55)
        for e in range(16):
            s.hit("shaker", b0 + e * 0.25, 0.27 if e % 4 == 0 else 0.15,
                  0.52, 0.52)
        s.hit("stomp", b0 + 0, 0.50, 0.0, 0.56)
        s.hit("stomp", b0 + 2, 0.38, 0.0, 0.50)
        s.hit("tamb", b0 + 1.5, 0.30, -0.45, 0.40)
        s.hit("tamb", b0 + 3.5, 0.26, 0.45, 0.36)
        if bar % 4 == 3:
            s.hit("woodblock", b0 + 3.75, 0.34, 0.35, 0.42,
                  f=1180.0 + 45.0 * (bar % 8))

    for b, d, m in RALLY_MELODY:
        vel = 0.78 + (0.10 if (b % 4) == 0 else 0.0) + (0.05 if d >= 1.0 else 0.0)
        s.note("fiddle", b, d, m, min(vel, 0.97), 0.30, 1.0, bow=0.26, vib=0.009)

    ir = ir_room(0.80, rng(6601), decay=0.23, bright=4300.0, predelay=0.011)
    return master(s.buf, carve=True, rev=(ir, 0.18), air=1.20, drive=1.85)


# --------------------------------------------------------------------------
# BLOOM RUSH - 16 bars, 128 BPM, D MIXOLYDIAN.  Stage three, and the last
# thing before the judges: four gardeners painting one walled arena at night.
#
# Same 64-beat grid as the round and the rally, same band, same carve.  Three
# things separate it, and each is a fact about the arrangement rather than a
# fader:
#
#   * the mode.  Same tonic as the other two stages, but the seventh is flat
#     (C natural, not C#).  See BLOOM_CHORDS.
#   * two chords a bar, where the round has one every two bars and the rally
#     has one a bar.  Stage three is the one that never stops.
#   * the glockenspiel, which appears in no other cue with a mower in it.
#     See make_bloom.
# --------------------------------------------------------------------------

# (first half of the bar, second half).  D Mixolydian: D Em F#dim G Am Bm C,
# every one of which is already in CHORD - the mode costs no new vocabulary,
# only the discipline of never writing an A major or a C#.
#
# The flat seventh is doing the work here.  A major key has a leading note,
# which is a promise that the phrase is going to land; Mixolydian has none, so
# the harmony can drive for thirty seconds without ever arriving.  That is
# exactly the brief for a stage with no serve, no rally and no downtime - and
# it is also, incidentally, what a lantern-lit barn dance sounds like rather
# than an afternoon garden fete, which is the other half of the brief.
#
# The pairs rock between neighbours rather than marching through a chart, so a
# chord change a second reads as ground swinging back and forth underfoot -
# which is the mechanic - instead of as a band changing key twice a bar.
#
# The loop turns over on C -> D, bVII -> I.  The round and the rally both turn
# over on A -> D, V -> I, a full stop that happens to be a loop point.  This
# one turns over on a push: the last bar is a whole bar of the flat seventh and
# the resolution only ever happens ACROSS the seam, so the tune is never once
# at rest inside its own thirty seconds.
BLOOM_CHORDS = [
    ("D", "C"), ("G", "D"), ("D", "C"), ("G", "Am"),
    ("Bm", "Am"), ("G", "D"), ("Em", "C"), ("G", "D"),
    ("D", "Em"), ("C", "G"), ("Bm", "Am"), ("G", "C"),
    ("D", "C"), ("G", "D"), ("Em", "Am"), ("C", "C"),
]

# (beat, duration in beats, midi).  D Mixolydian throughout - MIDI 72 and 84
# appear all over it and 73/85 appear nowhere, which is the whole difference
# between this tune and the round's.
#
# It climbs higher than either of the others: the round tops out at 81, the
# rally at 85, and this reaches 88 at bar 10, on the Bm/Am pair at the middle
# of the last eight bars.  A finale that does not out-reach the stage before it
# is not a finale, and pitch is the cheapest way to say so that survives a
# player's laptop speakers.
#
# It ends on 72 - the flat seventh - a whole step under the 74 it opens on, so
# the wrap is the bVII -> I the whole cue is built around rather than a splice.
BLOOM_MELODY = [
    (0.0, .5, 74), (0.5, .5, 78), (1.0, .5, 81), (1.5, .5, 79), (2.0, 1.0, 84), (3.0, 1.0, 81),
    (4.0, .5, 79), (4.5, .5, 76), (5.0, 1.0, 74), (6.0, .5, 78), (6.5, .5, 74), (7.0, 1.0, 76),
    (8.0, .5, 74), (8.5, .5, 78), (9.0, .5, 81), (9.5, .5, 83), (10.0, 1.0, 84), (11.0, 1.0, 81),
    (12.0, .5, 79), (12.5, .5, 76), (13.0, 1.0, 74), (14.0, 1.5, 72), (15.5, .5, 74),
    (16.0, .5, 78), (16.5, .5, 74), (17.0, .5, 78), (17.5, .5, 81), (18.0, 1.0, 84), (19.0, 1.0, 81),
    (20.0, .5, 79), (20.5, .5, 83), (21.0, 1.0, 86), (22.0, 1.0, 83), (23.0, 1.0, 81),
    (24.0, .5, 79), (24.5, .5, 76), (25.0, .5, 79), (25.5, .5, 83), (26.0, 1.0, 84), (27.0, 1.0, 79),
    (28.0, .5, 78), (28.5, .5, 74), (29.0, .5, 78), (29.5, .5, 81), (30.0, 1.0, 79), (31.0, 1.0, 78),
    (32.0, .5, 74), (32.5, .5, 78), (33.0, .5, 81), (33.5, .5, 84), (34.0, 1.0, 86), (35.0, 1.0, 83),
    (36.0, .5, 84), (36.5, .5, 81), (37.0, .5, 79), (37.5, .5, 83), (38.0, 1.5, 86),
    (40.0, .5, 83), (40.5, .5, 86), (41.0, 1.0, 88), (42.0, 1.0, 86), (43.0, 1.0, 84),
    (44.0, .5, 83), (44.5, .5, 79), (45.0, .5, 83), (45.5, .5, 86), (46.0, 1.5, 84),
    (48.0, .5, 74), (48.5, .5, 78), (49.0, .5, 81), (49.5, .5, 79), (50.0, 1.0, 84), (51.0, 1.0, 81),
    (52.0, .5, 79), (52.5, .5, 76), (53.0, .5, 79), (53.5, .5, 81), (54.0, 1.5, 78),
    (56.0, .5, 76), (56.5, .5, 72), (57.0, .5, 76), (57.5, .5, 79), (58.0, 1.0, 81), (59.0, 1.0, 76),
    (60.0, .5, 72), (60.5, .5, 76), (61.0, .5, 79), (61.5, .5, 76), (62.0, 1.0, 74), (63.0, 1.0, 72),
]

# Root/fifth, identical to ROUND_BASS/RALLY_BASS wherever the chords overlap,
# and every entry at MIDI 55 (196 Hz) or above so the engine carve still finds
# nothing of this arrangement to cut.
BLOOM_BASS = {"D": (62, 57), "C": (60, 67), "G": (55, 62),
              "Am": (57, 64), "Em": (64, 59), "Bm": (59, 66)}


def bloom_approach(nxt_root):
    """
    The eighth note that walks into the next bar.

    A step below the target is the note that wants to be there, and it is what
    the rally plays - but when the target is G (MIDI 55, the bottom of the
    band's bass register) a step below is MIDI 53, 174.6 Hz, sitting in the
    middle of the 60-160 Hz shoulder the carve exists to keep empty.  So the
    approach flips to a step ABOVE when the register runs out, which is still a
    stepwise approach and is still a note this bass sounds like an upright
    playing.  Transposing it up an octave instead, which is what BLOOM_BASS
    does for its roots, would turn a step into a descending seventh.
    """
    return nxt_root - 2 if nxt_root - 2 >= 55 else nxt_root + 2


def make_bloom():
    """
    Stage three: Bloom Rush.  Night, four mowers, one walled arena, 75 s.

    Built on the round's grid and the rally's escalation, then pushed in the
    four places where "the biggest of the three" and "at night" are things a
    listener can actually hear:

      * **The glockenspiel.**  It has appeared twice in this game, both times
        with nothing at stake - the reveal's sparkle and the storybook's tune.
        It has never once played over a running mower.  Here it rings out over
        every bar, high and sparse and well down in the mix, because this is
        the only level played after dark and the claimed ground glows: the
        lanterns needed an instrument and the band already had one.  It is two
        notes a bar at most, so it is a light in the distance rather than a
        counter-melody arguing with the fiddle.
      * **Four stomps a bar.**  The round puts the boot down on 1 and 3, the
        rally likewise; this one is on every beat, which is the urgent layer's
        trick and the single loudest statement of "final stage" available at
        215 Hz, i.e. without going anywhere near the engine.
      * **Two chords a bar, changing on the half-bar.**  The bass follows the
        change rather than pedalling through it, so the floor moves twice as
        often as the rally's.  Possession in this stage swings; the harmony
        swings with it.
      * **A roll under every bar AND the climbing figure at every phrase
        end.**  The rally alternates the two, a bar of each.  Here the roll
        never stops and the figure is laid over the top of it at bars 3, 7, 11
        and 15 - two banjos, panned apart, which is a band getting bigger
        rather than a part getting busier.

    What it deliberately does NOT take from the urgent layer is the fiddle
    tremolo, for the same reason make_rally does not: it is a tension texture,
    and this stage is a garden party being contested rather than a chase.  The
    energy here is celebratory.  Nothing in it should make a player feel they
    are running out of time - the arena has a clock of its own for that.
    """
    s = Seq(128, 64, loop=True, seed=99)

    for bar, (ch_a, ch_b) in enumerate(BLOOM_CHORDS):
        b0 = bar * 4
        nxt_a = BLOOM_CHORDS[(bar + 1) % 16][0]

        # squeezebox holds each half-bar.  Two shorter chords rather than one
        # long one is most of what makes the harmonic rhythm audible at all -
        # a pad that never lets go just sounds like the bass changed.
        s.chord("melodeon", b0 + 0, 1.9, [m + 12 for m in CHORD[ch_a]],
                vel=0.24, spread=0.40, gain=0.42, strum=0.008)
        s.chord("melodeon", b0 + 2, 1.9, [m + 12 for m in CHORD[ch_b]],
                vel=0.24, spread=0.40, gain=0.42, strum=0.008)

        ra, fa = BLOOM_BASS[ch_a]
        rb, fb = BLOOM_BASS[ch_b]
        s.note("bass", b0 + 0.0, 0.45, ra, 0.88, 0.0, 1.00)
        s.note("bass", b0 + 0.5, 0.40, fa, 0.56, 0.0, 0.76)
        s.note("bass", b0 + 1.0, 0.45, ra, 0.80, 0.0, 0.94)
        s.note("bass", b0 + 1.5, 0.40, fa, 0.52, 0.0, 0.72)
        s.note("bass", b0 + 2.0, 0.45, rb, 0.84, 0.0, 0.96)
        s.note("bass", b0 + 2.5, 0.40, fb, 0.54, 0.0, 0.74)
        s.note("bass", b0 + 3.0, 0.45, rb, 0.78, 0.0, 0.90)
        s.note("bass", b0 + 3.5, 0.45, bloom_approach(BLOOM_BASS[nxt_a][0]),
               0.60, 0.0, 0.82)

        # banjo one: the roll, every bar, over the bar's first chord.  The roll
        # is a bar-length figure and the chord moves inside the bar, so it has
        # to pick a side; the downbeat harmony is the one a roll belongs to.
        banjo_bar(s, bar, ch_a, drone=69, pat_idx=bar + 5, vel=0.66,
                  p=-0.38, lo=64, hi=83)

        if bar % 4 == 3:
            # banjo two, right of centre: the urgent layer's climbing figure,
            # on the second chord of the phrase-ending bar.  Four bars apart it
            # is a lift into the next phrase rather than a texture, and it is
            # the same shape the rally borrowed - the three stages are meant to
            # sound like one band with one bag of tricks.
            tri_ = CHORD[ch_b]
            fig = [tri_[0] + 24, tri_[1] + 24, tri_[2] + 24, tri_[0] + 36]
            for i, m in enumerate(fig):
                s.note("banjo", b0 + 2.0 + i * 0.5, 0.5, m, 0.44 + 0.05 * i,
                       0.34, 0.52)

        # the kit.  Boot on all four, brush on 2 and 4, sixteenth shaker, and
        # the tambourine on the two off-beats where a dancer would clap.
        for beat in range(4):
            s.hit("stomp", b0 + beat, 0.54 if beat % 2 == 0 else 0.38, 0.0,
                  0.58 if beat == 0 else 0.50)
        for beat in (1, 3):
            s.hit("brush", b0 + beat, 0.44, 0.30, 0.56)
        for e in range(16):
            s.hit("shaker", b0 + e * 0.25, 0.27 if e % 4 == 0 else 0.15,
                  0.52, 0.52)
        s.hit("tamb", b0 + 1.5, 0.32, -0.45, 0.42)
        s.hit("tamb", b0 + 3.5, 0.28, 0.45, 0.38)
        if bar % 4 == 3:
            # a two-tick woodblock flourish at the phrase line, climbing a
            # little each phrase so four repeats of the loop never tick the
            # same way twice in a row
            s.hit("woodblock", b0 + 3.50, 0.34, -0.35, 0.42,
                  f=1180.0 + 60.0 * (bar % 8))
            s.hit("woodblock", b0 + 3.75, 0.28, 0.35, 0.38,
                  f=1420.0 + 60.0 * (bar % 8))

        # the lanterns.  Two octaves above the tune, long, quiet, and rung on
        # the downbeat of every bar with an answer on the second chord of the
        # odd ones - so the ear hears a light every second beat rather than a
        # part it has to follow.
        pool = voicing(ch_a, 84, 96)
        s.note("glock", b0 + 0.0, 2.0, pool[bar % len(pool)], 0.34, 0.22, 0.46)
        if bar % 2 == 1:
            pool_b = voicing(ch_b, 84, 96)
            s.note("glock", b0 + 2.5, 1.5, pool_b[(bar + 2) % len(pool_b)],
                   0.26, -0.20, 0.38)

    # fiddle carries the tune, as it does in the round and the rally.  A touch
    # more bow noise and a touch more vibrato than either: outdoors at night in
    # a stone-walled arena, played hard.
    for b, d, m in BLOOM_MELODY:
        vel = 0.80 + (0.10 if (b % 4) == 0 else 0.0) + (0.05 if d >= 1.0 else 0.0)
        s.note("fiddle", b, d, m, min(vel, 0.98), 0.28, 1.0, bow=0.28, vib=0.011)

    # The biggest room any cue with a mower in it gets: 1.35 s against the
    # round's 0.9 and the rally's 0.80.  The other two stages are played in an
    # open field, this one inside a wall, and length is what a listener hears
    # as size.  It is NOT darker, which was the first guess and was wrong twice
    # over - a stone wall is a harder reflector than a hedge, and see below for
    # the measurement that settled it.
    #
    # The send is 0.20 and the drive 1.80, both a touch UNDER the rally's,
    # which looks backwards for the loudest cue in the set until you look at
    # where master() puts the reverb: the carve is a filter, the room is a
    # convolution AFTER it, and a wet, dark, hard-driven bus therefore refills
    # some of the hole the carve just dug.  Measured on this arrangement, going
    # from (1.35 s / decay 0.38 / 3600 Hz / send 0.24 / drive 1.90) to what is
    # written here moved the 60-160 Hz share from 0.019 % to 0.014 % - i.e. a
    # third of the energy sitting in the mower's octave was the room and the
    # saturator, not a single note anybody wrote.  The arrangement itself
    # measures 0.049 % pre-bus, marginally CLEANER than the round's 0.052 %.
    ir = ir_room(1.35, rng(9901), decay=0.32, bright=4000.0, predelay=0.016)
    return master(s.buf, carve=True, rev=(ir, 0.20), air=1.05, drive=1.80)


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
# CUTSCENE BED - 12 bars, 80 BPM, key of G.  Under the opening story page.
# --------------------------------------------------------------------------

# G - Em - C - G / Am - D - Em - C / G - Em - Am - D.
#
# The menu's 12-bar form and the menu's key, deliberately: the story page and
# the menu are the two beats where nothing is at stake, and a listener should
# hear them as the same place.  What makes this one wistful rather than jaunty
# is that the tune spends most of its time on the relative minor - Em and Am
# take five of the twelve bars, and the loop turns around on D rather than
# landing on G, so it never quite arrives.
CUTSCENE_CHORDS = ["G", "Em", "C", "G", "Am", "D", "Em", "C",
                   "G", "Em", "Am", "D"]

# (beat, duration in beats, midi).  Long notes, two-bar phrases, one octave
# lift at bar 9 and a settle back - a tune somebody could hum, because the
# panels it plays under are a story being told rather than an event.
CUTSCENE_MELODY = [
    (0.0, 1.5, 74), (1.5, 0.5, 76), (2.0, 2.0, 79),
    (4.0, 1.0, 78), (5.0, 1.0, 76), (6.0, 2.0, 71),
    (8.0, 1.0, 72), (9.0, 1.0, 76), (10.0, 2.0, 79),
    (12.0, 1.5, 78), (13.5, 0.5, 76), (14.0, 2.0, 74),
    (16.0, 1.0, 69), (17.0, 1.0, 72), (18.0, 2.0, 76),
    (20.0, 1.0, 78), (21.0, 1.0, 74), (22.0, 2.0, 69),
    (24.0, 1.0, 71), (25.0, 1.0, 74), (26.0, 2.0, 76),
    (28.0, 1.5, 72), (29.5, 0.5, 74), (30.0, 2.0, 76),
    (32.0, 1.0, 79), (33.0, 1.0, 83), (34.0, 2.0, 86),
    (36.0, 1.5, 83), (37.5, 0.5, 81), (38.0, 2.0, 79),
    (40.0, 1.0, 76), (41.0, 1.0, 72), (42.0, 2.0, 69),
    (44.0, 1.0, 71), (45.0, 1.0, 74), (46.0, 2.0, 78),
    # ...and 78 is the leading note, so the wrap back to bar 1 is a cadence
    # rather than a splice.  The glock's 1.6 s tail is wrap-added on top of it.
]

# A slow counter-line that only exists in the second half, so the loop has a
# shape.  Thirds and sixths under the tune, never above it.
CUTSCENE_COUNTER = [
    (24.0, 2.0, 62), (26.0, 2.0, 64),
    (28.0, 2.0, 67), (30.0, 2.0, 64),
    (32.0, 2.0, 71), (34.0, 2.0, 74),
    (36.0, 2.0, 71), (38.0, 2.0, 67),
    (40.0, 2.0, 64), (42.0, 2.0, 60),
    (44.0, 2.0, 62), (46.0, 2.0, 66),
]

# root / fifth, all voiced at 196 Hz (MIDI 55) or above, like the rest of the
# band's bass parts.  Not for the engine carve - no engine runs under this cue
# - but because the pizzicato upright is written for that register and sounds
# like a different instrument below it.
CUTSCENE_BASS = {"G": (55, 62), "Em": (64, 59), "C": (60, 67),
                 "Am": (57, 64), "D": (62, 57)}


def make_cutscene():
    """
    The bed under the opening story page.

    Three rules, all of them consequences of the fact that a narrator is
    talking over this:

      * **No percussion at all.**  Every other cue in the game has a pulse; a
        pulse under speech turns the reading into a rap.  The tempo is carried
        by the harmony changing once a bar and by nothing else.
      * **The speech band is carved**, exactly as the round loop carves the
        engine band, at `master(voice=True)`.  I tried to do it by arrangement
        alone first - tune two octaves above the voice, pad below it - and then
        measured it, which is the only reason I know it did not work: 70 % of
        the cue's energy was still in 300-3000 Hz, within a few points of the
        menu tune.  A squeezebox chord voiced at MIDI 67-86 *is* the speech
        band, whatever the melody is doing.  With the two shallow dips it sits
        at **58 %**, against 77 % for the menu and 85 % for the round loop.
      * **Long notes.**  Fast material reads as competition for attention.
        Nothing here is shorter than a half note except the two pickups.

    First attempt used the banjo rolling under all twelve bars, as the menu
    does, and it was wrong for a reason worth writing down: a roll is eight
    plucks a bar, which is a pulse whatever its velocity is, and it made the
    page sound like it was waiting for the round to start.  The banjo is still
    here - it is what makes this the same band - but two picked notes a bar,
    quiet and off to one side.
    """
    s = Seq(80, 48, loop=True, seed=88)

    for bar, ch in enumerate(CUTSCENE_CHORDS):
        b0 = bar * 4
        tri_ = CHORD[ch]

        # squeezebox pad: the whole harmony, soft, held nearly the full bar
        s.chord("melodeon", b0, 3.85, [m + 12 for m in tri_],
                vel=0.22, spread=0.32, gain=0.46, strum=0.014)

        root, fifth = CUTSCENE_BASS[ch]
        s.note("bass", b0, 2.4, root, 0.62, 0.0, 0.80)
        s.note("bass", b0 + 2.5, 1.4, fifth, 0.40, 0.0, 0.60)

        # two picked banjo notes a bar, left of centre and well down in the
        # mix: presence, not a groove
        pool = voicing(ch, 62, 76)
        s.note("banjo", b0 + 1.5, 1.0, pool[len(pool) // 2], 0.34, -0.44, 0.44)
        s.note("banjo", b0 + 3.0, 1.0, pool[0], 0.28, -0.38, 0.38)
        if bar % 4 == 3:
            # one grace note into every fourth bar line, so the accompaniment
            # is not literally identical twelve times over
            s.note("banjo", b0 + 3.75, 0.25, pool[-1] + 12, 0.26, -0.30, 0.34)

    # the music box carries the tune, slightly right of centre
    for b, d, m in CUTSCENE_MELODY:
        vel = 0.52 + (0.10 if d >= 2.0 else 0.0)
        s.note("glock", b, d, m, vel, 0.18, 0.62)

    # and the fiddle answers it from bar 7 on, quietly
    for b, d, m in CUTSCENE_COUNTER:
        s.note("fiddle", b, d, m, 0.34, -0.24, 0.46, bow=0.16, vib=0.008)

    # A bigger, darker room than any other cue uses.  This one is a memory, and
    # a memory should not sound like it is in the same field as the mower.
    ir = ir_room(1.9, rng(8801), decay=0.52, bright=3400.0, predelay=0.024)
    return master(s.buf, carve=False, voice=True, rev=(ir, 0.34), air=0.85,
                  drive=1.05)


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

    ra = make_rally()
    write(out("Music", "music_rally_loop.wav"), ra)
    info.append(verify_carve(ra, "rally"))

    bl = make_bloom()
    write(out("Music", "music_bloom_loop.wav"), bl)
    info.append(verify_carve(bl, "bloom"))
    # Not because anything plays the three together - nothing does - but
    # because "the three stage loops are the same 16 bars at 128 BPM" is a
    # claim made in AUDIO_SPEC and in every comment above, and an off-by-one
    # in a melody table is exactly how it would quietly stop being true.
    assert rl.shape == ra.shape == bl.shape, \
        "round, rally and bloom must all be 16 bars at 128 BPM"

    write(out("Music", "music_reveal.wav"), make_reveal(), fade_out=0.05)
    write(out("Music", "music_judging_bed_loop.wav"), make_judging())
    write(out("Music", "music_cutscene_loop.wav"), make_cutscene())
    write(out("Music", "fanfare_good.wav"), make_fanfare_good(), fade_out=0.04)
    write(out("Music", "fanfare_bad.wav"), make_fanfare_bad(), fade_out=0.05)
    return info


if __name__ == "__main__":
    for d in render():
        print(f"{d['label']:8s} 60-160 Hz = {d['band_frac_pct']:6.3f}% of total, "
              f"{d['band_vs_200_800_db']:+6.1f} dB vs 200-800 Hz")
