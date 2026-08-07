"""
Turn the measured loudness of every clip into a mixer plan.

The files are all peak-normalised to -1.5 dBFS as the spec requires, which
means their *perceived* levels are all over the place - a klaxon at -1.5 dBFS
peak is far louder than a wooden click at -1.5 dBFS peak.  This script states
the intended in-game loudness for each clip and prints the trim, in dB, that
gets it there.

    python levels.py            # the table that goes into AUDIO_SPEC.md
    python levels.py --md       # markdown rows
    python levels.py --spec     # section 4 of Docs/AUDIO_SPEC.md, verbatim

TARGET_RMS is the loudness each clip should have at the listener with the
master fader at 0 dB and no attenuation from 3D falloff.  It is the only
hand-authored number here; everything else is measured.
"""

from __future__ import annotations

import sys
import os
import analyze

# clip name -> intended RMS at the listener, dBFS
TARGET_RMS = {
    # --- engine: heard for 75 s, so it sits below the music's peaks -------
    "engine_idle_loop": -19.0, "engine_mid_loop": -19.0, "engine_high_loop": -19.0,
    "engine_start": -19.0, "engine_stop": -19.0,
    # --- blade -----------------------------------------------------------
    "blade_loop": -25.0, "blade_cut_grass_loop": -21.0,
    "blade_engage": -19.0, "blade_disengage": -19.0,
    "debris_ping_01": -22.0, "debris_ping_02": -22.0,
    "debris_ping_03": -22.0, "debris_ping_04": -22.0,
    # --- mower -----------------------------------------------------------
    "bonk_01": -14.0, "bonk_02": -14.0, "bonk_03": -14.0,
    "horn": -14.0, "drift_loop": -22.0,
    "boost_start": -16.0, "boost_loop": -20.0, "boost_end": -16.0,
    "suspension_bump_01": -24.0, "suspension_bump_02": -24.0,
    "suspension_bump_03": -24.0,
    # --- UI --------------------------------------------------------------
    "countdown_beep": -15.0, "countdown_go": -13.0, "klaxon": -11.0,
    "ui_click": -20.0, "ui_hover": -26.0, "ui_confirm": -18.0,
    "ui_back": -20.0, "score_tick": -30.0,
    "card_flip": -19.0, "card_raise": -19.0, "stamp": -13.0,
    # --- crowd -----------------------------------------------------------
    "crowd_ambient_loop": -32.0, "crowd_cheer_small": -18.0,
    "crowd_cheer_big": -15.0, "crowd_gasp": -19.0, "crowd_aww": -19.0,
    "crowd_laugh": -18.0, "applause_loop": -19.0,
    # --- ambience --------------------------------------------------------
    "birds_loop": -30.0, "wind_grass_loop": -33.0, "pond_loop": -33.0,
    "windmill_creak": -26.0,
    # --- duck ------------------------------------------------------------
    "quack_happy": -15.0, "quack_annoyed": -15.0,
    "quack_panic": -15.0, "quack_proud": -15.0,
    # --- judges ----------------------------------------------------------
    "judge_goat_low": -15.0, "judge_goat_high": -15.0,
    "judge_badger_low": -15.0, "judge_badger_high": -15.0,
    "judge_heron_low": -15.0, "judge_heron_high": -15.0,
    # --- geese (stage two) -----------------------------------------------
    # A notch under the duck and the judges: there is ONE duck and three judges,
    # and up to NINE geese calling every 2-5 s.  The same per-clip loudness that
    # is right for a soloist is a wall of birds for a flock.
    "goose_honk_1": -16.0, "goose_honk_2": -16.0, "goose_honk_3": -16.0,
    # The quietest thing in the game after the ambience beds, and it has to be.
    # Up to ~63 strokes a second across the flock; the flock test in
    # Art/Python/audio measures those summing to about +4 dB over one stroke,
    # which lands the whole flock ~9 dB under the engine where it belongs.
    "goose_wingbeat_1": -32.0, "goose_wingbeat_2": -32.0,
    "goose_wingbeat_3": -32.0,
    # A tell, not an event: it fires before every commit, and shaped 1-6 kHz
    # noise reads much louder than its RMS.
    "goose_hiss": -20.0,
    # The payoff.  One per elimination, so it can sit with the bonks.
    "goose_squawk": -14.0,
    # --- music -----------------------------------------------------------
    "music_menu_loop": -19.0, "music_round_loop": -22.0,
    "music_round_urgent_layer": -24.0, "music_reveal": -15.0,
    "music_judging_bed_loop": -23.0,
    # Quieter than anything else in Music/: it is the only cue with a voice on
    # top of it, and a bed that competes with the narration is a bed that has
    # to be turned down in the field anyway.
    "music_cutscene_loop": -27.0,
    # A notch hotter than the round loop it sits next to in the same slot -
    # stage two is the step up in energy, and the target has to say so or the
    # arrangement's extra drive gets erased by the mixer plan on the way out.
    "music_rally_loop": -21.0,
    # One more notch again.  Round -22, rally -21, bloom -20.5: the three stage
    # loops get louder in the order they are played, because "this is the
    # biggest of the three" is written into the arrangement and the mixer plan
    # has to agree with it or the target quietly cancels the writing.  Half a
    # dB rather than a full one - the arrangement is doing the work, and this
    # cue already carries four stomps a bar over a mower.
    "music_bloom_loop": -20.5,
    "fanfare_good": -14.0, "fanfare_bad": -14.0,
    # --- transition ------------------------------------------------------
    # These play over a cut, with the world audio already ducked, so they can
    # be louder than the same gesture would be in the field.  The ladder is
    # the whole point of the folder: the two wipes and the riser sit under the
    # thing they are pointing at, and the impact/fanfare land on top of it.
    "transition_leaf_sweep": -18.0, "transition_banner_whoosh": -17.0,
    # A bed with a peak on its last sample: targeted low so the level it
    # *arrives* at is the one that reads, not the average of the ramp.
    "transition_riser": -20.0,
    "transition_impact": -13.0,
    # Small sting and its escalated sibling, three dB apart - the arrangement
    # carries the rest of the step up.
    "transition_stamp_small": -16.0, "transition_fanfare_big": -13.0,
    # Level-matched to crowd_cheer_small/big so a swell can hand straight over
    # to crowd_ambient_loop without a jump.
    "transition_crowd_swell": -17.0,
    # Diegetic and positional: it gets 3D falloff on top of this.
    "transition_gate_creak": -20.0,
}


LAST_SHIFT = 0.0


def table():
    """
    Returns the per-clip trims, shifted so the hottest clip lands on exactly
    1.0.  `AudioSource.volume` is clamped to [0, 1] in Unity, and the very
    transient clips (the stamp, GO, the big cheer) have a low RMS at -1.5 dBFS
    peak, so they need the most gain - everything is referenced to them.  The
    shift is uniform, so the *relative* balance is exactly as intended; put the
    shift back on the AudioMixer master to restore absolute loudness.
    """
    rows = analyze.report(verbose=False)
    raw = []
    for m in rows:
        t = TARGET_RMS.get(m["name"])
        if t is None:
            continue
        raw.append((m, t, t - m["rms_dbfs"]))

    shift = -max(tr for _, _, tr in raw)
    out = []
    for m, t, tr in raw:
        v = tr + shift
        out.append({
            "cat": os.path.basename(os.path.dirname(m["path"])),
            "name": m["name"],
            "file_rms": m["rms_dbfs"],
            "target_rms": t,
            "trim_db": v,
            "volume01": 10.0 ** (v / 20.0),
            "loop": m["is_loop"],
            "dur": m["dur"],
            "ch": m["ch"],
        })
    missing = set(TARGET_RMS) - {m["name"] for m in rows}
    if missing:
        print("WARNING: no rendered file for", sorted(missing))
    global LAST_SHIFT
    LAST_SHIFT = shift
    return out


def main():
    if "--spec" in sys.argv:
        print(spec_section())
        return
    rows = table()
    print(f"# master headroom shift {LAST_SHIFT:+.1f} dB "
          f"(put +{-LAST_SHIFT:.1f} dB on the AudioMixer master)")
    md = "--md" in sys.argv
    if md:
        print("| clip | s | ch | loop | file RMS | target RMS | trim dB | "
              "AudioSource volume |")
        print("|---|---|---|---|---|---|---|---|")
        for r in rows:
            print(f"| `{r['cat']}/{r['name']}.wav` | {r['dur']:.2f} | {r['ch']} | "
                  f"{'yes' if r['loop'] else 'no'} | {r['file_rms']:.1f} | "
                  f"{r['target_rms']:.1f} | {r['trim_db']:+.1f} | "
                  f"{r['volume01']:.3f} |")
    else:
        cat = None
        for r in rows:
            if r["cat"] != cat:
                cat = r["cat"]
                print(f"\n[{cat}]")
            print(f"  {r['name']:<28} file {r['file_rms']:6.1f}  target "
                  f"{r['target_rms']:6.1f}  trim {r['trim_db']:+6.1f} dB  "
                  f"volume {r['volume01']:.3f}")


# --------------------------------------------------------------------------
# AUDIO_SPEC.md section 4 generator
#
#   python levels.py --spec
#
# so the clip tables in Docs/AUDIO_SPEC.md can be regenerated from measurement
# after a re-render instead of being maintained by hand.
# --------------------------------------------------------------------------

PURPOSE = {
    "engine_idle_loop": "idle putter, 28 Hz base (1 680 RPM)",
    "engine_mid_loop": "52 Hz base (3 120 RPM), more bite",
    "engine_high_loop": "95 Hz base (5 700 RPM), opened up",
    "engine_start": "pull cord: two failed coughs, then it fires",
    "engine_stop": "dies with a wheeze and one last cough",
    "blade_loop": "deck whine + 208 Hz chop; on whenever the blade spins",
    "blade_cut_grass_loop": "wet shredding, **added only over uncut grass** - the reward layer",
    "blade_engage": "clutch clunk + belt take-up + spin-up",
    "blade_disengage": "clunk + spin-down + settling thunk",
    "debris_ping_01": "stone off the deck, variation 1",
    "debris_ping_02": "stone off the deck, variation 2",
    "debris_ping_03": "stone off the deck, variation 3",
    "debris_ping_04": "stone off the deck, variation 4",
    "bonk_01": "collision: wood+metal impact then a wobbling boing",
    "bonk_02": "collision, lower and softer",
    "bonk_03": "collision, higher and more metallic",
    "horn": "the silly parp",
    "drift_loop": "grass slide - broad spectrum, no screech peak",
    "boost_start": "whoosh + turbo whistle that overshoots (the over-tune joke)",
    "boost_loop": "roar + 2 646 Hz whine + flutter",
    "boost_end": "spin-down + wastegate blow-off + deflating boing",
    "suspension_bump_01": "soft chassis knock, low",
    "suspension_bump_02": "soft chassis knock, tighter",
    "suspension_bump_03": "soft chassis knock, deepest",
    "countdown_beep": "3, 2, 1 - marimba-class bar at A5, not a sine",
    "countdown_go": "GO - D major bar chord + tambourine",
    "klaxon": "time up: fairground PA two-tone (415 + 622 Hz)",
    "ui_click": "wooden tick",
    "ui_hover": "softer, higher, shorter",
    "ui_confirm": "two wooden notes up (F5 -> C6)",
    "ui_back": "two wooden notes down (A5 -> D5)",
    "score_tick": "per-point counter - **28 ms, nothing below 950 Hz**",
    "card_flip": "scorecard turning, landing on the frame",
    "card_raise": "card sliding up, locking with a clack",
    "stamp": "the rank letter landing",
    "crowd_ambient_loop": "whole-round bed: murmur + 8 animals",
    "crowd_cheer_small": "modest score reaction",
    "crowd_cheer_big": "big score reaction",
    "crowd_gasp": "collective inhale, then hush",
    "crowd_aww": "descending sympathetic aww",
    "crowd_laugh": "ha-ha-ha + a goose and a goat losing it",
    "applause_loop": "13 clappers, seamless",
    "birds_loop": "7 distinct species, 9 calls over 12 s",
    "wind_grass_loop": "gusts that move level and brightness together",
    "pond_loop": "laps, 3 drips, one resident duck",
    "windmill_creak": "stick-slip wood under load - **mono, it is positional**",
    "quack_happy": "two bright quacks, the second lifts",
    "quack_annoyed": "one long flat quack that sags, plus a grumble",
    "quack_panic": "four accelerating rising quacks + a gasp",
    "quack_proud": "one long *rising* held quack + bill clatter",
    "judge_goat_low": "Mildred, dismissive: drops a fourth, cut off, then chews",
    "judge_goat_high": "Mildred, grudging: two bleats, the second lifts",
    "judge_badger_low": "Boris, deflated: one chuff sinking into a grumble",
    "judge_badger_high": "Boris, delighted: four rising chuffs, a bark, applause",
    "judge_heron_low": "Priscilla: a flat croak with no inflection at all",
    "judge_heron_high": "Priscilla: two croaks, the second barely lifts",
    "goose_honk_1": "the standard call: 190 Hz, snaps on, holds, sags",
    "goose_honk_2": "shorter and higher (232 Hz), cut off - the bird is already moving",
    "goose_honk_3": "the low rude one: 158 Hz in two syllables, a grunt then a honk",
    "goose_wingbeat_1": "one downstroke, mid - the default",
    "goose_wingbeat_2": "one downstroke, shorter and higher - a wing at rate",
    "goose_wingbeat_3": "one downstroke, heavier - a big stroke on take-off",
    "goose_hiss": "threat display: shaped air, sibilance rising 780 -> 3 400 Hz, **no pitch**",
    "goose_squawk": "hit by the mower: a shriek, a register break, then a fall to 118 Hz",
    "music_menu_loop": "menu / briefing",
    "music_round_loop": "the 75 s round",
    "music_round_urgent_layer": "additive; fade in over the last 15 s",
    "music_reveal": "overhead reveal ta-daa (one-shot)",
    "music_judging_bed_loop": "under the judges",
    "music_cutscene_loop": "under the opening story page - **no percussion**, sits below the narration",
    "music_rally_loop": "stage two: the goose rally - same key/tempo as the round, a step up in drive",
    "music_bloom_loop": "stage three: Bloom Rush at night - same tempo and tonic, D Mixolydian, two chords a bar, glockenspiel",
    "fanfare_good": "verdict, good",
    "fanfare_bad": "verdict, deflating",
    "transition_leaf_sweep": "foliage wipe: moving band + doppler + leaf crackle, **L -> R**",
    "transition_banner_whoosh": "banner sliding across the lens: canvas, slaps, one rope creak, **R -> L**",
    "transition_riser": "1.6 s tension bed that **peaks on its own last sample** - schedule it to land on the cut",
    "transition_impact": "the downbeat on the cut: wooden plank + tin-tray bloom + canvas room",
    "transition_stamp_small": "early-transition sting: three plucks up a G triad",
    "transition_fanfare_big": "final stage entrance / into judging: cornets, IV -> V -> I, crowd underneath",
    "transition_crowd_swell": "murmur -> cheer peak -> back to a murmur, built from `crowd.py`'s voices",
    "transition_gate_creak": "arena gate opening, latch at 0.78 s - **mono, it is positional**",
}

SPEC_ORDER = ["Engine", "Blade", "Mower", "UI", "Crowd", "Ambience", "Duck",
              "Judges", "Geese", "Music", "Transition"]

SPEC_HEAD = {
    "Engine": "### Engine/ - heard for 75 s straight, so it sits under the music's peaks",
    "Blade": "### Blade/ - gated on while cutting",
    "Mower": "### Mower/",
    "UI": "### UI/",
    "Crowd": "### Crowd/",
    "Ambience": "### Ambience/",
    "Duck": "### Duck/",
    "Judges": "### Judges/ - one set per judge, `low` for a bad score, `high` for a good one",
    "Geese": "### Geese/ - stage two, the Goose Rally. All one-shots, nine sources at once",
    "Music": "### Music/",
    "Transition": "### Transition/ - the eight cues that cover a cut. All one-shots",
}

SPEC_NOTE = {
    "Engine": "Then apply the layer trims from **2.5** and the handover trims from **2.6** on top.",
    "Blade": "Drive `blade_cut_grass_loop`'s volume from the same *uncut-grass-under-the-deck*\nvalue that fires the clipping particles (TECH_DESIGN 3.4). That is what makes it\nread as a reward. Round-robin the four `debris_ping` variations.",
    "Mower": "Modulate `drift_loop` volume with lateral slip speed and `boost_loop` with\nremaining boost fuel.",
    "UI": "`score_tick` is designed for 30 retriggers a second: 28 ms long, high-passed at\n950 Hz so repeated ticks cannot pile into mud, and short enough that consecutive\nticks never overlap. Add +-2 semitones of random pitch in Unity if you want it to\nstop sounding mechanical.",
    "Crowd": "The bed carries a sheep, a goose, a quack, a goat, a cow, a goose, a quack and a\nsheep in that order over its 8 s, all low-passed at 2.6 kHz for distance.",
    "Ambience": "Bird species: two-note whistle, chirp cluster, five-note warble, wood-pigeon coo,\ntrill, peep, and a hedgerow caw, placed at 0.35 / 1.90 / 3.10 / 5.05 / 6.85 /\n8.20 / 8.95 / 10.40 / 11.35 s with per-call distance filtering, so the 12 s\nrepeat is hard to hear. Fire `windmill_creak` on a randomised 20-40 s timer at\nthe windmill's transform.",
    "Duck": "The quack is a very narrow glottal pulse (width 0.145) through a bill that opens\nand then closes: F1 sweeps 640 -> 1070 -> 700 Hz and F2 falls 2250 -> 1420 Hz\nacross the note. Emotion is carried only by syllable count/rate, pitch direction\nand roughness - the vowel gesture stays the same, which is why they read as the\nsame duck.",
    "Judges": "Species separation is carried by three orthogonal parameters, not by the vowel:\ngoat = 245 Hz buzz with a hard **23 Hz bleat tremolo**; badger = 120 Hz and\nmostly **breath** (noise 0.55-0.62) in several bursts; heron = **88 Hz** with a\nvery narrow pulse (width 0.13) and heavy roughness.",
    "Geese": "Nothing in this folder loops, so the loop-seam gates do not apply to any of it.\n\nThe goose is kept off the duck by pitch centre and by nasality, not by the\nvowel: the honks sit at **158 / 190 / 232 Hz** against the duck's 250-480, carry\nthree times the breath, and have a **nasal anti-resonance notched out at\n940-1150 Hz** - which is why 73-79 % of a honk's energy lands in 320-640 Hz\nwhile every quack in `Duck/` puts 60-71 % in 640-1280 Hz. Round-robin the three\nhonks and the three wing beats; `RallyFX.Pick` already does.\n\nThe wing beats are the constraint the folder is built around - up to ~63 strokes\na second across nine birds. Each one is a 13 ms air whump under a mid-band\ncanvas flap and a 6 ms leather snap, highpassed at 105 Hz so a flock cannot sum\nin the engine's octave. Measured, nine birds at 7 strokes/s come out **+3.8 dB**\nover one stroke, keep a **16.4 dB** crest (i.e. still individual strokes, not a\nwall) and land their energy at a **1 646 Hz** centroid against the engine's\n164 Hz, with 1.9 % of it in the engine's 80-160 Hz band.",
    "Music": "`music_reveal` is 6 s and lands its big chord at **2.4 s** - fire it as the camera\nstarts rising so the arrival coincides with the picture appearing.\n\n`music_cutscene_loop` is the only cue with a narrator on top of it, which is why it\nis the quietest thing in this folder and why it has no percussion at all. It is\nalso the only cue that plays while the rest of the game is muted - see\n`AudioDirector.WorldAudio`.\n\n`music_rally_loop` is the same 16 bars/128 BPM/30 s grid as `music_round_loop`,\ncarved the same way for the same reason - the engine is still running under it,\nsee GooseDefence/RallyDirector - but with a chord change every bar instead of\nevery two, an eighth-note bass and the urgent layer's climbing banjo figure\nworked in as a fill rather than a texture. It is stage two's own tune, not an\nadditive layer like `music_round_urgent_layer` - see AudioDirector.musicRally.\n\n`music_bloom_loop` is the same 16 bars/128 BPM/30 s grid again, carved again for\nthe same reason - Bloom Rush is four mowers in a walled arena, see Turf/ - and\nit is stage three's own tune, not a layer. Three things separate it from the\nother two, all of them arrangement rather than mix: it is in **D Mixolydian**,\nso the tonic is shared with the round and the rally but there is no leading note\nand the loop turns over on bVII -> I, a push rather than a full stop, which is\nwhat a stage with no downtime in it needs at its seam; there are **two chords a\nbar**, against the round's one every two bars and the rally's one a bar, so the\nharmony swings as often as possession does; and it is the **only cue with a\nrunning mower under it that lets the glockenspiel play**, two notes a bar and\nwell down in the mix, because it is the only level played after dark and the\nlanterns needed an instrument the band already owned. It also reaches MIDI 88\nwhere the round tops out at 81 and the rally at 85, gets four boot-stomps a bar\ninstead of two, and is played in the longest room any cue with an engine in it\ngets (1.35 s, against 0.9 and 0.80). There is no additive urgent layer for it -\nsee AudioDirector.musicBloom.",
    "Transition": "Nothing in this folder loops, so the loop-seam gates do not apply to any of it.\nEverything is stereo except `transition_gate_creak`, which is diegetic and gets\nplaced at the gate's transform.\n\nA transition is assembled from three of these, not played as one clip:\n**riser -> impact -> sting**. `transition_riser` is the only cue in the game\nwhose peak is its own last sample - it has no decay at all - so it is scheduled\nbackwards from the cut (`PlayScheduled(cutTime - 1.6)`) and `transition_impact`\nfires on the cut frame itself. The sting is `transition_stamp_small` for early\nstages and `transition_fanfare_big` for the final entrance and the run into\njudging; the two are written as the same pickup-rise-arrival gesture in the same\nkey, so swapping one for the other reads as *more* rather than as a different\ncue.\n\nThe two wipes are objects rather than filter sweeps: each is a band whose centre\nopens with proximity multiplied by a **doppler** curve that drops the whole\nspectrum a third to a fourth through the pass-by, plus the debris that object\nwould actually make - 17 leaf-crackle transients weighted by proximity for the\nhedge, three cloth slaps and a rope creak for the banner. They also travel in\nopposite directions (leaf L -> R, banner R -> L) so a scene that uses both does\nnot read as the same wipe twice.\n\n`transition_crowd_swell` is built from `crowd.py`'s own `murmur_grain`, `shout`\nand `clap`, not from a second crowd model, and it ends back at a murmur at the\nsame level it started - crossfade it straight into `crowd_ambient_loop`.",
}


def spec_section():
    rows = table()
    bycat = {}
    for r in rows:
        bycat.setdefault(r["cat"], []).append(r)
    out = []
    for cat in SPEC_ORDER:
        out += [SPEC_HEAD[cat], "",
                "| clip | purpose | s | ch | loop | trim dB | volume |",
                "|---|---|---|---|---|---|---|"]
        for r in sorted(bycat[cat], key=lambda x: x["name"]):
            out.append(
                f"| `{cat}/{r['name']}.wav` | {PURPOSE[r['name']]} | "
                f"{r['dur']:.2f} | {r['ch']} | {'yes' if r['loop'] else 'no'} | "
                f"{r['trim_db']:+.1f} | {r['volume01']:.3f} |")
        out += ["", SPEC_NOTE[cat], ""]
    return "\n".join(out)


if __name__ == "__main__":
    main()
