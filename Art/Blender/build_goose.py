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
CHIN_COL  = "E8DFCB"     # the chinstrap. Small, on the throat, and it points forward.
EYE_COL   = "17130F"
COVERT    = "7A7470"     # inner wing
PRIMARY   = "4E4946"     # outer wing — darkest, so the beat reads as a dark arc on sky
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
    ( 0.760,  0.190, 0.030, 0.011, 0.010, 2.00),   # 0  tail tip
    ( 0.665,  0.170, 0.105, 0.018, 0.015, 3.40),   # 1  tail fan — wide and flat, not a cube
    ( 0.565,  0.142, 0.142, 0.027, 0.023, 3.20),   # 2
    ( 0.475,  0.112, 0.132, 0.050, 0.042, 2.80),   # 3  tail root
    ( 0.392,  0.080, 0.122, 0.084, 0.074, 2.50),   # 4  rump
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
    (-0.512,  0.100, 0.090, 0.094, 0.112, 2.30),   # 16 throat
    (-0.545,  0.140, 0.076, 0.078, 0.086, 2.25),   # 17 neck base
    (-0.580,  0.180, 0.068, 0.070, 0.072, 2.20),   # 18
    (-0.620,  0.212, 0.064, 0.065, 0.066, 2.20),   # 19
    (-0.662,  0.235, 0.062, 0.062, 0.062, 2.20),   # 20 mid neck
    (-0.705,  0.250, 0.062, 0.062, 0.060, 2.20),   # 21
    (-0.745,  0.259, 0.068, 0.070, 0.062, 2.20),   # 22 nape
    (-0.785,  0.262, 0.072, 0.074, 0.064, 2.20),   # 23 skull
    (-0.822,  0.256, 0.062, 0.058, 0.056, 2.40),   # 24 brow
    (-0.852,  0.247, 0.052, 0.036, 0.038, 2.70),   # 25 bill base
    (-0.905,  0.240, 0.046, 0.026, 0.030, 2.90),   # 26 bill
    (-0.955,  0.235, 0.036, 0.019, 0.022, 3.00),   # 27 bill
    (-0.988,  0.232, 0.020, 0.011, 0.013, 3.00),   # 28 bill tip
]
POLE_TAIL = Vector((0.0,  0.792, 0.198))
POLE_BILL = Vector((0.0, -1.010, 0.231))

WING_RINGS = [8, 9, 10, 11, 12]      # which body rings the shoulder patch spans
WING_COLS  = {"L": (1, 2), "R": (6, 5)}   # (lower row, upper row) — mirror pair
LEG_RINGS  = [5, 6, 7]
LEG_COLS   = {"L": (10, 11), "R": (9, 8)}

# ------------------------------------------------------------------------- the wing
# (x, y_centre, z_centre, chord, thickness, washout degrees)
# The leading edge runs nearly straight to the wrist and then sweeps hard back, which is
# what makes a bird wing a bird wing in silhouette rather than a paddle.
WING = [
    (0.285, -0.100, 0.115, 0.312, 0.088, 1.0),
    (0.375, -0.085, 0.126, 0.328, 0.070, 2.0),
    (0.495, -0.045, 0.130, 0.312, 0.054, 3.5),    # 2 = wrist
    (0.595,  0.010, 0.126, 0.272, 0.040, 5.0),
    (0.675,  0.070, 0.117, 0.216, 0.029, 6.5),
    (0.740,  0.125, 0.104, 0.156, 0.021, 8.0),
    (0.782,  0.170, 0.090, 0.092, 0.013, 9.0),
    (0.805,  0.196, 0.080, 0.032, 0.006, 10.0),
]
WRIST_M = 2
# chord stations, as a fraction of chord: +0.5 trailing edge .. -0.5 leading edge
CHORD_T = (0.50, 0.22, -0.04, -0.28, -0.50)
FLAT_H  = (0.50, 0.50, 0.50, 0.50, 0.50)
AIR_HU  = (0.07, 0.34, 0.46, 0.36, 0.10)
AIR_HD  = (0.07, 0.24, 0.30, 0.26, 0.10)

# -------------------------------------------------------------------------- the leg
# (x, y, z, lateral_half, deep_half).  Bound down-and-back at about 40 degrees, so the
# clips can tuck the legs flat for flight AND a future ground pose can drop them.  The
# last three stations are the webbed foot: the section frame turns almost horizontal at
# the ankle, so widening laterally and pinching vertically produces a flat paddle.
LEG = [
    (0.098, 0.268, -0.212, 0.030, 0.058),
    (0.107, 0.312, -0.290, 0.021, 0.038),
    (0.111, 0.360, -0.336, 0.015, 0.026),
    (0.113, 0.402, -0.368, 0.013, 0.022),    # 3 = ankle
    (0.113, 0.442, -0.379, 0.030, 0.014),
    (0.113, 0.492, -0.386, 0.048, 0.010),
    (0.113, 0.536, -0.390, 0.030, 0.008),
]
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
    pole_fan(mb, vr[-1], POLE_BILL, BILL_TOP)
    for v in mb.bm.verts:
        if v not in tag:      # the two apex vertices, nothing deleted yet so this is safe
            tag[v] = {"k": 0.0 if v.co.y > 0 else float(len(BODY) - 1), "limbs": []}

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
    paint(range(19, 24), (2, 3, 4), BACK_COL)          # crown
    paint(range(21, 24), (8, 9, 10), CHIN_COL)         # the chinstrap
    paint([22], (0, 6), EYE_COL)                       # one facet an eye, both sides
    paint(range(25, 28), range(NSIDE), BILL_LOW)
    paint(range(25, 28), (2, 3, 4), BILL_TOP)

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
            b = min(1.0, m / 2.0)
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
        rows, newv = extend(mb, loop, seq, COVERT)

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
        # colour by span: coverts, then the dark primaries that draw the beat on the sky
        for m, row in enumerate(rows):
            c = COVERT if m <= 1 else (BODY_COL if m <= 3 else PRIMARY)
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
        wing_bones[side] = (root, Vector((sx * WING[WRIST_M][0], WING[WRIST_M][1],
                                          WING[WRIST_M][2])),
                            Vector((sx * WING[-1][0], WING[-1][1], WING[-1][2])))

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
        leg_bones[side] = (root, Vector((sx * LEG[ANKLE_M][0], LEG[ANKLE_M][1],
                                         LEG[ANKLE_M][2])),
                           Vector((sx * LEG[-1][0], LEG[-1][1], LEG[-1][2])))

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
    nbill = 0
    for p in ob.data.polygons:
        if p.center.y < -0.845:
            p.material_index = 1
            nbill += 1
    print("  bill submesh: %d faces on slot 1" % nbill)
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
    spine = add_bone(eb, "Spine", (0, 0.340, 0.055), (0, 0.020, -0.005), root)
    chest = add_bone(eb, "Chest", (0, 0.020, -0.005), (0, -0.420, 0.045), spine, True)
    n1 = add_bone(eb, "Neck1", (0, -0.500, 0.092), (0, -0.600, 0.192), chest)
    n2 = add_bone(eb, "Neck2", (0, -0.600, 0.192), (0, -0.712, 0.252), n1, True)
    add_bone(eb, "Head", (0, -0.712, 0.252), (0, -1.000, 0.231), n2, True)
    add_bone(eb, "Tail", (0, 0.440, 0.095), (0, 0.790, 0.196), spine)

    for side in ("L", "R"):
        rt, wr, tp = wing_bones[side]
        w1 = add_bone(eb, "Wing_%s_1" % side, rt, wr, chest)
        add_bone(eb, "Wing_%s_2" % side, wr, tp, w1, True)
        rt, an, to = leg_bones[side]
        l1 = add_bone(eb, "Leg_%s_1" % side, rt, an, spine)
        add_bone(eb, "Leg_%s_2" % side, an, to, l1, True)

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
        c3 = ss(14.5, 17.5, k)
        c4 = ss(17.5, 20.0, k)
        c5 = ss(20.5, 22.5, k)
        return {"Tail": 1 - c1, "Spine": c1 - c2, "Chest": c2 - c3,
                "Neck1": c3 - c4, "Neck2": c4 - c5, "Head": c5}

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
            w2 = ss(0.30, 0.75, s + 0.14 * ct)
            w1 = (1.0 - w2) * ss(-0.14, 0.24, s)
            return {"Wing_%s_1" % side: w1, "Wing_%s_2" % side: w2}
        w2 = ss(0.60, 0.80, s)
        w1 = (1.0 - w2) * ss(-0.12, 0.26, s)
        return {"Leg_%s_1" % side: w1, "Leg_%s_2" % side: w2}

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


def flap(rig, side, frame, up, sweep=0.0, twist=0.0, up2=0.0, sweep2=0.0, twist2=0.0):
    u = UP_L if side == "L" else UP_R
    b = BK_L if side == "L" else BK_R
    rkey(rig, "Wing_%s_1" % side, frame, (u, up), (b, sweep), (TWIST, twist))
    rkey(rig, "Wing_%s_2" % side, frame, (u, up2), (b, sweep2), (TWIST, twist2))


def legs(rig, frame, tuck, foot=0.0):
    for side in ("L", "R"):
        rkey(rig, "Leg_%s_1" % side, frame, (TUCK, tuck))
        rkey(rig, "Leg_%s_2" % side, frame, (TUCK, foot))


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    act.use_fake_user = True
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
        rkey(rig, "Neck1", f, (NOSE_UP, a))
        rkey(rig, "Neck2", f, (NOSE_UP, a * 0.6))
        rkey(rig, "Head", f, (NOSE_UP, -a * 0.5))
    for f, a in ((1, 0), (5, -5.0), (9, 3.5), (12, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    # LEGS TUCKED. A flying goose trails its feet under the tail; leaving them in the
    # bound pose would hang two paddles below a bird whose flight path bottoms out at
    # y = 0.3, and they would clip the lawn on every low pass.
    legs(rig, 1, 46, -18)
    legs(rig, 12, 46, -18)
    return act


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
        rkey(rig, "Neck1", f, (NOSE_UP, a), (YAW_L, y))
        rkey(rig, "Neck2", f, (NOSE_UP, a * 0.7), (YAW_L, y * 0.7))
        rkey(rig, "Head", f, (NOSE_UP, -a * 0.4), (YAW_L, y * 0.5))
    for f, a in ((1, 0), (30, 1.5), (60, 0)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    legs(rig, 1, 46, -18)
    legs(rig, 60, 46, -18)
    return act


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
    """
    act = new_action(rig, "Struck")
    wing = [
        (1,  22,  -6,  -8,  10, -10, -16),
        (2,  30, -16, -18,  16, -22, -26),   # the check: wings still trying
        (4, -12,  56,  22, -22,  46,  30),   # collapsed rearward
        (9,  -6,  62,  18, -16,  52,  26),
        (16, -2,  58,  14, -12,  46,  22),
        (24,  4,  50,  10,  -8,  40,  18),
    ]
    for f, u, sw, tw, u2, sw2, tw2 in wing:
        for side in ("L", "R"):
            flap(rig, side, f, u, sw, tw, u2, sw2, tw2)
    neck = [(1, 0, 0), (3, 26, -10), (8, -30, 12), (13, 14, -7),
            (18, -7, 3), (24, 0, 0)]
    for f, up, yaw in neck:
        rkey(rig, "Neck1", f, (NOSE_UP, up), (YAW_L, yaw))
        rkey(rig, "Neck2", f, (NOSE_UP, up * 0.85), (YAW_L, yaw * 0.8))
        rkey(rig, "Head", f, (NOSE_UP, up * 0.6), (YAW_L, yaw * 0.6))
    for f, pit, rol, lift in ((1, 0, 0, 0.0), (3, -9, 5, -0.02), (8, 7, -14, 0.01),
                              (15, -4, 9, -0.005), (24, 0, 0, 0.0)):
        rkey(rig, "Spine", f, (NOSE_UP, pit * 0.6), (ROLL_L, rol * 0.5))
        rkey(rig, "Chest", f, (NOSE_UP, pit), (ROLL_L, rol))
        lkey(rig, "Spine", f, (0, 0, lift))
    for f, a in ((1, 0), (3, 16), (10, -10), (24, 2)):
        rkey(rig, "Tail", f, (TAIL_UP, a))
    legs(rig, 1, 46, -18)
    legs(rig, 4, 8, 14)
    legs(rig, 12, 26, -4)
    legs(rig, 24, 40, -14)
    return act


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
             ("Neck1", NOSE_UP, "nose up"), ("Head", NOSE_UP, "nose up"),
             ("Neck1", YAW_L, "yaw left(+x)"), ("Tail", TAIL_UP, "tail up"),
             ("Leg_L_1", TUCK, "tuck"), ("Chest", ROLL_L, "roll left up")]
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


def author(rig):
    made = [clip_fly(rig), clip_glide(rig), clip_struck(rig)]
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
        bake_space_transform=False, mesh_smooth_type='FACE', use_mesh_modifiers=False,
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
    clips = author(rig)
    for c in clips:
        lo, hi = c.frame_range
        print("  clip %-8s frames %d..%d  (%.2f s at %d fps)"
              % (c.name, lo, hi, (hi - lo) / FPS, FPS))

    export([rig, ob], os.path.join(L.MODELS_DIR, "Goose.fbx"))

    if "--preview" in sys.argv:
        render_previews([ob], "goose", res=560, solo=True,
                        extra_views=[("head", (-0.45, -1.0, 0.22), 0.42)])
        pose_sheet(rig, ob, [("Fly", (1, 3, 5, 7, 9, 11)),
                             ("Struck", (2, 4, 9, 16)),
                             ("Glide", (20,))], "poses")
        tiny_sheet(rig, ob, [("Glide", (20,)), ("Fly", (1, 5, 9))])
    print("DONE Goose")


main()
