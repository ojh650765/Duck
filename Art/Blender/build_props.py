# build_props.py — DUCK MOW: the dressing kit.  One file, many roots.
# Run: blender --background --python C:\Duck\Art\Blender\build_props.py
# Every prop is a single mesh object (= one draw call), pivot at its base centre,
# facing -Y, built at Z=0.  Budget 600 tris each.
import bpy, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc,
                      torus, rbox, recess, set_pivot, make_empty, attach, card_uv,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_Props"
WHITE, CREAM, CHALK = "F1EDE0", "F4E7CF", "F7F3E4"
RED, RED_D, TENT_R, TENT_C = "D6423C", "A32E2D", "D8534E", "F5EAD6"
GREY, BRASS, DARK = "4A4F55", "C9A55A", "241F1E"
WOOD, WOOD_D, DIRT = "9A6B41", "6E4A2C", "B99A6B"
WATER, HEDGE = "3E86A8", "2A5A34"
STRAW, STRAW_D = "D8C384", "B99A6B"


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


def plank(mb, col, c, size, r=0.010, n=8, e=6.0, col_top=None):
    f = rbox(mb, col, c, size, r=r, n=n, e=e, k=1)
    if col_top:
        for fa in f:
            if fa.is_valid and fa.normal.z > 0.7:
                fa[mb.lay] = mb.cid(col_top)
    return f


# ============================================================ props
def fence_post(mb):
    # a square picket with chamfered arrises and a pointed cap - not a dowel
    rings = [ring_z(8, 0.000, 0.055, 0.055, 9.0), ring_z(8, 0.070, 0.052, 0.052, 9.0),
             ring_z(8, 0.870, 0.046, 0.046, 9.0), ring_z(8, 0.930, 0.046, 0.046, 9.0),
             ring_z(8, 0.965, 0.040, 0.040, 7.0)]
    f, _ = mb.loft(rings, WHITE, cap_a=True, cap_b=False, smooth=False)
    f2, _ = mb.pole_loft([ring_z(8, 0.965, 0.040, 0.040, 7.0)] * 1 +
                         [ring_z(8, 0.985, 0.036, 0.036, 6.0)],
                         WHITE, pole_b=(0, 0, 1.050), smooth=False)
    for fa in f:
        if fa.is_valid and fa.calc_center_median().z < 0.10:
            fa[mb.lay] = mb.cid("DCC9A4")
    # the two rail mortices, cut in
    for z in (0.42, 0.76):
        m = [fa for fa in f if fa.is_valid and abs(fa.normal.x) > 0.7
             and abs(fa.calc_center_median().z - z) < 0.20]
        recess(mb, m[:2], 0.006, -0.004, col="DCC9A4")


def fence_rail(mb):
    # 2 m rail with a real sag; ends squared for butt joints
    pts, rad = [], []
    for i in range(5):
        t = i / 4.0
        pts.append(Vector((-1.0 + 2.0 * t, 0.0, 0.05 - 0.024 * math.sin(math.pi * t))))
        rad.append(0.056)
    tube(mb, WHITE, pts, rad, n=6, e=7.0, squash=[(0.52, 1.0)] * 5, smooth=False)


def hay_bale(mb):
    # rectangular bale, straw grain cut in, two twine bands
    out = [(0.44, -0.26), (0.46, 0.00), (0.44, 0.26), (0.20, 0.30), (-0.20, 0.30),
           (-0.44, 0.26), (-0.46, 0.00), (-0.44, -0.26), (-0.20, -0.30), (0.20, -0.30)]
    f = prof(mb, STRAW, out, 0.0, 0.52, col_top=STRAW, col_bot=STRAW_D)
    ends = [fa for fa in f if fa.is_valid and abs(fa.normal.x) > 0.8]
    recess(mb, ends, 0.045, -0.020, col=STRAW_D)
    top = [fa for fa in f if fa.is_valid and fa.normal.z > 0.8]
    recess(mb, top, 0.040, -0.014, col=STRAW_D)
    for y in (-0.16, 0.16):
        prof(mb, WOOD_D, [(0.47, y - 0.018), (0.47, y + 0.018), (-0.47, y + 0.018),
                          (-0.47, y - 0.018)], 0.0, 0.535)


def gnome(mb):
    # bonkable, and built to keep his dignity face-down in a pond:
    # wide low boots, a heavy coat, and a hat that reads from every angle.
    rings = [ring_z(10, 0.000, 0.120, 0.128, 3.0), ring_z(10, 0.045, 0.132, 0.140, 2.6),
             ring_z(10, 0.118, 0.130, 0.136, 2.4), ring_z(10, 0.156, 0.126, 0.132, 2.3),
             ring_z(10, 0.235, 0.104, 0.108, 2.1)]
    f, _ = mb.loft(rings, WATER, cap_a=True, cap_b=False, smooth=False)
    for fa in f:
        if fa.is_valid and fa.calc_center_median().z < 0.050:
            fa[mb.lay] = mb.cid(WOOD_D)
    # belt: one narrow band only (a wider pick swallows the whole coat)
    b = [fa for fa in f if fa.is_valid
         and abs(fa.calc_center_median().z - 0.137) < 0.014
         and abs(fa.normal.z) < 0.5]
    recess(mb, b, 0.008, -0.006, col=WOOD_D)
    sphere(mb, "F6EBD2", (0, -0.012, 0.278), (0.088, 0.092, 0.078), seg=10, rings=4)
    # the beard is the silhouette
    rings = [ring_z(10, 0.300, 0.086, 0.082, 2.4, 0.0, -0.030),
             ring_z(10, 0.232, 0.096, 0.090, 2.2, 0.0, -0.044),
             ring_z(10, 0.170, 0.070, 0.066, 2.0, 0.0, -0.048),
             ring_z(10, 0.132, 0.030, 0.028, 2.0, 0.0, -0.046)]
    mb.loft(rings, WHITE, cap_a=False, cap_b=True, smooth=False)
    sphere(mb, "D3792A", (0, -0.080, 0.300), (0.026, 0.024, 0.024), seg=6, rings=3)
    for sx in (-1, 1):
        sphere(mb, DARK, (sx * 0.038, -0.070, 0.330), (0.012, 0.011, 0.012), seg=5, rings=2)
    # tall floppy hat, leaning
    hp = [Vector((0, -0.008, 0.330)), Vector((0.010, 0.010, 0.400)),
          Vector((0.030, 0.030, 0.452)), Vector((0.058, 0.044, 0.478))]
    tube(mb, TENT_R, hp, [0.112, 0.078, 0.040, 0.010], n=10, cap_a=True, smooth=False)


def sprinkler(mb):
    # low cream sled base, brass riser, spinning arms - reads at a glance
    rings = [ring_z(10, 0.000, 0.116, 0.088, 3.0), ring_z(10, 0.030, 0.110, 0.082, 2.4),
             ring_z(10, 0.062, 0.076, 0.056, 2.2)]
    mb.loft(rings, CREAM, smooth=False)
    cyl(mb, TENT_R, (0, 0, 0.055), (0, 0, 0.104), 0.044, 0.036, n=8)
    cyl(mb, BRASS, (0, 0, 0.100), (0, 0, 0.196), 0.024, 0.021, n=8)
    cyl(mb, BRASS, (0, 0, 0.192), (0, 0, 0.228), 0.040, 0.034, n=8)
    for sx in (-1, 1):
        tube(mb, BRASS, [Vector((0, 0, 0.216)), Vector((sx * 0.086, -0.026, 0.246)),
                         Vector((sx * 0.150, -0.062, 0.230))],
             [0.016, 0.012, 0.008], n=6, smooth=False)
    sphere(mb, BRASS, (0, 0, 0.238), (0.026, 0.026, 0.022), seg=8, rings=3)


def wheelbarrow(mb):
    # tray
    lo = [(0.16, -0.24), (0.16, 0.22), (-0.16, 0.22), (-0.16, -0.24)]
    hi = [(0.30, -0.36), (0.28, 0.30), (-0.28, 0.30), (-0.30, -0.36)]
    bot = [mb.v((x, y, 0.300)) for (x, y) in lo]
    top = [mb.v((x, y, 0.520)) for (x, y) in hi]
    for i in range(4):
        j = (i + 1) % 4
        mb.f((bot[i], bot[j], top[j], top[i]), RED)
    mb.f(list(reversed(bot)), RED_D)
    mb.bm.normal_update()
    for sx in (-1, 1):     # handles
        tube(mb, WOOD, [Vector((sx * 0.20, -0.34, 0.330)), Vector((sx * 0.20, 0.34, 0.400)),
                        Vector((sx * 0.20, 0.60, 0.430))],
             [0.024, 0.022, 0.026], n=6, smooth=False)
        cyl(mb, GREY, (sx * 0.20, 0.16, 0.300), (sx * 0.22, 0.24, 0.030), 0.016, n=5)
    torus(mb, DARK, (0, -0.40, 0.150), (1, 0, 0), 0.135, 0.036, nmaj=12, nmin=5)
    cyl(mb, CREAM, (-0.030, -0.40, 0.150), (0.030, -0.40, 0.150), 0.052, n=8)
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * 0.045, -0.40, 0.150), (sx * 0.10, -0.26, 0.320), 0.014, n=5)


def bench(mb):
    # The judges' bench: a table they sit BEHIND (top 0.72) plus a seat plank at
    # z = 0.45, which is where the judge roots go.  Kept as three clearly
    # separated masses so it never reads as one slab.
    for sx in (-1, 1):                       # table legs
        plank(mb, WOOD_D, (sx * 1.14, -0.10, 0.345), (0.085, 0.085, 0.690), r=0.014)
        plank(mb, WOOD_D, (sx * 1.14, 0.16, 0.345), (0.075, 0.075, 0.690), r=0.014)
        plank(mb, WOOD_D, (sx * 0.98, 0.52, 0.215), (0.070, 0.070, 0.430), r=0.012)
    f = plank(mb, WOOD, (0, 0.02, 0.750), (2.62, 0.62, 0.070), r=0.018, n=10)
    top = [fa for fa in f if fa.is_valid and fa.normal.z > 0.8]
    recess(mb, top, 0.045, -0.008, col=WOOD)          # planked top
    # front skirt panel, hung below the table edge
    g = plank(mb, CREAM, (0, -0.28, 0.560), (2.56, 0.055, 0.300), r=0.014, n=8)
    fr = [fa for fa in g if fa.is_valid and fa.normal.y < -0.7]
    recess(mb, fr, 0.030, -0.006, col=TENT_R)
    # the seat, well behind the table
    plank(mb, WOOD, (0, 0.52, 0.450), (2.30, 0.34, 0.062), r=0.016, n=10)
    for sx in (-1, 1):
        cyl(mb, BRASS, (sx * 0.9, -0.312, 0.610), (sx * 0.9, -0.340, 0.610), 0.016, n=6)


def awning(mb):
    # striped canopy over the judges, scalloped front edge, on two posts
    for sx in (-1, 1):
        cyl(mb, CREAM, (sx * 1.42, 0.30, 0.0), (sx * 1.42, 0.30, 2.20), 0.045, 0.038, n=8)
        cyl(mb, CREAM, (sx * 1.42, -0.62, 0.0), (sx * 1.42, -0.62, 1.98), 0.045, 0.038, n=8)
    NS = 10
    # Each stripe is its own closed 22 mm slab, inset 0.6 mm so neighbours do
    # not weld into a shared non-manifold edge.  This used to be a single
    # one-sided quad per stripe plus a reversed twin that bmesh silently
    # dropped, so the canopy vanished when viewed from underneath.
    for i in range(NS):
        x0 = -1.45 + 2.90 * i / NS + 0.0006
        x1 = -1.45 + 2.90 * (i + 1) / NS - 0.0006
        c = TENT_R if i % 2 == 0 else TENT_C
        mb.slab([(x0, 0.34, 2.24), (x1, 0.34, 2.24), (x1, -0.68, 2.02),
                 ((x0 + x1) * 0.5, -0.76, 1.94), (x0, -0.68, 2.02)],
                c, (0.0, 0.006, -0.022), col_bot=CREAM)
    mb.bm.normal_update()
    cyl(mb, WOOD_D, (-1.46, 0.34, 2.245), (1.46, 0.34, 2.245), 0.030, n=6)
    cyl(mb, WOOD_D, (-1.46, -0.68, 2.025), (1.46, -0.68, 2.025), 0.026, n=6)


def scoreboard(mb):
    for sx in (-1, 1):
        cyl(mb, WOOD_D, (sx * 0.72, 0.06, 0.0), (sx * 0.72, 0.06, 1.30), 0.052, 0.044, n=8)
    f = plank(mb, WOOD, (0, 0.0, 1.62), (1.72, 0.10, 1.06), r=0.020, n=10)
    face = [fa for fa in f if fa.is_valid and fa.normal.y < -0.85]
    recess(mb, face, 0.055, -0.012, col=DARK)
    plank(mb, TENT_R, (0, -0.06, 2.20), (1.80, 0.09, 0.16), r=0.020, n=8)
    for sx in (-1, 1):
        cyl(mb, BRASS, (sx * 0.62, -0.062, 2.20), (sx * 0.62, -0.090, 2.20), 0.024, n=6)


def trophy_plinth(mb):
    rings = [ring_z(10, 0.000, 0.330, 0.330, 5.0), ring_z(10, 0.090, 0.310, 0.310, 5.0),
             ring_z(10, 0.110, 0.270, 0.270, 5.0), ring_z(10, 0.640, 0.250, 0.250, 5.0),
             ring_z(10, 0.680, 0.300, 0.300, 5.0), ring_z(10, 0.760, 0.290, 0.290, 5.0)]
    f, _ = mb.loft(rings, CREAM, smooth=False)
    side = [fa for fa in f if fa.is_valid and abs(fa.normal.z) < 0.4
            and 0.15 < fa.calc_center_median().z < 0.62]
    recess(mb, side, 0.045, -0.014, col=WOOD)


def trophy(mb):
    cyl(mb, WOOD_D, (0, 0, 0.0), (0, 0, 0.070), 0.130, 0.115, n=10)
    cyl(mb, BRASS, (0, 0, 0.068), (0, 0, 0.110), 0.060, 0.040, n=8)
    rings = [ring_z(12, 0.108, 0.048, 0.048, 2.0), ring_z(12, 0.150, 0.098, 0.098, 2.0),
             ring_z(12, 0.230, 0.126, 0.126, 2.2), ring_z(12, 0.320, 0.134, 0.134, 2.4),
             ring_z(12, 0.352, 0.138, 0.138, 2.4)]
    f, _ = mb.loft(rings, BRASS, cap_a=False, cap_b=False, smooth=True)
    mb.loft([ring_z(12, 0.352, 0.138, 0.138, 2.4), ring_z(12, 0.336, 0.126, 0.126, 2.4)],
            "A8863F", cap_a=False, cap_b=True, smooth=False)
    band = [fa for fa in f if fa.is_valid
            and abs(fa.calc_center_median().z - 0.230) < 0.05]
    recess(mb, band, 0.010, -0.007, col="A8863F")
    for sx in (-1, 1):     # handles
        tube(mb, BRASS, [Vector((sx * 0.120, 0, 0.310)), Vector((sx * 0.200, 0, 0.268)),
                         Vector((sx * 0.190, 0, 0.190)), Vector((sx * 0.108, 0, 0.168))],
             0.014, n=5, cap_a=False, cap_b=False, smooth=True)
    sphere(mb, BRASS, (0, 0, 0.400), (0.040, 0.040, 0.046), seg=8, rings=3)


def thermos(mb):
    rings = [ring_z(10, 0.000, 0.058, 0.058, 3.0), ring_z(10, 0.020, 0.064, 0.064, 2.4),
             ring_z(10, 0.180, 0.064, 0.064, 2.2), ring_z(10, 0.196, 0.058, 0.058, 2.4)]
    f, _ = mb.loft(rings, RED, smooth=False)
    band = [fa for fa in f if fa.is_valid and abs(fa.normal.z) < 0.4
            and 0.070 < fa.calc_center_median().z < 0.140]
    recess(mb, band, 0.010, -0.005, col=CREAM)
    cyl(mb, CREAM, (0, 0, 0.192), (0, 0, 0.268), 0.052, 0.056, n=10)
    torus(mb, GREY, (0, 0, 0.196), (0, 0, 1), 0.054, 0.008, nmaj=10, nmin=4)
    tube(mb, GREY, [Vector((0.062, 0, 0.170)), Vector((0.120, 0, 0.140)),
                    Vector((0.112, 0, 0.070)), Vector((0.058, 0, 0.050))],
         0.010, n=5, cap_a=False, cap_b=False)


def bunting_post(mb):
    rings = [ring_z(8, 0.000, 0.060, 0.060, 4.0), ring_z(8, 0.050, 0.048, 0.048, 3.0),
             ring_z(8, 2.020, 0.036, 0.036, 2.6), ring_z(8, 2.100, 0.032, 0.032, 2.4)]
    mb.loft(rings, CREAM, smooth=False)
    torus(mb, TENT_R, (0, 0, 2.055), (0, 0, 1), 0.048, 0.014, nmaj=8, nmin=4)
    sphere(mb, BRASS, (0, 0, 2.148), (0.048, 0.048, 0.056), seg=8, rings=4)
    cyl(mb, BRASS, (0, 0, 2.100), (0, 0, 2.120), 0.020, n=6)


def marker_stake(mb):
    rings = [ring_z(6, 0.000, 0.014, 0.014, 3.0), ring_z(6, 0.060, 0.028, 0.028, 4.0),
             ring_z(6, 0.820, 0.026, 0.026, 4.0), ring_z(6, 0.860, 0.022, 0.022, 3.0)]
    mb.loft(rings, WOOD, smooth=False)
    # a chalk-white pennant, sagging
    a = mb.v((0.020, 0.0, 0.855)); b = mb.v((0.020, 0.0, 0.700))
    c = mb.v((0.230, -0.030, 0.742)); d = mb.v((0.235, -0.030, 0.800))
    mb.f((a, d, c, b), CHALK)
    mb.f((b, c, d, a), CHALK)
    mb.bm.normal_update()
    for z in (0.30, 0.55):
        prof(mb, TENT_R, [(0.030, 0.030), (0.030, -0.030), (-0.030, -0.030),
                          (-0.030, 0.030)], z, z + 0.045)


def bicycle(mb):
    R, XW = 0.330, 0.024
    for y in (-0.52, 0.52):
        torus(mb, DARK, (0, y, R), (1, 0, 0), R, 0.032, nmaj=10, nmin=4)
        torus(mb, CREAM, (0, y, R), (1, 0, 0), R * 0.30, 0.014, nmaj=6, nmin=4)
        for k in range(3):
            a = TAU * k / 3 + 0.4
            cyl(mb, CREAM, (0, y + R * 0.28 * math.cos(a), R + R * 0.28 * math.sin(a)),
                (0, y + R * 0.96 * math.cos(a), R + R * 0.96 * math.sin(a)), 0.008, n=4)
    F = [(0, -0.52, R), (0, -0.10, 0.90), (0, 0.30, 0.86), (0, 0.52, R),
         (0, 0.05, 0.44), (0, -0.10, 0.90)]
    for (a, b) in [(0, 1), (1, 2), (2, 3), (3, 4), (4, 0), (4, 1)]:
        cyl(mb, TENT_R, F[a], F[b], 0.022, n=5)
    cyl(mb, GREY, (0, -0.10, 0.90), (0, -0.16, 1.06), 0.020, n=6)
    cyl(mb, GREY, (-0.20, -0.16, 1.06), (0.20, -0.16, 1.06), 0.016, n=6)
    for sx in (-1, 1):
        cyl(mb, WOOD_D, (sx * 0.20, -0.16, 1.06), (sx * 0.20, -0.10, 1.05), 0.024, n=5)
    plank(mb, WOOD_D, (0, 0.31, 0.905), (0.11, 0.24, 0.055), r=0.020, n=8)
    torus(mb, GREY, (0, 0.05, 0.44), (1, 0, 0), 0.070, 0.012, nmaj=8, nmin=4)
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * 0.055, 0.05, 0.44), (sx * 0.075, -0.06, 0.36), 0.010, n=5)


PROPS = [
    ("FencePost", fence_post), ("FenceRail", fence_rail), ("HayBale", hay_bale),
    ("Gnome", gnome), ("Sprinkler", sprinkler), ("Wheelbarrow", wheelbarrow),
    ("Bench", bench), ("Awning", awning), ("Scoreboard", scoreboard),
    ("TrophyPlinth", trophy_plinth), ("Trophy", trophy), ("Thermos", thermos),
    ("BuntingPost", bunting_post), ("MarkerStake", marker_stake), ("Bicycle", bicycle),
]


def main():
    fresh_scene()
    objs, layout = [], []
    for i, (name, fn) in enumerate(PROPS):
        mb = MB()
        fn(mb)
        ob = mb.finish(name, MAT, 34.0)
        gx, gy = (i % 5) * 4.0 - 8.0, (i // 5) * 4.0 - 4.0
        ob.data.transform(Matrix.Translation(Vector((gx, gy, 0))))
        bake_ao([ob], floor=0.76, dist=0.30, samples=24, ground=True, ground_size=3.0)
        set_pivot(ob, (gx, gy, 0))
        objs.append(ob)
        layout.append((ob, (gx, gy, 0)))
    card_uv(next(o for o in objs if o.name == "Scoreboard"))

    report_tris(objs, "Props")
    for ob, p in layout:
        ob.location = (0, 0, 0)
    L.verify_meshes(objs, "Props")
    export_fbx(objs, os.path.join(L.MODELS_DIR, "Props.fbx"))
    for ob, p in layout:
        ob.location = p
    L.render_gallery(objs, "props", res=300, cols=5)
    L.render_gallery(objs, "props", res=260, cols=5, sil=True)
    print("DONE Props")


main()
