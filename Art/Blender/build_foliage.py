# build_foliage.py — DUCK MOW: trees, hedges, reeds, lilypads, flowerbed.
# Run: blender --background --python C:\Duck\Art\Blender\build_foliage.py
#
# Canopies are built from several DIFFERENT-SIZED overlapping lobes with branch
# tips poking through, deliberately asymmetric.  A sphere on a stick is the
# thing this file exists to avoid.
import bpy, math, os, sys, random
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_Foliage"
LEAF, LEAF_LIT, LEAF_DK = "2F6B33", "4E9440", "24512A"
HEDGE, HEDGE_LIT = "2A5A34", "3E7C40"
BARK, BARK_LT = "6E4A2C", "9A6B41"
APPLE, BLOSSOM = "D6423C", "F5EAD6"
SOIL, DIRT = "6E4A2C", "B99A6B"
WATER_S, CHALK = "68B0C4", "F7F3E4"
PETAL_A, PETAL_B, PETAL_C = "D8534E", "F2A03D", "F7F3E4"


def lobe(mb, col, col_lit, col_dk, center, radii, seg=7, nrings=4, jit=0.16, seed=0):
    """A faceted leaf cluster: a sphere with per-vertex radial jitter, lit on
    top and darkened underneath.  Never smooth-shaded."""
    rnd = random.Random(seed)
    cx, cy, cz = center
    rx, ry, rz = radii
    rgs = []
    for i in range(1, nrings):
        t = math.pi * i / nrings
        s, cth = math.sin(t), math.cos(t)
        pts = []
        for k in range(seg):
            a = TAU * k / seg
            j = 1.0 + rnd.uniform(-jit, jit)
            pts.append(Vector((cx + rx * s * math.cos(a) * j,
                               cy + ry * s * math.sin(a) * j,
                               cz + rz * cth * (1.0 + rnd.uniform(-jit, jit) * 0.6))))
        rgs.append(pts)
    f, _ = mb.pole_loft(rgs, col,
                        pole_a=(cx + rx * 0.05, cy, cz + rz * (1 + jit * 0.4)),
                        pole_b=(cx, cy + ry * 0.05, cz - rz), smooth=False)
    for fa in f:
        if not fa.is_valid:
            continue
        n = fa.normal
        if n.z > 0.45:
            fa[mb.lay] = mb.cid(col_lit)
        elif n.z < -0.30:
            fa[mb.lay] = mb.cid(col_dk)
    return f


def branch(mb, pts, radii, n=5):
    tube(mb, BARK, pts, radii, n=n, e=2.6, cap_a=False, cap_b=False, smooth=False)


# ================================================================= TREES
def tree_oak():
    """Broad, 6.2 m.  Heavy low fork, canopy wider than it is tall."""
    mb = MB()
    branch(mb, [Vector((0, 0, 0)), Vector((0.10, 0.06, 0.55)),
                Vector((0.04, -0.02, 1.45)), Vector((-0.06, 0.04, 2.15))],
           [0.42, 0.30, 0.24, 0.19], n=7)
    FORKS = [((-1.35, 0.35, 3.55), (-0.55, 0.16, 2.70)),
             ((1.30, -0.45, 3.30), (0.52, -0.20, 2.65)),
             ((0.25, 1.10, 3.75), (0.08, 0.45, 2.80)),
             ((-0.15, -0.85, 4.35), (-0.05, -0.32, 2.95))]
    for tip, mid in FORKS:
        branch(mb, [Vector((-0.06, 0.04, 2.10)), Vector(mid), Vector(tip)],
               [0.19, 0.13, 0.07])
    LOBES = [((-1.30, 0.30, 3.85), (1.55, 1.45, 1.10), 1),
             ((1.35, -0.40, 3.60), (1.40, 1.30, 1.00), 2),
             ((0.20, 1.15, 4.05), (1.25, 1.20, 0.95), 3),
             ((-0.20, -0.95, 4.60), (1.50, 1.40, 1.15), 4),
             ((0.55, 0.10, 5.15), (1.10, 1.05, 0.85), 5),
             ((-1.55, -0.55, 3.05), (0.85, 0.80, 0.62), 6)]
    for c, r, s in LOBES:
        lobe(mb, LEAF, LEAF_LIT, LEAF_DK, c, r, seg=7, nrings=4, jit=0.18, seed=s)
    # root flare
    for k in range(5):
        a = TAU * k / 5 + 0.3
        cyl(mb, BARK_LT, (0.30 * math.cos(a), 0.30 * math.sin(a), 0.02),
            (0.62 * math.cos(a), 0.62 * math.sin(a), 0.0), 0.14, 0.09, n=4)
    return mb.finish("Tree_Oak", MAT, 26.0)


def tree_poplar():
    """Tall, narrow, 9.2 m, leaning 4 deg.

    ONE CONTINUOUS COLUMN.  The previous build stacked eight lobes up a stick
    and it read as beads on a string -- every lobe's equator cut a step into the
    silhouette.  Here the whole canopy is a single lofted spindle and the lumps
    are RADIUS MODULATION on that one skin, so the outline is a ragged spire
    with no seams to step at.
    """
    mb = MB()
    branch(mb, [Vector((0, 0, 0)), Vector((0.06, 0.03, 2.0)),
                Vector((0.16, 0.05, 5.0)), Vector((0.30, 0.02, 8.4))],
           [0.30, 0.20, 0.14, 0.06], n=6)

    rnd = random.Random(31)
    SEG, NR = 7, 15
    Z0, Z1 = 1.06, 8.62
    rings = []
    for i in range(NR):
        t = i / (NR - 1.0)
        z = Z0 + (Z1 - Z0) * t
        # Spindle: swells fast off the trunk, then tapers to a spire.  Both ends
        # must keep a real radius -- a ring that collapses to a point welds into
        # a degenerate fan and leaves non-manifold edges that recalc cannot then
        # orient, which is how the first attempt came out inside-out.
        r = 1.20 * min(1.0, (0.10 + t) / 0.24) ** 0.70 * ((1.08 - t) / 1.08) ** 0.62
        r *= 1.0 + 0.145 * math.sin(t * TAU * 3.2 + 0.7)      # the lumps
        r = max(r, 0.17)
        cx = 0.02 + 0.30 * t + 0.10 * math.sin(t * 7.1)        # a wandering axis
        cy = 0.03 + 0.09 * math.sin(t * 5.3 + 1.9)
        pts = []
        for k in range(SEG):
            a = TAU * k / SEG + t * 0.55                       # twist: no seam line
            j = 1.0 + rnd.uniform(-0.13, 0.13)
            pts.append(Vector((cx + r * math.cos(a) * j * 1.02,
                               cy + r * math.sin(a) * j * 0.96,
                               z + rnd.uniform(-0.10, 0.10) * (1.0 - t * 0.6))))
        rings.append(pts)
    f, _ = mb.pole_loft(rings, LEAF, pole_a=(0.04, 0.02, 0.86),
                        pole_b=(0.32, 0.02, 9.06), smooth=False)
    for fa in f:
        if not fa.is_valid:
            continue
        n = fa.normal
        # a near-vertical column has almost no upward faces, so the usual
        # 0.42 threshold leaves it one flat green.  Bias low: this picks out the
        # top and underside of every lump and bands the column as foliage.
        if n.z > 0.20:
            fa[mb.lay] = mb.cid(LEAF_LIT)
        elif n.z < -0.16:
            fa[mb.lay] = mb.cid(LEAF_DK)
    # a few bare twigs breaking the outline, so it is not a smooth cone
    for (zt, ang, ln) in [(3.10, 0.7, 1.34), (4.70, 3.4, 1.16),
                          (6.15, 1.9, 0.84), (7.30, 4.6, 0.58)]:
        b0 = Vector((0.02 + 0.30 * (zt / 8.6), 0.03, zt))
        tip = b0 + Vector((math.cos(ang) * ln, math.sin(ang) * ln, 0.34))
        branch(mb, [b0, b0.lerp(tip, 0.55), tip], [0.075, 0.045, 0.020], n=4)

    ob = mb.finish("Tree_Poplar", MAT, 26.0)
    ob.data.transform(Euler((math.radians(3.0), math.radians(-4.0), 0), 'XYZ')
                      .to_matrix().to_4x4())
    return ob


def tree_apple():
    """Small, gnarled, 3.5 m, with fruit.  Three clear lobes, not one ball."""
    mb = MB()
    branch(mb, [Vector((0, 0, 0)), Vector((0.14, -0.08, 0.45)),
                Vector((-0.05, 0.06, 1.05)), Vector((0.06, -0.02, 1.45))],
           [0.24, 0.20, 0.17, 0.14], n=6)
    for tip, mid in [((-0.90, 0.20, 2.20), (-0.38, 0.10, 1.70)),
                     ((0.85, 0.30, 2.05), (0.36, 0.14, 1.65)),
                     ((0.10, -0.80, 2.40), (0.04, -0.34, 1.75))]:
        branch(mb, [Vector((0.06, -0.02, 1.42)), Vector(mid), Vector(tip)],
               [0.14, 0.10, 0.055])
    LOB = [((-0.85, 0.20, 2.42), (0.98, 0.92, 0.74), 21),
           ((0.82, 0.28, 2.30), (0.92, 0.88, 0.70), 22),
           ((0.10, -0.82, 2.62), (0.90, 0.86, 0.72), 23),
           ((0.02, 0.06, 2.95), (0.80, 0.78, 0.62), 24)]
    for c, r, s in LOB:
        lobe(mb, LEAF, LEAF_LIT, LEAF_DK, c, r, seg=7, nrings=3, jit=0.20, seed=s)
    rnd = random.Random(9)
    # fruit must sit ON the canopy surface, not buried inside it
    for k in range(10):
        a = TAU * k / 10 + 0.4
        rr = rnd.uniform(1.28, 1.62)
        sphere(mb, APPLE, (rr * math.cos(a), rr * math.sin(a) * 0.92,
                           rnd.uniform(1.95, 2.60)),
               (0.105, 0.105, 0.098), seg=5, rings=3)
    return mb.finish("Tree_Apple", MAT, 26.0)


# ================================================================ HEDGES
def _hedge_block(mb, pts2d, z_top, bump=0.055, seed=0):
    """Clipped hedge: a slightly domed, lumpy top and a cut-in trim seam."""
    rnd = random.Random(seed)
    lo = [mb.v((x, y, 0.0)) for (x, y) in pts2d]
    mid = [mb.v((x * 1.03, y * 1.03, z_top * 0.62)) for (x, y) in pts2d]
    hi = [mb.v((x * 0.94, y * 0.94, z_top + rnd.uniform(-bump, bump)))
          for (x, y) in pts2d]
    n = len(pts2d)
    faces = []
    for A, B in ((lo, mid), (mid, hi)):
        for i in range(n):
            j = (i + 1) % n
            faces.append(mb.f((A[i], A[j], B[j], B[i]), HEDGE))
    faces.append(mb.f(list(reversed(lo)), SOIL))
    faces.append(mb.f(hi, HEDGE_LIT))
    faces = [f for f in faces if f]
    for f in faces:
        f.smooth = False
    mb.bm.normal_update()
    seam = [f for f in faces if f.is_valid and abs(f.normal.z) < 0.4
            and f.calc_center_median().z > z_top * 0.55]
    recess(mb, seam, 0.030, -0.018, col=LEAF_DK)
    return faces


def hedge_straight():
    mb = MB()
    _hedge_block(mb, [(1.00, -0.34), (1.00, 0.34), (-1.00, 0.34), (-1.00, -0.34)],
                 0.98, seed=1)
    return mb.finish("Hedge_Straight", MAT, 26.0)


def hedge_corner():
    mb = MB()
    _hedge_block(mb, [(1.00, -0.34), (1.00, 0.34), (-0.34, 0.34), (-0.34, 1.00),
                      (-1.00, 1.00), (-1.00, -0.34)], 0.98, seed=2)
    return mb.finish("Hedge_Corner", MAT, 26.0)


def hedge_topiary_duck():
    """A hedge clipped into a duck.  Environmental storytelling: someone here
    takes this competition as seriously as the duck does."""
    mb = MB()
    _hedge_block(mb, [(0.62, -0.52), (0.62, 0.52), (-0.62, 0.52), (-0.62, -0.52)],
                 0.34, bump=0.02, seed=3)
    # body: a clipped teardrop, faceted like shears did it
    B = [(0.330, 0.44, 0.50, 0.06, 4.0), (0.560, 0.52, 0.60, 0.02, 3.4),
         (0.800, 0.50, 0.58, -0.04, 3.2), (1.010, 0.38, 0.44, -0.10, 3.0),
         (1.120, 0.26, 0.30, -0.13, 3.0)]
    rings = [ring_z(10, z, rx, ry, e, 0.0, cy) for (z, rx, ry, cy, e) in B]
    f, _ = mb.loft(rings, HEDGE, cap_a=False, cap_b=True, smooth=False)
    for fa in f:
        if fa.is_valid and fa.normal.z > 0.4:
            fa[mb.lay] = mb.cid(HEDGE_LIT)
    # neck + head + a stubby clipped bill
    tube(mb, HEDGE, [Vector((0, -0.12, 1.06)), Vector((0, -0.16, 1.28)),
                     Vector((0, -0.18, 1.46))], [0.20, 0.17, 0.19], n=8, e=3.0,
         cap_a=False, cap_b=False, smooth=False)
    hr = [ring_z(10, 1.400, 0.20, 0.22, 3.0, 0.0, -0.18),
          ring_z(10, 1.520, 0.25, 0.27, 3.0, 0.0, -0.19),
          ring_z(10, 1.640, 0.20, 0.22, 3.0, 0.0, -0.18)]
    h, _ = mb.pole_loft(hr, HEDGE, pole_b=(0, -0.17, 1.72), smooth=False)
    for fa in h:
        if fa.is_valid and fa.normal.z > 0.45:
            fa[mb.lay] = mb.cid(HEDGE_LIT)
    rbox(mb, HEDGE_LIT, (0, -0.44, 1.50), (0.26, 0.32, 0.13), r=0.03, n=6, e=4.0, k=1)
    # a clipped tail, cocked up
    tube(mb, HEDGE, [Vector((0, 0.42, 0.86)), Vector((0, 0.62, 1.00)),
                     Vector((0, 0.74, 1.10))], [0.24, 0.17, 0.07], n=7, e=3.4,
         squash=[(0.8, 1.0)] * 3, smooth=False)
    return mb.finish("Hedge_Topiary_Duck", MAT, 26.0)


# ========================================================== POND DRESSING
def reeds():
    mb = MB()
    rnd = random.Random(5)
    for k in range(15):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0.0, 0.32)
        bx, by = r * math.cos(a), r * math.sin(a)
        h = rnd.uniform(0.72, 1.24)
        lean = rnd.uniform(0.10, 0.30)
        la = rnd.uniform(0, TAU)
        tip = Vector((bx + lean * math.cos(la), by + lean * math.sin(la), h))
        w = rnd.uniform(0.028, 0.042)
        a0 = mb.v((bx - w, by, 0.0)); b0 = mb.v((bx + w, by, 0.0))
        m0 = mb.v((bx + w * 0.4 + (tip.x - bx) * 0.5, by + (tip.y - by) * 0.5, h * 0.55))
        m1 = mb.v((bx - w * 0.4 + (tip.x - bx) * 0.5, by + (tip.y - by) * 0.5, h * 0.55))
        t0 = mb.v(tip)
        mb.f((a0, b0, m0, m1), LEAF_LIT if k % 3 else LEAF)
        mb.f((m1, m0, t0), LEAF_LIT if k % 3 else LEAF)
        if k % 4 == 0:      # a bulrush head
            cyl(mb, BARK, tip + Vector((0, 0, -0.10)), tip + Vector((0, 0, 0.16)),
                0.036, 0.030, n=5)
    mb.bm.normal_update()
    for f in mb.bm.faces:
        f.smooth = False
    return mb.finish("Reeds", MAT, 26.0)


def lilypad():
    mb = MB()
    N = 12
    pts = []
    for i in range(N):
        a = TAU * i / N
        r = 0.26 if (i not in (0, 1)) else 0.10       # the classic notch
        pts.append(Vector((r * math.cos(a), r * math.sin(a), 0.018)))
    lo = [Vector((p.x * 0.98, p.y * 0.98, 0.0)) for p in pts]
    f, _ = mb.loft([lo, pts], LEAF, cap_a=True, cap_b=True, smooth=False)
    for fa in f:
        if fa.is_valid and fa.normal.z > 0.5:
            fa[mb.lay] = mb.cid(LEAF_LIT)
    # veins cut in
    top = [fa for fa in f if fa.is_valid and fa.normal.z > 0.8]
    recess(mb, top, 0.035, -0.004, col=LEAF)
    # flower
    for k in range(6):
        a = TAU * k / 6
        cyl(mb, BLOSSOM, (0.02, 0.0, 0.030),
            (0.02 + 0.085 * math.cos(a), 0.085 * math.sin(a), 0.070),
            0.028, 0.012, n=4)
    sphere(mb, PETAL_B, (0.02, 0.0, 0.052), (0.030, 0.030, 0.026), seg=6, rings=3)
    return mb.finish("Lilypad", MAT, 26.0)


def flowerbed():
    mb = MB()
    out = [(0.78, -0.36), (0.80, 0.0), (0.78, 0.36), (0.30, 0.42),
           (-0.30, 0.42), (-0.78, 0.36), (-0.80, 0.0), (-0.78, -0.36),
           (-0.30, -0.42), (0.30, -0.42)]
    lo = [mb.v((x, y, 0.0)) for (x, y) in out]
    hi = [mb.v((x * 0.90, y * 0.86, 0.150)) for (x, y) in out]
    n = len(out)
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append(mb.f((lo[i], lo[j], hi[j], hi[i]), DIRT))
    faces.append(mb.f(hi, SOIL))
    for f in faces:
        if f:
            f.smooth = False
    mb.bm.normal_update()
    rnd = random.Random(13)
    cols = [PETAL_A, PETAL_B, PETAL_C, BLOSSOM]
    for k in range(16):
        x = rnd.uniform(-0.66, 0.66)
        y = rnd.uniform(-0.30, 0.30)
        h = rnd.uniform(0.16, 0.32)
        cyl(mb, LEAF, (x, y, 0.13), (x + rnd.uniform(-0.03, 0.03), y, 0.13 + h),
            0.016, 0.011, n=4)
        sphere(mb, cols[k % 4], (x + rnd.uniform(-0.03, 0.03), y, 0.15 + h),
               (0.058, 0.058, 0.044), seg=5, rings=3)
    return mb.finish("Flowerbed", MAT, 26.0)


ITEMS = [("Tree_Oak", tree_oak), ("Tree_Poplar", tree_poplar),
         ("Tree_Apple", tree_apple), ("Hedge_Straight", hedge_straight),
         ("Hedge_Corner", hedge_corner), ("Hedge_Topiary_Duck", hedge_topiary_duck),
         ("Reeds", reeds), ("Lilypad", lilypad), ("Flowerbed", flowerbed)]


def main():
    fresh_scene()
    objs, layout = [], []
    for i, (name, fn) in enumerate(ITEMS):
        ob = fn()
        gx, gy = (i % 3) * 14.0 - 14.0, (i // 3) * 14.0 - 14.0
        ob.data.transform(Matrix.Translation(Vector((gx, gy, 0))))
        bake_ao([ob], floor=0.72, dist=1.20, samples=24, ground=True, ground_size=8.0)
        set_pivot(ob, (gx, gy, 0))
        objs.append(ob)
        layout.append((ob, (gx, gy, 0)))

    report_tris(objs, "Foliage")
    for ob, p in layout:
        ob.location = (0, 0, 0)
    L.verify_meshes(objs, "Foliage")
    export_fbx(objs, os.path.join(L.MODELS_DIR, "Foliage.fbx"))
    for ob, p in layout:
        ob.location = p
    L.render_gallery(objs, "foliage", res=340, cols=3)
    L.render_gallery(objs, "foliage", res=300, cols=3, sil=True)
    print("DONE Foliage")


main()
