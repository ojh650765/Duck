# build_rally_props.py — DUCK MOW: the rally-mode arena kit.
#
# Run: blender --background --python C:\Duck\Art\Blender\build_rally_props.py
#      (add --preview to also render the gallery sheets)
#
# Ten objects that the goose rally needs and that nothing in Props.fbx or Foliage.fbx
# covers: a picket fence that comes apart, a flower bed with a ruined twin, and the
# furniture of an arena — barrier, stand, flag, totem.
#
# THE ONE RULE THAT SHAPED ALL OF IT.  Every piece here is either destroyed on screen or
# repeated along a line, and those two demands pull in opposite directions:
#
#   * Destroyed means the intact and the broken version must be RECOGNISABLY the same
#     object.  GardenBed_Trampled shares GardenBed's footprint, its edging boards, its
#     flower positions and its palette exactly, and differs only in height and posture.
#     A "ruined" prop modelled independently reads as a different prop appearing, not as
#     the one you were defending being wrecked.
#   * Repeated means tileable, which means the geometry has to stop dead at its half-width
#     with nothing overhanging.  ArenaBarrier's legs are inset 180 mm and CrowdStand has no
#     side cheeks for exactly this reason: a leg at the seam is two legs at the seam.
#
# Everything follows duck_lib: loft cross-sections, cut detail into the skin, colour in a
# CORNER BYTE_COLOR attribute named "Col", AO baked into it, one material (M_RallyProps)
# to match the convention every other kit in this project uses, pivots at the ground
# contact point, and Z-up facing -Y so the exporter lands it Y-up facing +Z in Unity.

import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, fresh_scene, bake_ao, report_tris, render_gallery, get_mat,
                      set_pivot, export_fbx, ring_z, sect, frame, cyl, tube, sphere,
                      rbox, disc, recess, pick_faces, TAU, tint)

MAT = "M_RallyProps"

# ------------------------------------------------------------------------------ colours
WHITE  = "F1EDE0"      # fence_white
CREAM  = "F5EAD6"      # tent_cream
RED    = "D8534E"      # tent_red
RED_D  = "A32E2D"
WOOD   = "9A6B41"
WOOD_D = "6E4A2C"
WOOD_R = "7E5636"      # raw split timber: between the two, and only ever on a break face
DIRT   = "B99A6B"
DIRT_D = "8E7550"
SOIL   = "6B5233"      # turned earth in a bed — darker and browner than a path
GREEN  = "2A5A34"      # hedge
GREEN_L = "3E7A42"
BRASS  = "C9A55A"
CHALK  = "F7F3E4"
BLUE   = "3E86A8"
YELLOW = "E8B84B"
PINK   = "E88AA0"


def ss(a, b, x):
    if b - a < 1e-9:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


# ============================================================== shared construction bits

def post_rings(prof, cx=0.0, cy=0.0, n=10, jitter=None, rnd=None):
    """Z-rings from (z, half_w, half_d, e) stations — the spine of every upright here."""
    out = []
    for i, (z, hw, hd, e) in enumerate(prof):
        r = ring_z(n, z, hw, hd, e, cx, cy)
        if jitter is not None and i == len(prof) - 1:
            for v in r:
                v.z += rnd.uniform(-jitter, jitter)
        out.append(r)
    return out


def timber(mb, col, path, prof_hw, prof_hd, e=3.6, n=10, cap=True, jag=None, rnd=None,
           col_break=WOOD_R):
    """A milled length of wood swept along `path`, with an optional SPLINTERED end.

    The break is the whole point of this helper.  A snapped fence rail modelled as a short
    rail with a flat end reads as a rail that was CUT, and a cut rail in the middle of a
    smashed fence is the single detail that makes the destruction look authored rather than
    violent.  Jittering the last ring in the sweep direction and letting the end cap follow
    it costs eight vertices and turns a saw cut into a fracture.
    """
    pts = [Vector(p) for p in path]
    m = len(pts)
    rings = []
    for i, p in enumerate(pts):
        a, b = pts[max(0, i - 1)], pts[min(m - 1, i + 1)]
        t = (b - a)
        t = t.normalized() if t.length > 1e-7 else Vector((0, 0, 1))
        up = Vector((0, 0, 1))
        if abs(t.dot(up)) > 0.96:
            up = Vector((0, 1, 0))
        rt = t.cross(up).normalized()
        up = rt.cross(t).normalized()
        hw = prof_hw[i] if hasattr(prof_hw, "__len__") else prof_hw
        hd = prof_hd[i] if hasattr(prof_hd, "__len__") else prof_hd
        ring = frame(sect(n, hw, hd, e), p, rt, up)
        if jag is not None and i == m - 1:
            for k, v in enumerate(ring):
                ring[k] = v + t * rnd.uniform(-jag, jag * 1.7)
        rings.append(ring)
    faces, vr = mb.loft(rings, col, cap_a=cap, cap_b=cap, smooth=False)
    if jag is not None:
        # the exposed end grain is raw timber, not painted surface
        mb.face_list([faces[-1]], col_break)
    return faces, vr


def splinter(mb, col, base, dirn, length, r0=0.010, seed=0):
    """A single loose sliver: a four-sided needle, tapered and bent a little."""
    rnd = random.Random(seed)
    b, d = Vector(base), Vector(dirn).normalized()
    mid = b + d * (length * 0.55) + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1),
                                            rnd.uniform(-1, 1))) * length * 0.10
    tip = b + d * length + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1),
                                   rnd.uniform(-1, 1))) * length * 0.16
    return tube(mb, col, [b, mid, tip], [r0, r0 * 0.62, r0 * 0.16], n=4, e=3.0,
                smooth=False)


def ribbon(mb, top, bot, thick, col_a, col_b):
    """A closed cloth strip from two edge paths — the only honest way to build a pennant.

    A single quad strip is invisible from behind under back-face culling, and duck_lib's
    seal() cannot rescue it because a WAVING flag is not planar, so its sheet test fails and
    it tries to hole-fill the outline into one twisted n-gon instead.  Front face, back face
    offset along `thick`, and all four edges closed, built explicitly.
    """
    t = Vector(thick)
    A = [mb.v(p) for p in top]
    B = [mb.v(p) for p in bot]
    C = [mb.v(Vector(p) + t) for p in top]
    D = [mb.v(Vector(p) + t) for p in bot]
    fs = []
    for i in range(len(top) - 1):
        fs.append(mb.f((A[i], A[i + 1], B[i + 1], B[i]), col_a))       # front
        fs.append(mb.f((C[i + 1], C[i], D[i], D[i + 1]), col_b))       # back
        fs.append(mb.f((A[i + 1], A[i], C[i], C[i + 1]), col_a))       # top edge
        fs.append(mb.f((B[i], B[i + 1], D[i + 1], D[i]), col_a))       # bottom edge
    fs.append(mb.f((A[0], B[0], D[0], C[0]), col_a))
    fs.append(mb.f((B[-1], A[-1], C[-1], D[-1]), col_a))
    fs = [f for f in fs if f]
    for f in fs:
        f.smooth = False
    mb.bm.normal_update()
    return fs


def rosette(mb, ctr, r, col_petal, col_heart, lobes=6, thick=0.022, tilt=0.0, spin=0.0):
    """A flower as ONE lobed slab plus a heart, not a stack of petal cards.

    Six separate petals cost six times the triangles and, at the 40 pixels this thing ever
    occupies, read as exactly the same shape with a noisier outline.  The lobed outline is
    what says 'flower'; everything past it is invisible from the only two cameras that
    exist.
    """
    c = Vector(ctr)
    R = Matrix.Rotation(tilt, 3, 'X') @ Matrix.Rotation(spin, 3, 'Z')
    pts = []
    for i in range(lobes * 2):
        a = TAU * i / (lobes * 2) + spin
        rr = r if i % 2 == 0 else r * 0.52
        pts.append(c + R @ Vector((math.cos(a) * rr, math.sin(a) * rr, 0.0)))
    up = R @ Vector((0, 0, 1))
    mb.slab(pts, col_petal, -up * thick, col_bot=col_petal, smooth=False)
    cyl(mb, col_heart, c + up * (thick * 0.4), c + up * (thick * 1.5),
        r * 0.30, r * 0.24, n=6, smooth=False)


def clump(mb, ctr, r, col, seed=0, n=3):
    """A leaf mound: overlapping squashed spheres, never one sphere."""
    rnd = random.Random(seed)
    for i in range(n):
        o = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * r * 0.55
        s = rnd.uniform(0.62, 1.0)
        sphere(mb, col, Vector(ctr) + o + Vector((0, 0, r * s * 0.42)),
               (r * s, r * s * 0.86, r * s * 0.60), seg=8, rings=4, smooth=True)


# ==================================================================== 1. the picket fence

POST_H = 0.90
POST_PROF = [
    (0.000, 0.058, 0.058, 5.0),     # a base flare, so it looks bedded rather than dropped
    (0.026, 0.050, 0.050, 4.6),
    (0.090, 0.047, 0.047, 4.4),
    (0.760, 0.043, 0.043, 4.2),
    (0.788, 0.058, 0.058, 3.9),     # the cap's overhang: the shadow line under it is the
    (0.828, 0.056, 0.056, 3.7),     # only thing that separates a post from a stick
    (0.858, 0.036, 0.036, 3.3),
    (0.892, 0.021, 0.021, 3.1),
    (0.900, 0.018, 0.018, 3.0),
]


def build_post(mb, x=0.0, y=0.0, top=POST_H, jag=None, rnd=None):
    prof = [p for p in POST_PROF if p[0] <= top]
    if jag is not None:
        # a SNAPPED post: cut the profile short and let the last ring break up
        prof = [p for p in POST_PROF if p[0] < top - 0.02]
        hw = prof[-1][1]
        prof = prof + [(top, hw * 0.97, hw * 0.97, 4.2)]
    rings = post_rings(prof, x, y, n=10, jitter=jag, rnd=rnd)
    faces, vr = mb.loft(rings, WHITE, smooth=False)
    # the soil line: 70 mm of weathered timber where the paint has gone
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.072), WOOD_D)
    if jag is None:
        mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z > 0.876), CHALK)
    else:
        mb.face_list([faces[-1]], WOOD_R)
    return faces, vr


def build_rail(mb, x0, x1, z, y=0.0, sag=0.007, jag=None, rnd=None, col=WHITE):
    n = 5
    path, hw, hd = [], [], []
    for i in range(n):
        t = i / (n - 1)
        path.append((x0 + (x1 - x0) * t, y, z - math.sin(math.pi * t) * sag))
        # tenon: the rail necks down where it enters the post
        k = 1.0 - 0.16 * (1.0 - ss(0.0, 0.14, min(t, 1 - t)))
        hw.append(0.048 * k)
        hd.append(0.022 * k)
    return timber(mb, col, path, hw, hd, e=3.6, n=8, jag=jag, rnd=rnd)


PICKET_PROF = [
    (0.000, 0.045, 0.014, 4.2),
    (0.640, 0.045, 0.014, 4.2),
    (0.686, 0.045, 0.014, 3.8),
    (0.718, 0.038, 0.013, 3.4),
    (0.750, 0.024, 0.011, 3.1),
    (0.770, 0.008, 0.007, 3.0),
]


def build_picket(mb, x, z0=0.0, y=0.0, col=WHITE, jag=None, rnd=None, lean=0.0):
    prof = [(z0 + p[0], p[1], p[2], p[3]) for p in PICKET_PROF]
    if jag is not None:
        prof = [p for p in prof if p[0] < z0 + jag[0]]
        prof.append((z0 + jag[0], prof[-1][1] * 0.96, prof[-1][2] * 0.96, 4.0))
    rings = post_rings(prof, x, y, n=8, jitter=(0.018 if jag else None), rnd=rnd)
    if lean:
        R = Matrix.Rotation(lean, 3, 'Y')
        base = Vector((x, y, z0))
        rings = [[base + R @ (v - base) for v in r] for r in rings]
    faces, vr = mb.loft(rings, col, smooth=False)
    if jag is not None:
        mb.face_list([faces[-1]], WOOD_R)
    return faces, vr


def fence_post():
    mb = MB()
    build_post(mb)
    return mb.finish("FencePost", MAT, smooth_angle=24.0)


def fence_rail():
    mb = MB()
    build_rail(mb, -1.0, 1.0, 0.0)
    return mb.finish("FenceRail", MAT, smooth_angle=24.0)


def fence_picket():
    mb = MB()
    build_picket(mb, 0.0)
    return mb.finish("FencePicket", MAT, smooth_angle=24.0)


def fence_section_broken():
    """2 m of fence that has been through a goose.

    Deliberately NOT symmetrical damage.  Both posts snapped at the same height, or every
    picket leaning the same way, reads as a texture; one post standing, one snapped at
    knee height, one rail hanging off its remaining tenon and a gap where two pickets used
    to be reads as a specific event that happened in a specific direction.
    """
    rnd = random.Random(41)
    mb = MB()
    # left post survives, leaning; right post is a splintered stump
    build_post(mb, x=-1.0, top=POST_H)
    build_post(mb, x=1.0, top=0.44, jag=0.030, rnd=rnd)

    # top rail: torn free at the right, dropped and rotated about the surviving tenon
    build_rail(mb, -1.02, 0.30, 0.62, sag=0.004)
    a = Vector((0.30, 0.0, 0.615))
    b = a + Vector((0.62, 0.06, -0.30))
    timber(mb, WHITE, [a, (a + b) * 0.5 + Vector((0, 0.02, 0.02)), b],
           [0.046, 0.044, 0.040], [0.021, 0.020, 0.018], e=3.6, n=8, jag=0.030, rnd=rnd)
    # bottom rail survives the full span but is split at the middle
    build_rail(mb, -1.02, 0.12, 0.24, sag=0.006, jag=0.026, rnd=rnd)
    build_rail(mb, 0.20, 1.02, 0.235, sag=0.004)

    # pickets: two gone entirely, one snapped, three leaning at different angles
    for x, lean, jag in ((-0.78, 0.05, None), (-0.50, -0.11, None),
                         (-0.22, 0.02, (0.31,)), (0.62, 0.16, None)):
        build_picket(mb, x, z0=0.10, lean=lean, jag=jag, rnd=rnd)
    # one picket knocked flat on the grass, still the same object
    pr = [(0.0, p[1], p[2], p[3]) for p in PICKET_PROF]
    rings = post_rings(pr, 0, 0, n=8)
    R = Matrix.Rotation(math.radians(88), 3, 'X') @ Matrix.Rotation(math.radians(24), 3, 'Z')
    base = Vector((0.16, -0.34, 0.020))
    rings = [[base + R @ v for v in r] for r in rings]
    mb.loft(rings, WHITE, smooth=False)

    for i, (b, d, ln) in enumerate((
            ((0.96, 0.03, 0.42), (0.3, 0.5, 1.0), 0.22),
            ((1.04, -0.04, 0.40), (0.6, -0.4, 0.9), 0.17),
            ((0.16, 0.02, 0.25), (0.9, 0.2, 0.4), 0.19),
            ((-0.20, 0.01, 0.40), (-0.2, 0.7, 0.7), 0.13))):
        splinter(mb, WOOD_R, b, d, ln, r0=0.011, seed=7 + i)
    return mb.finish("FenceSection_Broken", MAT, smooth_angle=24.0)


def fence_debris(name, seed, scale, kind):
    """Loose wreckage for the break burst: a plank end, a picket tip, a shard."""
    rnd = random.Random(seed)
    mb = MB()
    if kind == "plank":
        timber(mb, WHITE, [(-scale * 0.5, 0, 0.016), (0, 0.006, 0.019),
                           (scale * 0.5, -0.004, 0.015)],
               [0.044, 0.043, 0.038], [0.019, 0.018, 0.016], e=3.6, n=8,
               jag=0.024, rnd=rnd)
    elif kind == "tip":
        prof = [(0.000, 0.042, 0.014, 4.0), (0.062, 0.040, 0.013, 3.6),
                (0.104, 0.026, 0.010, 3.2), (0.140, 0.006, 0.005, 3.0)]
        rings = post_rings(prof, 0, 0, n=8, jitter=0.014, rnd=rnd)
        R = Matrix.Rotation(math.radians(74), 3, 'X')
        rings = [[Vector((0, 0, 0.016)) + R @ v for v in r] for r in rings]
        faces, _ = mb.loft(rings, WHITE, smooth=False)
        mb.face_list([faces[0]], WOOD_R)
    else:
        for i in range(3):
            splinter(mb, WHITE if i else WOOD_R,
                     (rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02), 0.016),
                     (rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.1, 0.5)),
                     scale * rnd.uniform(0.9, 1.4), r0=0.019, seed=seed + i)
    return mb.finish(name, MAT, smooth_angle=24.0)


# ====================================================================== 2. the flower bed

BED_W, BED_D = 1.60, 1.10
# (x, y, radius, petal, heart) — SHARED between the intact and the trampled bed, because
# the two have to be the same object in two states and a flower that moves between them
# turns the destruction into a substitution.
BLOOMS = [(-0.52, 0.22, 0.115, RED, YELLOW), (-0.16, -0.24, 0.100, CREAM, YELLOW),
          (0.14, 0.26, 0.122, PINK, CHALK), (0.50, -0.14, 0.106, YELLOW, RED_D),
          (-0.02, 0.02, 0.094, RED, CHALK), (0.58, 0.28, 0.088, CREAM, YELLOW),
          (-0.62, -0.22, 0.092, PINK, YELLOW)]
EDGING = [(0.0, BED_D * 0.5, BED_W, 0.0), (0.0, -BED_D * 0.5, BED_W, 0.0),
          (BED_W * 0.5, 0.0, BED_D, math.pi * 0.5),
          (-BED_W * 0.5, 0.0, BED_D, math.pi * 0.5)]


def bed_edging(mb, mb_h, damaged=False, rnd=None):
    """The board edging. Four lengths of 120 x 40 timber on edge, chamfered."""
    for i, (x, y, ln, rot) in enumerate(EDGING):
        h = mb_h
        hw, hd = 0.026, h * 0.5
        roll, shove, jag = 0.0, 0.0, None
        if damaged:
            # rolled about the board's OWN LENGTH, not tipped end over end.  Tipping put
            # one end 225 mm under the lawn -- a board cannot sink into turf, and the read
            # that is wanted is "knocked flat", which is a roll.
            roll = (1.05, -0.34, 0.16, 1.25)[i]
            shove = (0.035, -0.012, 0.008, -0.048)[i]
            jag = 0.022 if i == 3 else None
        d = Vector((math.cos(rot), math.sin(rot), 0))
        out = Vector((-d.y, d.x, 0)) * shove
        # sit the board on the grass whatever its roll: the rolled half-height is what
        # decides its centre, so the underside is always exactly on z = 0
        cz = hd * abs(math.cos(roll)) + hw * abs(math.sin(roll))
        c = Vector((x, y, cz)) + out
        a, b = c - d * (ln * 0.5), c + d * (ln * 0.5)
        prof = [(hw, hd)] * 3
        if roll:
            R = Matrix.Rotation(roll, 3, d)
            # rolling the SECTION, not the path: the path is horizontal either way
            hw2 = hw * abs(math.cos(roll)) + hd * abs(math.sin(roll))
            hd2 = hd * abs(math.cos(roll)) + hw * abs(math.sin(roll))
            prof = [(hw2, hd2)] * 3
        timber(mb, WOOD, [a, (a + b) * 0.5, b], [p[0] for p in prof],
               [p[1] for p in prof], e=3.4, n=8, jag=jag, rnd=rnd)


def garden_bed():
    """The thing the geese are trying to destroy — so it has to look CARED FOR.

    Turned soil sitting proud of the grass inside a board edging, mounded slightly toward
    the middle, planted in a scatter that is deliberately not a grid.  The mound is what
    does the work: a flat bed reads as a rug, and a rug is not something you defend.
    """
    rnd = random.Random(11)
    mb = MB()
    H = 0.150
    bed_edging(mb, H)
    # the soil: a 5 x 4 mounded grid, so it crowns instead of being a lid
    nx, ny = 6, 5
    grid = []
    for j in range(ny):
        row = []
        for i in range(nx):
            u, v = i / (nx - 1) - 0.5, j / (ny - 1) - 0.5
            x, y = u * (BED_W - 0.07), v * (BED_D - 0.07)
            crown = (1 - (2 * u) ** 2) * (1 - (2 * v) ** 2)
            row.append(Vector((x, y, H * 0.62 + 0.055 * crown +
                               rnd.uniform(-0.006, 0.006))))
        grid.append(row)
    for j in range(ny - 1):
        for i in range(nx - 1):
            mb.f([mb.v(p) for p in (grid[j][i], grid[j][i + 1],
                                    grid[j + 1][i + 1], grid[j + 1][i])], SOIL)
    # skirt the soil down to the edging so there is no floating slab
    ring = ([grid[0][i] for i in range(nx)] +
            [grid[j][nx - 1] for j in range(1, ny)] +
            [grid[ny - 1][i] for i in range(nx - 2, -1, -1)] +
            [grid[j][0] for j in range(ny - 2, 0, -1)])
    for a, b in zip(ring, ring[1:] + ring[:1]):
        mb.f([mb.v(p) for p in (a, b, Vector((b.x * 1.03, b.y * 1.03, 0.0)),
                                Vector((a.x * 1.03, a.y * 1.03, 0.0)))], SOIL)

    for i, (x, y, r, pc, hc) in enumerate(BLOOMS):
        z = H * 0.62 + 0.055 * max(0.0, (1 - (2 * x / BED_W) ** 2) *
                                   (1 - (2 * y / BED_D) ** 2))
        clump(mb, (x, y, z - 0.01), r * 0.86, GREEN, seed=20 + i, n=3)
        stem = z + r * 1.30
        cyl(mb, GREEN_L, (x, y, z), (x + r * 0.06, y, stem), 0.011, 0.009, n=5,
            smooth=False)
        rosette(mb, (x + r * 0.06, y, stem), r, pc, hc,
                lobes=6, tilt=rnd.uniform(-0.22, 0.22), spin=rnd.uniform(0, 1.0))
    return mb.finish("GardenBed", MAT, smooth_angle=32.0)


def garden_bed_trampled():
    """The same bed after a goose has been through it.

    Same footprint, same edging boards, same seven blooms in the same seven places — all
    of them flat, face down or snapped over, with the soil churned into ruts and three web
    prints pressed into it.  The prints are the whole gag: they name the culprit, and they
    are the reason this reads as an event rather than as a low-poly variant.
    """
    rnd = random.Random(29)
    mb = MB()
    H = 0.150
    bed_edging(mb, H, damaged=True, rnd=rnd)
    nx, ny = 6, 5
    grid = []
    for j in range(ny):
        row = []
        for i in range(nx):
            u, v = i / (nx - 1) - 0.5, j / (ny - 1) - 0.5
            x, y = u * (BED_W - 0.07), v * (BED_D - 0.07)
            # a rut ploughed diagonally across the bed, plus general churn
            rut = math.exp(-((y - x * 0.42) / 0.18) ** 2)
            z = H * 0.52 - 0.045 * rut + rnd.uniform(-0.012, 0.012)
            row.append(Vector((x, y, max(0.012, z))))
        grid.append(row)
    for j in range(ny - 1):
        for i in range(nx - 1):
            mb.f([mb.v(p) for p in (grid[j][i], grid[j][i + 1],
                                    grid[j + 1][i + 1], grid[j + 1][i])], SOIL)
    ring = ([grid[0][i] for i in range(nx)] +
            [grid[j][nx - 1] for j in range(1, ny)] +
            [grid[ny - 1][i] for i in range(nx - 2, -1, -1)] +
            [grid[j][0] for j in range(ny - 2, 0, -1)])
    for a, b in zip(ring, ring[1:] + ring[:1]):
        mb.f([mb.v(p) for p in (a, b, Vector((b.x * 1.03, b.y * 1.03, 0.0)),
                                Vector((a.x * 1.03, a.y * 1.03, 0.0)))], SOIL)

    def soil_z(x, y):
        rut = math.exp(-((y - x * 0.42) / 0.18) ** 2)
        return max(0.012, H * 0.52 - 0.045 * rut)

    # THREE WEB PRINTS, pressed in along the rut.  A goose foot is a wide triangle with
    # three toes; at this size the outline is the only part that survives, so it is built
    # as an outline and nothing else.
    for i, (px, py, ang) in enumerate(((-0.44, -0.26, 0.5), (-0.02, -0.06, 0.2),
                                       (0.44, 0.14, -0.3))):
        pts = []
        for a, r in ((-0.66, 0.150), (-0.34, 0.092), (0.0, 0.163), (0.34, 0.092),
                     (0.66, 0.150), (0.32, 0.036), (0.0, -0.072), (-0.32, 0.036)):
            th = a + ang + math.pi * 0.5
            pts.append(Vector((px + math.cos(th) * r, py + math.sin(th) * r,
                               soil_z(px, py) + 0.004)))
        mb.slab(pts, "4A3820", (0, 0, -0.034), col_bot="4A3820", smooth=False)

    for i, (x, y, r, pc, hc) in enumerate(BLOOMS):
        z = soil_z(x, y)
        clump(mb, (x, y, z - 0.02), r * 0.70, tint(GREEN, 0.80), seed=20 + i, n=2)
        # the stem is snapped: a short stub, and the bloom lying flat beside it
        cyl(mb, tint(GREEN_L, 0.82), (x, y, z), (x + r * 0.10, y + r * 0.05, z + r * 0.28),
            0.010, 0.008, n=5, smooth=False)
        fx = x + math.cos(i * 2.1) * r * 1.25
        fy = y + math.sin(i * 2.1) * r * 1.25
        rosette(mb, (fx, fy, soil_z(fx, fy) + 0.016), r * 0.94,
                tint(pc, 0.80), tint(hc, 0.80), lobes=6,
                tilt=rnd.uniform(-0.10, 0.10), spin=rnd.uniform(0, 1.0))
    # loose petals torn off, because a crushed flower loses pieces
    for i in range(6):
        a = rnd.uniform(0, TAU)
        rr = rnd.uniform(0.45, 0.78)
        px, py = math.cos(a) * rr, math.sin(a) * rr * 0.62
        pts = []
        for k in range(6):
            th = TAU * k / 6 + a
            q = 0.036 if k % 2 == 0 else 0.020
            pts.append(Vector((px + math.cos(th) * q, py + math.sin(th) * q,
                               soil_z(px, py) + 0.006)))
        mb.slab(pts, tint(BLOOMS[i % len(BLOOMS)][3], 0.82), (0, 0, -0.008), smooth=False)
    return mb.finish("GardenBed_Trampled", MAT, smooth_angle=32.0)


# ========================================================================= 3. the arena

def arena_flag():
    """A quadrant pennant, 3.30 m, so it is visible over the goose and the fence.

    The flag is a THREAT INDICATOR, which means the readable thing has to be the cloth and
    not the pole: it is 1.05 m long, it waves through two full periods so the silhouette
    changes along its length, and it is two-tone front to back so a flag seen edge-on still
    separates from the sky.
    """
    mb = MB()
    H = 3.30
    prof = [(0.000, 0.075, 0.075, 5.0), (0.040, 0.056, 0.056, 4.4),
            (0.110, 0.048, 0.048, 3.0), (2.900, 0.032, 0.032, 2.6),
            (3.010, 0.040, 0.040, 2.4), (3.060, 0.036, 0.036, 2.4)]
    faces, _ = mb.loft(post_rings(prof, n=10), WOOD, smooth=True)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.115), WOOD_D)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z > 2.895), BRASS)
    sphere(mb, BRASS, (0, 0, H - 0.16), (0.062, 0.062, 0.082), seg=10, rings=5)
    cyl(mb, BRASS, (0, 0, H - 0.09), (0, 0, H - 0.02), 0.020, 0.006, n=8)

    top, bot = [], []
    for i in range(11):
        t = i / 10.0
        wave = math.sin(t * 6.6) * 0.085 * ss(0.0, 0.28, t)
        x = 0.034 + t * 1.30
        top.append((x, wave, H - 0.26 - t * 0.070))
        bot.append((x, wave * 0.86, H - 0.26 - 0.60 + t * 0.52))
    ribbon(mb, top, bot, (0, 0.014, 0), RED, CREAM)
    # a cream chevron band, built as a second shorter ribbon laid on the front
    t2, b2 = [], []
    for i in range(11):
        t = i / 10.0
        wave = math.sin(t * 6.6) * 0.085 * ss(0.0, 0.28, t) - 0.0055
        x = 0.034 + t * 1.30
        t2.append((x, wave, H - 0.26 - t * 0.070 - 0.070))
        b2.append((x, wave * 0.86, H - 0.26 - t * 0.070 - 0.150))
    ribbon(mb, t2, b2, (0, -0.004, 0), CREAM, CREAM)
    return mb.finish("ArenaFlag", MAT, smooth_angle=30.0)


def arena_barrier():
    """2.40 m of arena hoarding, tileable end to end.

    Everything lives inside x = +/- 1.20 and the A-frame legs are set 180 mm in from the
    ends, so a run of these has one leg per joint instead of two crossed ones.  The face
    carries a recessed panel with a red band in it -- recessed, not applied, because a
    coloured board bolted to a fence is the asset-flip read the bible bans by name.
    """
    mb = MB()
    HW, H = 1.20, 0.76
    # top rail: a rounded bar, proud of the panel, running the full width
    cyl(mb, CREAM, (-HW, 0, H - 0.045), (HW, 0, H - 0.045), 0.046, 0.046, n=8,
        e=2.6, smooth=True)
    # the panel, lofted along X so the ends are square and it tiles
    rings = []
    for x, hd, z0, z1 in ((-HW, 0.030, 0.20, H - 0.075), (-HW + 0.02, 0.036, 0.19, H - 0.07),
                          (HW - 0.02, 0.036, 0.19, H - 0.07), (HW, 0.030, 0.20, H - 0.075)):
        cz, hz = (z0 + z1) * 0.5, (z1 - z0) * 0.5
        rings.append([Vector((x, p[0], cz + p[1])) for p in sect(10, hd, hz, 4.6)])
    faces, _ = mb.loft(rings, CREAM, smooth=False)
    front = pick_faces(faces, lambda f: f.normal.y < -0.72)
    inner = recess(mb, front, 0.040, -0.016, col=RED)
    recess(mb, inner, 0.030, 0.006, col=RED_D)
    back = pick_faces(faces, lambda f: f.normal.y > 0.72)
    recess(mb, back, 0.045, -0.012, col=tint(CREAM, 0.90))

    for sx in (-1, 1):
        x = sx * (HW - 0.18)
        for sy in (-1, 1):
            timber(mb, WOOD, [(x, 0.0, H - 0.10), (x, sy * 0.13, 0.30),
                              (x, sy * 0.24, 0.0)],
                   [0.036, 0.032, 0.030], [0.036, 0.032, 0.030], e=4.0, n=8)
        cyl(mb, WOOD_D, (x - 0.05, -0.20, 0.10), (x - 0.05, 0.20, 0.10), 0.020, 0.020,
            n=6, e=3.0, smooth=False)
    return mb.finish("ArenaBarrier", MAT, smooth_angle=26.0)


def crowd_stand():
    """A three-tier stand section, 3.00 m wide, tileable along X.

    NO SIDE CHEEKS.  They are the first thing you want to model and the one thing that
    makes the piece untileable -- two adjacent sections meet with a 90 mm double wall down
    the seam that catches the light and turns a grandstand into a row of separate boxes.
    The scaffold legs sit fully inside the half width for the same reason.
    """
    mb = MB()
    HW = 1.50
    steps = [(0.00, 0.78, 0.42), (0.78, 1.56, 0.84), (1.56, 2.34, 1.26)]
    for i, (y0, y1, z) in enumerate(steps):
        # riser, set back so the tread noses over it and casts a line
        rbox(mb, WOOD_D, (0, y0 + 0.045, z * 0.5 - 0.045),
             (HW * 2, 0.075, z - 0.09), r=0.010, n=8, k=1)
        # tread
        rbox(mb, WOOD, (0, (y0 + y1) * 0.5, z - 0.045), (HW * 2, y1 - y0, 0.09),
             r=0.014, n=8, k=1)
        # bench plank, proud of the tread and in a different tone so the tiers read
        rbox(mb, CREAM if i % 2 == 0 else WHITE,
             (0, y0 + 0.30, z + 0.045), (HW * 2 - 0.10, 0.34, 0.05), r=0.012, n=8, k=1)
        for sx in (-1, 0, 1):
            x = sx * (HW - 0.22)
            cyl(mb, RED, (x, y0 + 0.14, 0.0), (x, y0 + 0.14, z - 0.09), 0.038, 0.032,
                n=6, e=2.6, smooth=False)
    # back rail: posts and a bar, the thing that stops the top tier reading as a shelf
    for sx in (-1, 0, 1):
        x = sx * (HW - 0.22)
        cyl(mb, RED, (x, 2.30, 1.26), (x, 2.30, 1.78), 0.034, 0.028, n=6, e=2.6,
            smooth=False)
    cyl(mb, CREAM, (-HW, 2.30, 1.75), (HW, 2.30, 1.75), 0.032, 0.032, n=8, smooth=True)
    cyl(mb, CREAM, (-HW, 2.30, 1.52), (HW, 2.30, 1.52), 0.024, 0.024, n=6, smooth=True)
    # a bunting swag along the front, because this is a county fair and not a stadium
    for sx in (-1, 1):
        pts = [(sx * HW, 2.30, 1.75), (sx * HW * 0.5, 2.29, 1.60), (0, 2.30, 1.74)]
        tube(mb, CHALK, pts, 0.009, n=5, smooth=False)
        for k in range(4):
            t = (k + 0.5) / 4.0
            bx = sx * HW * (1.0 - t)
            bz = 1.75 - math.sin(math.pi * t) * 0.15
            mb.slab([Vector((bx - 0.055, 2.29, bz)), Vector((bx + 0.055, 2.29, bz)),
                     Vector((bx, 2.29, bz - 0.115))],
                    RED if k % 2 else CREAM, (0, 0.008, 0), smooth=False)
    return mb.finish("CrowdStand", MAT, smooth_angle=26.0)


def score_totem():
    """One per quadrant: whose garden this is, and how much of it is left.

    Two channels, deliberately different in KIND rather than in value -- a colour roundel
    at the top says WHO, a vertical bar down the shaft says HOW MUCH.  Encoding both as
    colour would make a damaged blue quadrant and an undamaged red one look like the same
    amount of information, and the bar has to be legible from the far end of the pitch
    where the roundel is four pixels across.

    The bar is a SEPARATE object (ScoreTotem_Bar) with its pivot at the bottom of its
    channel, so Unity scales it on Z from 1 to 0 and it empties downward like a gauge.
    Baked into the totem it would be a picture of a full gauge.
    """
    mb = MB()
    H = 1.92
    rbox(mb, WOOD_D, (0, 0, 0.055), (0.34, 0.34, 0.11), r=0.018, n=10, k=2,
         taper=(0.86, 0.86))
    prof = [(0.100, 0.078, 0.078, 5.0), (0.150, 0.070, 0.070, 4.6),
            (1.520, 0.056, 0.056, 4.2), (1.600, 0.062, 0.062, 3.8),
            (1.636, 0.058, 0.058, 3.6)]
    faces, _ = mb.loft(post_rings(prof, n=10), CREAM, smooth=False)

    # the gauge channel, cut into the shaft rather than stuck on it
    front = pick_faces(faces, lambda f: f.normal.y < -0.70 and
                       0.30 < f.calc_center_median().z < 1.45)
    if front:
        recess(mb, front, 0.012, -0.026, col=WOOD_D)
    # tick marks: three shallow steps across the channel so the bar has something to read
    for z in (0.58, 0.88, 1.18):
        rbox(mb, BRASS, (0, -0.058, z), (0.052, 0.012, 0.010), r=0.003, n=6, k=1)

    # the head: a bezel ring with a colour roundel in it, tilted toward the pitch
    head = Vector((0, -0.030, 1.74))
    cyl(mb, WOOD, (head.x, head.y + 0.030, head.z), (head.x, head.y - 0.012, head.z),
        0.150, 0.140, n=12, e=2.4, smooth=True)
    disc(mb, RED, (head.x, head.y - 0.014, head.z), (0, -1, 0), 0.116, n=12, smooth=False)
    disc(mb, CHALK, (head.x, head.y - 0.019, head.z), (0, -1, 0), 0.052, n=10, smooth=False)
    # a pennant crest above it so the totem has a silhouette instead of a lollipop
    for sx in (-1, 1):
        # started 5 mm off the centreline on each side: butted exactly together, seal()'s
        # 0.1 mm weld fuses the two root edges and the crest becomes non-manifold
        ribbon(mb, [(sx * 0.005, -0.03, 1.90), (sx * 0.15, -0.01, 1.955),
                    (sx * 0.30, -0.03, 1.905)],
               [(sx * 0.005, -0.03, 1.845), (sx * 0.15, -0.01, 1.865),
                (sx * 0.30, -0.03, 1.800)],
               (0, 0.012, 0), RED, RED_D)
    ob = mb.finish("ScoreTotem", MAT, smooth_angle=28.0)

    mb2 = MB()
    rbox(mb2, RED, (0, -0.052, 0.42), (0.058, 0.020, 0.84), r=0.006, n=8, k=1)
    bar = mb2.finish("ScoreTotem_Bar", MAT, smooth_angle=24.0)
    set_pivot(bar, (0, 0, 0.0))
    bar.location = (0, 0, 0.42)
    return ob, bar


# ============================================================================== assemble

def main():
    fresh_scene()
    objs = [fence_post(), fence_rail(), fence_picket(), fence_section_broken(),
            fence_debris("FenceDebris_A", 101, 0.30, "plank"),
            fence_debris("FenceDebris_B", 202, 0.20, "tip"),
            fence_debris("FenceDebris_C", 303, 0.11, "shard"),
            garden_bed(), garden_bed_trampled(),
            arena_flag(), arena_barrier(), crowd_stand()]
    totem, bar = score_totem()
    objs += [totem, bar]

    bpy.context.view_layer.update()
    for i, ob in enumerate(objs):
        bake_ao([ob], floor=0.72, dist=0.30, samples=24, ground=True, ground_size=6.0)
    # lay the kit out on a grid ONLY for the preview render; the export is at the origin
    layout = []
    for i, ob in enumerate(objs):
        layout.append((ob, ((i % 5) * 3.6, (i // 5) * 3.6, 0.0)))
    L.verify_meshes(objs, "RallyProps")
    total = report_tris(objs, "RallyProps")

    print("BOUNDS (metres, Blender Z-up; pivot should sit on the ground plane)")
    for o in objs:
        lo = Vector((min(v.co.x for v in o.data.vertices),
                     min(v.co.y for v in o.data.vertices),
                     min(v.co.z for v in o.data.vertices)))
        hi = Vector((max(v.co.x for v in o.data.vertices),
                     max(v.co.y for v in o.data.vertices),
                     max(v.co.z for v in o.data.vertices)))
        print("  %-22s size %5.3f x %5.3f x %5.3f   z %+.3f .. %+.3f   tris %5d"
              % (o.name, hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.z + o.location.z,
                 hi.z + o.location.z, L.tri_count(o)))
    print("TOTAL %d triangles (budget 12000)" % total)

    export_fbx(objs, os.path.join(L.MODELS_DIR, "RallyProps.fbx"))
    for ob, p in layout:
        ob.location = Vector(ob.location) + Vector(p)
    if "--preview" in sys.argv:
        render_gallery(objs, "rally", res=340, cols=5)
        render_gallery(objs, "rally", res=340, cols=5, sil=True)
    print("DONE RallyProps")


main()
