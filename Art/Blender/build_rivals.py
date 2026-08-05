# build_rivals.py — DUCK MOW: the three rival contestants, seated on their own mowers.
# Run: blender --background --python C:\Duck\Art\Blender\build_rivals.py
#
# Three roots in one FBX.  Base z = 0 is the SEAT contact point (same convention as
# build_judges.py) because they sit on a mower; the legs deliberately hang below 0
# onto the footplate.  No rig: Unity animates the named transforms, so the head
# pivot is at the NECK and the arm pivots are at the SHOULDER.
#
# They are seen from 30-100 m over a hedge and from a 60 m tour camera, so the
# silhouette is the entire design.  Three different shape families:
#   Horace  (hare)   0.66 m  tall + lean, leaning forward, ears streaming BACK
#                           -> a thin forward-raked spike with a long back barb
#   Margot  (badger) 0.47 m  low + broad, hunched over the wheel, scarf trailing
#                           -> a wide low wedge with a diagonal banner
#   Bramble (sheep)  0.60 m  round + fluffy, bolt upright, arms tucked in
#                           -> a ball on a ball
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc, quad,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_Rivals"
CREAM, CREAM_SH = "F6EBD2", "DCC9A4"
WHITE = "F1EDE0"
DARK, SLATE = "241F1E", "4A4F55"
WOOD_D, BRASS = "6E4A2C", "C9A55A"
ORANGE, ORANGE_D = "F2A03D", "D3792A"
GLASS = "9FD4E4"
TYRE = "3A3B3E"

# Rival liveries.  Given by the brief; each one sits inside an ART_BIBLE hue
# family so the set still reads as one world:
#   blue  4C85D1 <- sky zenith 4E9BD4
#   amber F0B83D <- brass C9A55A / duck bill F2A03D
#   green 70B861 <- cut grass 6E9E37 / cut tip A8CB55
BLUE,  BLUE_D  = "4C85D1", "315C96"
AMBER, AMBER_D = "F0B83D", "B8811F"
GREEN, GREEN_D = "70B861", "48803E"


def egg_ring(n, z, rx, ryF, ryB, cy, e=2.0):
    """Ring whose front and back depth differ — the workhorse body section."""
    pts, k = [], 2.0 / e
    for i in range(n):
        a = TAU * (i + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** k, c) * rx
        y = math.copysign(abs(s) ** k, s)
        y *= ryB if y >= 0 else ryF
        pts.append(Vector((x, cy + y, z)))
    return pts


def col_faces(mb, faces, fn, col):
    c = mb.cid(col)
    for f in faces:
        if f.is_valid and fn(f):
            f[mb.lay] = c


def leg(mb, col, col_foot, pts, radii, foot_c, foot_s):
    tube(mb, col, pts, radii, n=6, e=2.6, cap_a=False, cap_b=True, smooth=False)
    rbox(mb, col_foot, foot_c, foot_s, r=0.014, n=6, e=4.0, k=1)


# ===================================================================== HORACE
# The precise, fast one.  Everything about him is raked forward except the ears,
# which stream backward — that opposition is the whole silhouette.
def horace(ox):
    P = {}
    global HOR_PIV, HOR_R, HOR_AX
    HOR_PIV = Vector((ox, -0.150, 0.516))      # inside the base of the skull
    HOR_R = 0.046
    HOR_AX = (0, -0.206, 0.978)

    # ---- BODY: tall, narrow, leaning hard over the wheel -------------------
    mb = MB()
    BODY = [
        (0.000, 0.136, 0.144, 0.140,  0.000, 3.8),
        (0.080, 0.131, 0.137, 0.131, -0.026, 3.3),
        (0.172, 0.121, 0.123, 0.115, -0.058, 3.0),
        (0.266, 0.109, 0.107, 0.099, -0.092, 2.8),
        (0.346, 0.093, 0.089, 0.081, -0.120, 2.6),
        (0.400, 0.069, 0.065, 0.059, -0.136, 2.5),
    ]
    rings = [egg_ring(12, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, BLUE, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.050, BLUE_D)
    # cream collar cut in at the shoulders so the head reads joined on
    coll = [f for f in body if f.is_valid and f.calc_center_median().z > 0.352]
    recess(mb, coll, 0.011, -0.005, col=CREAM)
    # Racing number panel CUT INTO the back — rivals are usually seen from
    # behind or side.  A disc laid on top floats off a lofted back and shows as
    # a sliver from side-on; an inset region follows the surface.
    bk = [f for f in body if f.is_valid and f.normal.y > 0.52
          and 0.115 < f.calc_center_median().z < 0.310]
    recess(mb, bk, 0.015, -0.007, col=CREAM)
    # Neck, raked forward and LONG — he is the tall one.  It runs on THROUGH
    # the head pivot and dies inside the skull's throat socket, so the join
    # cannot open however far Unity swings the head.
    neck = [Vector((ox, -0.116, 0.352)), Vector((ox, -0.138, 0.457)),
            HOR_PIV, HOR_PIV + Vector(HOR_AX) * 0.030]
    nr = [0.058, 0.042, 0.034, 0.016]
    tube(mb, CREAM, neck, nr, n=10, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(HOR_PIV, neck[-2:], nr[-2:], HOR_R * 0.92, "Horace neck")
    # legs down to the footplate
    for sx in (-1, 1):
        leg(mb, BLUE_D, SLATE,
            [Vector((ox + sx * 0.082, -0.060, 0.014)),
             Vector((ox + sx * 0.094, -0.176, -0.006)),
             Vector((ox + sx * 0.096, -0.232, -0.066)),
             Vector((ox + sx * 0.094, -0.240, -0.126))],
            [0.070, 0.056, 0.043, 0.036],
            (ox + sx * 0.094, -0.272, -0.148), (0.078, 0.142, 0.048))
    P["Body"] = (mb.finish("Horace_Body", MAT, 30.0), (ox, 0.0, 0.0))

    # ---- HEAD: long narrow hare skull, goggles, ears back ------------------
    mb = MB()
    HEAD = [
        ( 0.048, 0.054, 0.060, 0.574, 3.0),
        (-0.014, 0.068, 0.075, 0.578, 2.8),
        (-0.082, 0.062, 0.064, 0.564, 2.8),
        (-0.146, 0.045, 0.046, 0.548, 2.8),
        (-0.196, 0.031, 0.031, 0.536, 2.6),
    ]
    hr = [ring_y(10, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    head, _ = mb.pole_loft(hr, CREAM, pole_a=(ox, 0.086, 0.570),
                           pole_b=(ox, -0.232, 0.530), smooth=False)
    col_faces(mb, head, lambda f: f.calc_center_median().y < -0.150, CREAM_SH)
    # throat: sphere about the pivot below the jaw, buried in the skull above
    L.throat(mb, CREAM, HOR_PIV, HOR_AX, HOR_R,
             [(ox, -0.142, 0.550), (ox, -0.124, 0.576)], [0.036, 0.026],
             n=10, e=2.6, smooth=False)
    sphere(mb, ORANGE_D, (ox, -0.228, 0.532), (0.020, 0.016, 0.015), seg=6, rings=3)

    # EARS — the silhouette.  Long flat blades swept back over the shoulders,
    # narrow in X and tall in Z so they read from the side at 40 px, and splayed
    # outward in X so they still read as TWO ears head-on.
    for sx in (-1, 1):
        tube(mb, CREAM,
             [Vector((ox + sx * 0.038, 0.016, 0.610)),
              Vector((ox + sx * 0.066, 0.098, 0.646)),
              Vector((ox + sx * 0.088, 0.184, 0.650)),
              Vector((ox + sx * 0.100, 0.256, 0.620))],
             [0.034, 0.034, 0.026, 0.008], n=6, e=2.8,
             squash=[(0.46, 1.0), (0.44, 1.0), (0.42, 1.0), (0.40, 1.0)],
             smooth=False)
        # inner ear, darker, so the blade does not read as a bone
        tube(mb, CREAM_SH,
             [Vector((ox + sx * 0.034, 0.028, 0.608)),
              Vector((ox + sx * 0.061, 0.104, 0.642)),
              Vector((ox + sx * 0.083, 0.180, 0.646))],
             [0.020, 0.020, 0.014], n=5, e=2.8,
             squash=[(0.34, 1.0)] * 3, smooth=False)

    # Blue racing skullcap, worn a touch askew (ART_BIBLE: asymmetry sells).
    # It has to sit ABOVE the crown (skull top is z 0.653) or it is swallowed.
    sphere(mb, BLUE, (ox + 0.009, -0.008, 0.628), (0.083, 0.093, 0.048),
           seg=10, rings=4)
    # goggles: a dark strap round the skull with two glass lenses
    tube(mb, DARK,
         [Vector((ox + 0.080, -0.038, 0.572)), Vector((ox + 0.052, 0.024, 0.608)),
          Vector((ox - 0.052, 0.024, 0.608)), Vector((ox - 0.080, -0.038, 0.572)),
          Vector((ox - 0.058, -0.078, 0.550)), Vector((ox + 0.058, -0.078, 0.550))],
         0.011, n=4, e=3.0, loop=True, smooth=False)
    for sx in (-1, 1):
        sphere(mb, DARK, (ox + sx * 0.052, -0.086, 0.564),
               (0.030, 0.020, 0.028), seg=8, rings=3)
        sphere(mb, GLASS, (ox + sx * 0.052, -0.098, 0.564),
               (0.023, 0.014, 0.021), seg=8, rings=3)
        # whiskers of cheek fluff keep the muzzle from reading as a cone
        sphere(mb, CREAM, (ox + sx * 0.046, -0.150, 0.536),
               (0.028, 0.030, 0.023), seg=6, rings=3)
    P["Head"] = (mb.finish("Horace_Head", MAT, 30.0), tuple(HOR_PIV))

    # ---- ARMS: straight out to the wheel, elbows locked --------------------
    for nm, sx in (("Arm_L", 1), ("Arm_R", -1)):
        mb = MB()
        sh = Vector((ox + sx * 0.098, -0.108, 0.352))
        h = Vector((ox + sx * 0.112, -0.318, 0.298))
        mid = Vector((ox + sx * 0.116, -0.216, 0.316))
        tube(mb, BLUE, [sh, mid, h], [0.048, 0.036, 0.029], n=7, e=2.8,
             smooth=False)
        sphere(mb, DARK, h + Vector((0, -0.014, -0.006)),
               (0.033, 0.032, 0.030), seg=8, rings=3)
        P[nm] = (mb.finish("Horace_" + nm, MAT, 30.0), tuple(sh))
    return P


# ===================================================================== MARGOT
# The wild, flamboyant one.  Low and broad, folded over the wheel, with an amber
# scarf streaming off the back — a diagonal nothing else in the set has.
def margot(ox):
    P = {}
    global MAR_PIV, MAR_R, MAR_AX
    MAR_PIV = Vector((ox, -0.140, 0.318))      # inside the base of the skull
    MAR_R = 0.088
    MAR_AX = (0, 0.30, 0.954)

    mb = MB()
    BODY = [
        (0.000, 0.206, 0.176, 0.176,  0.000, 4.2),
        (0.068, 0.230, 0.194, 0.188, -0.030, 3.8),
        (0.148, 0.238, 0.194, 0.180, -0.070, 3.4),
        (0.226, 0.222, 0.176, 0.156, -0.110, 3.0),
        (0.288, 0.182, 0.146, 0.126, -0.142, 2.8),
        (0.324, 0.128, 0.108, 0.092, -0.158, 2.6),
    ]
    rings = [egg_ring(14, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, AMBER, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.048, AMBER_D)
    # a flapping open coat: darker panels cut into the flanks
    fl = [f for f in body if f.is_valid and abs(f.normal.x) > 0.42
          and 0.055 < f.calc_center_median().z < 0.250]
    recess(mb, fl, 0.016, -0.008, col=AMBER_D)
    # dark racing stripe over the spine, back to front
    col_faces(mb, body, lambda f: abs(f.calc_center_median().x - ox) < 0.052
              and f.normal.z > 0.30, DARK)
    # neck: up through the pivot, dying inside the skull's throat socket
    neck = [Vector((ox, -0.160, 0.240)), Vector((ox, -0.150, 0.286)),
            MAR_PIV, MAR_PIV + Vector(MAR_AX) * 0.040]
    nr = [0.132, 0.100, 0.078, 0.040]
    tube(mb, SLATE, neck, nr, n=12, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(MAR_PIV, neck[-2:], nr[-2:], MAR_R * 0.92, "Margot neck")

    # THE SCARF — an UPRIGHT amber banner torn back over her shoulder, with a
    # wave in it.  Flat in X and tall in Z: she is read from the side over a
    # hedge, and a ribbon lying flat is invisible from there.
    # It is thrown over her RIGHT SHOULDER, well outboard of the skull.  Run up
    # the centreline it passed straight through the head, and every time the
    # head turned it sliced out through the banner.
    tube(mb, AMBER,
         [Vector((ox + 0.100, -0.190, 0.250)),
          Vector((ox + 0.142, -0.060, 0.288)),
          Vector((ox + 0.124,  0.100, 0.332)),
          Vector((ox + 0.060,  0.242, 0.348)),
          Vector((ox - 0.020,  0.352, 0.386))],
         [0.046, 0.058, 0.058, 0.048, 0.030], n=6, e=3.2,
         squash=[(0.34, 1.10), (0.30, 1.25), (0.28, 1.30),
                 (0.28, 1.25), (0.26, 1.05)], smooth=False)
    sphere(mb, AMBER_D, (ox + 0.096, -0.202, 0.244), (0.054, 0.040, 0.040),
           seg=8, rings=3)
    # legs — short, splayed wide
    for sx in (-1, 1):
        leg(mb, DARK, SLATE,
            [Vector((ox + sx * 0.132, -0.104, 0.012)),
             Vector((ox + sx * 0.146, -0.214, -0.022)),
             Vector((ox + sx * 0.148, -0.268, -0.086))],
            [0.078, 0.060, 0.046],
            (ox + sx * 0.148, -0.300, -0.108), (0.086, 0.146, 0.046))
    P["Body"] = (mb.finish("Margot_Body", MAT, 30.0), (ox, 0.0, 0.0))

    # ---- HEAD: badger wedge, thrust forward and DOWN ------------------------
    mb = MB()
    # Chin down but MUZZLE UP — a hunched driver whose face you can still see.
    HEAD = [
        ( 0.076, 0.096, 0.088, 0.334, 3.0),
        (-0.006, 0.116, 0.104, 0.338, 3.2),
        (-0.096, 0.090, 0.078, 0.334, 3.4),
        (-0.172, 0.058, 0.050, 0.332, 3.4),
        (-0.218, 0.041, 0.035, 0.334, 3.0),
    ]
    hr = [ring_y(12, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    head, _ = mb.loft(hr, WHITE, cap_a=True, cap_b=True, smooth=False)
    L.throat(mb, SLATE, MAR_PIV, MAR_AX, MAR_R,
             [(ox, -0.102, 0.352), (ox, -0.062, 0.362)], [0.058, 0.046],
             n=12, e=2.6, smooth=False)

    # The badger mask, banded by ANGLE about the head axis (banding by world X
    # breaks where the skull narrows into the snout).
    def band(f):
        c = f.calc_center_median()
        return abs(math.atan2(c.x - ox, c.z - 0.336))
    col_faces(mb, head, lambda f: 0.36 < band(f) < 1.24, DARK)
    col_faces(mb, head, lambda f: band(f) >= 2.30, CREAM_SH)
    sphere(mb, DARK, (ox, -0.244, 0.332), (0.032, 0.028, 0.026), seg=8, rings=3)
    for sx in (-1, 1):
        sphere(mb, WHITE, (ox + sx * 0.100, 0.026, 0.406),
               (0.038, 0.032, 0.036), seg=6, rings=3)
        sphere(mb, DARK, (ox + sx * 0.106, 0.018, 0.414),
               (0.024, 0.020, 0.022), seg=6, rings=3)
        sphere(mb, "F7F3E4", (ox + sx * 0.068, -0.098, 0.362),
               (0.025, 0.022, 0.025), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.072, -0.113, 0.360),
               (0.016, 0.015, 0.016), seg=6, rings=3)
    # amber flat cap, worn askew, high enough to leave the mask readable
    sphere(mb, AMBER, (ox + 0.018, -0.020, 0.410), (0.096, 0.094, 0.036),
           seg=10, rings=4)
    rbox(mb, AMBER_D, (ox + 0.012, -0.132, 0.398), (0.126, 0.098, 0.016),
         r=0.009, n=8, e=4.5, k=1)
    P["Head"] = (mb.finish("Margot_Head", MAT, 30.0), tuple(MAR_PIV))

    # ---- ARMS: flung right out to the ends of the bar ----------------------
    # Amber sleeves, dark paws: the livery has to survive next to her black legs.
    for nm, sx in (("Arm_L", 1), ("Arm_R", -1)):
        mb = MB()
        sh = Vector((ox + sx * 0.196, -0.128, 0.272))
        h = Vector((ox + sx * 0.184, -0.398, 0.228))
        mid = Vector((ox + sx * 0.212, -0.272, 0.238))
        tube(mb, AMBER, [sh, mid, h], [0.078, 0.058, 0.045], n=8, e=2.6,
             smooth=False)
        sphere(mb, DARK, h + Vector((0, -0.016, -0.004)),
               (0.048, 0.044, 0.044), seg=8, rings=3)
        P[nm] = (mb.finish("Margot_" + nm, MAT, 30.0), tuple(sh))
    return P


# ==================================================================== BRAMBLE
# The steady one.  A perfectly upright ball of fleece with a small calm head on
# top and the arms tucked in — no rake, no diagonal, nothing streaming.
def bramble(ox):
    P = {}
    global BRA_PIV, BRA_R, BRA_AX
    BRA_PIV = Vector((ox, -0.066, 0.462))      # inside the base of the skull
    BRA_R = 0.072
    BRA_AX = (0, -0.19, 0.982)

    mb = MB()
    FLEECE = [(0.030, 0.126, 0.122, 0.000, 0.014),
              (0.112, 0.192, 0.186, -0.006, 0.023),
              (0.212, 0.214, 0.206, -0.010, 0.025),
              (0.312, 0.194, 0.188, -0.012, 0.023),
              (0.382, 0.132, 0.128, -0.014, 0.016)]
    rings = []
    for (z, rx, ry, cy, bump) in FLEECE:
        r = []
        for i in range(14):
            a = TAU * (i + 0.5) / 14
            k = bump if i % 2 == 0 else -bump * 0.55
            r.append(Vector((ox + (rx + k) * math.cos(a),
                             cy + (ry + k) * math.sin(a), z)))
        rings.append(r)
    body, _ = mb.pole_loft(rings, CREAM, pole_a=(ox, 0.0, -0.006),
                           pole_b=(ox, -0.020, 0.424), smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.070, CREAM_SH)
    # the green knitted tank top: a wide band round the fattest part, cut in so
    # it reads as a garment sitting in the fleece rather than paint on it
    vest = [f for f in body if f.is_valid
            and 0.130 < f.calc_center_median().z < 0.300]
    recess(mb, vest, 0.013, -0.010, col=GREEN)
    for k in range(2):
        cyl(mb, GREEN_D, (ox, -0.212 - 0.006 * k, 0.180 + k * 0.078),
            (ox, -0.234 - 0.006 * k, 0.180 + k * 0.078), 0.017, 0.014, n=6)
    # neck: on through the pivot, dying inside the fleece ruff socket
    neck = [Vector((ox, -0.050, 0.372)), Vector((ox, -0.058, 0.420)),
            BRA_PIV, BRA_PIV + Vector(BRA_AX) * 0.040]
    nr = [0.098, 0.076, 0.062, 0.030]
    tube(mb, CREAM, neck, nr, n=12, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(BRA_PIV, neck[-2:], nr[-2:], BRA_R * 0.92, "Bramble neck")
    for sx in (-1, 1):
        leg(mb, SLATE, SLATE,
            [Vector((ox + sx * 0.098, -0.096, 0.010)),
             Vector((ox + sx * 0.104, -0.176, -0.036)),
             Vector((ox + sx * 0.104, -0.196, -0.092))],
            [0.044, 0.036, 0.030],
            (ox + sx * 0.104, -0.222, -0.110), (0.066, 0.116, 0.042))
    P["Body"] = (mb.finish("Bramble_Body", MAT, 34.0), (ox, 0.0, 0.0))

    # ---- HEAD: small, dark, calm, dead level -------------------------------
    mb = MB()
    sphere(mb, SLATE, (ox, -0.096, 0.514), (0.074, 0.094, 0.076), seg=10, rings=5)
    # fleece ruff = the socket: spherical about the pivot, so the join under his
    # chin is fixed however far the head turns
    L.throat(mb, CREAM, BRA_PIV, BRA_AX, BRA_R,
             [(ox, -0.080, 0.510), (ox, -0.090, 0.540)], [0.058, 0.040],
             n=12, e=2.4, smooth=False)
    sphere(mb, SLATE, (ox, -0.172, 0.486), (0.043, 0.048, 0.038), seg=8, rings=3)
    sphere(mb, DARK, (ox, -0.208, 0.482), (0.024, 0.016, 0.019), seg=6, rings=3)
    # the forelock: fleece flopping over the brow — reads sheep instantly
    sphere(mb, CREAM, (ox - 0.006, -0.062, 0.572), (0.086, 0.070, 0.050),
           seg=10, rings=4)
    for sx in (-1, 1):
        sphere(mb, "F7F3E4", (ox + sx * 0.044, -0.150, 0.518),
               (0.023, 0.020, 0.023), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.047, -0.164, 0.516),
               (0.014, 0.013, 0.014), seg=6, rings=3)
        # ears out to the side, flat, level — calm
        tube(mb, SLATE, [Vector((ox + sx * 0.062, -0.086, 0.522)),
                         Vector((ox + sx * 0.136, -0.072, 0.508))],
             [0.030, 0.013], n=6, e=3.0, squash=[(1.0, 0.52)] * 2, smooth=False)
        # small curled horn, brass, so he has one hard detail
        tube(mb, BRASS, [Vector((ox + sx * 0.050, -0.026, 0.582)),
                         Vector((ox + sx * 0.084, 0.014, 0.566)),
                         Vector((ox + sx * 0.086, 0.022, 0.524))],
             [0.019, 0.015, 0.008], n=5, e=3.0, smooth=False)
    # green flat cap, level, worn exactly straight — that IS his character
    sphere(mb, GREEN, (ox, -0.048, 0.584), (0.080, 0.076, 0.030), seg=10, rings=4)
    sphere(mb, GREEN_D, (ox, -0.048, 0.608), (0.020, 0.020, 0.013), seg=6, rings=3)
    P["Head"] = (mb.finish("Bramble_Head", MAT, 34.0), tuple(BRA_PIV))

    # ---- ARMS: short, tucked in, holding the bar close ---------------------
    # Green sleeves and set outboard of the fleece, or they vanish into it (and
    # tear out of it the moment Unity swings them).
    for nm, sx in (("Arm_L", 1), ("Arm_R", -1)):
        mb = MB()
        sh = Vector((ox + sx * 0.190, -0.084, 0.288))
        h = Vector((ox + sx * 0.140, -0.284, 0.252))
        mid = Vector((ox + sx * 0.180, -0.194, 0.260))
        tube(mb, GREEN, [sh, mid, h], [0.054, 0.041, 0.032], n=7, e=2.8,
             smooth=False)
        sphere(mb, SLATE, h + Vector((0, -0.014, -0.004)),
               (0.035, 0.032, 0.032), seg=8, rings=3)
        P[nm] = (mb.finish("Bramble_" + nm, MAT, 34.0), tuple(sh))
    return P


# ======================================================================= BUILD
PARTS = ("Body", "Head", "Arm_L", "Arm_R")


def main():
    fresh_scene()
    specs = [("Horace", horace, -1.05), ("Margot", margot, 0.0),
             ("Bramble", bramble, 1.05)]
    roots, allobjs, allmesh, groups = [], [], [], []
    for name, fn, ox in specs:
        P = fn(ox)
        meshes = [P[k][0] for k in PARTS]
        # no ground plane: they sit on a mower, their feet hang below z = 0
        bake_ao(meshes, floor=0.76, dist=0.20, samples=28, ground=False)
        hi = max(max(v.co.z for v in ob.data.vertices) for ob in meshes)
        lo = min(min(v.co.z for v in ob.data.vertices) for ob in meshes)
        L.joint_audit(P["Body"][0], P["Head"][0], P["Head"][1], name, near=0.26)
        for k in PARTS:
            set_pivot(P[k][0], P[k][1])
        root = make_empty(name + "_Root", (ox, 0, 0))
        for k in PARTS:
            attach(P[k][0], root)
        roots.append((root, ox))
        groups.append((name, meshes, P["Head"][1]))
        allobjs += [root] + meshes
        allmesh += meshes
        report_tris(meshes, "%s (seat->top %.2f m, foot %.2f m)" % (name, hi, lo))

    for root, ox in roots:
        root.location = (0, 0, 0)
    L.verify_meshes(allmesh, "Rivals")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "Rivals.fbx"))
    for root, ox in roots:
        root.location = (ox, 0, 0)
    if L.want_tilt():
        for name, meshes, piv in groups:
            hd = [m for m in meshes if m.name.endswith("_Head")][0]
            L.tilt_sheet(hd, meshes, piv, None, "rival_" + name.lower())
    for name, meshes, piv in groups:
        render_previews(meshes, "rival_" + name.lower(), res=512, solo=True)
    render_previews(allmesh, "rivals", res=700)
    print("DONE Rivals")


main()
