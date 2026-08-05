# build_judges.py — DUCK MOW: the three animal judges, seated behind the bench.
# Run: blender --background --python C:\Duck\Art\Blender\build_judges.py
#
# Three roots in one FBX.  Base z = 0 is the SEAT contact (built from the bench up).
# Each has a deliberately different silhouette:
#   Mildred (goat)     1.13 m  angular + a very long thin neck   -> vertical spike
#   Boris   (badger)   0.93 m  wide, low, round, arms up         -> broad triangle
#   Priscilla (heron)  1.27 m  extremely narrow + long S-neck    -> thin S
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc, quad,
                      torus, rbox, recess, set_pivot, make_empty, attach, card_uv,
                      flat_card, report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_Judges"
WHITE, WHITE_SH = "F1EDE0", "DCC9A4"
DARK, SLATE = "241F1E", "4A4F55"
WOOD_D, BRASS = "wood_dark", "brass"
CREAM, TIE_RED = "F4E7CF", "D8534E"
AMBER, ORANGE = "C9A55A", "F2A03D"
SAGE = "7FA8A0"
CARD_FACE, CARD_EDGE = "F7F3E4", "DCC9A4"


def egg_ring(n, z, rx, ryF, ryB, cy, e=2.0):
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


# --------------------------------------------------------------------- hands
#
# THE JUDGES HAVE NO ARMS. They have floating mitts, the way a Mii does.
#
# They used to have jointed ones: a tapered tube from shoulder to hand with a fist
# sphere on the end, modelled held out because these characters are built to read in
# silhouette from sixty metres. Rigging that for a seated judge never came good, and
# the reasons were structural rather than a matter of finding better numbers:
#
#   - The limb is a separate closed solid butted against the torso. Any rotation at
#     the shoulder either tilts the end cap out from under the body and opens a seam,
#     or — if the pivot is sunk far enough in to prevent that — swings the whole limb
#     through the torso. Boris's arms needed 110 degrees of correction, which is well
#     past where those two failures stop overlapping.
#   - The bench makes it worse. The desk top sits barely above the judges' own base,
#     so an arm hanging correctly puts the hand at exactly tabletop height, where the
#     desk plate occludes it completely. Correct and invisible.
#
# A detached mitt has none of those problems, because there is no joint to preserve
# and nothing to intersect: it sits on the desk where it can be seen, and clapping is
# two mitts moving toward each other rather than a chain of rotations that has to stay
# attached at the far end. It also matches the rest of the game's read — these are
# chunky, simple, silhouette-first characters.
#
# WHERE THE BENCH TOP IS, in each judge's own model space.
#
# DuckSceneBuilder seats the judges at y = 0.68 and DuckMeshLibrary.BuildJudgeBenchOnly
# puts the bench top plate at y = 0.83 (centre 0.78, thickness 0.10), so the tabletop
# lands 0.15 above a judge's base. Change either of those and this has to move with them.
TABLE_Z = 0.15

# HOW FAR THE MITTS HOVER ABOVE THE TABLETOP.
#
# This started at 0.008, worked out so the hands would rest on the desk without quite
# touching it. Measured in the built scene, that is exactly what happened: Boris's mitt
# bottom sits at y = 0.838 against a tabletop surface at 0.830. Correct — and it still
# read as buried, because eight millimetres is not visible at the judging camera and a
# contact with no gap and no contact shadow looks like an intersection.
#
# Mii hands float, so the gap is free to be an obvious one. It also has to absorb
# everything the clips subtract: the idle bob takes the whole torso down (Boris's low
# point is -0.004) and the hands hang off Chest so they go with it, the hand keys
# themselves dip a few millimetres, and spinning a squashed mitt raises its own effective
# half height. 0.03 covers all of that with room to see.
TABLE_CLEARANCE = 0.03


def mitts(P, judge, ox, mat, col, spacing, forward, radius):
    """One floating mitt per side, resting on the bench in front of the torso.

    The height is DERIVED from the mitt's own size rather than typed once for all three
    judges. A single shared constant was the first attempt and it buried Boris's hands in
    the desk: his mitts have nearly twice Mildred's radius, so the same centre height put
    his 17 mm under the tabletop while hers cleared it. Anything measured off one judge
    is wrong for the other two — these characters are deliberately different sizes.

    The pivot is the mitt's own centre, which is what lets the rig move a hand by
    translating it and spin it in place without anything coming apart.
    """
    # The sphere is flattened front-to-back and slightly squashed vertically, so the half
    # height is radius * 0.92 rather than radius.
    half_height = radius * 0.92
    z = TABLE_Z + half_height + TABLE_CLEARANCE

    for nm, sx in (("Arm_L", 1), ("Arm_R", -1)):
        centre = Vector((ox + sx * spacing, forward, z))
        mb = MB()
        # Slightly wider than tall and a little flattened front to back: a mitt, not a
        # ball. Low segment counts keep it in the same faceted language as the bodies.
        sphere(mb, col, centre, (radius, radius * 0.82, half_height), seg=10, rings=5)
        P[nm] = (mb.finish("%s_%s" % (judge, nm), mat, 34.0), tuple(centre))


# ==================================================================== MILDRED
def mildred(ox):
    P = {}
    global MILD_PIV, MILD_R
    MILD_PIV = Vector((ox, -0.052, 0.846))     # base of the skull / top of neck
    MILD_R = 0.060                             # socket sphere about that pivot

    # ---- BODY: angular, narrow, bolt upright.  Severe. --------------------
    mb = MB()
    BODY = [
        (0.000, 0.146, 0.166, 0.150, 0.012, 4.2),
        (0.090, 0.164, 0.184, 0.166, 0.006, 3.8),
        (0.190, 0.166, 0.180, 0.160, -0.004, 3.5),
        (0.300, 0.154, 0.162, 0.142, -0.016, 3.2),
        (0.400, 0.128, 0.134, 0.116, -0.024, 3.0),
        (0.462, 0.098, 0.102, 0.088, -0.028, 2.8),
    ]
    rings = [egg_ring(12, z, rx, rf, rb, cy + 0, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, WHITE, cap_a=True, cap_b=True, smooth=False)
    # a severe dark collar / stole, cut in
    col = [f for f in body if f.is_valid and 0.395 < f.calc_center_median().z < 0.470]
    recess(mb, col, 0.012, -0.006, col=WOOD_D)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.09, WHITE_SH)
    # Long thin neck — the whole silhouette idea, and the joint that used to
    # come apart.  It straightens onto the pivot axis for the last 60 mm and
    # dies in a 26 mm cap 38 mm ABOVE the pivot, entirely inside the throat's
    # 60 mm socket sphere.
    neck = [Vector((ox, -0.028, 0.450)), Vector((ox, -0.034, 0.560)),
            Vector((ox, -0.042, 0.680)), Vector((ox, -0.050, 0.786)),
            MILD_PIV, MILD_PIV + Vector((0, 0, 0.038))]
    nr = [0.066, 0.056, 0.050, 0.050, 0.050, 0.026]
    tube(mb, WHITE, neck, nr, n=10, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(MILD_PIV, neck[-2:], nr[-2:], MILD_R, "Mildred neck")
    P["Body"] = (mb.finish("Mildred_Body", MAT, 30.0), (ox, 0.0, 0.0))

    # ---- HEAD: long wedge face, horns, square specs ------------------------
    mb = MB()
    HEAD = [
        (0.055, 0.058, 0.066, 0.898, 3.4),
        (-0.020, 0.079, 0.086, 0.905, 3.6),
        (-0.095, 0.071, 0.071, 0.890, 3.8),
        (-0.170, 0.053, 0.051, 0.868, 4.0),
        (-0.248, 0.043, 0.039, 0.845, 4.2),
        (-0.306, 0.037, 0.031, 0.828, 4.0),
    ]
    hr = [ring_y(12, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    head, _ = mb.loft(hr, WHITE, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, head, lambda f: f.calc_center_median().y < -0.235, WHITE_SH)
    # THROAT: the only part of the head the neck ever meets.  Its lower face is
    # a piece of the sphere centred on the pivot, so the join cannot move; it
    # then runs forward inside the skull and comes out as her jawline.
    L.throat(mb, WHITE, MILD_PIV, (0, -0.10, 0.995), MILD_R,
             [(ox, -0.100, 0.876), (ox, -0.160, 0.874), (ox, -0.215, 0.860)],
             [0.056, 0.048, 0.038], n=12, e=2.6, smooth=False)
    # muzzle seam cut in
    mz = [f for f in head if f.is_valid and f.normal.z < -0.4
          and f.calc_center_median().y < -0.14]
    recess(mb, mz, 0.008, -0.006, col=WHITE_SH)
    # horns: swept back, dark, angular
    for sx in (-1, 1):
        tube(mb, WOOD_D, [Vector((ox + sx * 0.046, 0.008, 0.968)),
                          Vector((ox + sx * 0.058, 0.078, 1.030)),
                          Vector((ox + sx * 0.058, 0.156, 1.072)),
                          Vector((ox + sx * 0.046, 0.208, 1.070))],
             [0.022, 0.017, 0.012, 0.007], n=6, e=3.0, smooth=False)
        # ears, angular, out to the side
        tube(mb, WHITE, [Vector((ox + sx * 0.070, -0.012, 0.948)),
                         Vector((ox + sx * 0.130, 0.036, 0.938)),
                         Vector((ox + sx * 0.176, 0.078, 0.920))],
             [0.030, 0.024, 0.010], n=6, e=3.4,
             squash=[(1.0, 0.55), (1.0, 0.45), (1.0, 0.40)], smooth=False)
    # goatee: a proper hanging tuft, darker so it reads off the white muzzle
    tube(mb, WHITE_SH, [Vector((ox, -0.272, 0.822)), Vector((ox, -0.278, 0.780)),
                        Vector((ox, -0.276, 0.744)), Vector((ox, -0.266, 0.716))],
         [0.028, 0.026, 0.019, 0.008], n=6, e=3.0,
         squash=[(0.85, 1.0)] * 4, smooth=False)
    # eyes: amber with a NARROW RECTANGULAR pupil, sat on the brow where the
    # skull is actually wide, so they clear the spectacle rims
    for sx in (-1, 1):
        c = Vector((ox + sx * 0.058, -0.106, 0.902))
        sphere(mb, AMBER, c, (0.026, 0.022, 0.021), seg=8, rings=3)
        rbox(mb, DARK, (c.x + sx * 0.003, c.y - 0.016, c.z),
             (0.032, 0.014, 0.010), r=0.003, n=6, e=6.0)
    # small SQUARE spectacles, sitting ON the face with the arms hugging the head
    for sx in (-1, 1):
        cx = ox + sx * 0.058
        sq = [Vector((cx - 0.030, -0.126, 0.876)), Vector((cx + 0.030, -0.128, 0.876)),
              Vector((cx + 0.030, -0.128, 0.930)), Vector((cx - 0.030, -0.126, 0.930)),
              Vector((cx - 0.030, -0.126, 0.876))]
        tube(mb, BRASS, sq, 0.0055, n=4, e=3.0, cap_a=False, cap_b=False, smooth=False)
        tube(mb, BRASS, [Vector((cx + sx * 0.030, -0.127, 0.912)),
                         Vector((ox + sx * 0.078, -0.070, 0.922)),
                         Vector((ox + sx * 0.070, -0.006, 0.930))],
             0.005, n=4, cap_a=False, cap_b=False, smooth=False)
    cyl(mb, BRASS, (ox - 0.028, -0.127, 0.906), (ox + 0.028, -0.127, 0.906), 0.005, n=4)
    P["Head"] = (mb.finish("Mildred_Head", MAT, 30.0), tuple(MILD_PIV))

    # ---- HANDS -------------------------------------------------------------
    mitts(P, "Mildred", ox, MAT, WOOD_D, spacing=0.150, forward=-0.245,
          radius=0.052)

    # ---- CARD --------------------------------------------------------------
    mb = MB()
    grip = Vector((ox + 0.130, -0.246, 0.600))
    flat_card(mb, CARD_FACE, CARD_EDGE, (grip.x, grip.y, grip.z + 0.118),
              0.200, 0.240, 0.009)
    P["Card"] = (mb.finish("Mildred_Card", MAT, 30.0), tuple(grip))
    return P


# ====================================================================== BORIS
def boris(ox):
    P = {}
    global BOR_PIV, BOR_R
    BOR_PIV = Vector((ox, -0.048, 0.575))      # inside the base of the skull
    BOR_R = 0.108
    mb = MB()
    BODY = [
        (0.000, 0.296, 0.198, 0.192, 0.000, 4.2),
        (0.105, 0.330, 0.218, 0.212, -0.006, 3.9),
        (0.225, 0.348, 0.226, 0.218, -0.016, 3.6),
        (0.345, 0.334, 0.212, 0.202, -0.030, 3.2),
        (0.445, 0.272, 0.176, 0.166, -0.040, 2.8),
        (0.515, 0.176, 0.122, 0.114, -0.044, 2.5),
    ]
    rings = [egg_ring(14, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, SLATE, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.10, DARK)
    # cream waistcoat cut into the chest
    wc = [f for f in body if f.is_valid and f.normal.y < -0.35
          and 0.090 < f.calc_center_median().z < 0.470]
    recess(mb, wc, 0.016, -0.008, col=CREAM)
    for k in range(3):     # brass buttons
        cyl(mb, BRASS, (ox, -0.214 + 0.012 * k, 0.170 + k * 0.105),
            (ox, -0.236 + 0.012 * k, 0.170 + k * 0.105), 0.017, 0.014, n=6)
    # Thick low neck + a festive bow tie.  The neck now carries on THROUGH the
    # pivot and dies 48 mm above it, inside the head's throat socket — before,
    # head and neck were two solids that merely touched (the audit's "butt
    # joint"), so every tilt opened a crescent of daylight under his jaw.
    neck = [Vector((ox, -0.044, 0.470)), Vector((ox, -0.046, 0.520)),
            BOR_PIV, BOR_PIV + Vector((0, 0, 0.048))]
    nr = [0.150, 0.118, 0.098, 0.052]
    # 16 sides, matching the throat socket: a coarse socket's facets are what
    # let the join line jitter as the head yaws.
    tube(mb, SLATE, neck, nr, n=16, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(BOR_PIV, neck[-2:], nr[-2:], BOR_R, "Boris neck")
    for sx in (-1, 1):
        tube(mb, TIE_RED, [Vector((ox, -0.168, 0.508)),
                           Vector((ox + sx * 0.088, -0.150, 0.520))],
             [0.020, 0.052], n=6, e=3.0,
             squash=[(1.0, 0.7), (1.0, 0.7)], smooth=False)
    sphere(mb, TIE_RED, (ox, -0.172, 0.508), (0.026, 0.024, 0.026), seg=6, rings=3)
    P["Body"] = (mb.finish("Boris_Body", MAT, 30.0), (ox, 0.0, 0.0))

    # ---- HEAD: bold black-and-white stripes -------------------------------
    mb = MB()
    HEAD = [
        (0.106, 0.132, 0.116, 0.722, 3.0),
        (0.020, 0.170, 0.152, 0.726, 3.2),
        (-0.070, 0.172, 0.150, 0.718, 3.4),
        (-0.162, 0.126, 0.108, 0.700, 3.6),
        (-0.244, 0.083, 0.070, 0.686, 3.6),
        (-0.296, 0.059, 0.048, 0.678, 3.2),
    ]
    hr = [ring_y(14, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    head, _ = mb.loft(hr, WHITE, cap_a=True, cap_b=True, smooth=False)
    # throat: sphere-about-the-pivot below, buried in the skull above
    L.throat(mb, SLATE, BOR_PIV, (0, -0.06, 0.998), BOR_R,
             [(ox, -0.085, 0.640), (ox, -0.125, 0.690)], [0.100, 0.072],
             n=16, e=2.4, lats=(22, 44, 64, 82, 98, 112), smooth=False)

    # The badger mask.  Banding by world X breaks where the head narrows toward
    # the snout, so band by ANGLE around the head axis instead: a white blaze on
    # top, a black band each side running nose -> ear through the eye, white cheeks.
    def band(f):
        c = f.calc_center_median()
        return abs(math.atan2(c.x - ox, c.z - 0.714))
    col_faces(mb, head, lambda f: 0.34 < band(f) < 1.22, DARK)
    col_faces(mb, head, lambda f: band(f) >= 2.30, WHITE_SH)
    for sx in (-1, 1):
        sphere(mb, WHITE, (ox + sx * 0.138, 0.028, 0.842),
               (0.048, 0.040, 0.046), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.144, 0.020, 0.852),
               (0.030, 0.026, 0.028), seg=6, rings=3)
        # eyes sit INSIDE the black band (angle ~0.75 rad)
        sphere(mb, "F7F3E4", (ox + sx * 0.096, -0.132, 0.762),
               (0.027, 0.024, 0.027), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.100, -0.146, 0.760),
               (0.017, 0.016, 0.017), seg=6, rings=3)
    sphere(mb, DARK, (ox, -0.300, 0.680), (0.036, 0.030, 0.028), seg=8, rings=4)
    P["Head"] = (mb.finish("Boris_Head", MAT, 30.0), tuple(BOR_PIV))

    # ---- HANDS -------------------------------------------------------------
    mitts(P, "Boris", ox, MAT, DARK, spacing=0.268, forward=-0.250, radius=0.094)

    # card held out to the side so it never covers his face when it goes up
    mb = MB()
    grip = Vector((ox - 0.292, -0.248, 0.556))
    flat_card(mb, CARD_FACE, CARD_EDGE, (grip.x, grip.y, grip.z + 0.130),
              0.210, 0.250, 0.009)
    P["Card"] = (mb.finish("Boris_Card", MAT, 30.0), tuple(grip))
    return P


# ================================================================== PRISCILLA
def priscilla(ox):
    P = {}
    global PRIS_PIV, PRIS_R
    PRIS_PIV = Vector((ox, -0.050, 1.118))     # base of the skull / top of neck
    PRIS_R = 0.052                             # socket sphere about that pivot
    mb = MB()
    BODY = [
        (0.000, 0.126, 0.156, 0.140, 0.010, 3.0),
        (0.090, 0.148, 0.184, 0.160, 0.004, 2.6),
        (0.200, 0.152, 0.190, 0.162, -0.008, 2.4),
        (0.320, 0.138, 0.172, 0.146, -0.020, 2.2),
        (0.430, 0.108, 0.134, 0.112, -0.030, 2.1),
        (0.500, 0.074, 0.088, 0.074, -0.036, 2.0),
    ]
    rings = [egg_ring(12, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, WHITE, cap_a=True, cap_b=True, smooth=False)
    # folded sage wings on the flanks, cut in so they read as feathers not paint
    wg = [f for f in body if f.is_valid and abs(f.normal.x) > 0.35
          and 0.110 < f.calc_center_median().z < 0.450
          and f.calc_center_median().y > -0.090]
    recess(mb, wg, 0.014, -0.008, col=SAGE)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.075, WHITE_SH)
    # THE LONG S-NECK — the entire character, and the joint the whole game
    # leans on: the reveal camera is right on her while she tilts her head.
    #
    # The last three sections leave the S and run dead straight up the PIVOT
    # AXIS, swelling to 46 mm at the pivot and then dying to a 20 mm cap 32 mm
    # ABOVE it.  Everything from the pivot up is therefore inside the head's
    # 52 mm socket sphere, which is centred on that same pivot and so cannot
    # move when the head turns.  The neck emerges 23 mm BELOW the pivot, where
    # it is fractionally wider than the socket, and that emergence line is the
    # only join you ever see — at any angle, in any pose.
    S = [Vector((ox, -0.016, 0.390)), Vector((ox, -0.020, 0.478)),
         Vector((ox, 0.012, 0.610)),
         Vector((ox, 0.030, 0.732)), Vector((ox, 0.008, 0.856)),
         Vector((ox, -0.038, 0.958)), Vector((ox, -0.056, 1.042)),
         Vector((ox, -0.050, 1.090)), PRIS_PIV, PRIS_PIV + Vector((0, 0, 0.032))]
    RS = [0.058, 0.060, 0.052, 0.046, 0.043, 0.041, 0.042,
          0.045, 0.046, 0.020]
    tube(mb, WHITE, S, RS, n=10, cap_a=False, cap_b=True, smooth=True)
    L.socket_fit(PRIS_PIV, S[-2:], RS[-2:], PRIS_R, "Priscilla neck")
    P["Body"] = (mb.finish("Priscilla_Body", MAT, 38.0), (ox, 0.0, 0.0))

    # ---- HEAD: small, immaculate, long dagger beak, half-lidded eyes -------
    # Skull and socket are ONE swept skin: four sections lying exactly on the
    # sphere of radius PRIS_R about the pivot, then straight on into the skull.
    # No sphere sat on a tube anywhere.
    mb = MB()
    sp, sr = L.socket_path(PRIS_PIV, (0, 0, 1), PRIS_R, lats=(24, 55, 84, 106))
    path = list(sp) + [Vector((ox, -0.054, 1.152)), Vector((ox, -0.058, 1.178)),
                       Vector((ox, -0.058, 1.198))]
    radii = list(sr) + [0.056, 0.050, 0.032]
    sq = [(1.0, 1.0)] * 4 + [(1.0, 1.22), (1.0, 1.30), (1.0, 1.22)]
    tg = [(0, 0, 1)] * 4 + [None] * 3      # socket rings stay on the sphere
    mb.pole_loft(L.sweep_rings(path, radii, n=10, squash=sq, tangents=tg), WHITE,
                 pole_a=tuple(PRIS_PIV - Vector((0, 0, PRIS_R))),
                 pole_b=(ox, -0.058, 1.212), smooth=True)
    BEAK = [
        (-0.086, 0.034, 0.036, 1.158, 2.6),
        (-0.150, 0.028, 0.028, 1.152, 3.0),
        (-0.230, 0.019, 0.019, 1.144, 3.0),
        (-0.310, 0.010, 0.010, 1.136, 2.8),
    ]
    br = [ring_y(8, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in BEAK]
    bk, _ = mb.pole_loft(br, ORANGE, pole_b=(ox, -0.362, 1.131))
    col_faces(mb, bk, lambda f: f.normal.z < -0.3, "D3792A")
    # crest plumes, swept back - aloof.  Rooted INSIDE the new crown, not on it.
    for k, sx in ((0, -1), (1, 0), (2, 1)):
        tube(mb, SLATE, [Vector((ox + sx * 0.014, -0.034, 1.192)),
                         Vector((ox + sx * 0.020, 0.056, 1.230)),
                         Vector((ox + sx * 0.024, 0.126, 1.226))],
             [0.014, 0.009, 0.004], n=5, e=3.0, smooth=False)
    # eyes, half-lidded
    for sx in (-1, 1):
        c = Vector((ox + sx * 0.046, -0.088, 1.170))
        sphere(mb, "F7F3E4", c, (0.021, 0.019, 0.020), seg=8, rings=3)
        sphere(mb, DARK, c + Vector((sx * 0.004, -0.010, -0.003)),
               (0.013, 0.012, 0.012), seg=6, rings=3)
        # the lid: a slab across the top half.  This is her whole personality.
        rbox(mb, SAGE, (c.x, c.y - 0.008, c.z + 0.011), (0.052, 0.034, 0.019),
             r=0.006, n=8, e=3.0)
    P["Head"] = (mb.finish("Priscilla_Head", MAT, 38.0), tuple(PRIS_PIV))

    mitts(P, "Priscilla", ox, MAT, SAGE, spacing=0.128, forward=-0.232,
          radius=0.044)

    mb = MB()
    grip = Vector((ox + 0.112, -0.234, 0.352))
    flat_card(mb, CARD_FACE, CARD_EDGE, (grip.x, grip.y, grip.z + 0.112),
              0.190, 0.230, 0.009)
    P["Card"] = (mb.finish("Priscilla_Card", MAT, 30.0), tuple(grip))
    return P


# =============================================================== BUILD
def main():
    fresh_scene()
    specs = [("Mildred", mildred, -0.95), ("Boris", boris, 0.0),
             ("Priscilla", priscilla, 0.95)]
    roots, allobjs, allmesh, groups = [], [], [], []
    for name, fn, ox in specs:
        P = fn(ox)
        meshes = [P[k][0] for k in ("Body", "Head", "Arm_L", "Arm_R", "Card")]
        bake_ao(meshes, floor=0.76, dist=0.20, samples=28, ground=True,
                ground_size=1.6)
        card_uv(P["Card"][0])
        # the joint has to survive Unity spinning _Head about its own pivot
        L.joint_audit(P["Body"][0], P["Head"][0], P["Head"][1], name)
        for k in ("Body", "Head", "Arm_L", "Arm_R", "Card"):
            set_pivot(P[k][0], P[k][1])
        root = make_empty(name + "_Root", (ox, 0, 0))
        for k in ("Body", "Head", "Arm_L", "Arm_R", "Card"):
            attach(P[k][0], root)
        roots.append((root, ox))
        groups.append((name, meshes, P["Head"][1]))
        allobjs += [root] + meshes
        allmesh += meshes
        report_tris(meshes, name)

    for root, ox in roots:      # each root ships at its own origin
        root.location = (0, 0, 0)
    L.verify_meshes(allmesh, "Judges")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "Judges.fbx"))
    for root, ox in roots:      # spread them out again just for the preview
        root.location = (ox, 0, 0)
    if L.want_tilt():
        for name, meshes, piv in groups:
            hd = [m for m in meshes if m.name.endswith("_Head")][0]
            L.tilt_sheet(hd, meshes, piv, None, "judge_" + name.lower())
    for name, meshes, piv in groups:
        render_previews(meshes, "judge_" + name.lower(), res=512, solo=True,
                        extra_views=[("face", (-0.25, -1.0, 0.18), 0.55)])
    render_previews(allmesh, "judges", res=640)
    print("DONE Judges")


main()
