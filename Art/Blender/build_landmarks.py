# build_landmarks.py — DUCK MOW: the skyline.  One file, many roots.
# Run: blender --background --python C:\Duck\Art\Blender\build_landmarks.py
#   Barn      11 m ridge, the hero landmark        <=2500 tris
#   Windmill  16 m, Windmill_Blades is a child pivoted at the hub  <=2000 tris
#   Tent_A / Tent_B  striped refreshment tents
#   Stands    one repeatable section of tiered seating
import bpy, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_Landmarks"
RED, RED_D, CREAM, WHITE = "D6423C", "A32E2D", "F4E7CF", "F1EDE0"
TENT_R, TENT_C = "D8534E", "F5EAD6"
SLATE, BRASS, DARK = "4A4F55", "C9A55A", "241F1E"
WOOD, WOOD_D, DIRT = "9A6B41", "6E4A2C", "B99A6B"


def prof(mb, col, pts2d, z0, z1, col_top=None, col_bot=None, smooth=False):
    bot = [mb.v((x, y, z0)) for (x, y) in pts2d]
    top = [mb.v((x, y, z1)) for (x, y) in pts2d]
    faces, n = [], len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        faces.append(mb.f((bot[i], bot[j], top[j], top[i]), col))
    faces.append(mb.f(list(reversed(bot)), col_bot or col))
    faces.append(mb.f(top, col_top or col))
    faces = [f for f in faces if f]
    for f in faces:
        f.smooth = smooth
    mb.bm.normal_update()
    return faces


def panel(mb, col, x0, x1, y, z0, z1, col_back=None):
    a = mb.v((x0, y, z0)); b = mb.v((x1, y, z0))
    c = mb.v((x1, y, z1)); d = mb.v((x0, y, z1))
    f1 = mb.f((a, b, c, d), col)
    f2 = mb.f((d, c, b, a), col_back or col)
    for f in (f1, f2):
        if f:
            f.smooth = False
    mb.bm.normal_update()
    return [f1, f2]


# ==================================================================== BARN
def barn():
    mb = MB()
    # One lofted shell: walls and gambrel roof are the same skin, so the
    # eave line is a real edge rather than two boxes meeting.
    P = [(5.50, 0.00), (5.50, 4.85), (5.86, 4.82), (5.74, 5.20),
         (4.45, 8.15), (0.00, 11.00), (-4.45, 8.15), (-5.74, 5.20),
         (-5.86, 4.82), (-5.50, 4.85), (-5.50, 0.00)]
    rings = []
    for y in (-7.50, -3.00, 3.00, 7.50):
        rings.append([Vector((x, y, z)) for (x, z) in P])
    shell, _ = mb.loft(rings, RED, cap_a=True, cap_b=True, smooth=False)
    for f in shell:
        if f.is_valid and f.calc_center_median().z > 4.95:
            f[mb.lay] = mb.cid(SLATE)
    # cream trim: a skirt along the bottom and a band under the eaves
    skirt = [f for f in shell if f.is_valid and abs(f.normal.z) < 0.4
             and f.calc_center_median().z < 1.20]
    recess(mb, skirt, 0.10, -0.05, col=CREAM)
    # windows cut into the long sides
    for sx in (-1, 1):
        for y in (-5.4, -1.8, 1.8, 5.4):
            panel(mb, CREAM, sx * 5.52, sx * 5.52, y, 0, 0)   # placeholder no-op
    for sx in (-1, 1):
        for y in (-5.2, -1.6, 2.0, 5.6):
            prof(mb, CREAM, [(sx * 5.55, y - 0.62), (sx * 5.55, y + 0.62),
                             (sx * 5.42, y + 0.62), (sx * 5.42, y - 0.62)],
                 2.35, 3.85)
            prof(mb, SLATE, [(sx * 5.60, y - 0.48), (sx * 5.60, y + 0.48),
                             (sx * 5.50, y + 0.48), (sx * 5.50, y - 0.48)],
                 2.50, 3.70)
    # the big gable doors, with cream barn-door X bracing
    for sx in (-1, 1):
        prof(mb, CREAM, [(sx * 0.10, -7.62), (sx * 2.00, -7.62),
                         (sx * 2.00, -7.50), (sx * 0.10, -7.50)], 0.0, 4.60)
        for k in (-1, 1):
            tube(mb, RED_D, [Vector((sx * 0.22, -7.66, 0.25)),
                             Vector((sx * 1.88, -7.66, 4.35))][::k],
                 0.10, n=4, e=4.0, squash=[(1.0, 0.35)] * 2, smooth=False)
        prof(mb, RED_D, [(sx * 0.14, -7.66), (sx * 1.96, -7.66),
                         (sx * 1.96, -7.63), (sx * 0.14, -7.63)], 2.20, 2.42)
    # hayloft door + hoist beam
    prof(mb, CREAM, [(-1.05, -7.60), (1.05, -7.60), (1.05, -7.48), (-1.05, -7.48)],
         5.60, 7.40)
    cyl(mb, WOOD_D, (0, -7.55, 7.85), (0, -8.55, 7.95), 0.16, 0.13, n=6)
    # cream corner boards
    for sx in (-1, 1):
        for sy in (-1, 1):
            prof(mb, CREAM, [(sx * 5.56, sy * 7.56), (sx * 5.20, sy * 7.56),
                             (sx * 5.20, sy * 7.20), (sx * 5.56, sy * 7.20)], 0.0, 4.90)
    # cupola + brass duck weathervane
    prof(mb, CREAM, [(0.62, 2.62), (0.62, 3.86), (-0.62, 3.86), (-0.62, 2.62)],
         10.85, 12.10)
    rings = [ring_z(4, 12.10, 0.90, 0.90, 9.0, 0.0, 3.24, rot=math.pi / 4),
             ring_z(4, 12.95, 0.30, 0.30, 9.0, 0.0, 3.24, rot=math.pi / 4)]
    mb.loft(rings, SLATE, cap_a=False, cap_b=True, smooth=False)
    cyl(mb, BRASS, (0, 3.24, 12.90), (0, 3.24, 13.70), 0.06, n=5)
    sphere(mb, BRASS, (0, 3.10, 13.85), (0.14, 0.20, 0.16), seg=6, rings=3)
    cyl(mb, BRASS, (0, 2.92, 13.87), (0, 2.62, 13.84), 0.07, 0.04, n=5)
    return mb.finish("Barn", MAT, 30.0)


# ================================================================ WINDMILL
def windmill():
    mb = MB()
    rings = [ring_z(10, 0.00, 2.30, 2.30, 4.0), ring_z(10, 0.55, 2.18, 2.18, 3.4),
             ring_z(10, 3.20, 1.90, 1.90, 3.2), ring_z(10, 6.20, 1.58, 1.58, 3.2),
             ring_z(10, 9.10, 1.32, 1.32, 3.2), ring_z(10, 9.55, 1.44, 1.44, 3.2)]
    tower, _ = mb.loft(rings, TENT_C, cap_a=True, cap_b=False, smooth=False)
    for f in tower:
        if f.is_valid and f.calc_center_median().z < 0.60:
            f[mb.lay] = mb.cid(WOOD_D)     # a stone plinth, not a lighthouse band
    # tall narrow windows cut into the tower
    for k in range(3):
        z = 2.4 + k * 2.5
        w = [f for f in tower if f.is_valid and f.normal.y < -0.55
             and abs(f.calc_center_median().z - z) < 1.2]
        recess(mb, w[:1], 0.34, -0.14, col=SLATE)
    # gallery
    torus(mb, WOOD_D, (0, 0, 5.30), (0, 0, 1), 2.02, 0.10, nmaj=12, nmin=4)
    torus(mb, WOOD_D, (0, 0, 6.10), (0, 0, 1), 2.02, 0.07, nmaj=12, nmin=4)
    for k in range(10):
        a = TAU * k / 10
        cyl(mb, WOOD_D, (2.0 * math.cos(a), 2.0 * math.sin(a), 5.30),
            (2.0 * math.cos(a), 2.0 * math.sin(a), 6.10), 0.045, n=4)
    # door
    prof(mb, WOOD_D, [(-0.60, -2.30), (0.60, -2.30), (0.60, -2.18), (-0.60, -2.18)],
         0.0, 2.00)
    # cap
    rings = [ring_z(10, 9.55, 1.52, 1.52, 3.2), ring_z(10, 10.30, 1.46, 1.60, 3.0),
             ring_z(10, 10.95, 1.06, 1.24, 2.6)]
    mb.loft(rings, SLATE, cap_a=False, cap_b=False, smooth=False)
    mb.pole_loft([ring_z(10, 10.95, 1.06, 1.24, 2.6)] +
                 [ring_z(10, 11.25, 0.70, 0.84, 2.4)], SLATE,
                 pole_b=(0, 0.10, 11.55), smooth=False)
    cyl(mb, WOOD_D, (0, -1.30, 10.30), (0, -0.20, 10.30), 0.34, 0.30, n=8)
    cyl(mb, BRASS, (0, -1.55, 10.30), (0, -1.28, 10.30), 0.40, 0.36, n=10)
    tower_ob = mb.finish("Windmill", MAT, 30.0)

    # ---- blades: separate child, pivot at the hub, spins about local Y ------
    mb = MB()
    HUB = Vector((0, -1.68, 10.30))
    cyl(mb, BRASS, (0, HUB.y - 0.10, HUB.z), (0, HUB.y + 0.22, HUB.z), 0.28, 0.24, n=10)
    for k in range(4):
        a = TAU * k / 4 + math.radians(18)
        ux, uz = math.cos(a), math.sin(a)
        px, pz = -math.sin(a), math.cos(a)
        for t in (0.0, 1.0):
            pass
        # spar
        cyl(mb, TENT_C, (HUB.x, HUB.y, HUB.z),
            (HUB.x + ux * 5.60, HUB.y, HUB.z + uz * 5.60), 0.13, 0.07, n=5)
        # sail frame + red slats
        for s in range(6):
            t0 = 0.30 + s * 0.115
            cx = HUB.x + ux * (t0 * 5.60)
            cz = HUB.z + uz * (t0 * 5.60)
            col = TENT_R if s % 2 == 0 else TENT_C
            cyl(mb, col, (cx - px * 0.04, HUB.y + 0.10, cz - pz * 0.04),
                (cx + px * 0.86, HUB.y + 0.10, cz + pz * 0.86), 0.055, n=4)
        cyl(mb, TENT_C, (HUB.x + ux * 1.60 + px * 0.86, HUB.y + 0.10, HUB.z + uz * 1.60 + pz * 0.86),
            (HUB.x + ux * 5.50 + px * 0.86, HUB.y + 0.10, HUB.z + uz * 5.50 + pz * 0.86),
            0.055, n=4)
    blades = mb.finish("Windmill_Blades", MAT, 30.0)
    return tower_ob, blades, tuple(HUB)


# =================================================================== TENTS
def tent_a():
    """Open-sided marquee, 5.0 x 4.0, ridge 3.6."""
    mb = MB()
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(mb, TENT_C, (sx * 2.40, sy * 1.90, 0.0), (sx * 2.40, sy * 1.90, 2.45),
                0.10, 0.085, n=6)
    NS = 8
    # Canvas gets REAL THICKNESS.  The old roof was one single-sided quad per
    # stripe whose reversed twin bmesh silently refused, so half the marquee
    # disappeared under back-face culling and the ridge came out non-manifold.
    for i in range(NS):
        y0 = -1.98 + 3.96 * i / NS + 0.0008
        y1 = -1.98 + 3.96 * (i + 1) / NS - 0.0008
        c = TENT_R if i % 2 == 0 else TENT_C
        for sx in (-1, 1):
            mb.slab([(sx * 2.62, y0, 2.42), (sx * 2.62, y1, 2.42),
                     (sx * 0.010, y1, 3.60), (sx * 0.010, y0, 3.60)],
                    c, (0.0, 0.0, -0.030), col_bot=TENT_C)
    mb.bm.normal_update()
    # scalloped valance along both eaves, each scallop a closed 70 mm slab
    for sx in (-1, 1):
        for i in range(NS):
            y0 = -1.98 + 3.96 * i / NS + 0.0008
            y1 = -1.98 + 3.96 * (i + 1) / NS - 0.0008
            c = TENT_R if i % 2 == 0 else TENT_C
            mb.slab([(sx * 2.66, y0, 2.46), (sx * 2.66, y1, 2.46),
                     (sx * 2.66, (y0 + y1) * 0.5, 2.02)],
                    c, (-sx * 0.070, 0.0, -0.004), col_bot=TENT_C)
    mb.bm.normal_update()
    cyl(mb, WOOD_D, (0, -2.02, 3.60), (0, 2.02, 3.60), 0.07, n=5)
    # a counter under it
    rbox(mb, WOOD, (0, 1.55, 0.55), (4.20, 0.60, 1.10), r=0.05, n=8, e=5.0, k=1)
    rbox(mb, TENT_R, (0, 1.24, 0.95), (4.30, 0.06, 0.34), r=0.03, n=6, e=5.0, k=1)
    return mb.finish("Tent_A", MAT, 30.0)


def tent_b():
    """Closed hexagonal peaked tent, 4.0 across, 4.2 tall, with a door flap."""
    mb = MB()
    N = 8
    base = ring_z(N, 0.0, 2.00, 2.00, 2.0)
    eave = ring_z(N, 2.20, 2.00, 2.00, 2.0)
    lip = ring_z(N, 2.28, 2.22, 2.22, 2.0)
    walls, _ = mb.loft([base, eave], TENT_C, cap_a=True, cap_b=False, smooth=False)
    for i, f in enumerate(walls):
        if f.is_valid and i % 2 == 0:
            f[mb.lay] = mb.cid(TENT_R)
    mb.loft([eave, lip], TENT_C, cap_a=False, cap_b=False, smooth=False)
    roof, _ = mb.pole_loft([lip, ring_z(N, 3.30, 1.30, 1.30, 2.0)], TENT_C,
                           pole_b=(0, 0, 4.20), smooth=False)
    for i, f in enumerate(roof):
        if f.is_valid and i % 2 == 0:
            f[mb.lay] = mb.cid(TENT_R)
    # door flap, tied back
    prof(mb, DARK, [(-0.62, -2.02), (0.62, -2.02), (0.62, -1.96), (-0.62, -1.96)],
         0.0, 1.85)
    tube(mb, TENT_C, [Vector((-0.66, -2.10, 1.85)), Vector((-0.92, -2.02, 1.05)),
                      Vector((-0.72, -2.02, 0.10))], [0.10, 0.16, 0.10], n=5,
         e=4.0, squash=[(1.0, 0.35)] * 3, smooth=False)
    sphere(mb, BRASS, (0, -0.06, 4.28), (0.16, 0.16, 0.20), seg=6, rings=3)
    cyl(mb, BRASS, (0, 0, 4.18), (0, 0, 4.62), 0.05, n=5)
    return mb.finish("Tent_B", MAT, 30.0)


# ================================================================== STANDS
def stands():
    """One repeatable section: 6.0 m wide, 4 tiers, 2.35 m to the back rail."""
    mb = MB()
    W, TIERS = 3.00, 4
    for t in range(TIERS):
        y = 0.55 + t * 0.85
        z = 0.40 + t * 0.46
        rbox(mb, WOOD, (0, y, z), (2 * W - 0.10, 0.72, 0.10), r=0.03, n=6, e=6.0, k=1)
        rbox(mb, CREAM, (0, y + 0.36, z + 0.20), (2 * W - 0.10, 0.09, 0.32),
             r=0.03, n=6, e=6.0, k=1)
        rbox(mb, WOOD_D, (0, y - 0.34, z - 0.22), (2 * W - 0.30, 0.09, 0.36),
             r=0.03, n=6, e=6.0, k=1)
    for sx in (-1, 1):     # raked side frames
        for k in range(3):
            x = sx * (W - 0.12) - sx * k * 0.0
            pts = [Vector((x, 0.20 + k * 1.30, 0.0)),
                   Vector((x, 0.20 + k * 1.30, 0.38 + k * 0.72))]
            cyl(mb, WOOD_D, pts[0], pts[1], 0.09, n=5)
        cyl(mb, WOOD_D, (sx * (W - 0.12), 0.16, 0.30),
            (sx * (W - 0.12), 3.30, 2.05), 0.07, n=5)
    # back rail
    for sx in (-1, 1):
        cyl(mb, CREAM, (sx * (W - 0.12), 3.35, 1.94), (sx * (W - 0.12), 3.35, 2.60),
            0.07, n=5)
    cyl(mb, CREAM, (-W, 3.35, 2.52), (W, 3.35, 2.52), 0.065, n=5)
    cyl(mb, CREAM, (-W, 3.35, 2.20), (W, 3.35, 2.20), 0.055, n=5)
    return mb.finish("Stands", MAT, 30.0)


# =============================================================== BUILD
def main():
    fresh_scene()
    b = barn()
    wt, wb, hub = windmill()
    ta, tb, st = tent_a(), tent_b(), stands()

    groups = [("Barn", [b], [(b, (0, 0, 0))], None),
              ("Windmill", [wt, wb], [(wt, (0, 0, 0)), (wb, hub)], wt),
              ("Tent_A", [ta], [(ta, (0, 0, 0))], None),
              ("Tent_B", [tb], [(tb, (0, 0, 0))], None),
              ("Stands", [st], [(st, (0, 0, 0))], None)]

    allobjs, allmesh, layout = [], [], []
    ox = -30.0
    for name, meshes, pivots, parent in groups:
        bake_ao(meshes, floor=0.74, dist=1.10, samples=24, ground=True, ground_size=40.0)
        for ob, p in pivots:
            set_pivot(ob, p)
        if parent is not None:
            for ob, p in pivots:
                if ob is not parent:
                    attach(ob, parent)
        allobjs += meshes
        allmesh += meshes
        report_tris(meshes, name)
        layout.append((meshes[0], ox))
        ox += 22.0

    L.verify_meshes(allmesh, "Landmarks")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "Landmarks.fbx"))
    for ob, x in layout:
        ob.location = (x, 0, 0)
    L.render_gallery([b, ta, tb, st], "landmarks", res=380, cols=3)
    L.render_previews([wt, wb], "landmark_windmill", res=460, solo=True)
    print("DONE Landmarks")


main()
