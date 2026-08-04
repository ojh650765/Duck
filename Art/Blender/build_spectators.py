# build_spectators.py — DUCK MOW: five crowd species, GPU-instanced by the hundred.
# Run: blender --background --python C:\Duck\Art\Blender\build_spectators.py
#
# <Species>_Root / _Body / _Head  (head pivot at the neck so it can bob).
# Bodies are deliberately a light neutral base so a per-instance colour tint
# multiplies cleanly into real variety; species character lives in the shape and
# in a few fixed accent colours that survive being tinted.
import bpy, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, ring_z, ring_y, cyl, tube, sphere, disc, torus, rbox,
                      recess, set_pivot, make_empty, attach, report_tris,
                      export_fbx, render_previews, bake_ao, fresh_scene, TAU)

MAT = "M_Spectators"
BASE, BASE_SH = "F1EDE0", "DCC9A4"
DARK, SLATE = "241F1E", "4A4F55"
ORANGE, ORANGE_D = "F2A03D", "D3792A"
WOOD, WOOD_D, DIRT = "9A6B41", "6E4A2C", "B99A6B"
PINKISH = "D8534E"


def egg(n, z, rx, ryF, ryB, cy, e=2.0):
    pts, k = [], 2.0 / e
    for i in range(n):
        a = TAU * (i + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** k, c) * rx
        y = math.copysign(abs(s) ** k, s)
        pts.append(Vector((x, cy + y * (ryB if y >= 0 else ryF), z)))
    return pts


def legs(mb, col, xs, ys, z0, z1, r0, r1, n=6):
    for x in xs:
        for y in ys:
            cyl(mb, col, (x, y, z0), (x, y, z1), r1, r0, n=n)


# ------------------------------------------------------------------- RABBIT
def rabbit():
    mb = MB()
    B = [(0.010, 0.058, 0.070, 0.062, 0.0, 2.6), (0.070, 0.092, 0.112, 0.100, -0.004, 2.3),
         (0.160, 0.100, 0.126, 0.112, -0.010, 2.1), (0.260, 0.092, 0.112, 0.098, -0.020, 2.0),
         (0.340, 0.070, 0.082, 0.070, -0.026, 2.0)]
    rings = [egg(10, *s) for s in B]
    mb.pole_loft(rings, BASE, pole_a=(0, 0, -0.004), pole_b=(0, -0.030, 0.382))
    for sx in (-1, 1):     # haunches + big back feet
        sphere(mb, BASE, (sx * 0.088, 0.010, 0.080), (0.046, 0.070, 0.052), seg=6, rings=3)
        rbox(mb, BASE_SH, (sx * 0.070, -0.088, 0.020), (0.056, 0.130, 0.040),
             r=0.014, n=6, e=3.0, k=1)
    sphere(mb, BASE, (0, 0.126, 0.150), (0.048, 0.042, 0.048), seg=6, rings=3)  # tail
    body = mb.finish("Rabbit_Body", MAT, 34.0)

    mb = MB()
    sphere(mb, BASE, (0, -0.044, 0.430), (0.072, 0.080, 0.070), seg=10, rings=5)
    sphere(mb, BASE, (0, -0.108, 0.408), (0.040, 0.038, 0.032), seg=8, rings=3)
    sphere(mb, ORANGE_D, (0, -0.140, 0.414), (0.014, 0.012, 0.012), seg=6, rings=3)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.050, -0.098, 0.442), (0.015, 0.014, 0.016), seg=6, rings=3)
        # the ears ARE the silhouette
        tube(mb, BASE, [Vector((sx * 0.030, -0.010, 0.480)),
                        Vector((sx * 0.042, -0.004, 0.560)),
                        Vector((sx * 0.048, -0.018, 0.618))],
             [0.026, 0.028, 0.014], n=6, e=2.6,
             squash=[(0.55, 1.0)] * 3, smooth=False)
    head = mb.finish("Rabbit_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.040, 0.360))]


# -------------------------------------------------------------------- SHEEP
def sheep():
    mb = MB()
    # fleece: alternate the ring radius so the outline is lumpy, not an egg
    rings = []
    for (z, rx, ry, cy, bump) in [(0.150, 0.108, 0.130, 0.0, 0.012),
                                  (0.230, 0.152, 0.190, -0.004, 0.020),
                                  (0.330, 0.164, 0.208, -0.010, 0.022),
                                  (0.430, 0.150, 0.186, -0.018, 0.020),
                                  (0.510, 0.104, 0.126, -0.024, 0.014)]:
        r = []
        for i in range(12):
            a = TAU * (i + 0.5) / 12
            k = bump if i % 2 == 0 else -bump * 0.5
            r.append(Vector(((rx + k) * math.cos(a), cy + (ry + k) * math.sin(a), z)))
        rings.append(r)
    mb.pole_loft(rings, BASE, pole_a=(0, 0, 0.116), pole_b=(0, -0.026, 0.556))
    legs(mb, SLATE, (-0.078, 0.078), (-0.108, 0.108), 0.0, 0.190, 0.020, 0.026)
    body = mb.finish("Sheep_Body", MAT, 40.0)

    mb = MB()
    sphere(mb, SLATE, (0, -0.140, 0.482), (0.062, 0.084, 0.064), seg=10, rings=4)
    sphere(mb, SLATE, (0, -0.212, 0.452), (0.036, 0.040, 0.032), seg=8, rings=3)
    sphere(mb, BASE, (0, -0.116, 0.536), (0.062, 0.050, 0.038), seg=8, rings=3)  # forelock
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.040, -0.192, 0.492), (0.014, 0.014, 0.014), seg=6, rings=3)
        tube(mb, SLATE, [Vector((sx * 0.052, -0.118, 0.494)),
                         Vector((sx * 0.118, -0.104, 0.472))],
             [0.026, 0.012], n=6, e=3.0, squash=[(1.0, 0.55)] * 2, smooth=False)
    head = mb.finish("Sheep_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.096, 0.462))]


# ---------------------------------------------------------------------- PIG
def pig():
    mb = MB()
    B = [(0.120, 0.088, 0.130, 0.116, 0.0, 2.6), (0.200, 0.130, 0.190, 0.170, -0.006, 2.3),
         (0.300, 0.138, 0.202, 0.178, -0.014, 2.2), (0.390, 0.122, 0.174, 0.150, -0.022, 2.1),
         (0.450, 0.090, 0.122, 0.104, -0.026, 2.0)]
    rings = [egg(12, *s) for s in B]
    mb.pole_loft(rings, BASE, pole_a=(0, 0, 0.078), pole_b=(0, -0.030, 0.488))
    legs(mb, BASE_SH, (-0.088, 0.088), (-0.116, 0.116), 0.0, 0.150, 0.026, 0.034)
    # curly tail
    t = [Vector((0.0, 0.180, 0.330)), Vector((0.026, 0.212, 0.352)),
         Vector((0.006, 0.232, 0.380)), Vector((-0.024, 0.212, 0.386))]
    tube(mb, BASE_SH, t, [0.016, 0.013, 0.010, 0.007], n=5, smooth=False)
    body = mb.finish("Pig_Body", MAT, 34.0)

    mb = MB()
    sphere(mb, BASE, (0, -0.146, 0.396), (0.088, 0.086, 0.080), seg=10, rings=5)
    cyl(mb, ORANGE_D, (0, -0.212, 0.378), (0, -0.252, 0.376), 0.044, 0.048, n=10)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.048, -0.208, 0.418), (0.014, 0.013, 0.015), seg=6, rings=3)
        sphere(mb, DARK, (sx * 0.017, -0.2545, 0.376), (0.009, 0.006, 0.011), seg=5, rings=2)
        tube(mb, BASE_SH, [Vector((sx * 0.052, -0.108, 0.452)),
                           Vector((sx * 0.066, -0.128, 0.522))],
             [0.036, 0.006], n=5, e=3.0, squash=[(1.0, 0.5)] * 2, smooth=False)
    head = mb.finish("Pig_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.110, 0.360))]


# ---------------------------------------------------------------------- FOX
def fox():
    mb = MB()
    B = [(0.170, 0.062, 0.120, 0.108, 0.0, 2.4), (0.250, 0.086, 0.164, 0.148, -0.006, 2.2),
         (0.340, 0.088, 0.170, 0.150, -0.014, 2.1), (0.420, 0.078, 0.140, 0.120, -0.022, 2.0),
         (0.480, 0.058, 0.096, 0.080, -0.028, 2.0)]
    rings = [egg(10, *s) for s in B]
    mb.pole_loft(rings, BASE, pole_a=(0, 0, 0.126), pole_b=(0, -0.034, 0.512))
    legs(mb, DARK, (-0.062, 0.062), (-0.104, 0.104), 0.0, 0.210, 0.018, 0.024)
    # big brush tail, up and back - the silhouette
    t = [Vector((0, 0.130, 0.320)), Vector((0, 0.232, 0.400)),
         Vector((0, 0.296, 0.520)), Vector((0, 0.286, 0.646))]
    tube(mb, BASE, t, [0.060, 0.086, 0.082, 0.048], n=8, smooth=True)
    sphere(mb, BASE_SH, (0, 0.276, 0.680), (0.046, 0.046, 0.046), seg=8, rings=3)
    body = mb.finish("Fox_Body", MAT, 36.0)

    mb = MB()
    H = [(0.036, 0.062, 0.062, 0.560, 2.4), (-0.030, 0.074, 0.072, 0.558, 2.6),
         (-0.100, 0.050, 0.046, 0.542, 2.8), (-0.164, 0.030, 0.026, 0.530, 2.6)]
    hr = [ring_y(10, y, rx, rz, e, 0.0, cz) for (y, rx, rz, cz, e) in H]
    mb.pole_loft(hr, BASE, pole_a=(0, 0.062, 0.560), pole_b=(0, -0.196, 0.526))
    sphere(mb, DARK, (0, -0.192, 0.528), (0.017, 0.016, 0.014), seg=6, rings=3)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.046, -0.086, 0.572), (0.014, 0.013, 0.015), seg=6, rings=3)
        tube(mb, BASE, [Vector((sx * 0.040, 0.006, 0.606)),
                        Vector((sx * 0.056, 0.014, 0.680)),
                        Vector((sx * 0.062, 0.006, 0.722))],
             [0.036, 0.022, 0.005], n=5, e=3.0,
             squash=[(0.55, 1.0)] * 3, smooth=False)
    head = mb.finish("Fox_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.010, 0.500))]


# ----------------------------------------------------------------- TORTOISE
def tortoise():
    mb = MB()
    # low dome shell with scutes cut in
    rings = [ring_z(12, 0.070, 0.176, 0.222, 2.4, 0.0, 0.0),
             ring_z(12, 0.170, 0.170, 0.214, 2.2, 0.0, 0.0),
             ring_z(12, 0.260, 0.130, 0.164, 2.1, 0.0, 0.0),
             ring_z(12, 0.320, 0.070, 0.090, 2.0, 0.0, 0.0)]
    shell, _ = mb.pole_loft(rings, WOOD_D, pole_b=(0, 0, 0.344))
    # scutes: recolour alternating plates (a per-face recess costs 12 tris each)
    for i, f in enumerate(shell):
        if f.is_valid and f.normal.z > 0.10 and (i % 2 == 0):
            f[mb.lay] = mb.cid(WOOD)
    # plastron rim + stubby legs
    mb.loft([ring_z(12, 0.070, 0.176, 0.222, 2.4), ring_z(12, 0.040, 0.164, 0.206, 2.4)],
            DIRT, cap_a=False, cap_b=True, smooth=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(mb, DIRT, (sx * 0.122, sy * 0.152, 0.010), (sx * 0.148, sy * 0.176, 0.078),
                0.036, 0.030, n=5)
    cyl(mb, DIRT, (0, 0.166, 0.130), (0, 0.226, 0.108), 0.030, 0.016, n=6)   # tail
    body = mb.finish("Tortoise_Body", MAT, 34.0)

    mb = MB()
    tube(mb, DIRT, [Vector((0, -0.164, 0.180)), Vector((0, -0.238, 0.216)),
                    Vector((0, -0.300, 0.242))], [0.046, 0.044, 0.046], n=8, smooth=True)
    sphere(mb, DIRT, (0, -0.318, 0.248), (0.050, 0.052, 0.046), seg=8, rings=4)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.030, -0.352, 0.262), (0.013, 0.012, 0.013), seg=6, rings=3)
    head = mb.finish("Tortoise_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.150, 0.176))]


# =============================================================== BUILD
def main():
    fresh_scene()
    specs = [("Rabbit", rabbit, -1.4), ("Sheep", sheep, -0.7), ("Pig", pig, 0.0),
             ("Fox", fox, 0.7), ("Tortoise", tortoise, 1.4)]
    roots, allobjs, allmesh = [], [], []
    for name, fn, ox in specs:
        parts = fn()
        meshes = [p[0] for p in parts]
        hi = max(max(v.co.z for v in ob.data.vertices) for ob in meshes)
        bake_ao(meshes, floor=0.78, dist=0.16, samples=24, ground=True, ground_size=1.2)
        for ob, piv in parts:
            set_pivot(ob, piv)
        root = make_empty(name + "_Root", (0, 0, 0))
        for ob, piv in parts:
            attach(ob, root)
        roots.append((root, ox))
        allobjs += [root] + meshes
        allmesh += meshes
        report_tris(meshes, "%s (h=%.2f m)" % (name, hi))

    L.verify_meshes(allmesh, "Spectators")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "Spectators.fbx"))
    for root, ox in roots:
        root.location = (ox, 0, 0)
    render_previews(allmesh, "spectators", res=640)
    print("DONE Spectators")


main()
