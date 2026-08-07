"""
Render every DUCK MOW audio clip, then verify them.

    python render_all.py                  # everything, 44.1 kHz
    python render_all.py engine music     # just those modules
    python render_all.py --rate=22050     # half-size source files
    python render_all.py --no-verify

Output goes to C:/Duck/Assets/Audio/<Category>/.  Nothing else under Assets/ is
touched.  Rendering is deterministic - every generator is seeded - so a
re-render after a tweak only changes the clips you actually edited.
"""

from __future__ import annotations

import sys
import time
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MODULES = ["engine", "blade", "mower", "ui", "crowd", "ambience",
           "duck", "judges", "geese", "music", "transition"]


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    flags = {a for a in argv if a.startswith("--")}

    import dsp
    for f in list(flags):
        if f.startswith("--rate="):
            dsp.OUTPUT_SR = int(f.split("=", 1)[1])
            print(f"writing at {dsp.OUTPUT_SR} Hz")
    todo = [m for m in MODULES if not args or m in args]

    extra = {}
    for name in todo:
        t0 = time.time()
        mod = __import__(name)
        info = mod.render()
        print(f"  {name:<10} {time.time() - t0:6.2f} s")
        if info:
            extra[name] = info

    if "engine" in extra:
        e = extra["engine"]
        print("\nengine layer voicing (apply as a mixer trim in Unity):")
        for k in ("idle", "mid", "high"):
            print(f"  engine_{k:<5} rms {e['rms_dbfs'][k]:6.2f} dBFS "
                  f"-> trim {e['recommended_trim_db'][k]:+.2f} dB")
    if "music" in extra:
        print("\nmusic 60-160 Hz engine carve:")
        for d in extra["music"]:
            print(f"  {d['label']:<8} {d['band_frac_pct']:6.3f}% of total energy, "
                  f"{d['band_vs_200_800_db']:+6.1f} dB vs the 200-800 Hz band")

    if "--no-verify" not in flags and dsp.OUTPUT_SR == dsp.SR:
        print()
        import analyze
        rows = analyze.report()
        print()
        analyze.check(rows)


if __name__ == "__main__":
    main(sys.argv[1:])
