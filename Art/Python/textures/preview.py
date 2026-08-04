"""Preview / verification helpers. Run:  python preview.py <group>"""
import os, sys
import numpy as np
from PIL import Image
import duckart as D

P = D.PREVIEW
os.makedirs(P, exist_ok=True)


def p(cat, name):
    return os.path.join(D.OUT, cat, name)


def sizes():
    tot = 0
    for cat in sorted(os.listdir(D.OUT)):
        d = os.path.join(D.OUT, cat)
        if not os.path.isdir(d):
            continue
        s = 0
        for f in sorted(os.listdir(d)):
            if f.endswith(".png"):
                n = os.path.getsize(os.path.join(d, f))
                s += n
                print("  %-42s %8.1f KB" % (cat + "/" + f, n / 1024))
        print("%-44s %8.1f KB" % (cat.upper() + " subtotal", s / 1024))
        tot += s
    print("TOTAL %.2f MB" % (tot / 1024 / 1024))


def channels(path, out):
    a = D.load(path)
    if a.ndim == 2:
        D.save(out, a); return
    n = a.shape[2]
    cols = [a[..., i] for i in range(n)]
    if n == 4:
        sheet = np.concatenate([np.concatenate(cols[:2], 1), np.concatenate(cols[2:], 1)], 0)
    else:
        sheet = np.concatenate(cols, 1)
    D.save(out, sheet)


def noise():
    D.tile_preview(p("Noise", "noise_perlin_rgba_512.png"), P + r"\tile_perlin.png", 2, 0.5)
    D.tile_preview(p("Noise", "noise_worley_512.png"), P + r"\tile_worley.png", 2, 0.5)
    D.tile_preview(p("Noise", "noise_blue_256.png"), P + r"\tile_blue.png", 2, 1.0)
    channels(p("Noise", "noise_perlin_rgba_512.png"), P + r"\perlin_channels.png")
    channels(p("Noise", "noise_worley_512.png"), P + r"\worley_channels.png")
    im = Image.open(p("Noise", "grass_blade_alpha_128.png")).convert("RGBA").resize((256, 256), Image.NEAREST)
    D.flatten_alpha(im, 8).save(P + r"\grass_blade.png")


def seam_report(path):
    """Numeric seam check: compare the wrap gradient to the interior gradient."""
    a = D.load(path)
    if a.ndim == 3:
        a = a[..., :3].mean(2)
    gx_in = np.abs(np.diff(a, axis=1)).mean()
    gy_in = np.abs(np.diff(a, axis=0)).mean()
    gx_seam = np.abs(a[:, 0] - a[:, -1]).mean()
    gy_seam = np.abs(a[0, :] - a[-1, :]).mean()
    print("  %-40s interior dx %.4f dy %.4f | seam dx %.4f dy %.4f  %s"
          % (os.path.basename(path), gx_in, gy_in, gx_seam, gy_seam,
             "OK" if gx_seam < gx_in * 2.2 + 1e-4 and gy_seam < gy_in * 2.2 + 1e-4 else "SEAM!"))


if __name__ == "__main__":
    g = sys.argv[1] if len(sys.argv) > 1 else "sizes"
    if g == "sizes":
        sizes()
    elif g == "noise":
        noise()
    elif g == "seams":
        for cat, name in [("Noise", "noise_perlin_rgba_512.png"), ("Noise", "noise_worley_512.png"),
                          ("Noise", "noise_blue_256.png"),
                          ("Ground", "dirt_path_albedo_512.png"), ("Ground", "dirt_path_normal_512.png"),
                          ("Ground", "gravel_albedo_512.png"), ("Ground", "gravel_normal_512.png"),
                          ("Ground", "apron_grass_detail_512.png"), ("Ground", "soil_scuff_512.png"),
                          ("Decals", "tyre_track_256.png")]:
            f = p(cat, name)
            if os.path.exists(f):
                seam_report(f)
