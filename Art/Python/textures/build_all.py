"""DUCK MOW - regenerate the entire texture set.

    cd C:\\Duck\\Art\\Python\\textures
    python build_all.py            # everything
    python build_all.py noise ui   # only those groups

Groups: noise ground decals sky particles ui fonts
Outputs land in C:\\Duck\\Assets\\Art\\Textures\\<Category>\\.
Every generator is deterministic - fixed seeds, no wall-clock, no randomness that
is not seeded - so a re-run reproduces the shipped PNGs byte for byte.
"""
import sys
import time
import duckart as D

GROUPS = {}


def group(name):
    def deco(fn):
        GROUPS[name] = fn
        return fn
    return deco


@group("noise")
def _noise():
    import gen_noise as g
    g.perlin_rgba(); g.worley_rgba(); g.blue_noise(); g.verify_blue(); g.grass_blade()


@group("ground")
def _ground():
    import gen_ground as g
    g.dirt_path(); g.gravel(); g.apron_grass_detail(); g.soil_scuff()


@group("decals")
def _decals():
    import gen_decals as g
    g.chalk_line(); g.chalk_corner(); g.chalk_dash(); g.tyre_track()
    for i, s in enumerate([31337, 90210, 55501], start=1):
        g.mud_splat(i, seed=s)
    g.shadow_blob(); g.old_mow_pattern()


@group("sky")
def _sky():
    import gen_sky as g
    g.sky_gradient(); g.cloud_puff()


@group("particles")
def _particles():
    import gen_particles as g
    g.clippings(); g.dust_puff(); g.spark(); g.confetti(); g.water_droplet()


@group("ui")
def _ui():
    import gen_ui_panels as p, gen_ui_more as m, gen_ui_rosettes as r
    p.panel_card(); p.panel_card_dark(); p.button(); p.button_pressed()
    p.progress_bar(); p.boost_gauge(); p.minimap_frame(); p.vignette()
    m.banner_ribbon(); m.timer_ring(); m.scorecard()
    m.icon_speed(); m.icon_accuracy(); m.icon_coverage(); m.icon_style()
    for name, spec in r.SPECS.items():
        r.make_rosette(name, spec)
    sl = dict(p.SLICES); sl.update(m.SLICES)
    print("  --- 9-slice borders (left, bottom, right, top) ---")
    for k, v in sorted(sl.items()):
        print("  %-30s %s" % (k, v))


@group("fonts")
def _fonts():
    import gen_fonts as g
    g.numerals_atlas()


if __name__ == "__main__":
    want = sys.argv[1:] or list(GROUPS)
    for name in want:
        if name not in GROUPS:
            print("unknown group %r; known: %s" % (name, " ".join(GROUPS)))
            continue
        t = time.time()
        print("[%s]" % name)
        GROUPS[name]()
        print("  %.1fs" % (time.time() - t))
    print("\nsizes:")
    import preview
    preview.sizes()
