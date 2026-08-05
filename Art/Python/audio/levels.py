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
    # --- music -----------------------------------------------------------
    "music_menu_loop": -19.0, "music_round_loop": -22.0,
    "music_round_urgent_layer": -24.0, "music_reveal": -15.0,
    "music_judging_bed_loop": -23.0,
    "fanfare_good": -14.0, "fanfare_bad": -14.0,
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
    "music_menu_loop": "menu / briefing",
    "music_round_loop": "the 75 s round",
    "music_round_urgent_layer": "additive; fade in over the last 15 s",
    "music_reveal": "overhead reveal ta-daa (one-shot)",
    "music_judging_bed_loop": "under the judges",
    "fanfare_good": "verdict, good",
    "fanfare_bad": "verdict, deflating",
}

SPEC_ORDER = ["Engine", "Blade", "Mower", "UI", "Crowd", "Ambience", "Duck",
              "Judges", "Music"]

SPEC_HEAD = {
    "Engine": "### Engine/ - heard for 75 s straight, so it sits under the music's peaks",
    "Blade": "### Blade/ - gated on while cutting",
    "Mower": "### Mower/",
    "UI": "### UI/",
    "Crowd": "### Crowd/",
    "Ambience": "### Ambience/",
    "Duck": "### Duck/",
    "Judges": "### Judges/ - one set per judge, `low` for a bad score, `high` for a good one",
    "Music": "### Music/",
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
    "Music": "`music_reveal` is 6 s and lands its big chord at **2.4 s** - fire it as the camera\nstarts rising so the arrival coincides with the picture appearing.",
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
