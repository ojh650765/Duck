# build_goose.py — DUCK MOW: the defence goose, as ONE CONTINUOUS SKINNED MESH.
#
# Run: blender --background --python C:\Duck\Art\Blender\build_goose.py
#      (add --preview to also render the sheets)
#
# WHY THIS FILE EXISTS
#
# The bird was a runtime greybox in GooseDefence.BuildGoose: a capsule body with two
# PrimitiveType.Cube wings parented to a root, animated by rotating those boxes about
# their own pivots. The verdict on it was exact and correct — "거위 날개짓이 너무 사각형팔
# 날개짓임" — the flap read as two square arms swinging, because that is literally what it
# was. No amount of easing fixes a plank on a hinge: a wing reads as a wing when the
# MEMBRANE bends across its span, and a membrane that bends has to be part of the same
# surface as the body.
#
# So everything here is one closed manifold: tail, body, breast, neck, skull, bill, both
# wings and both legs. There is not a single separate solid anywhere on the animal, and
# there is no place where two shells intersect and hope AO hides the seam. The technique
# that makes that possible without a boolean:
#
#   1  Loft the whole spine of the animal — tail fan through body, breast, neck, skull and
#      bill — as ONE swept skin over 29 stations, 12 sides, with per-station asymmetric
#      up/down radii so the breast and belly can be full while the back stays lean.
#      The grid of faces is kept as F[ring_pair][column], so every later operation can
#      name the exact patch of skin it wants instead of guessing at it geometrically.
#   2  For each limb, DELETE a one-column-wide patch of that skin (bmesh 'FACES', which
#      also takes the interior edges with it) and treat the boundary loop that is left as
#      ring zero of the limb. The limb is then lofted outward from the body's own
#      vertices. There is no bridge, no weld, no boolean: the wing's root ring and the
#      body's shoulder ring are THE SAME TEN VERTICES.
#   3  Because the limb inherits the socket's cross-section, the shoulder is a real
#      shoulder. A 10-vertex socket becomes a 10-vertex wing section — five stations
#      along the chord, two across the thickness — which is exactly the topology an
#      airfoil wants, and which lets the wrist fold on a SWEPT line rather than a
#      straight hinge (see WEIGHTS below).
#
# ORIENTATION. Built Z-up facing -Y in Blender, which duck_lib's exporter
# (axis_forward='-Z', axis_up='Y') turns into Y-up facing +Z in Unity. The origin is the
# BODY CENTRE, matching the greybox root, so PlaceGoose's single LookRotation with no
# correction Euler keeps working.
#
# READABILITY. Two decisions from the greybox are load-bearing and are preserved exactly:
# the body is dark (linear 0.17/0.15/0.14 — see BODY_COL) because a dark value separates
# from pale sky AND pale sage ground where any hue only separates from one of them; and
# the bill stays bright orange and lives on its OWN MATERIAL SLOT, so the strike flash can
# recolour the body without ever losing the one hard value edge that states where the bird
# is going.

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import MB, fresh_scene, bake_ao, report_tris, render_previews, get_mat, TAU

MAT      = "M_Goose"
MAT_BILL = "M_Goose_Bill"
NAME     = "Goose"
FPS      = 24                      # what the judges' FBX ships at (Blender factory default)

# ---------------------------------------------------------------------------- colour
#
# These hexes are sRGB, which is the space the whole pipeline speaks: duck_lib.hexcol
# converts to linear for the Col attribute, the exporter writes it back out as sRGB
# bytes, Unity imports the bytes untouched, and Duck/Prop's _VCOL_SRGB keyword converts
# once more. The round trip is exact, so the hex below IS what the shader multiplies.
#
# BODY_COL is the greybox's Color(0.17, 0.15, 0.14) expressed in that space: those floats
# went straight into _BaseColor, which Duck/Prop uses as a LINEAR albedo, and linear 0.17
# is sRGB 0x72. Do not "correct" this to 0x2C on the grounds that it looks light in a
# swatch — 0x2C would be four times darker than the value the readability pass settled on.
BODY_COL  = "726C69"     # flanks — linear (0.170, 0.150, 0.140)
BACK_COL  = "7A7470"     # spine and crown, a touch lighter so the form turns
BELLY_COL = "615C58"     # underside, a touch darker; the bird is dark from every angle
TAIL_COL  = "56514E"     # tail fan, darkest of the body tones
RUMP_COL  = "8E867B"     # the goose's pale uppertail band. Deliberately mid, not white:
                         # a white rump would compete with the bill for "which end is
                         # which", and the bill has to win that.
CHIN_COL  = "BCB3A0"     # the chinstrap: throat and lower cheek, and it points forward.
                         # Held at mid value on purpose. At cream (E8DFCB) it out-shone the
                         # bill, and the bill has to hold the brightest edge on the animal.
EYE_COL   = "17130F"
BROW_COL  = "45403C"     # the ridge over the eye: darker than the crown so the raised
                         # facet still reads as a shadow line when the sun is behind
SECOND    = "645F5B"     # mid wing
PRIMARY   = "4E4946"     # outer wing — darkest, so the beat reads as a dark arc on sky
                         # The INNER wing is deliberately the plain body tone. It was a
                         # lighter covert grey for one pass and the wing root read as a
                         # shoulder pad bolted to the flank — a lighter value on a raised
                         # surface is exactly how "applied panel" looks, which is the read
                         # this whole rebuild exists to get rid of.
LEG_COL   = "605A56"     # legs stay dark: they are anatomy, not signal
BILL_TOP  = "FFC24A"
BILL_LOW  = "F2A03D"     # duck_orange, so the goose's bill belongs to the same set

NSIDE = 12
ROT0  = -15.0            # so vertex j sits at -15 + 30j degrees: column 0 straddles +X,
                         # column 3 is dead top, column 9 is dead bottom, and the mirror
                         # pairs are (0,6) (1,5) (2,4) (7,11) (8,10).

# ------------------------------------------------------------------------- the spine
# (y, z, rx, rz_up, rz_down, e)   forward is -Y.  Ring 0 is the tail tip.
BODY = [
    ( 0.782,  0.166, 0.058, 0.014, 0.013, 2.60),   # 0  tail tip — BLUNT, not a spike: the
                                                   #    first pass tapered to 30 mm and read
                                                   #    as a dorsal fin from the side
    ( 0.690,  0.158, 0.130, 0.019, 0.016, 3.60),   # 1  tail fan — wide and flat, not a cube
    ( 0.585,  0.140, 0.168, 0.028, 0.024, 3.40),   # 2  widest point of the fan, 336 mm
    ( 0.482,  0.112, 0.145, 0.052, 0.044, 2.90),   # 3  tail root
    ( 0.392,  0.080, 0.124, 0.086, 0.076, 2.50),   # 4  rump
    ( 0.310,  0.048, 0.142, 0.122, 0.114, 2.40),   # 5  leg socket
    ( 0.230,  0.025, 0.163, 0.150, 0.150, 2.30),   # 6  leg socket
    ( 0.150,  0.008, 0.182, 0.170, 0.172, 2.25),   # 7  leg socket
    ( 0.070, -0.002, 0.196, 0.182, 0.188, 2.20),   # 8  wing socket
    (-0.010, -0.005, 0.204, 0.188, 0.196, 2.20),   # 9  wing socket
    (-0.090, -0.004, 0.204, 0.190, 0.196, 2.20),   # 10 wing socket
    (-0.170,  0.000, 0.198, 0.188, 0.192, 2.20),   # 11 wing socket
    (-0.250,  0.008, 0.186, 0.180, 0.186, 2.20),   # 12 wing socket
    (-0.330,  0.022, 0.166, 0.166, 0.176, 2.20),   # 13
    (-0.405,  0.042, 0.140, 0.145, 0.162, 2.25),   # 14
    (-0.465,  0.072, 0.112, 0.118, 0.142, 2.30),   # 15 breast
    # THE NECK IS THE SPECIES. The first pass ran the skull to y = -0.785 and the bird read
    # as a duck: breast straight into head, no neck to speak of, and nothing for the strike
    # to whip. It is now 370 mm from throat to nape and 20% thinner, which is what makes the
    # head a separate mass in silhouette instead of a bulge on the chest.
    # EIGHT stations became TWELVE for the second pass.  The neck is now a four-bone chain
    # because the strike whips it, and two rings per bone cannot draw an S-curve -- the
    # first version of `Struck` bent the neck at two visible corners.  Three rings per bone
    # is the minimum that reads as a curve rather than as a linkage.
    (-0.512,  0.100, 0.090, 0.094, 0.112, 2.30),   # 16 throat
    (-0.560,  0.146, 0.072, 0.074, 0.082, 2.25),   # 17 neck base
    (-0.586,  0.170, 0.066, 0.068, 0.072, 2.22),   # 18
    (-0.612,  0.192, 0.062, 0.063, 0.064, 2.20),   # 19
    (-0.640,  0.212, 0.060, 0.061, 0.062, 2.20),   # 20
    (-0.668,  0.230, 0.058, 0.059, 0.060, 2.20),   # 21
    (-0.722,  0.258, 0.056, 0.056, 0.056, 2.20),   # 22 mid neck
    (-0.750,  0.268, 0.056, 0.056, 0.056, 2.20),   # 23
    (-0.778,  0.276, 0.056, 0.056, 0.055, 2.20),   # 24
    # THE SKULL IS A MASS NOW.  At rx 0.070 against a 0.056 neck the head was a swelling on
    # a hose: at 30 px the bird had no head at all, just a tapering line ending in an orange
    # dot.  164 mm across against a 112 mm neck is what makes the head read as its own
    # shape, which is what "comic-menacing" needs before any brow or eye can help.
    (-0.828,  0.286, 0.070, 0.074, 0.064, 2.20),   # 25 nape
    (-0.872,  0.288, 0.082, 0.086, 0.070, 2.20),   # 26 skull
    (-0.910,  0.281, 0.068, 0.062, 0.058, 2.40),   # 27 brow
    (-0.940,  0.271, 0.050, 0.035, 0.037, 2.70),   # 28 bill base
    (-0.992,  0.263, 0.044, 0.025, 0.029, 2.90),   # 29 bill
    (-1.040,  0.257, 0.034, 0.018, 0.021, 3.00),   # 30 bill
    (-1.072,  0.254, 0.019, 0.011, 0.012, 3.00),   # 31 bill tip
]
K_NAPE, K_SKULL, K_BROW, K_BILL0 = 25, 26, 27, 28
POLE_TAIL = Vector((0.0,  0.800, 0.168))
POLE_BILL = Vector((0.0, -1.094, 0.253))

WING_RINGS = [8, 9, 10, 11, 12]      # which body rings the shoulder patch spans
WING_COLS  = {"L": (1, 2), "R": (6, 5)}   # (lower row, upper row) — mirror pair
LEG_RINGS  = [5, 6, 7]
LEG_COLS   = {"L": (10, 11), "R": (9, 8)}

# ------------------------------------------------------------------------- the wing
# (x, y_centre, z_centre, chord, thickness, washout degrees)
# The leading edge runs nearly straight to the wrist and then sweeps hard back, which is
# what makes a bird wing a bird wing in silhouette rather than a paddle.
WING = [
    (0.225, -0.098, 0.104, 0.318, 0.100, 0.5),    # hugs the flank: the shoulder needs two
    (0.300, -0.096, 0.116, 0.316, 0.086, 1.0),    # short steps to become an aerofoil, or it
    (0.392, -0.082, 0.126, 0.328, 0.068, 2.0),    # reads as a plate bolted to the side
    (0.500, -0.045, 0.130, 0.312, 0.053, 3.5),    # 3 = wrist
    (0.598,  0.010, 0.126, 0.272, 0.040, 5.0),
    (0.676,  0.070, 0.117, 0.216, 0.029, 6.5),
    (0.740,  0.125, 0.104, 0.156, 0.021, 8.0),
    (0.782,  0.170, 0.090, 0.092, 0.013, 9.0),
    (0.805,  0.196, 0.080, 0.032, 0.006, 10.0),
]
WRIST_M = 3     # elbow/wrist: where Wing_1 hands over to Wing_2
HAND_M  = 5     # the hand: where Wing_2 hands over to Wing_3 (the primaries)
# chord stations, as a fraction of chord: +0.5 trailing edge .. -0.5 leading edge
CHORD_T = (0.50, 0.22, -0.04, -0.28, -0.50)
FLAT_H  = (0.50, 0.50, 0.50, 0.50, 0.50)
AIR_HU  = (0.07, 0.34, 0.46, 0.36, 0.10)
AIR_HD  = (0.07, 0.24, 0.30, 0.26, 0.10)

# -------------------------------------------------------------------------- the leg
# (x, y, z, lateral_half, deep_half).
#
# THE BIND POSE IS NOW STANDING, and that is a correction, not a preference.  The first
# version bound the leg trailing down-and-BACK with the web pointing astern, which is the
# flight pose — fine while the only clips were Fly, Glide and Struck.  Half the clips are
# now on the ground, and you cannot rotate a backward-pointing foot to point forward: it
# needs 150 degrees at the ankle, which collapses a joint that has two rings of transition
# either side of it.  Bound standing, the flight tuck is an ordinary 50-degree fold that
# the same two rings carry easily, and every ground pose is a small correction from rest.
#
# Down the hock, then the tarsus almost vertical, then the web FORWARD.  The last three
# stations are the foot: the section frame turns almost horizontal there, so widening
# laterally and pinching in the frame's deep axis produces a flat paddle.
LEG = [
    (0.093, 0.276, -0.190, 0.032, 0.060),
    (0.100, 0.300, -0.268, 0.023, 0.040),    # 1 = hock (the joint a bird bends)
    (0.106, 0.292, -0.330, 0.017, 0.028),
    (0.110, 0.284, -0.392, 0.015, 0.024),    # 3 = ankle
    (0.111, 0.232, -0.410, 0.036, 0.015),
    (0.112, 0.172, -0.418, 0.056, 0.011),    # 112 mm across, 11 mm thick: a web
    (0.113, 0.118, -0.420, 0.034, 0.009),
]
KNEE_M  = 1     # the hock — the joint a bird actually bends, high and forward
ANKLE_M = 3


# =========================================================================== helpers

def ss(a, b, x):
    if b - a < 1e-9:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def body_frame(k):
    """Origin, right and up for body ring k. Right is always world X — the animal is
    bilaterally symmetric and its path never leaves the Y/Z plane, so a Frenet frame
    would only introduce roll that has to be undone."""
    y, z = BODY[k][0], BODY[k][1]
    a = BODY[max(0, k - 1)]
    b = BODY[min(len(BODY) - 1, k + 1)]
    t = Vector((0.0, b[0] - a[0], b[1] - a[1]))
    t = t.normalized() if t.length > 1e-9 else Vector((0, -1, 0))
    right = Vector((1, 0, 0))
    up = t.cross(right).normalized()
    return Vector((0.0, y, z)), right, up


def body_ring(k):
    ctr, right, up = body_frame(k)
    _, _, rx, ru, rd, e = BODY[k]
    pts, kk = [], 2.0 / e
    for j in range(NSIDE):
        a = math.radians(ROT0) + TAU * j / NSIDE
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** kk, c) * rx
        v = math.copysign(abs(s) ** kk, s) * (ru if s >= 0 else rd)
        pts.append(ctr + right * x + up * v)
    return pts


def align(loop_co, pts):
    """The index permutation of PTS that pairs it most tightly with LOOP_CO.

    Every rotation and both reflections of a closed ring are tried and the cheapest wins.
    This is deliberately not reasoned about: the socket loop's order comes from walking a
    patch of the body's face grid and the limb's order comes from a chord/thickness
    profile, and matching those two by argument is exactly the class of mistake that cost
    rig_judges five rounds on bone axes. A measurement has no sign to get wrong.
    """
    n = len(loop_co)
    best, bestd = list(range(n)), 1e18
    for refl in (1, -1):
        for sh in range(n):
            idx = [(sh + refl * i) % n for i in range(n)]
            d = sum((Vector(pts[idx[i]]) - loop_co[i]).length for i in range(n))
            if d < bestd:
                bestd, best = d, idx
    return best, bestd


def grid_loft(mb, rings, col):
    """Loft a list of equal-length rings into a quad grid, returning F[pair][column] and
    the vertex rings. No normal fixing here: the whole animal is recalculated once, as a
    single closed manifold, which is the only context in which "outward" is well defined."""
    vr = [[mb.v(p) for p in r] for r in rings]
    F = []
    for k in range(len(vr) - 1):
        A, B = vr[k], vr[k + 1]
        row = []
        for i in range(len(A)):
            j = (i + 1) % len(A)
            row.append(mb.f((A[i], A[j], B[j], B[i]), col))
        F.append(row)
    return F, vr


def fill_untagged(mb, tag):
    """Give any vertex an operator created behind our back — an inset's rim, say — the
    weighting tag of its nearest neighbour. Cheap at this vertex count and it means a
    later detail pass cannot silently ship a vertex weighted to nothing."""
    known = [(v, tag[v]) for v in mb.bm.verts if v in tag]
    new = [v for v in mb.bm.verts if v not in tag]
    for v in new:
        src = min(known, key=lambda p: (p[0].co - v.co).length_squared)[1]
        # a COPY: the socket passes append to tag[v]["limbs"], and a shared list would
        # hand one vertex's wing influence to an unrelated one
        tag[v] = dict(src, limbs=list(src["limbs"]))
    if new:
        print("  tagged %d generated vertices from their nearest neighbour" % len(new))


def pole_fan(mb, ring, apex, col):
    p = mb.v(apex)
    n = len(ring)
    return [mb.f((ring[i], ring[(i + 1) % n], p), col) for i in range(n)]


def extend(mb, prev, rings, col, cap=True):
    """Carry an existing vertex loop outward through new rings and cap the end.

    Returns the face rows AND the vertex rows. Handing the vertices back matters: the
    alternative — finding "the verts that appeared" by diffing bmesh against a set — is
    only correct while nothing has ever been deleted from the mesh, because a freed
    element's address can be handed straight back to the next allocation and a dict keyed
    on BMesh elements then answers for the wrong vertex.
    """
    faces, verts = [], []
    for pts in rings:
        cur = [mb.v(p) for p in pts]
        row = []
        for i in range(len(cur)):
            j = (i + 1) % len(cur)
            row.append(mb.f((prev[i], prev[j], cur[j], cur[i]), col))
        faces.append(row)
        verts.append(cur)
        prev = cur
    if cap:
        mb.f(prev, col)
    return faces, verts


# ============================================================================= build

def build():
    mb = MB()
    tag = {}                       # BMVert -> weighting tag, recorded as it is created

    # ---- the one skin: tail -> body -> breast -> neck -> skull -> bill
    rings = [body_ring(k) for k in range(len(BODY))]
    F, vr = grid_loft(mb, rings, BODY_COL)
    for k, row in enumerate(vr):
        for v in row:
            tag[v] = {"k": float(k), "limbs": []}
    pole_fan(mb, vr[0], POLE_TAIL, TAIL_COL)
    bill_faces = pole_fan(mb, vr[-1], POLE_BILL, BILL_TOP)
    for k in range(K_BILL0, len(BODY) - 1):
        bill_faces += [F[k][i] for i in range(NSIDE)]
    for v in mb.bm.verts:
        if v not in tag:      # the two apex vertices, nothing deleted yet so this is safe
            tag[v] = {"k": 0.0 if v.co.y > 0 else float(len(BODY) - 1), "limbs": [],
                      "bl": 0.45 if v.co.y < 0 else 0.0}

    # THE LOWER MANDIBLE.  A honk is the single most characterful thing this animal does
    # and the charge is unreadable without it, so the bill's lower half gets its own bone.
    # The bill is part of the ONE skin, so there is no seam to open: the share is a smooth
    # product of how far down the section the vertex sits and how far out the bill it is,
    # which means the commissure stretches like rubber instead of tearing.  A real hinge
    # would need the loft split in two, and a split bill on a 1200-triangle bird is a
    # bigger lie than a rubbery one.
    BL_RING = {K_BILL0: 0.35, K_BILL0 + 1: 0.85, K_BILL0 + 2: 1.0, K_BILL0 + 3: 1.0}
    for k, row in enumerate(vr):
        rr = BL_RING.get(k, 0.0)
        for j, v in enumerate(row):
            s = math.sin(math.radians(ROT0) + TAU * j / NSIDE)
            tag[v]["bl"] = rr * max(0.0, -s)
    for v in mb.bm.verts:
        tag[v].setdefault("bl", 0.0)

    # ---- paint.  Named by grid index, not by predicate: a predicate on a swept skin
    #      picks up whatever else happens to satisfy it, and on a bird that means the
    #      belly stripe wandering onto the underside of the bill.
    def paint(pairs, cols, col):
        for k in pairs:
            for i in cols:
                f = F[k][i]
                if f and f.is_valid:
                    f[mb.lay] = mb.cid(col)

    paint(range(0, 3), range(NSIDE), TAIL_COL)
    paint(range(3, 5), (2, 3, 4), RUMP_COL)
    paint(range(5, 16), (2, 3, 4), BACK_COL)
    paint(range(4, 17), (8, 9, 10), BELLY_COL)
    paint(range(21, K_BROW), (2, 3, 4), BACK_COL)      # crown
    paint(range(24, 25), (8, 9, 10), CHIN_COL)         # the chinstrap: throat...
    paint(range(K_NAPE, K_BROW), (7, 8, 9, 10, 11), CHIN_COL)   # ...up both cheeks
    paint(range(K_BILL0, len(BODY) - 1), range(NSIDE), BILL_LOW)
    paint(range(K_BILL0, len(BODY) - 1), (2, 3, 4), BILL_TOP)

    # THE EYE, as an inset rather than a painted facet. Colouring the whole quad put a
    # 40 x 37 mm black rectangle on the side of the head — legible, and legible as a slot
    # cut in a box. Insetting first gives a 20 mm patch with a rim of skin around it, and
    # sinking it 3.5 mm lets the AO bake do the rest. Done here, BEFORE any face is
    # deleted, so the inset's new vertices are created while nothing has been freed.
    for c in (0, 6):
        f = F[K_NAPE][c]
        if f and f.is_valid:
            L.recess(mb, [f], 0.011, -0.0035, col=EYE_COL)

    # THE BROW. One face column forward and 30 degrees up from the eye — the quad centred
    # on the upper cheek between skull and brow rings — pushed 6 mm PROUD and taken to the
    # crown tone. A goose that is going to charge a lawnmower needs the eye to sit UNDER
    # something; without it the recessed eye reads as a bolt hole. Proud rather than sunk
    # because the only light in this game comes from above, so a raised ridge is the only
    # form that draws its own shadow onto the face.
    for c in (1, 5):
        f = F[K_SKULL][c]
        if f and f.is_valid:
            L.recess(mb, [f], 0.010, 0.006, col=BROW_COL)
    fill_untagged(mb, tag)

    # ---- wings.  Socket first: lift a one-column patch out of the flank and loft the
    #      wing from the vertices that are left.  The wing IS the body's skin.
    wing_bones = {}
    for side in ("L", "R"):
        sx = 1.0 if side == "L" else -1.0
        lo, up = WING_COLS[side]
        ks = WING_RINGS
        loop = [vr[k][lo] for k in ks] + [vr[k][up] for k in reversed(ks)]
        col = min(lo, up)                        # the face column between the two rows
        dead = [F[k][col] for k in ks[:-1]]
        bmesh.ops.delete(mb.bm, geom=[f for f in dead if f and f.is_valid], context='FACES')
        for k in ks[:-1]:
            F[k][col] = None

        loop_co = [v.co.copy() for v in loop]
        root = sum(loop_co, Vector()) / len(loop_co)

        def wring(m):
            x, yc, zc, ch, th, tw = WING[m]
            R = Matrix.Rotation(math.radians(tw), 3, 'X')
            cdir = R @ Vector((0, 1, 0))
            udir = R @ Vector((0, 0, 1))
            b = min(1.0, m / 3.5)
            hu = [FLAT_H[c] + (AIR_HU[c] - FLAT_H[c]) * b for c in range(5)]
            hd = [FLAT_H[c] + (AIR_HD[c] - FLAT_H[c]) * b for c in range(5)]
            ctr = Vector((sx * x, yc, zc))
            low = [ctr + cdir * (CHORD_T[c] * ch) - udir * (hd[c] * th) for c in range(5)]
            hig = [ctr + cdir * (CHORD_T[c] * ch) + udir * (hu[c] * th) for c in range(5)]
            return low + list(reversed(hig))

        perm, dist = align(loop_co, wring(0))
        print("  wing %s socket: root=(%.3f %.3f %.3f) pairing cost %.4f"
              % (side, root.x, root.y, root.z, dist))
        seq = [[wring(m)[p] for p in perm] for m in range(len(WING))]
        rows, newv = extend(mb, loop, seq, BODY_COL)

        # Spanwise and chordwise parameters come from the RING INDICES the vertex was
        # built at, not from measuring its position back out again. perm is a rotation or
        # reflection of the profile order, so position i of a built ring carries profile
        # slot perm[i], and chord slot c counts 0 = trailing edge .. 4 = leading edge.
        xr = abs(root.x)
        span = WING[-1][0] - xr
        for m, ring in enumerate(newv):
            s = (WING[m][0] - xr) / span
            for i, v in enumerate(ring):
                p = perm[i]
                c = p if p < 5 else 9 - p
                tag[v] = {"k": None, "home": "Chest",
                          "limbs": [("wing", side, max(0.0, min(1.0, s)),
                                     (4 - c) / 4.0, 1.0)]}
        # Value darkens monotonically outboard — body tone, secondaries, primaries. Three
        # feather groups, and the darkest is at the tip, which is the part that travels
        # furthest during a beat and therefore does the most to draw the motion on the sky.
        for m, row in enumerate(rows):
            c = BODY_COL if m <= 3 else (SECOND if m <= 5 else PRIMARY)
            for f in row:
                if f and f.is_valid:
                    f[mb.lay] = mb.cid(c)
        # The socket ring is shared skin, so it carries BOTH a body ring index and a wing
        # influence: it is the vertex ring that makes the shoulder a shoulder instead of a
        # crease, and it has to be dragged part of the way by the flap.
        #
        # The two body columns either side of the socket get a weaker share of the same
        # influence. Without them the flank steps from 30% wing to 0% across a single
        # quad, and a 40-degree beat puts a hard fold line down the bird's side — the
        # visible symptom of which is indistinguishable from the plank it replaced.
        for q, v in enumerate(loop):
            c = q if q < 5 else 9 - q
            tag[v]["limbs"].append(("wing", side, 0.0, (4 - c) / 4.0, 1.0))
        for row in ((min(lo, up) - 1) % NSIDE, (max(lo, up) + 1) % NSIDE):
            for c, k in enumerate(ks):
                tag[vr[k][row]]["limbs"].append(
                    ("wing", side, 0.0, (4 - c) / 4.0, 0.35))
        def wpt(m):
            return Vector((sx * WING[m][0], WING[m][1], WING[m][2]))
        wing_bones[side] = (root, wpt(WRIST_M), wpt(HAND_M), wpt(len(WING) - 1))

    # ---- legs.  Same operation, one column narrower, on the lower flank.
    leg_bones = {}
    for side in ("L", "R"):
        sx = 1.0 if side == "L" else -1.0
        lo, up = LEG_COLS[side]
        ks = LEG_RINGS
        loop = [vr[k][lo] for k in ks] + [vr[k][up] for k in reversed(ks)]
        col = min(lo, up)
        dead = [F[k][col] for k in ks[:-1]]
        bmesh.ops.delete(mb.bm, geom=[f for f in dead if f and f.is_valid], context='FACES')
        for k in ks[:-1]:
            F[k][col] = None

        loop_co = [v.co.copy() for v in loop]
        root = sum(loop_co, Vector()) / len(loop_co)

        path = [Vector((sx * p[0], p[1], p[2])) for p in LEG]
        full = [root] + path
        arc = [0.0]
        for a, b in zip(full, full[1:]):
            arc.append(arc[-1] + (b - a).length)
        total = arc[-1]

        def lring(m):
            p = full[m + 1]
            a = full[max(0, m)]
            b = full[min(len(full) - 1, m + 2)]
            t = (b - a)
            t = t.normalized() if t.length > 1e-9 else Vector((0, 1, 0))
            lat = Vector((sx, 0, 0))
            lat = (lat - t * t.dot(lat))
            lat = lat.normalized() if lat.length > 1e-6 else Vector((sx, 0, 0))
            deep = t.cross(lat).normalized()
            lh, dh = LEG[m][3], LEG[m][4]
            # d index 0 is the REAR of the socket, which is -deep (deep points forward
            # and down out of a trailing leg)
            offs = (-dh, 0.0, dh)
            inner = [p + deep * offs[d] - lat * lh for d in range(3)]
            outer = [p + deep * offs[d] + lat * lh for d in range(3)]
            return inner + list(reversed(outer))

        perm, dist = align(loop_co, lring(0))
        print("  leg  %s socket: root=(%.3f %.3f %.3f) pairing cost %.4f"
              % (side, root.x, root.y, root.z, dist))
        seq = [[lring(m)[p] for p in perm] for m in range(len(LEG))]
        rows, newv = extend(mb, loop, seq, LEG_COL)

        for m, ring in enumerate(newv):
            s = arc[m + 1] / total
            for v in ring:
                tag[v] = {"k": None, "home": "Spine",
                          "limbs": [("leg", side, s, 0.0, 1.0)]}
        for v in loop:
            tag[v]["limbs"].append(("leg", side, 0.0, 0.0, 1.0))
        # Only the UPPER neighbour column. The lower one is the other leg's socket — the
        # two thighs are adjacent under the belly — and lending it a share of this leg
        # would have one leg dragging the other. The crease that leaves is on the exact
        # line between the thighs, which is where a bird has one.
        row = (max(lo, up) + 1) % NSIDE if side == "L" else (min(lo, up) - 1) % NSIDE
        for k in ks:
            tag[vr[k][row]]["limbs"].append(("leg", side, 0.0, 0.0, 0.25))
        def lpt(m):
            return Vector((sx * LEG[m][0], LEG[m][1], LEG[m][2]))
        leg_bones[side] = (root, lpt(KNEE_M), lpt(ANKLE_M), lpt(len(LEG) - 1))

    # ---- close the book on normals.  ONE island, closed, so "outward" is decidable and
    #      every hand-wound quad above is settled here rather than argued about.
    mb.bm.normal_update()
    bmesh.ops.recalc_face_normals(mb.bm, faces=list(mb.bm.faces))
    mb.bm.normal_update()
    for f in mb.bm.faces:
        f.smooth = True

    # ---- freeze the tag table against vertex order.  seal() is skipped deliberately:
    #      its remove_doubles would renumber vertices and silently invalidate every
    #      weight below, and this mesh has nothing to weld — every shared vertex is
    #      shared by construction.
    # Which faces are the bill, decided by IDENTITY rather than by a y threshold. The
    # threshold version worked until the neck was lengthened, at which point -0.845 quietly
    # swallowed the whole skull and put the head on the bill's material slot — so the
    # strike flash would have stopped at the neck. bmesh preserves face order into the
    # Mesh, so recording indices here is exact.
    mb.bm.faces.index_update()
    bill_idx = {f.index for f in bill_faces if f and f.is_valid}

    mb.bm.verts.index_update()
    tags = {}
    for v in mb.bm.verts:
        tags[v.index] = tag.get(v, {"k": 6.0, "limbs": []})
    nvert = len(mb.bm.verts)

    ob = mb.finish(NAME + "_Skin", MAT, smooth_angle=38.0, seal=False)
    assert len(ob.data.vertices) == nvert, "vertex order moved: %d vs %d" % (
        len(ob.data.vertices), nvert)

    # ---- the bill gets its own material slot.  This is the whole reason it can keep its
    #      colour through a strike: SetGooseHot swaps slot 0 and slot 1 is untouched.
    ob.data.materials.append(get_mat(MAT_BILL))
    for p in ob.data.polygons:
        if p.index in bill_idx:
            p.material_index = 1
    ys = [p.center.y for p in ob.data.polygons if p.index in bill_idx]
    print("  bill submesh: %d faces on slot 1, y from %.3f to %.3f"
          % (len(bill_idx), min(ys), max(ys)))
    return ob, tags, wing_bones, leg_bones


# ============================================================================ rigging

def add_bone(eb, name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    b.roll = 0.0
    if parent is not None:
        b.parent = parent
        b.use_connect = connect
    return b


def build_rig(wing_bones, leg_bones):
    arm = bpy.data.armatures.new(NAME + "_Rig")
    rig = bpy.data.objects.new(NAME + "_Rig", arm)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones

    root = add_bone(eb, "Root", (0, 0, 0), (0, 0, 0.09))
    # BODY exists for ONE reason: GooseDefence squashes the bird on impact and it needs a
    # single transform to write a non-uniform scale onto.  It sits between Root and
    # everything else, it is vertical so its local Y is world up (and stays Unity's +Y
    # after the axis conversion), its head is exactly the model origin so the squash is
    # about the body centre, and NO CLIP EVER KEYS IT -- a Generic animator only writes
    # transforms it has curves for, so the script's scale survives every state it plays.
    body = add_bone(eb, "Body", (0, 0, 0), (0, 0, 0.16), root)
    spine = add_bone(eb, "Spine", (0, 0.340, 0.055), (0, 0.020, -0.005), body)
    chest = add_bone(eb, "Chest", (0, 0.020, -0.005), (0, -0.420, 0.045), spine, True)
    # Four bones from body to bill.  Three was a linkage; the whip in `Struck` needs enough
    # segments to carry a travelling wave down the neck rather than to fold it twice.
    n1 = add_bone(eb, "Neck1", (0, -0.505, 0.090), (0, -0.640, 0.212), chest)
    n2 = add_bone(eb, "Neck2", (0, -0.640, 0.212), (0, -0.760, 0.270), n1, True)
    n3 = add_bone(eb, "Neck3", (0, -0.760, 0.270), (0, -0.872, 0.290), n2, True)
    hd = add_bone(eb, "Head", (0, -0.872, 0.290), (0, -1.086, 0.253), n3, True)
    add_bone(eb, "Bill_Lower", (0, -0.938, 0.262), (0, -1.086, 0.243), hd)
    add_bone(eb, "Tail", (0, 0.440, 0.095), (0, 0.798, 0.170), spine)

    for side in ("L", "R"):
        rt, wr, hn, tp = wing_bones[side]
        w1 = add_bone(eb, "Wing_%s_1" % side, rt, wr, chest)
        w2 = add_bone(eb, "Wing_%s_2" % side, wr, hn, w1, True)
        add_bone(eb, "Wing_%s_3" % side, hn, tp, w2, True)
        rt, kn, an, to = leg_bones[side]
        l1 = add_bone(eb, "Leg_%s_1" % side, rt, kn, spine)
        l2 = add_bone(eb, "Leg_%s_2" % side, kn, an, l1, True)
        add_bone(eb, "Foot_%s" % side, an, to, l2, True)

    bpy.ops.object.mode_set(mode='OBJECT')
    return rig


def skin(rig, ob, tags):
    """Explicit, measured weights.

    Automatic weights were never on the table. The whole point of the socket construction
    is that the shoulder is a specific, known ring of vertices shared between flank and
    wing; envelope weights would smear the wing bones across the belly and the spine
    bones out to the wingtip, and the flap would go back to looking like a hinge because
    the bend line would no longer be where the wrist is.

    THE WING MEMBRANE. Spanwise blend from Wing_1 to Wing_2 across the wrist, offset
    along the chord so the fold line is SWEPT: the trailing edge starts handing over to
    the outer bone 10% of the span earlier than the leading edge does. A straight blend
    line is a hinge and reads as one however smooth the falloff; a swept one is what a
    real wing folds along, and it is the single change that stops the beat looking like an
    arm. At the root the wing bones fade into Chest over the first fifth of the span, so a
    wingbeat drags the flank skin with it and the socket never shows a crease.
    """
    G = {}

    def g(n):
        if n not in G:
            G[n] = ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)
        return G[n]

    def chain(k):
        """Body chain weights from ring index. Written as differences of cumulative
        step functions, so they partition to exactly 1 for every k by construction."""
        c1 = ss(1.5, 5.5, k)
        c2 = ss(8.0, 13.5, k)
        c3 = ss(14.5, 17.0, k)
        c4 = ss(17.5, 20.5, k)
        c5 = ss(20.5, 23.5, k)
        c6 = ss(23.2, 25.8, k)
        return {"Tail": 1 - c1, "Spine": c1 - c2, "Chest": c2 - c3,
                "Neck1": c3 - c4, "Neck2": c4 - c5, "Neck3": c5 - c6, "Head": c6}

    def limb_w(kind, side, s, ct):
        """(bone -> weight) for one limb influence at spanwise position s.

        THE FOLD LINE IS SWEPT, and only for the outer bone. s_eff shifts the handover
        point by 14% of the span between the leading edge (ct=0) and the trailing edge
        (ct=1), so the trailing edge starts following the wrist earlier: the wing bends
        along a diagonal, the way a folding wing does, rather than along a straight
        spanwise line, which is a hinge no matter how soft the falloff is.

        The ROOT fade deliberately uses plain s, not s_eff. Sweeping the root fade as well
        put 90% wing weight on the trailing edge of the shoulder and 26% on the leading
        edge, which shears the socket instead of flexing it.
        """
        if kind == "wing":
            # Three segments now: humerus, forearm, hand.  The hand is what carries the
            # primaries, and giving it its own bone is what lets `Land` and `Struck` flare
            # the tips independently of the arm -- a wing that flails with a rigid tip
            # reads as a signboard being waved.  Both handovers are swept by the chord.
            w3 = ss(0.66, 0.96, s + 0.12 * ct)
            w2 = (1.0 - w3) * ss(0.30, 0.74, s + 0.14 * ct)
            w1 = (1.0 - w3 - w2) * ss(-0.14, 0.24, s)
            return {"Wing_%s_1" % side: w1, "Wing_%s_2" % side: w2,
                    "Wing_%s_3" % side: w3}
        # thigh / shank / webbed foot.  The hock has to be a real joint: the ground clips
        # all turn on the legs coming forward under the bird, and a straight two-bone leg
        # rotating out of the flight tuck swings the whole paddle like a pendulum.
        w3 = ss(0.56, 0.73, s)          # handover centred on the ankle (s = 0.64)
        w2 = (1.0 - w3) * ss(0.27, 0.49, s)     # ...and on the hock (s = 0.38)
        w1 = (1.0 - w3 - w2) * ss(-0.14, 0.22, s)
        return {"Leg_%s_1" % side: w1, "Leg_%s_2" % side: w2, "Foot_%s" % side: w3}

    stats = {}
    for vi, t in tags.items():
        w = {}
        used = 0.0
        for kind, side, s, ct, mult in t["limbs"]:
            for n, x in limb_w(kind, side, s, ct).items():
                w[n] = w.get(n, 0.0) + x * mult
                used += x * mult
        rest = max(0.0, 1.0 - used)
        if t["k"] is not None:
            for n, x in chain(t["k"]).items():
                w[n] = w.get(n, 0.0) + x * rest
        elif rest > 1e-4:
            w[t["home"]] = w.get(t["home"], 0.0) + rest

        # the lower mandible takes its share OUT of Head, never on top of it, so the
        # partition stays exactly 1 and a closed bill is bit-identical to the bind pose
        bl = t.get("bl", 0.0)
        if bl > 1e-4 and w.get("Head", 0.0) > 1e-6:
            take = w["Head"] * bl
            w["Head"] -= take
            w["Bill_Lower"] = w.get("Bill_Lower", 0.0) + take

        tot = sum(w.values())
        if tot < 1e-6:
            w = {"Spine": 1.0}
            tot = 1.0
        for n, x in w.items():
            if x / tot > 1e-4:
                g(n).add([vi], x / tot, 'REPLACE')
                stats[n] = stats.get(n, 0) + 1

    ob.parent = rig
    ob.matrix_parent_inverse = rig.matrix_world.inverted()
    m = ob.modifiers.new("Armature", 'ARMATURE')
    m.object = rig
    print("  weights: " + ", ".join("%s=%d" % (k, stats[k]) for k in sorted(stats)))
    return stats


# ========================================================================= animation
#
# Rotations are stated as WORLD AXES and converted into each bone's own space here, once.
# rig_judges paid five iterations for guessing at bone-local axes and left a warning; the
# warning is taken. probe() below then drives every bone and prints where its tip
# actually went, so the sign of every convention in this file is a measurement in the
# build log rather than an assertion in a comment.

SIDE = {"L": 1.0, "R": -1.0}


def waxis(rig, bone, world):
    rest = rig.data.bones[bone].matrix_local.to_3x3()
    return (rest.inverted() @ Vector(world)).normalized()


def rkey(rig, bone, frame, *pairs):
    pb = rig.pose.bones[bone]
    pb.rotation_mode = 'QUATERNION'
    q = Quaternion((1, 0, 0, 0))
    for ax, deg in pairs:
        if abs(deg) > 1e-9:
            q = q @ Quaternion(waxis(rig, bone, ax), math.radians(deg))
    pb.rotation_quaternion = q
    pb.keyframe_insert("rotation_quaternion", frame=frame)


def lkey(rig, bone, frame, world_vec):
    pb = rig.pose.bones[bone]
    rest = rig.data.bones[bone].matrix_local.to_3x3()
    pb.location = rest.inverted() @ Vector(world_vec)
    pb.keyframe_insert("location", frame=frame)


# named world axes, so a clip reads as intent
UP_L, UP_R = (0, -1, 0), (0, 1, 0)          # wing flap, positive = up
BK_L, BK_R = (0, 0, 1), (0, 0, -1)          # wing sweep, positive = back
TWIST = (1, 0, 0)                           # wing washout, positive = leading edge down
NOSE_UP = (-1, 0, 0)
YAW_L = (0, 0, 1)
ROLL_L = (0, -1, 0)
TUCK = (1, 0, 0)                            # leg, positive = up and back
# (-1,0,0) was the first guess here and the probe measured the tail tip going 126 mm DOWN
# for a key that says "up". That is the entire reason probe() exists.
TAIL_UP = (1, 0, 0)


SPLAY_L, SPLAY_R = (0, -1, 0), (0, 1, 0)    # leg abduction, positive = knee outboard


def wingpose(rig, side, frame, a1, a2, a3):
    """Three (up, sweep, twist) triples, one per wing segment, in world axes."""
    u = UP_L if side == "L" else UP_R
    b = BK_L if side == "L" else BK_R
    for tmpl, (up, sw, tw) in zip(("Wing_%s_1", "Wing_%s_2", "Wing_%s_3"), (a1, a2, a3)):
        rkey(rig, tmpl % side, frame, (u, up), (b, sw), (TWIST, tw))


def flap(rig, side, frame, up, sweep=0.0, twist=0.0, up2=0.0, sweep2=0.0, twist2=0.0,
         up3=None, sweep3=None, twist3=None):
    """The two-segment call the first pass was written in, with the hand following.

    Defaulting segment 3 off segment 2 rather than requiring it keeps every line of the
    original Fly and Glide readable, and a hand that continues its forearm's arc at a bit
    over half amplitude is what a bird's primaries actually do under load.
    """
    if up3 is None:
        up3 = up2 * 0.55
    if sweep3 is None:
        sweep3 = sweep2 * 0.60
    if twist3 is None:
        twist3 = twist2 * 0.85
    wingpose(rig, side, frame, (up, sweep, twist), (up2, sweep2, twist2),
             (up3, sweep3, twist3))


def leg(rig, side, frame, thigh, shank=0.0, foot=0.0, splay=0.0):
    sp = SPLAY_L if side == "L" else SPLAY_R
    rkey(rig, "Leg_%s_1" % side, frame, (TUCK, thigh), (sp, splay))
    rkey(rig, "Leg_%s_2" % side, frame, (TUCK, shank))
    rkey(rig, "Foot_%s" % side, frame, (TUCK, foot))


def legs(rig, frame, tuck, foot=0.0, shank=0.0, splay=0.0):
    for side in ("L", "R"):
        leg(rig, side, frame, tuck, shank, foot, splay)


# The flight tuck, in one place because five clips start or end in it.  Bound standing,
# the whole leg folds up and back and the ankle straightens out of its standing 90 degrees
# so the web trails in line with the shank instead of sticking out like a flap.
TUCKED = dict(tuck=54, shank=48, foot=-92)


NECK_W = (1.00, 0.88, 0.74, 0.52)


def neck(rig, frame, up, yaw=0.0, roll=0.0, head=None, w=NECK_W):
    """Spread one bend over the neck chain, tapering outward.  `up` is the TOTAL bend.

    Normalised deliberately.  The first version handed the same angle to every segment
    scaled by its share, so a stated 20 degrees became 52 across a three-bone chain — and
    the symptom was not "the neck bends a bit much", it was CHARGE putting the bird's bill
    220 mm underground while every number in the file looked reasonable.  A parameter whose
    value does not mean what it says will be mis-set by whoever touches it next, including
    the author, ten minutes later.

    `head` is the skull's OWN key on top of the chain, because the head almost always
    counter-rotates: a goose keeps its skull level while its neck does anything at all, and
    a head that simply follows the neck's tangent reads as a snake.
    """
    s = sum(w[:3])
    for name, k in zip(("Neck1", "Neck2", "Neck3"), w):
        rkey(rig, name, frame, (NOSE_UP, up * k / s), (YAW_L, yaw * k / s),
             (ROLL_L, roll * k / s))
    rkey(rig, "Head", frame, (NOSE_UP, head or 0.0), (YAW_L, yaw * 0.30),
         (ROLL_L, roll * 0.30))


def neck_whip(rig, seq, lag=(0, 1, 2, 3), amp=(0.62, 0.53, 0.44, 0.40), last=None):
    """A TRAVELLING wave down the neck: the same curve, delayed one frame per segment.

    This is the whole reason the neck is four bones.  Keying every segment on the same
    frame produces a neck that bends as a unit — a boom swinging — however large the
    angles are.  One frame of lag per joint at 24 fps is 42 ms, which is enough that the
    head is still going back while the throat has already started forward, and that is
    what a whip looks like.
    """
    for name, lg, a in zip(("Neck1", "Neck2", "Neck3", "Head"), lag, amp):
        for (f, up, yaw) in seq:
            ff = f + lg if last is None else min(f + lg, last)
            rkey(rig, name, ff, (NOSE_UP, up * a), (YAW_L, yaw * a))


def gape(rig, frame, deg):
    """Open the bill. Positive = open; the lower mandible drops."""
    rkey(rig, "Bill_Lower", frame, (NOSE_UP, -deg))


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    act.use_fake_user = True
    return act


def complete(rig, act, frame=1):
    """Give every bone this action does not mention an explicit rest key at frame 1.

    Without it a clip is only a DIFFERENCE from whatever ran before it: `Fly` never
    mentions the bill, so a goose that transitions out of `Charge` flies away still
    hissing, and `Glide` never mentions the legs' splay so it inherits the sprawl from
    `Recover`.  Eight clips on two animator layers is far past the point where that can be
    reasoned about, so every clip is made to state the whole pose instead.

    Body is the deliberate exception and must stay one: it carries the runtime squash and
    nothing in an action may ever touch it.
    """
    rot, loc = set(), set()
    for fc in act.fcurves:
        if '"' not in fc.data_path:
            continue
        nm = fc.data_path.split('"')[1]
        (loc if fc.data_path.endswith(".location") else rot).add(nm)
    added = 0
    for pb in rig.pose.bones:
        if pb.name == "Body":
            continue
        if pb.name not in rot:
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = Quaternion((1, 0, 0, 0))
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            added += 1
        if pb.name not in loc:
            pb.location = (0, 0, 0)
            pb.keyframe_insert("location", frame=frame)
    return act


def clip_fly(rig):
    """The wingbeat. 12 frames at 24 fps = 2 Hz, which is the rate AnimateLimbs was
    running the greybox at while diving (13 rad/s).

    Three things make it a wingbeat rather than a hinge, and all three are things the box
    wings could not do:
      - the downstroke takes four frames and the recovery takes seven, so there is an
        accent instead of a sine;
      - the OUTER segment leads at the top and lags at the bottom, so the tip traces an
        arc and the membrane visibly bends across the wrist;
      - the hand twists nose-down through the downstroke and nose-up through the
        recovery, which is what a bird does to get thrust and what turns the shape from a
        flat plate into something with a leading edge.
    """
    act = new_action(rig, "Fly")
    beat = [
        # f,  up,  sweep, twist, up2, sweep2, twist2
        (1,  40, -4,  -6,  22,  -8, -14),
        (3,   6,  2,  10,  -6,   4,  16),
        (5, -32,  6,  16, -20,  10,  26),
        (7, -14,  2,   6,  -6,   4,  10),
        (9,  12, -2,  -8,   8,  -6, -14),
        (11, 34, -4,  -6,  18,  -8, -16),
        (12, 40, -4,  -6,  22,  -8, -14),
    ]
    for f, u, sw, tw, u2, sw2, tw2 in beat:
        for side in ("L", "R"):
            flap(rig, side, f, u, sw, tw, u2, sw2, tw2)
    # the body rides the beat: up on the downstroke, and the neck and tail answer late
    for f, lift, pitch in ((1, 0.000, 0.0), (5, 0.026, -2.4), (9, -0.006, 1.6),
                           (12, 0.000, 0.0)):
        lkey(rig, "Spine", f, (0, 0, lift))
        rkey(rig, "Spine", f, (NOSE_UP, pitch * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pitch))
    for f, a in ((1, 0), (6, 3.0), (10, -2.0), (12, 0)):
        neck(rig, f, a * 1.8, head=-a * 0.5)
    for f, a in ((1, 0), (5, -5.0), (9, 3.5), (12, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    # LEGS TUCKED. A flying goose trails its feet under the tail; leaving them in the
    # bound pose would hang two paddles below a bird whose flight path bottoms out at
    # y = 0.3, and they would clip the lawn on every low pass.
    legs(rig, 1, **TUCKED)
    legs(rig, 12, **TUCKED)
    return complete(rig, act)


def clip_glide(rig):
    """Wings held, nothing much happening — 60 frames of it. Its job is to be the state
    the bird is in when it is NOT beating, so the Fly clip has something to be different
    from: the greybox's flap was always on, which made it read as texture rather than as
    effort. A held wing with a two-degree tip flex is what makes the next downstroke
    land."""
    act = new_action(rig, "Glide")
    for f, u, u2, tw2 in ((1, 9, 4, -4), (20, 12, 7, -7), (40, 7, 2, -2), (60, 9, 4, -4)):
        for side in ("L", "R"):
            flap(rig, side, f, u, -2, -3, u2, -4, tw2)
    for f, lift in ((1, 0.0), (24, 0.009), (48, -0.004), (60, 0.0)):
        lkey(rig, "Spine", f, (0, 0, lift))
    for f, a, y in ((1, 0, 0), (18, 1.6, 2.5), (38, -1.2, -2.0), (60, 0, 0)):
        neck(rig, f, a * 1.8, yaw=y * 1.8, head=-a * 0.4)
    for f, a in ((1, 0), (30, 1.5), (60, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    legs(rig, 1, **TUCKED)
    legs(rig, 60, **TUCKED)
    return complete(rig, act)


def clip_struck(rig):
    """The hit. One shot, 24 frames.

    Written against what the strike already does elsewhere in the phase rather than
    invented: AnimateLimbs folded the wings back 62 degrees and lashed the neck 54 past
    its rest angle on the same envelope as the body squash, because a hit has to read as
    having gone THROUGH the animal. So the wings collapse rearward in two frames and stay
    collapsed, and the neck snaps back, whips forward past neutral, and rings down. The
    body rolls, because a bird that has been hit is no longer flying level.

    The legs kick out of their tuck on the impact frame and drift back — the one part of
    the animal that says "this was involuntary".

    WHY THE WINGS GO UP AND BACK AND NOT ALL THE WAY BACK. The first pass swept the inner
    bone 62 degrees rearward with another 52 on the hand, on the reasoning that a struck
    bird folds. Rendered, that put the wingtip behind the tail and folded the hand back
    across the forearm: the wing self-intersected and the frames read as crumpled paper.
    A wing that is 640 mm of one continuous skin cannot be folded like a real one without
    the wrist tucking sideways, which this two-bone rig deliberately cannot do. Up and
    back reads as a bird knocked out of the air, holds a clean silhouette, and — the point
    of the pose — is STILL, which is what makes the absence of the beat readable.
    """
    act = new_action(rig, "Struck")
    wing = [
        (1,  22,  -6,  -8,  10, -10, -16),
        (2,  34, -18, -20,  20, -24, -28),   # the check: wings still trying
        (4,  40,  28,  16,  22,  22,  24),   # flung up and back, and held
        (9,  34,  28,  12,  18,  22,  20),   # sweep HOLDS from here: letting it wander
        (16, 28,  28,  10,  14,  22,  18),   # made the settle read as a second event
        (24, 24,  28,   8,  12,  22,  16),
    ]
    # Peak dihedral is 40 + 22 and not 52 + 30. The taller version put the two wings
    # nearly vertical and converging over the back, and from three quarters the pair read
    # as one shell rather than as two wings. The struck bird is also the thing the player
    # is tracking across the garden for up to four seconds, so its silhouette has to stay
    # WIDE — a folded bird is a dot.
    # The two wings do NOT do the same thing. A symmetrical collapse reads as a machine
    # switching state; a bird that has been hit on one side goes over. The right wing runs
    # a frame late and a fifth shallower, which with the neck's yaw is enough to make the
    # whole event look like it happened TO the animal.
    for f, u, sw, tw, u2, sw2, tw2 in wing:
        # the hand throws OPEN — primaries splayed, well past the forearm's own arc. The
        # first version continued the forearm at half amplitude and the wing read as one
        # rigid board flung upward; the tips have to overshoot for the wing to read as
        # something that has been hit rather than something that has been raised.
        flap(rig, "L", f, u, sw, tw, u2, sw2, tw2,
             up3=u2 * 0.9 + 14, sweep3=sw2 * 0.5 - 16, twist3=tw2 * 1.3)
        flap(rig, "R", min(f + 1, 24), u * 0.80, sw * 1.15, tw, u2 * 0.80, sw2 * 1.15, tw2,
             up3=u2 * 0.72 + 10, sweep3=sw2 * 0.58 - 12, twist3=tw2 * 1.15)
    # THE NECK LASHES, one frame of delay per joint. Struck is the clip the whole four-bone
    # neck exists for: the head is still travelling backwards on frame 9 while the throat
    # has already turned forward, so the neck draws an S and the skull arrives late and
    # hard. Amplitudes are up because the segments no longer fire together.
    neck_whip(rig, [(1, 0, 0), (3, 44, -18), (8, -52, 22), (13, 25, -13),
                    (18, -12, 6), (24, -3, 2)], last=24)
    for f, pit, rol, lift in ((1, 0, 0, 0.0), (3, -11, 6, -0.026), (8, 8, -17, 0.012),
                              (15, -5, 11, -0.006), (24, -1, 3, 0.0)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.6), (ROLL_L, rol * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pit), (ROLL_L, rol))
        lkey(rig, "Spine", f, (0, 0, lift))
    for f, a in ((1, 0), (3, 16), (10, -10), (24, 2)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    # the bill flies open on the check and never quite shuts again inside the clip
    for f, d in ((1, 2), (3, 30), (10, 22), (24, 12)):
        gape(rig, f, d)
    legs(rig, 1, **TUCKED)
    leg(rig, "L", 4, -18, -26, 30, splay=22)
    leg(rig, "R", 5, -8, -20, 26, splay=16)
    legs(rig, 13, 14, 4, 16, splay=10)
    legs(rig, 24, 40, -60, 34, splay=4)
    return complete(rig, act)


# ------------------------------------------------------------------ the ground clips
#
# Everything below is new for the rally mode, where the goose is no longer only a thing
# that flies past and gets hit. It lands badly, runs at the garden, braces, is struck,
# picks itself up and is eventually knocked out — and all six of those have to read at
# thirty pixels from a wide arcade camera, which means each one owns a distinct
# SILHOUETTE, not just a distinct set of angles.
#
#   Land     legs late, body pitching down, wings forward   — a shape falling
#   Charge   long low horizontal line, bill open            — an arrow
#   Brace    short, compressed, everything pulled in        — a fist
#   Struck   wide, high, splayed                            — an explosion
#   Recover  low then rising, asymmetric                    — a shape getting up
#   KO       flat, limp, on its side                        — a shape that has stopped

STAND_WING = ((-6, 30, -4), (-4, 26, -6), (2, 14, -4))
"""Wings folded on the body. Not folded far: this wing is 640 mm of ONE continuous skin
and it cannot cross itself, so past about 35 degrees of sweep the hand drives through the
flank. Thirty degrees plus a little droop reads as 'stowed' at the distance that matters
and never self-intersects, which is the trade the mesh forces and it is the right one."""


def stand(rig, f, lean=0.0, roll=0.0, yaw=0.0, lift=0.0):
    """The ground stance three clips resolve into: legs under the bird, wings stowed."""
    rkey(rig, "Spine", f, (NOSE_UP, lean * 0.35), (ROLL_L, roll * 0.4), (YAW_L, yaw * 0.4))
    rkey(rig, "Chest", f, (NOSE_UP, lean), (ROLL_L, roll), (YAW_L, yaw * 0.6))
    lkey(rig, "Spine", f, (0, 0, lift))
    for side in ("L", "R"):
        wingpose(rig, side, f, *STAND_WING)


def clip_land(rig):
    """A BAD landing, on purpose — 24 frames.

    A goose landing well is a beautiful and completely unreadable thing at this camera
    distance. The clip has to say 'that arrived badly' in under a second, so the timing is
    built around one specific mistake: the legs come down LATE. The bird is still in the
    flight tuck on frame 5, only starts reaching on frame 8, and the feet get there two
    frames after the body needed them — so the touchdown is a stumble that has to be caught
    with the wings rather than a settle.

    The wings do the opposite of what they do in Fly: they throw FORWARD past the head on
    the catch, which is the pose that reads as 'not in control' from any angle.
    """
    act = new_action(rig, "Land")
    W = [
        # f,  seg1 (up, sweep, twist),  seg2,               seg3
        (1,  (18, -6, 6),   (10, -8, 10),   (6, -6, 12)),
        (4,  (44, 14, 22),  (28, 16, 26),   (20, 8, 30)),    # the flare: braking
        (8,  (40, -4, 14),  (26, -18, 16),  (24, -22, 20)),
        (11, (-6, -34, -8), (-14, -30, -12), (-18, -26, -16)),  # thrown forward, contact
        (14, (46, 6, 18),   (30, -4, 22),   (26, -10, 26)),  # left wing high...
        (17, (14, 20, -4),  (6, 18, -2),    (2, 12, 0)),
        (20, (6, 26, -4),   (0, 24, -5),    (4, 16, -4)),
        (24, STAND_WING[0], STAND_WING[1], STAND_WING[2]),
    ]
    for f, a1, a2, a3 in W:
        wingpose(rig, "L", f, a1, a2, a3)
        # the right wing runs two frames late and shallower through the catch, which is
        # what turns a symmetrical flap into a bird fighting to stay upright
        m = 0.72 if 10 <= f <= 18 else 0.95
        wingpose(rig, "R", min(f + (2 if 10 <= f <= 18 else 0), 24),
                 tuple(v * m for v in a1), tuple(v * m for v in a2),
                 tuple(v * m for v in a3))

    for f, pit, rol, lift in ((1, 10, 0, 0.02), (4, 22, -3, 0.03), (8, 14, 2, 0.01),
                              (11, -14, -6, -0.05), (13, -18, 4, -0.03),
                              (16, 8, -5, 0.01), (20, -3, 2, -0.004), (24, 2, 0, 0.0)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.45), (ROLL_L, rol * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pit), (ROLL_L, rol))
        lkey(rig, "Spine", f, (0, 0, lift))

    for f, up, yaw, hd in ((1, 12, 0, -4), (4, 34, 0, -14), (8, 20, -9, -6),
                           (11, -30, 14, 34), (13, -34, -10, 44), (16, 38, 9, -16),
                           (20, 8, -5, -2), (24, 0, 0, 0)):
        neck(rig, f, up, yaw=yaw, head=hd)
    for f, a in ((1, -6), (4, -22), (11, 14), (14, -8), (24, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    for f, d in ((1, 4), (4, 20), (11, 26), (17, 10), (24, 2)):
        gape(rig, f, d)

    legs(rig, 1, **TUCKED)
    legs(rig, 5, tuck=48, shank=42, foot=-84)          # STILL TUCKED. this is the mistake
    legs(rig, 8, tuck=16, shank=10, foot=-42, splay=10)
    legs(rig, 10, tuck=-30, shank=-18, foot=-6, splay=16)   # reaching, too late
    leg(rig, "L", 11, -14, 20, 8, splay=12)                 # contact, compressing
    leg(rig, "R", 12, -4, 26, 12, splay=10)
    leg(rig, "L", 14, -30, -6, 4, splay=8)                  # the stumble step
    leg(rig, "R", 14, 18, 14, 6, splay=6)
    leg(rig, "L", 18, -6, 6, 0, splay=5)
    leg(rig, "R", 17, -12, 10, 2, splay=5)
    legs(rig, 24, tuck=-2, shank=2, foot=0, splay=4)
    return complete(rig, act)


def clip_charge(rig):
    """The ground run — 15 frames, two strides, loops on frame 15 back to frame 1.

    The pose is the point. Head LOW and pushed forward on an extended neck, body tipped
    onto the shoulders, bill open, wings half up and cocked back. From the side that is
    one long horizontal line with an open beak at the end of it, which is the only goose
    silhouette that reads as INTENT rather than as locomotion.

    The waddle is a roll, not a sway: nine degrees of roll onto the loaded leg with the
    body dropping 15 mm as the weight lands. A goose runs badly and looks furious doing it,
    and the roll is the whole of that.
    """
    act = new_action(rig, "Charge")
    LAST = 15

    # wings half raised and cocked, with a small beat on the stride so they never freeze
    for f, u, u2, u3 in ((1, 30, 16, 10), (4, 22, 10, 4), (8, 30, 16, 10),
                         (11, 22, 10, 4), (15, 30, 16, 10)):
        for side in ("L", "R"):
            wingpose(rig, side, f, (u, 12, -8), (u2, 20, -10), (u3, 14, -6))

    for f, roll, lift, yaw in ((1, 9, 0.0, -4), (3, 4, 0.022, -2), (5, -2, -0.012, 1),
                               (8, -9, 0.0, 4), (10, -4, 0.022, 2),
                               (12, 2, -0.012, -1), (15, 9, 0.0, -4)):
        rkey(rig, "Spine", f, (NOSE_UP, -5), (ROLL_L, roll * 0.45), (YAW_L, yaw * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, -14), (ROLL_L, roll), (YAW_L, yaw))
        lkey(rig, "Spine", f, (0, 0, lift))

    # neck out and down against the pitched chest, skull levelled so the bill aims forward
    for f, up, yaw in ((1, -10, -7), (4, -15, 0), (8, -10, 7), (11, -15, 0), (15, -10, -7)):
        neck(rig, f, up, yaw=yaw, head=40)
    for f, a in ((1, -10), (4, -4), (8, -10), (11, -4), (15, -10)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    for f, d in ((1, 16), (4, 22), (8, 16), (11, 22), (15, 16)):
        gape(rig, f, d)

    # the stride: thigh forward is negative, and the hock closes as the foot picks up
    # thigh, shank, foot.  The shank closes as the thigh swings forward: a stride that
    # only swings the thigh makes the leg longest exactly when it reaches furthest, and the
    # measured plant then lifts the whole bird 88 mm twice a cycle -- a hop, not a run.
    STRIDE = [(0, -26, 20, -10), (3, -8, 8, 0), (5, 8, 12, 2),
              (7, 22, 32, -20), (9, 4, 34, -26), (11, -18, 26, -16), (14, -26, 20, -10)]
    for side, off in (("L", 0), ("R", 7)):
        for df, th, sh, ft in STRIDE:
            f = 1 + ((df + off) % 14)
            leg(rig, side, f, th, sh, ft, splay=7)
    # both ends of the loop must be identical or the run ticks once per cycle
    leg(rig, "L", LAST, -26, 20, -10, splay=7)
    leg(rig, "R", LAST, 22, 32, -20, splay=7)
    leg(rig, "L", 1, -26, 20, -10, splay=7)
    leg(rig, "R", 1, 22, 32, -20, splay=7)
    return complete(rig, act)


def clip_brace(rig):
    """The half second before contact — 9 frames.

    Anticipation, and nothing else. The neck RETRACTS: a goose about to take a hit pulls
    its head back into its shoulders in an S, and that shortening is the most legible thing
    the animal can do because it changes the silhouette's LENGTH, which survives any
    camera angle and any distance. Wings clamp, legs compress, the body drops 40 mm.

    It ends held rather than resolved, because whatever plays next is the hit.
    """
    act = new_action(rig, "Brace")
    for f, w in ((1, ((26, 10, -8), (14, 18, -10), (8, 12, -6))),
                 (3, ((34, 2, -12), (22, 8, -14), (16, 4, -10))),
                 (6, ((-8, 32, 4), (-10, 28, 6), (-6, 18, 4))),
                 (9, ((-12, 34, 6), (-12, 30, 8), (-8, 20, 5)))):
        for side in ("L", "R"):
            wingpose(rig, side, f, *w)
    for f, pit, lift in ((1, -16, 0.0), (3, -15, 0.012), (6, -5, -0.042), (9, -3, -0.048)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.4))
        rkey(rig, "Chest", f, (NOSE_UP, pit))
        lkey(rig, "Spine", f, (0, 0, lift))
    # the S: throat comes UP and back while the upper neck folds DOWN over it, which is
    # what shortens the animal. A uniform bend just points the head somewhere else.
    for f, n1, n2, n3, hd in ((1, -4, -4, -3, 30), (3, -6, -5, -4, 34),
                              (6, 30, -30, -26, 26), (9, 34, -33, -28, 22)):
        rkey(rig, "Neck1", f, (NOSE_UP, n1))
        rkey(rig, "Neck2", f, (NOSE_UP, n2))
        rkey(rig, "Neck3", f, (NOSE_UP, n3))
        rkey(rig, "Head", f, (NOSE_UP, hd))
    for f, a in ((1, -10), (3, -14), (6, 12), (9, 14)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    for f, d in ((1, 18), (3, 24), (6, 2), (9, 0)):        # the beak snaps shut
        gape(rig, f, d)
    for f, th, sh, ft, sp in ((1, -20, 4, 0, 7), (3, -24, -2, -2, 7),
                              (6, -6, 30, 10, 14), (9, -4, 34, 12, 15)):
        legs(rig, f, tuck=th, shank=sh, foot=ft, splay=sp)
    return complete(rig, act)


def clip_recover(rig):
    """Getting up off the grass — 22 frames.

    Three beats and they have to stay separate or the whole thing reads as one blur:
    HEAD FIRST (frames 1-5, only the skull moves — the body is still dead), PUSH
    (frames 6-12, legs gather and the wings scrabble at the ground), SHAKE (frames 13-18,
    a fast four-beat yaw oscillation through the body and the neck, the thing every bird
    does after an indignity). It resolves into the same stance Charge starts from, so the
    two blend without a pop.
    """
    act = new_action(rig, "Recover")
    # asymmetric all the way through the push: it fell on one side and it gets up off it
    LW = [(1, (-20, 10, -12)), (5, (4, 2, -8)), (8, (30, -26, 10)), (12, (26, 6, -2)),
          (14, (20, 14, -6)), (16, (10, 22, -4)), (18, (14, 18, -6)), (22, STAND_WING[0])]
    # the DOWNHILL wing is pinned under the bird, not splayed: left flung open, right
    # trapped.  Splayed, it was the lowest thing on the animal and the plant stood the
    # whole goose on its own wingtip.
    RW = [(1, (2, 30, -4)), (5, (0, 28, -2)), (8, (22, -18, 6)), (12, (24, 8, -2)),
          (14, (12, 20, -5)), (16, (18, 16, -6)), (18, (8, 22, -4)), (22, STAND_WING[0])]
    for side, tbl in (("L", LW), ("R", RW)):
        for f, a1 in tbl:
            wingpose(rig, side, f, a1,
                     tuple(v * 0.72 for v in a1), tuple(v * 0.5 for v in a1))

    # THE ROLL LIVES ON ROOT, not on the chest.  Rolling the body rotates the neck's base
    # but not the axes its keys are expressed in, so a "nose down" bend on a chest-rolled
    # bird sends the head sideways and the skull stays half a metre off the grass however
    # large the number gets.  Rolled at Root, the whole animal including its axes goes over
    # together and the same droop that works in KO works here.
    for f, rol in ((1, 55), (3, 51), (5, 42), (8, 22), (12, 3), (14, 0), (22, 0)):
        rkey(rig, "Root", f, (ROLL_L, rol))
    for f, pit, rol, yaw, lift in ((1, -16, 0, 10, -0.055), (5, -14, 0, 8, -0.050),
                                   (8, -4, 0, 4, -0.105), (12, 6, 4, -2, -0.020),
                                   (14, 2, -2, 14, 0.004), (15, 1, 3, -15, 0.0),
                                   (16, 1, -2, 10, 0.002), (17, 0, 1, -7, 0.0),
                                   (18, 0, 0, 4, 0.0), (22, -3, 0, 0, 0.0)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.5), (ROLL_L, rol * 0.45), (YAW_L, yaw * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pit), (ROLL_L, rol), (YAW_L, yaw))
        lkey(rig, "Spine", f, (0, 0, lift))

    for f, up, yaw, roll, hd in ((1, -102, -12, -44, 42), (3, -84, -6, -36, 38),
                                 (5, -40, 6, -18, 28), (8, 4, 4, -6, 6),
                                 (12, 16, -4, 0, -12), (14, 8, -18, 6, -6),
                                 (15, 6, 19, -7, -4), (16, 5, -13, 5, -3),
                                 (17, 4, 9, -3, -2), (18, 3, -4, 1, -1),
                                 (22, 0, 0, 0, 0)):
        neck(rig, f, up, yaw=yaw, roll=roll, head=hd)
    for f, a in ((1, -16), (8, 8), (13, -6), (15, 7), (17, -4), (22, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    for f, d in ((1, 14), (5, 20), (10, 8), (14, 16), (18, 4), (22, 2)):
        gape(rig, f, d)

    # sprawled -> gathered -> planted. The left leg is folded under the bird at the start
    # and has to come out, which is the mechanical reason the push is asymmetric.
    # BOTH legs folded at the start.  The first version had the right leg stretched
    # forward and nearly straight, and the measured plant obediently stood the whole bird
    # up on it: a "collapsed" goose propped 270 mm off the grass on one rigid leg.
    leg(rig, "L", 1, 12, 34, -18, splay=8)
    leg(rig, "R", 1, 16, 40, -20, splay=10)
    leg(rig, "L", 5, 6, 34, -14, splay=10)
    leg(rig, "R", 5, 4, 44, -18, splay=14)
    leg(rig, "L", 8, -8, 30, 8, splay=18)
    leg(rig, "R", 8, -16, 22, 10, splay=20)
    legs(rig, 12, tuck=-6, shank=14, foot=4, splay=10)
    legs(rig, 16, tuck=-3, shank=5, foot=1, splay=8)
    legs(rig, 22, tuck=-2, shank=2, foot=0, splay=6)
    return complete(rig, act)


def clip_ko(rig):
    """The elimination — 29 frames, and the only clip that touches Root.

    Root is the whole animal about its own centre, so keying it here is what lets the bird
    SPIN OUT: 206 degrees of yaw and 95 of roll, arriving on its side. Every key steps less
    than 180 degrees, because quaternion channels interpolated past a half turn take the
    short way round and a spin authored in two keys silently plays backwards.

    The spin stops on frame 16 and everything goes slack THREE frames later. That delay is
    the clip: limbs that stop when the body stops read as a switch being thrown, and limbs
    that carry on for an eighth of a second read as a body that is no longer driving them.
    """
    act = new_action(rig, "KO")
    for f, yaw, roll, pit in ((1, 0, 0, 0), (4, -58, 18, -12), (8, -118, 38, 6),
                              (12, -172, 52, -4), (16, -198, 61, 8), (20, -205, 58, 3),
                              (24, -206, 56, 1), (29, -206, 55, 0)):
        rkey(rig, "Root", f, (YAW_L, yaw), (ROLL_L, roll), (NOSE_UP, pit))

    W = [(1, (30, -18, 14)), (3, (48, 16, 26)), (7, (40, 22, 18)), (12, (30, 26, 10)),
         (16, (16, 20, 2)), (19, (-14, 12, -8)), (23, (-24, 8, -12)), (29, (-26, 6, -14))]
    for f, a1 in W:
        wingpose(rig, "L", f, a1, tuple(v * 0.8 for v in a1), tuple(v * 0.62 for v in a1))
        wingpose(rig, "R", min(f + 2, 29), tuple(v * 0.85 for v in a1),
                 tuple(v * 0.66 for v in a1), tuple(v * 0.5 for v in a1))

    neck_whip(rig, [(1, 4, 0), (3, 34, -18), (7, -30, 22), (11, 20, -14),
                    (15, -14, 9), (19, -40, 5)], last=29)
    for f, up, yaw, roll, hd in ((23, -86, -8, -34, 34), (29, -100, -12, -42, 42)):
        neck(rig, f, up, yaw=yaw, roll=roll, head=hd)
    for f, pit, rol, lift in ((1, 0, 0, 0.0), (4, -14, 8, -0.02), (9, 10, -12, 0.01),
                              (14, -6, 6, -0.01), (18, -12, 3, -0.03),
                              (24, -16, 1, -0.05), (29, -17, 0, -0.055)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.6), (ROLL_L, rol * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pit), (ROLL_L, rol))
        lkey(rig, "Spine", f, (0, 0, lift))
    for f, a in ((1, 6), (4, 22), (10, -14), (18, 6), (29, -4)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    for f, d in ((1, 8), (3, 30), (12, 26), (19, 16), (29, 11)):   # never quite shuts
        gape(rig, f, d)

    legs(rig, 1, **TUCKED)
    leg(rig, "L", 3, -34, -30, 26, splay=30)          # the involuntary kick
    leg(rig, "R", 4, -22, -34, 30, splay=26)
    leg(rig, "L", 9, 26, 10, -12, splay=18)
    leg(rig, "R", 10, 14, 18, -8, splay=22)
    legs(rig, 19, tuck=6, shank=26, foot=-14, splay=14)
    legs(rig, 29, tuck=10, shank=32, foot=-18, splay=12)
    return complete(rig, act)


def probe(rig):
    """Drive every animated bone +20 degrees about the axis this file uses for it and
    print where the bone's tip went, in millimetres of world displacement.

    This is the honest test of a rotation convention and it takes one second. Everything
    that has ever gone wrong with these rigs went wrong because a sign was reasoned about
    instead of measured — rig_judges spent three rounds reading a bone's local Y as
    height when it was depth. A table in the build log cannot make that mistake.
    """
    print("  AXIS PROBE (+20 deg, world displacement of the bone tip, mm)")
    cases = [("Wing_L_1", UP_L, "flap up"), ("Wing_R_1", UP_R, "flap up"),
             ("Wing_L_1", BK_L, "sweep back"), ("Wing_R_1", BK_R, "sweep back"),
             ("Wing_L_3", UP_L, "hand up"),
             ("Neck1", NOSE_UP, "nose up"), ("Neck3", NOSE_UP, "nose up"),
             ("Head", NOSE_UP, "nose up"),
             ("Neck1", YAW_L, "yaw left(+x)"), ("Tail", TAIL_UP, "tail up"),
             ("Bill_Lower", NOSE_UP, "gape (want -)"),
             ("Leg_L_1", TUCK, "tuck"), ("Leg_L_2", TUCK, "shank back"),
             ("Foot_L", TUCK, "foot back"), ("Leg_L_1", SPLAY_L, "splay out"),
             ("Leg_R_1", SPLAY_R, "splay out"),
             ("Root", YAW_L, "root yaw left"), ("Root", ROLL_L, "root roll left up"),
             ("Chest", ROLL_L, "roll left up")]
    for bone, ax, label in cases:
        pb = rig.pose.bones[bone]
        pb.rotation_mode = 'QUATERNION'
        base = pb.rotation_quaternion.copy()
        rest = rig.data.bones[bone].tail_local.copy()
        pb.rotation_quaternion = Quaternion(waxis(rig, bone, ax), math.radians(20.0))
        bpy.context.view_layer.update()
        now = pb.tail.copy()
        d = (now - rest) * 1000.0
        print("    %-10s %-14s dx=%+7.1f dy=%+7.1f dz=%+7.1f" % (bone, label, d.x, d.y, d.z))
        pb.rotation_quaternion = base
    bpy.context.view_layer.update()


GROUND_Z = -0.430
"""How far below the model's origin the lawn is, for every clip that touches it.

The origin is the body centre — it has to be, because PlaceGoose aims the bird with a
single LookRotation about it and the strike squashes it about it.  So a ground clip cannot
just be posed and hoped over: a goose lying on its side has its body centre 200 mm off the
grass and a goose standing has it 430 mm off, and a Recover that goes from one to the other
without saying so drives the bird 230 mm into the lawn and then lifts it back out.

Fixing that by eye is how you get a bird that hovers in one clip and sinks in the next.
plant() measures instead."""


def contact_verts(ob, groups):
    """Vertex indices dominated by the named groups — the parts that touch the grass."""
    gi = {g.index for g in ob.vertex_groups if g.name in groups}
    out = []
    for v in ob.data.vertices:
        if sum(g.weight for g in v.groups if g.group in gi) > 0.5:
            out.append(v.index)
    return out


def plant(rig, ob, act, frames, target=GROUND_Z, feet=None):
    """Key Root's height so the bird's CONTACT SURFACE sits exactly on the lawn.

    Measured through the depsgraph — the same deformation the game draws — and every frame
    is measured BEFORE any correction is written, because the offset is a rigid translation
    of the whole animal and applying one would contaminate the next reading.

    `feet` restricts the measurement to the webs, and it is not an optimisation.  Planting
    on the whole mesh plants on whatever hangs lowest, and in `Land` frame 13 that is the
    BILL: the bird throws its head down in the stumble, and a whole-mesh plant answered by
    hoisting the entire animal 365 mm into the air for two frames to keep the beak off the
    grass.  Feet are what stand on things.  Clips where the bird is lying on its side pass
    feet=None on purpose, because there the body IS the contact surface.
    """
    rig.animation_data.action = act
    dg = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene
    fixes = []
    for f in frames:
        sc.frame_set(f)
        dg.update()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        pool = feet if feet else range(len(me.vertices))
        lo = min(me.vertices[i].co.z for i in pool)
        ev.to_mesh_clear()
        fixes.append((f, target - lo))
    for f, dz in fixes:
        lkey(rig, "Root", f, (0, 0, dz))
    print("    plant %-8s %s" % (act.name,
                                 " ".join("%d:%+.3f" % (f, d) for f, d in fixes)))


def airborne(rig, act, frames):
    """Explicit zero-height keys, so a planted clip does not drag its offset back through
    the frames where the bird is meant to be in the air."""
    rig.animation_data.action = act
    for f in frames:
        lkey(rig, "Root", f, (0, 0, 0.0))


def frame_floor(rig, ob, act):
    """Lowest point of the skinned mesh, per frame, in one line."""
    rig.animation_data.action = act
    dg = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene
    f0, f1 = (int(round(x)) for x in act.frame_range)
    out, who = [], []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        dg.update()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        lo = min(me.vertices, key=lambda v: v.co.z)
        out.append(lo.co.z)
        # WHICH PART is on the ground, named. The plant is a measurement and it will
        # happily stand the bird on a wingtip; the only way to notice is to print the
        # bone the contact vertex belongs to.
        gs = ob.data.vertices[lo.index].groups
        who.append(ob.vertex_groups[max(gs, key=lambda g: g.weight).group].name
                   if gs else "?")
        ev.to_mesh_clear()
    print("    %-8s floor %s" % (act.name, " ".join("%.2f" % v for v in out)))
    print("    %-8s on    %s" % ("", " ".join(sorted(set(who)))))
    return out


def bill_trace(rig, ob, act):
    """Height of the bill tip per frame, relative to the lawn at GROUND_Z.

    The one number that decides whether CHARGE reads as a threat or as grazing, and it is
    not visible in any single angle a preview render offers.  A charging goose holds its
    bill at roughly half its standing height: on the grass it is eating, at shoulder height
    it is merely walking.
    """
    tip = min(ob.data.vertices, key=lambda v: v.co.y).index
    rig.animation_data.action = act
    dg = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene
    f0, f1 = (int(round(x)) for x in act.frame_range)
    out = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        dg.update()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        out.append(me.vertices[tip].co.z - GROUND_Z)
        ev.to_mesh_clear()
    print("    %-8s bill  %s" % (act.name, " ".join("%.2f" % v for v in out)))
    return out


def ground_report(rig, ob, clips):
    """Per clip, the lowest and highest point the SKINNED mesh actually reaches.

    The bird's origin is its body centre, so a ground clip is only usable if the engine
    knows how far under that origin the feet end up — and that number cannot be reasoned
    out of the bone angles, because the skin is what touches the grass. Evaluated through
    the depsgraph, which is the same deformation the game will draw.
    """
    dg = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene
    print("  GROUND (skinned mesh extent per clip, metres from the body-centre origin)")
    for act in clips:
        rig.animation_data.action = act
        lo, hi, at = 9e9, -9e9, 0
        f0, f1 = (int(round(x)) for x in act.frame_range)
        for f in range(f0, f1 + 1):
            sc.frame_set(f)
            dg.update()
            me = ob.evaluated_get(dg).to_mesh()
            z = [v.co.z for v in me.vertices]
            if min(z) < lo:
                lo, at = min(z), f
            hi = max(hi, max(z))
            ob.evaluated_get(dg).to_mesh_clear()
        print("    %-8s lowest=%+.3f (frame %2d)   highest=%+.3f   height=%.3f"
              % (act.name, lo, at, hi, hi - lo))
    for act in clips:
        if act.name in ("Land", "Charge", "Brace", "Recover", "KO"):
            frame_floor(rig, ob, act)
            bill_trace(rig, ob, act)
    rig.animation_data.action = None
    sc.frame_set(1)


def author(rig, ob):
    made = [clip_fly(rig), clip_glide(rig), clip_land(rig), clip_charge(rig),
            clip_brace(rig), clip_struck(rig), clip_recover(rig), clip_ko(rig)]
    by = {a.name: a for a in made}

    # ---- put the ground clips on the ground, by measurement
    print("  PLANTING (Root height key, metres)")
    web = contact_verts(ob, ("Foot_L", "Foot_R"))
    print("    contact set: %d web vertices" % len(web))
    airborne(rig, by["Land"], range(1, 10))
    plant(rig, ob, by["Land"], range(10, 25), feet=web)
    plant(rig, ob, by["Charge"], range(1, 16), feet=web)
    plant(rig, ob, by["Brace"], range(1, 10), feet=web)
    # Recover and KO end up ON the bird's side, so the
    # the webs and not the whole mesh: a drooping head or a splayed wingtip hangs lower than
    # the body does, and planting on those hoists the animal off the grass by its own beak.
    # Both get a long interpolation out of the airborne phase rather than a keyed jump — a
    # 350 mm correction in one frame is a bounce, and a bounce at the end of a knockout
    # reads as the animation being broken.
    plant(rig, ob, by["Recover"], range(1, 23))
    airborne(rig, by["KO"], range(1, 10))
    plant(rig, ob, by["KO"], range(13, 30))
    rig.animation_data.action = None
    for a in made:
        tr = rig.animation_data.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, 1, a)
        st.name = a.name
        # Left UNMUTED on purpose: the FBX exporter walks the strips and skips muted
        # ones, and tidying the timeline that way ships a file with no animation in it
        # that still looks healthy.
        tr.mute = False
    return made


# ============================================================================ export

def export(objs, filepath):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=filepath, use_selection=True, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        colors_type='SRGB', path_mode='COPY',
        object_types={'MESH', 'EMPTY', 'ARMATURE'},
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        bake_anim=True, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True, bake_anim_simplify_factor=0.0)
    print("EXPORTED", filepath)


# ------------------------------------------------------------------------- previews

def pose_sheet(rig, ob, shots, name, res=420, d=(-0.62, -1.0, 0.30)):
    sc = bpy.context.scene
    if "PW" not in bpy.data.worlds:
        L._setup_lights()
    cd = bpy.data.cameras.new("ACam")
    cd.lens = 60
    cam = bpy.data.objects.new("ACam", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    sc.render.engine = 'BLENDER_EEVEE'
    sc.eevee.taa_render_samples = 24
    sc.eevee.use_gtao = True
    sc.view_settings.view_transform = 'Standard'
    ctr, rad = L._bbox([ob])
    paths = []
    for act_name, frames in shots:
        act = bpy.data.actions[act_name]
        rig.animation_data.action = act
        for f in frames:
            sc.frame_set(f)
            bpy.context.view_layer.update()
            L._place_cam(cam, ctr, rad * 1.15, d, pad=1.10)
            p = os.path.join(L.PREVIEWS_DIR, "goose_%s_%s_%02d.png" % (name, act_name, f))
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            paths.append(p)
    rig.animation_data.action = None
    sc.frame_set(1)
    bpy.data.objects.remove(cam, do_unlink=True)
    L.contact_sheet(paths, os.path.join(L.PREVIEWS_DIR, "goose_%s_sheet.png" % name),
                    cols=4)
    return paths


def tiny_sheet(rig, ob, shots, res=44):
    """The 30-pixel test, done literally. The bird is seen at 20 to 50 m from a low chase
    camera, which is a few dozen pixels of screen; anything about the silhouette that only
    works at 512 px does not exist in the game."""
    sc = bpy.context.scene
    if "PW" not in bpy.data.worlds:
        L._setup_lights()
    cd = bpy.data.cameras.new("TCam2")
    cd.lens = 60
    cam = bpy.data.objects.new("TCam2", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'FLAT'
    sh.color_type = 'SINGLE'
    sh.single_color = (0, 0, 0)
    sh.background_type = 'VIEWPORT'
    sh.background_color = (1, 1, 1)
    sc.display.render_aa = '8'
    ctr, rad = L._bbox([ob])
    paths = []
    for act_name, frames in shots:
        rig.animation_data.action = bpy.data.actions[act_name]
        for f in frames:
            sc.frame_set(f)
            bpy.context.view_layer.update()
            for vn, d in (("side", (-1, 0.05, 0.10)), ("tq", (-0.7, -1.0, 0.26))):
                L._place_cam(cam, ctr, rad, d, pad=1.10)
                p = os.path.join(L.PREVIEWS_DIR,
                                 "goose_tiny_%s_%02d_%s.png" % (act_name, f, vn))
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                paths.append(p)
    rig.animation_data.action = None
    sc.frame_set(1)
    sc.render.engine = 'BLENDER_EEVEE'
    bpy.data.objects.remove(cam, do_unlink=True)
    L.contact_sheet(paths, os.path.join(L.PREVIEWS_DIR, "goose_tiny_sheet.png"), cols=4)
    return paths


# ============================================================================== main

def main():
    fresh_scene()
    bpy.context.scene.render.fps = FPS

    ob, tags, wb, lb = build()
    bake_ao([ob], floor=0.80, dist=0.22, samples=28, ground=False)
    L.verify_meshes([ob], "Goose")
    report_tris([ob], "Goose")
    print("  verts=%d  tris=%d  islands should be 1, boundary 0"
          % (len(ob.data.vertices), L.tri_count(ob)))

    rig = build_rig(wb, lb)
    bpy.context.view_layer.update()
    skin(rig, ob, tags)
    print("  bones: " + ", ".join(b.name for b in rig.data.bones))

    probe(rig)
    clips = author(rig, ob)
    for c in clips:
        lo, hi = c.frame_range
        print("  clip %-8s frames %d..%d  (%.2f s at %d fps, Unity %d..%d)"
              % (c.name, lo, hi, (hi - lo) / FPS, FPS, lo - 1, hi - 1))
    ground_report(rig, ob, clips)

    export([rig, ob], os.path.join(L.MODELS_DIR, "Goose.fbx"))

    if "--preview" in sys.argv:
        render_previews([ob], "goose", res=560, solo=True,
                        extra_views=[("head", (-0.45, -1.0, 0.22), 0.42)])
        pose_sheet(rig, ob, [("Fly", (1, 5, 9)), ("Land", (4, 11, 14, 24)),
                             ("Charge", (1, 4, 8, 11))], "poses")
        pose_sheet(rig, ob, [("Brace", (1, 6, 9)), ("Struck", (4, 9, 16)),
                             ("Recover", (1, 8, 14, 22)), ("KO", (3, 12, 20, 29))],
                   "poses2")
        tiny_sheet(rig, ob, [("Glide", (20,)), ("Charge", (1, 4)), ("Brace", (9,)),
                             ("Struck", (9,)), ("KO", (29,))])
    print("DONE Goose")


main()
