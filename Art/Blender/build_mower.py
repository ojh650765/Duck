# build_mower.py — DUCK MOW hero: the ride-on mower.
# Run: blender --background --python C:\Duck\Art\Blender\build_mower.py
#
# Layout (Blender: -Y = forward, +Z = up, root at ground, centre of wheelbase):
#   front axle y = -0.40  r 0.15  x +-0.320      rear axle y = +0.40  r 0.22  x +-0.372
#   deck shell y[-0.815,-0.549] 1.20 wide, top z 0.150 -- slung LOW and FORWARD
#   catcher back face y = +0.628   fender tips y = +0.632  -> 1.45 m long
#   seat contact (0, +0.10, 0.42)  == Unity (0, 0.42, -0.10)
#   steering hub (0, -0.164, 0.636) == the duck's wing grip circle
#
# MASS HIERARCHY (the 3/4 silhouette gate):
#   1 dominant  the red body: tall tapered bonnet + tub, only 0.48 m wide
#   2 secondary the cutting deck: 1.20 m wide, low, out FRONT, hung on visible
#               arms with a 50-70 mm air gap under the bonnet
#   3 detail    wheel arches, catcher, seat, stack, lamp
# Every tyre runs inside an arch with a visible radial gap (front 42 mm, rear
# 48 mm) and NO body panel touches a tyre.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc, quad,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, arc_sweep, fender_prof, mirror_half,
                      scale_outline, ccw, TAU)

MAT = "M_Mower"
RED, DEEP, CREAM, GREY, BRASS = "red", "red_deep", "cream", "grey", "brass"
LENS = "chalk"
TYRE = "3E4247"
CANVAS = "F5EAD6"
CANVAS_SH = "DCC9A4"

FRONT_AXLE_Y, REAR_AXLE_Y = -0.40, 0.40
FR, RR = 0.15, 0.22            # tyre radii
FX, RX = 0.320, 0.372          # wheel centre X
FW, RW = 0.092, 0.134          # tyre widths
SEAT = Vector((0.0, 0.10, 0.42))
HUB = Vector((0.0, -0.206, 0.638))
COL_BASE = Vector((0.0, -0.300, 0.300))
DECK_C = Vector((0.0, -0.682, 0.0))
DECK_PIVOT = Vector((0.0, -0.430, 0.225))

# deck plan: a crescent, NOT a rounded rectangle.  The rear edge is scalloped
# back where the front tyres run so nothing can ever chew into them.
DECK_HALF = [
    (0.000, -0.549), (0.190, -0.555), (0.262, -0.609), (0.320, -0.623),
    (0.386, -0.609), (0.460, -0.563), (0.600, -0.555),
    (0.600, -0.743), (0.540, -0.785), (0.380, -0.805), (0.190, -0.813),
    (0.000, -0.815),
]


# ---------------------------------------------------------------- helpers
def extrude_profile(mb, col, pts2d, z0, z1, col_top=None, col_bot=None, smooth=False):
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


def taper_profile(mb, col, a2d, z0, b2d, z1, col_top=None, col_bot=None, smooth=False):
    """Extrude between two different outlines -> a moulded shell, not a box."""
    bot = [mb.v((x, y, z0)) for (x, y) in a2d]
    top = [mb.v((x, y, z1)) for (x, y) in b2d]
    faces, n = [], len(a2d)
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


def dshape(x, yf, yb, rf, rb, seg=4, s=1.0):
    """Rounded rect with independent front/back corner radii, scaled by s."""
    pts = []
    for (cx, cy, a0, r) in [(x - rb, yb - rb, 0.0, rb), (-x + rb, yb - rb, math.pi / 2, rb),
                            (-x + rf, yf + rf, math.pi, rf), (x - rf, yf + rf, 1.5 * math.pi, rf)]:
        for i in range(seg + 1):
            a = a0 + math.pi * 0.5 * i / seg
            pts.append(((cx + r * math.cos(a)) * s, cy + r * math.sin(a) * 1.0))
    if s != 1.0:
        cy0 = (yf + yb) * 0.5
        pts = [(px, cy0 + (py - cy0) * s) for (px, py) in pts]
    return pts


def wheel_arch(mb, col, cx, cy, cz, r_tyre, gap, width_in, width_out, thick,
               a0, a1, steps=8, end_taper=0.45):
    """A fender that ENCLOSES a tyre with a visible radial gap.  The inboard
    edge is buried inside the bodywork so the arch springs out of the flank
    instead of hovering beside it; the section feathers at both ends."""
    r = r_tyre + gap
    prof = fender_prof(-width_in, width_out, thick)

    def tp(t):
        e = min(t, 1.0 - t) * 2.0            # 0 at the ends, 1 mid-arc
        s = end_taper + (1.0 - end_taper) * min(1.0, e * 2.2)
        return (1.0, s, 0.0, 0.0)

    return arc_sweep(mb, col, cx, cy, cz, r, a0, a1, steps, prof, taper=tp)[0]


# ============================================================ BODY
def build_body():
    mb = MB()

    # ---- HOOD: the dominant mass.  Tall, NARROW (0.48 m across) so the front
    # tyres run clear outboard of it.  Detail is cut in, never glued on.
    HOOD = [
        # y,     rx,    rz,    cz,    e
        (-0.700, 0.140, 0.078, 0.356, 3.0),
        (-0.678, 0.188, 0.104, 0.350, 3.4),
        (-0.612, 0.220, 0.128, 0.346, 3.9),
        (-0.520, 0.238, 0.148, 0.340, 4.3),
        (-0.440, 0.240, 0.156, 0.338, 4.5),
        (-0.372, 0.234, 0.150, 0.336, 4.5),
    ]
    hr = [ring_y(14, y, rx, rz, e, 0.0, cz, rot=math.pi / 14)
          for (y, rx, rz, cz, e) in HOOD]
    hood, _ = mb.loft(hr, RED, cap_a=True, cap_b=True, smooth=False)

    # grille: recess the nose cap twice, dark inside, brass surround
    nose = [f for f in hood if f.is_valid and f.normal.y < -0.9]
    inner = recess(mb, nose, 0.022, 0.0, col=BRASS)
    inner = recess(mb, inner, 0.008, -0.042, col=GREY)
    for k in range(2):     # grille bars
        z = 0.322 + k * 0.048
        pts = [(0.092, -0.664), (-0.092, -0.664), (-0.092, -0.652), (0.092, -0.652)]
        extrude_profile(mb, DEEP, pts, z, z + 0.013)

    # side vent louvres cut into the flanks (3 per side)
    vent = [f for f in hood if f.is_valid and abs(f.normal.x) > 0.85
            and abs(f.calc_center_median().z - 0.340) < 0.06
            and -0.590 < f.calc_center_median().y < -0.380]
    for f in vent:
        recess(mb, [f], 0.014, -0.020, col=GREY)

    # panel seam behind the grille (cut FIRST, full width)
    seam = [f for f in hood if f.is_valid and f.normal.z > 0.5
            and -0.650 < f.calc_center_median().y < -0.605]
    recess(mb, seam, 0.005, -0.007, col=DEEP)
    # cream centre stripe down the hood top - reads from the chase camera.
    # Must EXCLUDE the seam band: recessing the same face twice leaves the two
    # inset borders sitting on top of each other, which z-fights.
    stripe = [f for f in hood if f.is_valid and f.normal.z > 0.80
              and abs(f.calc_center_median().x) < 0.060
              and not (-0.650 < f.calc_center_median().y < -0.605)]
    recess(mb, stripe, 0.010, -0.005, col=CREAM)
    # cream flank flash low on the bonnet
    flash = [f for f in hood if f.is_valid and abs(f.normal.x) > 0.5
             and f.calc_center_median().z < 0.262]
    recess(mb, flash, 0.010, -0.004, col=CREAM)

    # ---- MAIN TUB ----------------------------------------------------------
    TUB = [
        (-0.410, 0.120, 0.048, 0.300, 3.0),   # buried inside the hood: its cap
        (-0.372, 0.230, 0.086, 0.286, 4.5),   # must not land on the hood's cap
        (-0.280, 0.216, 0.078, 0.268, 4.7),
        (-0.060, 0.212, 0.076, 0.262, 4.8),
        (0.010,  0.219, 0.078, 0.263, 4.8),   # extra ring: opens the footwell
        (0.160,  0.234, 0.082, 0.264, 4.8),
        (0.380,  0.262, 0.092, 0.268, 4.6),
        (0.540,  0.256, 0.088, 0.270, 4.4),
        (0.600,  0.212, 0.070, 0.272, 4.0),
    ]
    tr = [ring_y(14, y, rx, rz, e, 0.0, cz, rot=math.pi / 14)
          for (y, rx, rz, cz, e) in TUB]
    tub, _ = mb.loft(tr, RED, cap_a=False, cap_b=True, smooth=False)
    # the two flank cuts must not share a face (see the hood seam note)
    low = [f for f in tub if f.is_valid and abs(f.normal.x) > 0.5
           and f.calc_center_median().z < 0.234]
    recess(mb, low, 0.010, -0.005, col=CREAM)
    sh = [f for f in tub if f.is_valid and abs(f.normal.x) > 0.85
          and 0.246 < f.calc_center_median().z < 0.296]
    recess(mb, sh, 0.006, -0.006, col=DEEP)

    # ---- FOOTWELL: cut DOWN into the tub skin.  Floor lands at z = 0.252 and
    # the duck's soles land on PEDALS at 0.290 (soles are at 0.282, so they
    # press 8 mm into the rubber -- a merge, never a coplanar kiss).
    well = [f for f in tub if f.is_valid and f.normal.z > 0.55
            and -0.300 < f.calc_center_median().y < 0.020]
    floor = recess(mb, well, 0.018, 0.0, col=DEEP)
    floor = recess(mb, floor, 0.006, -0.078, col=GREY)
    for sx in (-1, 1):     # rubber foot pedals
        rbox(mb, GREY, (sx * 0.082, -0.150, 0.264), (0.112, 0.168, 0.052),
             r=0.012, n=8, e=4.5, k=1)

    # ---- WHEEL ARCHES ------------------------------------------------------
    # rear: a tall haunch, the widest thing on the machine (+-0.460 -> 0.92 m).
    # width_in buries the inboard 150 mm inside the tub so the arch SPRINGS from
    # the flank; a hoop hovering beside the body is the flap failure mode.
    # The arch stays RED -- only its outboard bevel takes the cream pinstripe,
    # so it reads as one machine, not as white slabs bolted to a red body.
    for sx in (-1, 1):
        arc = wheel_arch(mb, RED, sx * RX, REAR_AXLE_Y, RR, RR, 0.048,
                         width_in=0.150, width_out=0.088, thick=0.034,
                         a0=30.0, a1=152.0, steps=8)
        mb.face_list([f for i, f in enumerate(arc[:48]) if i % 6 == 3], CREAM)
        mb.face_list([f for i, f in enumerate(arc[:48]) if i % 6 == 0], DEEP)
    # front: a lighter mudguard, still a full enclosure over the tyre
    for sx in (-1, 1):
        arc = wheel_arch(mb, RED, sx * FX, FRONT_AXLE_Y, FR, FR, 0.042,
                         width_in=0.130, width_out=0.072, thick=0.026,
                         a0=26.0, a1=154.0, steps=8)
        mb.face_list([f for i, f in enumerate(arc[:48]) if i % 6 == 3], CREAM)
        mb.face_list([f for i, f in enumerate(arc[:48]) if i % 6 == 0], DEEP)

    # ---- CONSOLE / dash ----------------------------------------------------
    # keep the console's front face well clear of the hood's rear cap plane
    rbox(mb, GREY, (0.0, -0.304, 0.268), (0.30, 0.100, 0.170), r=0.022, n=8, e=4.5)
    rbox(mb, DEEP, (0.0, -0.258, 0.334), (0.23, 0.032, 0.078), r=0.012, n=8, e=4.0)
    for sx in (-1, 1):      # stand the dials clearly PROUD of the dash face
        disc(mb, BRASS, (sx * 0.060, -0.2340, 0.338), (0, 1, 0), 0.022, n=8, t=0.030)
    # ignition key + throttle lever
    cyl(mb, BRASS, (0.100, -0.258, 0.356), (0.100, -0.258, 0.398), 0.009, 0.007, n=6)
    cyl(mb, GREY, (-0.122, -0.262, 0.360), (-0.144, -0.226, 0.428), 0.010, 0.008, n=6)
    sphere(mb, RED, (-0.146, -0.222, 0.436), (0.018, 0.018, 0.018), seg=6, rings=3)

    # ---- UNDERFRAME / axles ------------------------------------------------
    # axle beams run OUT to the hubs; they enter the wheels coaxially, which is
    # the one intersection that is always invisible.
    cyl(mb, GREY, (-FX - 0.01, FRONT_AXLE_Y, FR), (FX + 0.01, FRONT_AXLE_Y, FR),
        0.028, n=8)
    cyl(mb, GREY, (-RX - 0.01, REAR_AXLE_Y, RR), (RX + 0.01, REAR_AXLE_Y, RR),
        0.032, n=8)
    rbox(mb, GREY, (0.0, -0.420, 0.186), (0.30, 0.20, 0.084), r=0.016, n=6, e=5.0)
    cyl(mb, GREY, (0.0, -0.500, 0.185), (0.0, -0.200, 0.185), 0.030, n=8)
    rbox(mb, GREY, (0.0, 0.320, 0.196), (0.28, 0.34, 0.12), r=0.018, n=6, e=5.0)
    # deck-arm pivot bosses: the visible joint the deck hangs from
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * 0.150, -0.430, 0.225), (sx * 0.212, -0.430, 0.225),
            0.033, 0.028, n=8)

    # ---- BUG-EYE HEADLIGHT -------------------------------------------------
    lc = Vector((0.0, -0.700, 0.358))
    cyl(mb, BRASS, lc + Vector((0, 0.030, 0)), lc + Vector((0, -0.032, 0)),
        0.062, 0.066, n=12)
    # a flaring cone, not a flat puck parked 1.5 mm off the housing's front cap
    cyl(mb, LENS, lc + Vector((0, -0.018, 0)), lc + Vector((0, -0.042, 0)),
        0.048, 0.056, n=12)
    torus(mb, BRASS, lc + Vector((0, -0.032, 0)), (0, 1, 0), 0.060, 0.010, nmaj=12, nmin=5)
    cyl(mb, GREY, lc + Vector((0, 0.030, 0)), lc + Vector((0, 0.060, -0.020)), 0.022, n=6)

    # ---- HOOD ORNAMENT: a tiny brass duck ---------------------------------
    oc = Vector((0.0, -0.640, 0.454))
    cyl(mb, BRASS, oc, oc + Vector((0, 0, 0.016)), 0.024, 0.016, n=8)
    sphere(mb, BRASS, oc + Vector((0, 0.006, 0.046)), (0.020, 0.025, 0.027), seg=8, rings=4)
    sphere(mb, BRASS, oc + Vector((0, -0.008, 0.077)), (0.014, 0.015, 0.014), seg=8, rings=3)
    cyl(mb, BRASS, oc + Vector((0, -0.013, 0.077)), oc + Vector((0, -0.042, 0.073)),
        0.009, 0.006, n=6)

    # ---- HANDBRAKE lever ---------------------------------------------------
    rbox(mb, GREY, (0.212, 0.034, 0.310), (0.06, 0.10, 0.07), r=0.012, n=8, e=4.0)
    b0 = Vector((0.212, 0.034, 0.334))
    b1 = Vector((0.202, 0.096, 0.500))
    cyl(mb, GREY, b0, b1, 0.017, 0.013, n=8)
    sphere(mb, RED, b1 + Vector((-0.004, 0.008, 0.020)), (0.028, 0.028, 0.030),
           seg=8, rings=4)

    # ---- fuel cap + hitch --------------------------------------------------
    cyl(mb, BRASS, (-0.150, 0.480, 0.352), (-0.150, 0.480, 0.372), 0.036, 0.030, n=8)
    rbox(mb, GREY, (0.0, 0.560, 0.155), (0.12, 0.14, 0.075), r=0.012, n=8, e=4.0)

    return mb.finish("Mower_Body", MAT, smooth_angle=32.0)


# ============================================================ DECK
def build_deck():
    """A distinct sub-assembly: a wide crescent shell slung LOW and FORWARD on
    two visible arms, with a 50-70 mm air gap between its top and the bonnet."""
    mb = MB()
    out = mirror_half(DECK_HALF)
    mid = scale_outline(out, 0.997)
    chm = scale_outline(out, 0.983)
    top = scale_outline(out, 0.930)

    lo = [Vector((x, y, 0.048)) for (x, y) in out]
    md = [Vector((x, y, 0.116)) for (x, y) in mid]
    ch = [Vector((x, y, 0.127)) for (x, y) in chm]
    hi = [Vector((x, y, 0.142)) for (x, y) in top]
    shell, _ = mb.loft([lo, md, ch, hi], RED, cap_a=True, cap_b=True, smooth=False)
    n = len(out)
    # band 0 = skirt (dark, sits down in the grass), band 1 = cream bevel line
    # that runs the whole way round and separates the deck from the bodywork.
    mb.face_list(shell[0:n], DEEP)
    mb.face_list(shell[n:2 * n], CREAM)

    # a panel line cut into the top plate so it is not one bald red field
    plate = [f for f in shell if f.is_valid and f.normal.z > 0.90]
    recess(mb, plate, 0.010, -0.012, col=RED)

    # ---- three spindle covers pressed up out of the deck top ---------------
    for (bx, by, r0, r1, zt) in [(0.0, -0.684, 0.126, 0.054, 0.190),
                                 (0.355, -0.706, 0.082, 0.034, 0.172),
                                 (-0.355, -0.706, 0.082, 0.034, 0.172)]:
        rings = [ring_z(10, 0.100, r0, r0, 2.0, bx, by),
                 ring_z(10, zt - 0.012, r0 * 0.86, r0 * 0.86, 2.0, bx, by),
                 ring_z(10, zt, r1, r1, 2.0, bx, by)]
        mb.loft(rings, RED, cap_a=False, cap_b=True, smooth=False)
        cyl(mb, BRASS, (bx, by, zt - 0.004), (bx, by, zt + 0.014),
            r1 * 0.62, r1 * 0.52, n=8)

    # ---- discharge chute: a tapered duct, right hand side, mouth facing up
    lo_c = [(0.400, -0.744), (0.578, -0.702), (0.578, -0.598), (0.400, -0.640)]
    hi_c = [(0.430, -0.724), (0.564, -0.692), (0.564, -0.620), (0.430, -0.654)]
    taper_profile(mb, RED, lo_c, 0.124, hi_c, 0.226, col_top=GREY)

    # ---- front bumper bar + anti-scalp rollers -----------------------------
    tube(mb, GREY, [Vector((-0.545, -0.760, 0.052)), Vector((-0.300, -0.790, 0.048)),
                    Vector((0.300, -0.790, 0.048)), Vector((0.545, -0.760, 0.052))],
         0.015, n=6)
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * 0.470 - 0.030, -0.738, 0.040),
            (sx * 0.470 + 0.030, -0.738, 0.040), 0.032, n=8)
        cyl(mb, BRASS, (sx * 0.470 - 0.033, -0.738, 0.040),
            (sx * 0.470 + 0.033, -0.738, 0.040), 0.014, n=6)

    # ---- LIFT ARMS: the whole point.  Two struts and a centre push-rod that
    # cross the air gap and plug into the chassis pivot bosses at y = -0.430.
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * 0.218, -0.586, 0.140), (sx * 0.184, -0.430, 0.225),
            0.021, 0.019, n=7)
        cyl(mb, BRASS, (sx * 0.218, -0.586, 0.146), (sx * 0.218, -0.586, 0.108),
            0.026, 0.030, n=7)
    cyl(mb, GREY, (0.0, -0.566, 0.168), (0.0, -0.446, 0.216), 0.016, 0.014, n=6)
    return mb.finish("Mower_Deck", MAT, smooth_angle=32.0)


def build_blade():
    """A single rotor slung under the deck pan, 12 mm clear of it."""
    mb = MB()
    cz = 0.030
    R = 0.116
    for sx in (-1, 1):
        pts = ccw([(sx * 0.026, -0.714), (sx * (R - 0.030), -0.718),
                   (sx * R, -0.702), (sx * R, -0.668), (sx * (R - 0.030), -0.652),
                   (sx * 0.026, -0.654)])
        extrude_profile(mb, GREY, pts, cz - 0.007, cz + 0.007, col_top=BRASS)
    cyl(mb, BRASS, (0, -0.684, cz - 0.010), (0, -0.684, cz + 0.008), 0.042, 0.032, n=10)
    return mb.finish("Mower_Blade", MAT, smooth_angle=30.0)


# ============================================================ WHEELS
def build_wheel(name, cx, cy, r, w, lug=0.016, n=14):
    mb = MB()
    C = Vector((cx, cy, r))
    hw = w * 0.5

    def wring(x, rad, lugged=False):
        pts = []
        for i in range(n):
            a = TAU * i / n
            rr = rad + (lug if (lugged and (i % 2 == 0)) else 0.0)
            pts.append(Vector((C.x + x, C.y + rr * math.cos(a), C.z + rr * math.sin(a))))
        return pts

    rings = [wring(-hw, r * 0.54), wring(-hw, r - 0.032),
             wring(-hw * 0.62, r - 0.004, True), wring(hw * 0.62, r - 0.004, True),
             wring(hw, r - 0.032), wring(hw, r * 0.54)]
    mb.loft(rings, TYRE, cap_a=False, cap_b=False, smooth=False)
    for s in (-1, 1):
        x = C.x + s * hw
        cyl(mb, CREAM, (x, C.y, C.z), (C.x + s * hw * 0.55, C.y, C.z),
            r * 0.54, r * 0.50, n=n, cap_a=False, cap_b=True, smooth=False)
        cyl(mb, BRASS, (x, C.y, C.z), (x + s * 0.014, C.y, C.z),
            r * 0.22, r * 0.17, n=8)
    return mb.finish(name, MAT, smooth_angle=28.0)


# ============================================================ STEERING
def build_steering():
    mb = MB()
    ax = (HUB - COL_BASE).normalized()
    cyl(mb, GREY, COL_BASE, HUB - ax * 0.030, 0.028, 0.021, n=8)
    torus(mb, DEEP, HUB, ax, 0.093, 0.017, nmaj=14, nmin=5)
    up = Vector((0, 0, 1))
    e1 = ax.cross(up).normalized()
    e2 = ax.cross(e1).normalized()
    for k in range(3):
        a = TAU * k / 3 + math.radians(90)
        d = e1 * math.cos(a) + e2 * math.sin(a)
        cyl(mb, CREAM, HUB - ax * 0.006, HUB + d * 0.090, 0.014, 0.010, n=6)
    cyl(mb, BRASS, HUB - ax * 0.026, HUB + ax * 0.022, 0.046, 0.024, n=10)
    return mb.finish("Mower_Steering", MAT, smooth_angle=30.0)


# ============================================================ EXHAUST
def build_exhaust():
    """A tractor stack through the bonnet shoulder: a strong vertical accent
    that reads at 3/4 and never goes near a tyre."""
    mb = MB()
    B = Vector((0.148, -0.524, 0.436))
    T = B + Vector((0.010, 0.012, 0.176))
    cyl(mb, GREY, B, T, 0.038, 0.031, n=10)
    torus(mb, BRASS, B + Vector((0, 0.004, 0.086)), (0, 0, 1), 0.036, 0.008,
          nmaj=10, nmin=4)
    cyl(mb, BRASS, T - Vector((0, 0, 0.007)), T + Vector((0, 0, 0.020)),
        0.036, 0.035, n=10)
    cap_c = T + Vector((0.007, -0.005, 0.040))
    cyl(mb, GREY, cap_c, cap_c + Vector((-0.011, 0.007, 0.016)), 0.046, 0.025, n=10)
    cyl(mb, BRASS, T + Vector((0.030, 0, 0.008)), cap_c + Vector((0.026, 0, 0.003)),
        0.006, n=5)
    return mb.finish("Mower_Exhaust", MAT, smooth_angle=30.0)


# ============================================================ CATCHER BAG
def build_bag():
    mb = MB()
    # A canvas sack HANGING on a frame - stacked rings so it reads as fabric
    # with a heavy bottom, not a pillow.  Narrowed to 0.226 so it clears the
    # rear haunches by 24 mm.
    BAG = [
        # z,     rx,    ry,    cy,    e
        (0.286, 0.134, 0.064, 0.512, 2.4),
        (0.330, 0.194, 0.090, 0.515, 2.9),
        (0.396, 0.224, 0.104, 0.518, 3.3),
        (0.462, 0.217, 0.098, 0.519, 3.4),   # a slight waist: fabric, not a box
        (0.530, 0.226, 0.102, 0.520, 3.6),
        (0.588, 0.217, 0.094, 0.520, 3.8),
        (0.610, 0.210, 0.089, 0.520, 3.8),
    ]
    rings = [ring_z(10, z, rx, ry, e, 0.0, cy) for (z, rx, ry, cy, e) in BAG]
    faces, _ = mb.loft(rings, CANVAS, cap_a=True, cap_b=True, smooth=False)
    # red trim band around the mouth, as ONE region so it reads as a hem
    band = [f for f in faces if f.is_valid and f.normal.z < 0.4
            and f.calc_center_median().z > 0.590]
    recess(mb, band, 0.008, -0.004, col=RED)
    # one continuous slack fold around the sack - a groove, not a row of panels
    fold = [f for f in faces if f.is_valid and f.normal.z < 0.4
            and 0.430 < f.calc_center_median().z < 0.500]
    recess(mb, fold, 0.010, -0.011, col=CANVAS_SH)
    # frame rail round the mouth + hinge + brass handle
    rail = [Vector((0.226, 0.418, 0.614)), Vector((0.226, 0.618, 0.614)),
            Vector((-0.226, 0.618, 0.614)), Vector((-0.226, 0.418, 0.614))]
    tube(mb, GREY, rail, 0.013, n=6, loop=True)
    for sx in (-1, 1):
        cyl(mb, BRASS, (sx * 0.172, 0.418, 0.612), (sx * 0.172, 0.390, 0.606),
            0.019, n=6)
        cyl(mb, GREY, (sx * 0.226, 0.494, 0.614), (sx * 0.236, 0.494, 0.380),
            0.011, n=6)
    tube(mb, BRASS, [Vector((-0.082, 0.608, 0.500)), Vector((-0.082, 0.632, 0.516)),
                     Vector((0.082, 0.632, 0.516)), Vector((0.082, 0.608, 0.500))],
         0.011, n=6)
    ob = mb.finish("Mower_CatcherBag", MAT, smooth_angle=32.0)
    H = Vector((0.0, 0.418, 0.612))
    ob.data.transform(Matrix.Translation(H)
                      @ Euler((math.radians(3.0), 0, math.radians(1.5)), 'XYZ').to_matrix().to_4x4()
                      @ Matrix.Translation(-H))
    return ob


# ============================================================ SEAT
def build_seat():
    mb = MB()
    # bucket pan: rim at z = 0.42 (the seat contact), dished to 0.370 so the
    # duck's rump sinks INTO the dish instead of kissing it (z-fight).
    pan = dshape(0.172, -0.030, 0.232, 0.060, 0.070, seg=2)
    panl = dshape(0.186, -0.042, 0.244, 0.062, 0.072, seg=2)
    pf = taper_profile(mb, RED, pan, 0.336, panl, 0.420, col_top=RED)
    top = [f for f in pf if f.is_valid and f.normal.z > 0.9]
    recess(mb, top, 0.030, -0.050, col=DEEP)

    # backrest: a real volume, curving up and back with side bolsters
    path, sq = [], []
    for i in range(5):
        t = i / 4.0
        path.append(Vector((0.0, 0.220 + 0.078 * t * t + 0.030 * t,
                            0.352 + 0.278 * t)))
        sq.append((1.0, 0.30 - 0.06 * t))
    bf = tube(mb, RED, path, [0.182, 0.180, 0.174, 0.164, 0.144], n=10, e=4.0,
              squash=sq, cap_a=True, cap_b=True, smooth=False)
    fr = [f for f in bf if f.is_valid and f.normal.y < -0.88]
    recess(mb, fr, 0.024, -0.016, col=DEEP)

    # cream piping around the pan rim
    pip = L.scale_outline(panl, 1.048)
    tube(mb, CREAM, [Vector((p[0], p[1], 0.392)) for p in pip], 0.010, n=6,
         loop=True)
    cyl(mb, GREY, (0.0, 0.100, 0.292), (0.0, 0.100, 0.352), 0.040, 0.046, n=8)
    rbox(mb, GREY, (0.0, 0.100, 0.300), (0.20, 0.14, 0.05), r=0.012, n=8, e=4.5)
    return mb.finish("Mower_Seat", MAT, smooth_angle=32.0)


# =============================================================== BUILD
def build_all():
    body = build_body()
    deck = build_deck()
    blade = build_blade()
    wfl = build_wheel("Mower_Wheel_FL", FX, FRONT_AXLE_Y, FR, FW, lug=0.012)
    wfr = build_wheel("Mower_Wheel_FR", -FX, FRONT_AXLE_Y, FR, FW, lug=0.012)
    wrl = build_wheel("Mower_Wheel_RL", RX, REAR_AXLE_Y, RR, RW, lug=0.019)
    wrr = build_wheel("Mower_Wheel_RR", -RX, REAR_AXLE_Y, RR, RW, lug=0.019)
    steer = build_steering()
    exh = build_exhaust()
    bag = build_bag()
    seat = build_seat()
    return [body, deck, blade, wfl, wfr, wrl, wrr, steer, exh, bag, seat]


# Sockets: parts that are DELIBERATELY plugged into one another (axles into
# hubs, arms into pivot bosses, seat/steering/bag into the tub).  Anything not
# on this list must not intersect.
SOCKETS = (
    ("Mower_Body", "Mower_Seat"), ("Mower_Body", "Mower_Steering"),
    ("Mower_Body", "Mower_CatcherBag"), ("Mower_Body", "Mower_Exhaust"),
    ("Mower_Body", "Mower_Wheel_FL"), ("Mower_Body", "Mower_Wheel_FR"),
    ("Mower_Body", "Mower_Wheel_RL"), ("Mower_Body", "Mower_Wheel_RR"),
)


def main():
    fresh_scene()
    meshes = build_all()
    bake_ao(meshes, floor=0.74, dist=0.22, samples=28, ground=True, ground_size=6.0)

    set_pivot(meshes[0], (0.0, 0.0, 0.0))
    set_pivot(meshes[1], tuple(DECK_PIVOT))
    set_pivot(meshes[2], (0.0, -0.682, 0.031))
    set_pivot(meshes[3], (FX, FRONT_AXLE_Y, FR))
    set_pivot(meshes[4], (-FX, FRONT_AXLE_Y, FR))
    set_pivot(meshes[5], (RX, REAR_AXLE_Y, RR))
    set_pivot(meshes[6], (-RX, REAR_AXLE_Y, RR))
    set_pivot(meshes[7], tuple(COL_BASE))
    set_pivot(meshes[8], (0.148, -0.524, 0.436))
    set_pivot(meshes[9], (0.0, 0.418, 0.612))
    set_pivot(meshes[10], tuple(SEAT))

    root = make_empty("Mower_Root", (0, 0, 0))
    for o in meshes:
        attach(o, root)

    objs = [root] + meshes
    report_tris(meshes, "Mower")
    try:
        import audit_lib
        audit_lib.audit(meshes, "MOWER", ignore=SOCKETS)
    except Exception as e:
        print("audit skipped:", e)
    L.verify_meshes(meshes, "Mower")
    export_fbx(objs, os.path.join(L.MODELS_DIR, "Mower.fbx"))
    render_previews(meshes, "mower", res=560,
                    extra_views=[("backq", (0.72, 1.0, 0.36)),
                                 ("lowq", (-0.85, -1.0, 0.16)),
                                 ("hoodcu", (-0.62, -1.0, 0.34), 0.50),
                                 ("rearcu", (0.62, 1.0, 0.34), 0.52)])
    print("DONE Mower")


main()
