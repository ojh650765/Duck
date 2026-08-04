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


def socket_head(mb, col, piv, ax, R, path, radii, tip, n=8, lats=(26, 62, 96),
                squash=None, smooth=True):
    """One unbroken head skin whose lower/rear cap is a piece of the SPHERE of
    radius `R` centred exactly on the head's pivot, lofted straight on into the
    skull sections `path`/`radii` and closed at `tip`.

    The engine rotates `<Species>_Head` about that pivot every frame.  A sphere
    about the pivot maps onto itself under that rotation, so the join line
    cannot move at any angle — which is the whole point.  Everything of the body
    that the head swallows has to sit INSIDE that ball; anything the muzzle or
    the ears swallow further out than R is what pops back into view.
    """
    sp, sr = L.socket_path(piv, ax, R, lats=lats)
    pts = list(sp) + [Vector(p) for p in path]
    rad = list(sr) + list(radii)
    sq = ([(1.0, 1.0)] * len(sp) + list(squash)) if squash else None
    tg = [Vector(ax).normalized()] * len(sp) + [None] * len(path)
    rings = L.sweep_rings(pts, rad, n=n, squash=sq, tangents=tg)
    return mb.pole_loft(rings, col, smooth=smooth, pole_b=tuple(tip),
                        pole_a=tuple(Vector(piv)
                                     - Vector(ax).normalized() * R))


def col_faces(mb, faces, fn, col):
    c = mb.cid(col)
    for f in faces:
        if f.is_valid and fn(f.calc_center_median()):
            f[mb.lay] = c


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
# The worst joint in the set: head, muzzle and forelock were three loose balls
# planted on the front of the fleece, and the muzzle ball reached 111 mm past
# the pivot INTO the wool.  Turning the head dragged that muzzle out through the
# fleece and opened up to 73 mm of previously-hidden body.  Now the skull IS the
# socket sphere about the pivot, half-nestled in the wool where it can never
# move, and a slate neck runs up through the pivot to die inside it.
SH_PIV = Vector((0, -0.176, 0.492))
SH_R = 0.056
SH_AX = (0, -0.985, -0.174)


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
    # neck: enters the fleece, runs dead straight up the pivot axis and dies in
    # a 16 mm cap 30 mm PAST the pivot, permanently inside the socket ball.
    neck = [Vector((0, -0.060, 0.520)), Vector((0, -0.120, 0.503)),
            SH_PIV, SH_PIV + Vector(SH_AX) * 0.030]
    nr = [0.062, 0.048, 0.040, 0.016]
    tube(mb, SLATE, neck, nr, n=6, cap_a=False, cap_b=True, smooth=True)
    L.socket_fit(SH_PIV, neck[-2:], nr[-2:], SH_R, "Sheep neck")
    body = mb.finish("Sheep_Body", MAT, 40.0)

    # HEAD: socket sphere and skull are one swept skin — three rings lying on
    # the sphere about the pivot, then straight on into the muzzle.  The cream
    # forelock and the dark muzzle are recoloured faces of that same skin, not
    # extra balls, so nothing can slide.
    mb = MB()
    faces, _ = socket_head(mb, SLATE, SH_PIV, SH_AX, SH_R,
                           [(0, -0.228, 0.483), (0, -0.266, 0.476),
                            (0, -0.292, 0.472)], [0.040, 0.030, 0.024],
                           (0, -0.306, 0.470), n=8)
    col_faces(mb, faces, lambda c: c.y < -0.238, DARK)
    col_faces(mb, faces, lambda c: c.z > 0.522 and c.y > -0.216, BASE)
    sphere(mb, DARK, (0, -0.300, 0.472), (0.018, 0.014, 0.014), seg=6, rings=3)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.040, -0.216, 0.508), (0.014, 0.014, 0.014),
               seg=5, rings=2)
        tube(mb, SLATE, [Vector((sx * 0.048, -0.186, 0.522)),
                         Vector((sx * 0.118, -0.178, 0.508))],
             [0.026, 0.012], n=6, e=3.0, squash=[(1.0, 0.55)] * 2, smooth=False)
    head = mb.finish("Sheep_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, tuple(SH_PIV))]


# ---------------------------------------------------------------------- PIG
# A pig has no neck, so the skull itself has to be the socket: the head skin is
# the sphere of radius PG_R about the pivot, sunk into the chest, then swept on
# into the snout.  Everything the chest hides is therefore inside that ball and
# stays hidden.  The old ears rooted at z=0.452 were burying shoulder 101 mm out
# from the pivot and dragging it into view; they now root on the ball's brow.
PG_PIV = Vector((0, -0.156, 0.400))
PG_R = 0.082
PG_AX = (0, -0.996, 0.087)


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
    faces, _ = socket_head(mb, BASE, PG_PIV, PG_AX, PG_R,
                           [(0, -0.218, 0.405), (0, -0.260, 0.409),
                            (0, -0.276, 0.410)], [0.056, 0.046, 0.044],
                           (0, -0.283, 0.411), n=8)
    col_faces(mb, faces, lambda c: c.y < -0.200, ORANGE_D)   # snout
    for sx in (-1, 1):
        # 86 mm out from the pivot: an eye at exactly PG_R vanishes, because the
        # faceted ball's flats sit 7.6% inside the true sphere and swallow it.
        sphere(mb, DARK, (sx * 0.063, -0.199, 0.439), (0.015, 0.014, 0.016),
               seg=5, rings=2)
        sphere(mb, DARK, (sx * 0.017, -0.278, 0.408), (0.009, 0.006, 0.011),
               seg=5, rings=2)
        tube(mb, BASE_SH, [Vector((sx * 0.048, -0.152, 0.466)),
                           Vector((sx * 0.070, -0.170, 0.548))],
             [0.036, 0.006], n=5, e=3.0, squash=[(1.0, 0.5)] * 2, smooth=False)
    head = mb.finish("Pig_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, tuple(PG_PIV))]


# ---------------------------------------------------------------------- FOX
# The fox's snout head was already one lofted skin, but it sat straight down on
# the shoulder cap: every sample the audit flagged was a shoulder pole-fan edge
# 4 mm below the pivot and up to 62 mm out, hidden under the jaw at rest and
# swept clear the moment the head pitched.  The head is lifted 36 mm onto a real
# neck, and a throat() socket — sphere about the pivot below, buried in the
# skull above — is now the only thing that ever meets the shoulder.
FX_PIV = Vector((0, -0.028, 0.534))
FX_R = 0.048
FX_AX = (0, -0.28, 0.960)


def fox():
    mb = MB()
    B = [(0.170, 0.062, 0.120, 0.108, 0.0, 2.4), (0.250, 0.086, 0.164, 0.148, -0.006, 2.2),
         (0.340, 0.088, 0.170, 0.150, -0.014, 2.1), (0.420, 0.078, 0.140, 0.120, -0.022, 2.0),
         (0.480, 0.058, 0.096, 0.080, -0.028, 2.0)]
    rings = [egg(10, *s) for s in B]
    mb.pole_loft(rings, BASE, pole_a=(0, 0, 0.126), pole_b=(0, -0.034, 0.512))
    legs(mb, DARK, (-0.062, 0.062), (-0.104, 0.104), 0.0, 0.210, 0.018, 0.024, n=5)
    # big brush tail, up and back - the silhouette
    t = [Vector((0, 0.130, 0.320)), Vector((0, 0.232, 0.400)),
         Vector((0, 0.296, 0.520)), Vector((0, 0.286, 0.646))]
    tube(mb, BASE, t, [0.060, 0.086, 0.082, 0.048], n=6, smooth=True)
    sphere(mb, BASE_SH, (0, 0.276, 0.680), (0.046, 0.046, 0.046), seg=6, rings=3)
    neck = [Vector((0, -0.010, 0.462)), FX_PIV, FX_PIV + Vector(FX_AX) * 0.026]
    nr = [0.052, 0.038, 0.016]
    tube(mb, BASE, neck, nr, n=5, cap_a=False, cap_b=True, smooth=True)
    L.socket_fit(FX_PIV, neck[-2:], nr[-2:], FX_R, "Fox neck")
    body = mb.finish("Fox_Body", MAT, 36.0)

    mb = MB()
    H = [(0.036, 0.062, 0.062, 0.596, 2.4), (-0.030, 0.074, 0.072, 0.594, 2.6),
         (-0.100, 0.050, 0.046, 0.578, 2.8), (-0.164, 0.030, 0.026, 0.566, 2.6)]
    hr = [ring_y(8, y, rx, rz, e, 0.0, cz) for (y, rx, rz, cz, e) in H]
    mb.pole_loft(hr, BASE, pole_a=(0, 0.062, 0.596), pole_b=(0, -0.196, 0.562))
    L.throat(mb, BASE, FX_PIV, FX_AX, FX_R, [(0, -0.062, 0.580)], [0.038],
             n=8, e=2.2, lats=(26, 62, 96), smooth=True)
    sphere(mb, DARK, (0, -0.192, 0.564), (0.017, 0.016, 0.014), seg=5, rings=3)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.046, -0.086, 0.608), (0.014, 0.013, 0.015),
               seg=5, rings=2)
        tube(mb, BASE, [Vector((sx * 0.040, 0.006, 0.636)),
                        Vector((sx * 0.056, 0.014, 0.700)),
                        Vector((sx * 0.062, 0.006, 0.740))],
             [0.036, 0.022, 0.005], n=5, e=3.0,
             squash=[(0.55, 1.0)] * 3, smooth=False)
    head = mb.finish("Fox_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, tuple(FX_PIV))]


# ----------------------------------------------------------------- TORTOISE
# The head tube used to start behind the shell's front lip and swallow it, with
# the pivot 64 mm back from that lip — so the lip slid out from under the neck
# on every turn.  The pivot now sits on the neck axis just outside the shell
# mouth, the head's rear cap is the sphere about it, and the lip lives inside
# that ball where it cannot be uncovered.
TO_PIV = Vector((0, -0.222, 0.200))
TO_R = 0.046          # the FACETED socket is only 0.951 R across its flats at
TO_N = 10             # ten sides, so R has to clear the shell lip by ~8 mm, not
TO_AX = (0, -0.910, 0.415)   # graze it — 2.7 mm still flicked out at 52 mm.


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
    # the neck now belongs to the BODY: it leaves the shell mouth, runs up the
    # pivot axis and dies 28 mm past the pivot inside the head's socket ball
    neck = [Vector((0, -0.130, 0.160)), TO_PIV - Vector(TO_AX) * 0.045,
            TO_PIV, TO_PIV + Vector(TO_AX) * 0.026]
    nr = [0.048, 0.040, 0.030, 0.012]
    tube(mb, DIRT, neck, nr, n=6, cap_a=False, cap_b=True, smooth=True)
    L.socket_fit(TO_PIV, neck[-2:], nr[-2:], TO_R, "Tortoise neck")
    body = mb.finish("Tortoise_Body", MAT, 34.0)

    mb = MB()
    socket_head(mb, DIRT, TO_PIV, TO_AX, TO_R,
                [(0, -0.266, 0.220), (0, -0.306, 0.238), (0, -0.333, 0.251)],
                [0.038, 0.048, 0.042], (0, -0.360, 0.263), n=TO_N)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.038, -0.299, 0.268), (0.013, 0.012, 0.013),
               seg=5, rings=2)
    head = mb.finish("Tortoise_Head", MAT, 34.0)
    return [(body, (0, 0, 0)), (head, tuple(TO_PIV))]


# =============================================================== BUILD
def main():
    fresh_scene()
    specs = [("Rabbit", rabbit, -1.4), ("Sheep", sheep, -0.7), ("Pig", pig, 0.0),
             ("Fox", fox, 0.7), ("Tortoise", tortoise, 1.4)]
    roots, allobjs, allmesh, groups = [], [], [], []
    for name, fn, ox in specs:
        parts = fn()
        meshes = [p[0] for p in parts]
        hi = max(max(v.co.z for v in ob.data.vertices) for ob in meshes)
        bake_ao(meshes, floor=0.78, dist=0.16, samples=24, ground=True, ground_size=1.2)
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

    L.verify_meshes(allmesh, "Spectators")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "Spectators.fbx"))
    if L.want_tilt():
        for name, meshes, piv in groups:
            hd = [m for m in meshes if m.name.endswith("_Head")][0]
            L.tilt_sheet(hd, meshes, piv, None, "spec_" + name.lower())
    for root, ox in roots:
        root.location = (ox, 0, 0)
    render_previews(allmesh, "spectators", res=640)
    print("DONE Spectators")


main()
