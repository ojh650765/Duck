# build_crowd_extra.py — DUCK MOW: three MORE crowd species, to widen the five
# already in build_spectators.py (rabbit, sheep, pig, fox, tortoise).
# Run: blender --background --python C:\Duck\Art\Blender\build_crowd_extra.py
#
# <Species>_Root / _Body / _Head  (head pivot at the neck so it can bob), same
# contract as build_spectators.py.  These are GPU-instanced by the hundred, so
# the budget is brutal: UNDER 250 TRIANGLES each, body and head combined.
#
# Bodies are a light neutral base on purpose — the crowd shader multiplies a
# per-instance colour into the vertex colour, and only a pale base tints into
# real variety.  Species character therefore has to live entirely in the
# outline, which is why these three were chosen: nothing in the existing five
# is tall-and-thin, flat-and-low, or has a big arcing tail.
#
#   Goose    0.93 m  a vertical exclamation mark — the tallest thing in a crowd
#   Hedgehog 0.25 m  a jagged loaf on the floor — the lowest
#   Squirrel 0.58 m  upright with a tail arcing over its own head
import bpy, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, ring_z, ring_y, cyl, tube, sphere, disc, torus, rbox,
                      recess, set_pivot, make_empty, attach, report_tris,
                      export_fbx, render_previews, bake_ao, fresh_scene, TAU)

MAT = "M_Spectators"          # shares the crowd material with the first five
BASE, BASE_SH = "F1EDE0", "DCC9A4"
DARK, SLATE = "241F1E", "4A4F55"
ORANGE, ORANGE_D = "F2A03D", "D3792A"


def egg(n, z, rx, ryF, ryB, cy, e=2.0):
    pts, k = [], 2.0 / e
    for i in range(n):
        a = TAU * (i + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** k, c) * rx
        y = math.copysign(abs(s) ** k, s)
        pts.append(Vector((x, cy + y * (ryB if y >= 0 else ryF), z)))
    return pts


# -------------------------------------------------------------------- GOOSE
G_PIV = Vector((0, -0.046, 0.856))     # base of the skull / top of the neck
G_R = 0.052                            # socket sphere about that pivot
G_AX = (0, -0.10, 0.995)


def goose():
    mb = MB()
    B = [(0.300, 0.072, 0.100, 0.092,  0.000, 2.4),
         (0.382, 0.110, 0.154, 0.142, -0.006, 2.2),
         (0.470, 0.106, 0.150, 0.132, -0.012, 2.1),
         (0.548, 0.076, 0.102, 0.088, -0.018, 2.0)]
    mb.pole_loft([egg(8, *s) for s in B], BASE,
                 pole_a=(0, 0.010, 0.258), pole_b=(0, -0.026, 0.598))
    for sx in (-1, 1):
        cyl(mb, ORANGE_D, (sx * 0.044, -0.004, 0.0), (sx * 0.046, -0.014, 0.308),
            0.019, 0.022, n=4)
    # The neck IS the species: one long stalk with a slight S in it.  It used to
    # stop dead at 0.842 with a flat cap and carry a separate ball head on top —
    # from any tilt you could see the post and the ball as two objects.  Now it
    # runs THROUGH the pivot and dies inside the head's socket sphere.
    neck = [Vector((0, -0.028, 0.500)), Vector((0, -0.046, 0.640)),
            Vector((0, -0.036, 0.788)), G_PIV,
            G_PIV + Vector((0, -0.003, 0.030))]
    nr = [0.062, 0.042, 0.036, 0.036, 0.015]
    tube(mb, BASE, neck, nr, n=6, cap_a=False, cap_b=True, smooth=True)
    L.socket_fit(G_PIV, neck[-2:], nr[-2:], G_R * 0.84, "Goose neck")
    body = mb.finish("Goose_Body", MAT, 40.0)

    # HEAD: socket and skull are one swept skin — three sections lying on the
    # sphere about the pivot, then two more for the crown.  Six sides, because
    # the crowd budget is 250 triangles for the whole animal.
    mb = MB()
    sp, sr = L.socket_path(G_PIV, G_AX, G_R, lats=(26, 62, 96))
    path = list(sp) + [Vector((0, -0.052, 0.888)), Vector((0, -0.054, 0.906))]
    rad = list(sr) + [0.048, 0.030]
    sq = [(1.0, 1.0)] * 3 + [(1.0, 1.20), (1.0, 1.25)]
    tg = [G_AX] * 3 + [None] * 2           # socket rings stay on the sphere
    mb.pole_loft(L.sweep_rings(path, rad, n=6, squash=sq, tangents=tg), BASE,
                 pole_a=tuple(G_PIV - Vector(G_AX) * G_R),
                 pole_b=(0, -0.054, 0.918), smooth=True)
    br = [ring_y(5, -0.076, 0.028, 0.024, 2.4, 0.0, 0.872),
          ring_y(5, -0.126, 0.022, 0.016, 2.4, 0.0, 0.866)]
    mb.pole_loft(br, ORANGE, pole_b=(0, -0.166, 0.862), smooth=False)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.034, -0.078, 0.886), (0.012, 0.011, 0.012),
               seg=5, rings=2)
    head = mb.finish("Goose_Head", MAT, 36.0)
    return [(body, (0, 0, 0)), (head, tuple(G_PIV))]


# ----------------------------------------------------------------- HEDGEHOG
def hedgehog():
    mb = MB()
    # Spines are not affordable as geometry at this budget, so the RING itself
    # is jagged: alternate vertices pushed out and pulled in, which gives a
    # genuinely spiky outline for zero extra triangles.
    rings = []
    # The jag has to be hard: the existing crowd already has a low smooth dome
    # (the tortoise), so a gentle bump would just read as a second tortoise.
    for (z, rx, ry, bump) in [(0.040, 0.100, 0.130, 0.022),
                              (0.102, 0.130, 0.174, 0.036),
                              (0.170, 0.116, 0.158, 0.032),
                              (0.216, 0.070, 0.096, 0.020)]:
        r = []
        for i in range(10):
            a = TAU * (i + 0.5) / 10
            k = bump if i % 2 == 0 else -bump * 0.72
            r.append(Vector(((rx + k) * math.cos(a), (ry + k) * math.sin(a),
                             z + (0.010 if i % 2 == 0 else -0.006))))
        rings.append(r)
    shell, _ = mb.pole_loft(rings, BASE_SH, pole_a=(0, 0.008, 0.004),
                            pole_b=(0, -0.028, 0.248), smooth=False)
    for i, f in enumerate(shell):     # two-tone the spines
        if f.is_valid and i % 2 == 0:
            f[mb.lay] = mb.cid(BASE)
    for sx in (-1, 1):
        sphere(mb, BASE, (sx * 0.078, -0.122, 0.028), (0.030, 0.034, 0.026),
               seg=5, rings=2)
    body = mb.finish("Hedgehog_Body", MAT, 34.0)

    mb = MB()
    sphere(mb, BASE, (0, -0.148, 0.110), (0.058, 0.058, 0.052), seg=8, rings=3)
    sn = [ring_y(6, -0.190, 0.036, 0.032, 2.4, 0.0, 0.100),
          ring_y(6, -0.232, 0.022, 0.020, 2.4, 0.0, 0.092)]
    mb.pole_loft(sn, BASE, pole_b=(0, -0.262, 0.088), smooth=True)
    sphere(mb, DARK, (0, -0.258, 0.090), (0.014, 0.012, 0.013), seg=5, rings=2)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.036, -0.196, 0.126), (0.012, 0.011, 0.012),
               seg=5, rings=2)
        sphere(mb, BASE_SH, (sx * 0.048, -0.124, 0.146), (0.024, 0.018, 0.024),
               seg=5, rings=2)
    head = mb.finish("Hedgehog_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, (0, -0.116, 0.104))]


# ----------------------------------------------------------------- SQUIRREL
def squirrel():
    mb = MB()
    B = [(0.060, 0.058, 0.072, 0.064,  0.000, 2.4),
         (0.152, 0.088, 0.106, 0.096, -0.008, 2.2),
         (0.250, 0.082, 0.100, 0.088, -0.016, 2.1),
         (0.332, 0.060, 0.072, 0.062, -0.022, 2.0)]
    mb.pole_loft([egg(8, *s) for s in B], BASE,
                 pole_a=(0, 0.006, 0.014), pole_b=(0, -0.030, 0.374))
    # the tail arcs up and over its own head — nothing else in the crowd does
    # ...and it is canted out to one side, so the plume still shows head-on
    # instead of collapsing to a one-pixel edge.
    tube(mb, BASE, [Vector((0.000, 0.080, 0.108)), Vector((0.032, 0.180, 0.284)),
                    Vector((0.058, 0.142, 0.474)), Vector((0.056, 0.046, 0.556))],
         [0.044, 0.080, 0.072, 0.034], n=6, e=2.4,
         squash=[(0.66, 1.0)] * 4, cap_a=False, cap_b=False, smooth=True)
    for sx in (-1, 1):
        sphere(mb, BASE_SH, (sx * 0.046, -0.082, 0.228), (0.026, 0.030, 0.024),
               seg=5, rings=2)
    body = mb.finish("Squirrel_Body", MAT, 38.0)

    mb = MB()
    sphere(mb, BASE, (0, -0.050, 0.418), (0.063, 0.067, 0.059), seg=8, rings=3)
    sphere(mb, BASE_SH, (0, -0.102, 0.396), (0.033, 0.035, 0.029), seg=6, rings=3)
    sphere(mb, DARK, (0, -0.130, 0.398), (0.013, 0.011, 0.012), seg=5, rings=2)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.036, -0.088, 0.438), (0.013, 0.012, 0.013),
               seg=5, rings=2)
        cyl(mb, BASE_SH, (sx * 0.038, -0.016, 0.470), (sx * 0.046, -0.010, 0.532),
            0.026, 0.008, n=4, e=2.6)
    head = mb.finish("Squirrel_Head", MAT, 34.0)
    # Pivot inside the skull rather than under it.  A ball head pivoted at its
    # own centre-ish is its own socket: rotation maps the sphere onto itself, so
    # the body's shoulder stays swallowed at every angle, for zero triangles.
    return [(body, (0, 0, 0)), (head, (0, -0.044, 0.400))]


# =============================================================== BUILD
def main():
    fresh_scene()
    specs = [("Goose", goose, -0.70), ("Hedgehog", hedgehog, 0.0),
             ("Squirrel", squirrel, 0.70)]
    roots, allobjs, allmesh, groups = [], [], [], []
    for name, fn, ox in specs:
        parts = fn()
        meshes = [p[0] for p in parts]
        hi = max(max(v.co.z for v in ob.data.vertices) for ob in meshes)
        bake_ao(meshes, floor=0.80, dist=0.14, samples=20, ground=True,
                ground_size=1.0)
        L.joint_audit(parts[0][0], parts[1][0], parts[1][1], name, near=0.22)
        for ob, piv in parts:
            set_pivot(ob, piv)
        root = make_empty(name + "_Root", (0, 0, 0))
        for ob, piv in parts:
            attach(ob, root)
        roots.append((root, ox))
        groups.append((name, meshes, parts[1][1]))
        allobjs += [root] + meshes
        allmesh += meshes
        report_tris(meshes, "%s (h=%.2f m)" % (name, hi))

    L.verify_meshes(allmesh, "CrowdExtra")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "CrowdExtra.fbx"))
    if L.want_tilt():
        for name, meshes, piv in groups:
            hd = [m for m in meshes if m.name.endswith("_Head")][0]
            L.tilt_sheet(hd, meshes, piv, None, "crowd_" + name.lower())
    for root, ox in roots:
        root.location = (ox, 0, 0)
    render_previews(allmesh, "crowdextra", res=640)
    print("DONE CrowdExtra")


main()
