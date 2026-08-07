# build_turf_props.py — DUCK MOW: the BLOOM RUSH arena kit.
#
# Run: blender --background --python C:\Duck\Art\Blender\build_turf_props.py
#      (add --preview to also render the gallery sheets)
#
# BLOOM RUSH is a 1v1v1v1 territory-control wheel: a circular flower plaza at the hub,
# a ring of clipped hedge broken by eight gateway gaps, and a broad outer lawn loop.
# Nine pieces, all pivoted on the ground plane, all coloured through the same CORNER
# BYTE_COLOR "Col" convention as every other kit in this project so they wear the shared
# white vertex-lit material.
#
# THE RULE THAT SHAPED THE HEDGE.  A hedge wall is the one piece here that gets placed
# end to end around a 17-30 m ring, so its silhouette has to be lumpy and hand-clipped
# in the MIDDLE while staying flat and canonical at the very ends -- if the top edge or
# the depth wandered all the way to x = +/-halfLength, two arcs butted together would
# show a visible step at every seam. Every organic wobble in HedgeArc is therefore faded
# to zero over the last 12% of its length with the same smoothstep rally props uses for
# splinter jitter, and only the interior is left to look clipped by hand.

import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, fresh_scene, bake_ao, report_tris, render_gallery, get_mat,
                      set_pivot, export_fbx, ring_z, sect, frame, cyl, tube, sphere,
                      rbox, disc, recess, pick_faces, torus, TAU, tint, verify_meshes,
                      tri_count)

MAT = "M_TurfProps"

# ------------------------------------------------------------------------------ colours
HEDGE       = "355E36"      # clipped foliage, base tone
HEDGE_L     = "4C8548"      # sun-side / dome highlight
HEDGE_TIP   = "5C9650"      # new growth at the very top of a lump
HEDGE_WOOD  = "5B4128"      # visible woody base under the clip line

STONE       = "C9C2AC"
STONE_D     = "A79E86"
STONE_L     = "E7E1CC"
STONE_JOINT = "8D8570"

WOOD        = "9A6B41"
WOOD_D      = "6E4A2C"

IRON        = "2B2C2E"
IRON_L      = "45474B"

ROSE_PINK   = "E27F97"
ROSE_RED    = "C94B4F"
ROSE_LEAF   = "3E7A42"
ROSE_LEAF_D = "2C5A31"

DIRT        = "B08A57"
DIRT_D      = "80613C"
DIRT_L      = "C9A46E"

WATER       = "3E86A8"
WATER_L     = "6FB6D1"

BRASS       = "C9A55A"
GLASS_WARM  = "F3D98B"


def ss(a, b, x):
    if b - a < 1e-9:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


# ============================================================== shared construction bits

def loaf_ring(hw, z0, ztop, dome=0.16, arc_n=4):
    """A 'loaf of bread' 2D outline: flat bottom at z0, rounded clipped-hedge top.

    Returned as a closed CCW loop of (y, z) points, meant to be lofted along X. The flat
    bottom is what keeps a hedge looking planted rather than like a partly-buried log.
    """
    shoulder = max(z0 + 0.02, ztop - dome)
    pts = [(-hw, z0), (hw, z0), (hw, shoulder)]
    for i in range(1, arc_n):
        th = math.pi * i / arc_n
        pts.append((hw * math.cos(th), shoulder + dome * math.sin(th)))
    pts.append((-hw, shoulder))
    return pts


def hedge_wall(name, length, seed, depth=1.6, height=2.2, sagitta=None, stations=None):
    """A modular clipped-hedge section, origin at its centre on the ground.

    Runs along local +X. The whole cross-section is shifted sideways by a parabolic
    offset (`sagitta`, zero at both ends) so several placed end to end trace a very
    gentle arc -- the maths matches a circle of radius roughly (L/2)^2/(2*sagitta),
    which for the numbers used below sits inside the 17-30 m hedge-ring band this stage
    calls for. Top height and depth wobble organically in the interior and fade back to
    the canonical flat rectangle at both ends (see module docstring) so runs tile clean.
    """
    rnd = random.Random(seed)
    if sagitta is None:
        sagitta = length * length / (2.0 * 26.8)   # ~R=26.8 m ring, mid-band of 17-30 m
    if stations is None:
        stations = max(7, int(length * 1.7) | 1)    # odd count -> a station lands at apex
    mb = MB()
    hd = depth * 0.5
    ph1, ph2, ph3 = rnd.uniform(0, TAU), rnd.uniform(0, TAU), rnd.uniform(0, TAU)

    rings = []
    for i in range(stations):
        t = i / (stations - 1)
        x = -length * 0.5 + length * t
        env = ss(0.0, 0.12, t) * ss(0.0, 0.12, 1.0 - t)
        y_bend = sagitta * (1.0 - (2.0 * t - 1.0) ** 2)
        top = height + env * (0.16 * math.sin(TAU * 2.3 * t + ph1) +
                              0.10 * math.sin(TAU * 4.1 * t + ph2) +
                              rnd.uniform(-0.05, 0.05))
        hw = hd + env * (0.07 * math.sin(TAU * 1.7 * t + ph3))
        prof = loaf_ring(hw, 0.0, top, dome=0.20 + env * 0.05, arc_n=4)
        rings.append([Vector((x, y_bend + p[0], p[1])) for p in prof])

    faces, vr = mb.loft(rings, HEDGE, cap_a=True, cap_b=True, smooth=False)
    mb.face_list(pick_faces(faces, lambda f: f.normal.z > 0.55), HEDGE_TIP)
    mb.face_list(pick_faces(faces, lambda f: f.normal.z <= 0.55 and
                            f.calc_center_median().z > height * 0.55), HEDGE_L)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.26), HEDGE_WOOD)

    # a handful of proud leaf lumps break up the loft's regularity into something clipped
    # by hand rather than extruded -- kept out of the fade zone near the ends
    n_lumps = max(2, int(length * 0.9))
    for i in range(n_lumps):
        t = lerp(0.16, 0.84, (i + 0.5) / n_lumps) + rnd.uniform(-0.03, 0.03)
        x = -length * 0.5 + length * t
        y_bend = sagitta * (1.0 - (2.0 * t - 1.0) ** 2)
        top = height + (0.16 * math.sin(TAU * 2.3 * t + ph1) +
                       0.10 * math.sin(TAU * 4.1 * t + ph2))
        r = rnd.uniform(0.16, 0.26)
        sphere(mb, HEDGE_TIP if rnd.random() > 0.4 else HEDGE_L,
               (x, y_bend + rnd.uniform(-hd * 0.3, hd * 0.3), top - r * 0.3),
               (r, r * 0.86, r * 0.72), seg=7, rings=4, smooth=True)
    return mb.finish(name, MAT, smooth_angle=32.0)


def tier_bowl(mb, base_z, stem_r, rim_out_r, rim_h, rim_thick, wall_in_r, basin_depth,
             floor_r, n=18, stone=STONE, stone_d=STONE_D, water=WATER):
    """One tier of the fountain: a stem flaring into an outer bowl, folding over the rim
    and back down an inner wall to a water-coloured floor cap.

    Built as two lofts that share their seam ring exactly so seal()'s weld fuses them
    into one shell: outer stem/bowl/rim (stone), then rim-top-shelf/inner-wall/floor
    (stone with a water cap). Returns the z of the rim top, so tiers and columns stack.
    """
    R1 = [(base_z, stem_r),
          (base_z + rim_h * 0.60, stem_r * 1.05),
          (base_z + rim_h * 0.92, rim_out_r * 0.94),
          (base_z + rim_h, rim_out_r),
          (base_z + rim_h + rim_thick, rim_out_r - rim_thick * 0.35)]
    mb.loft([ring_z(n, z, r, r, 2.2) for z, r in R1], stone, cap_a=True, cap_b=False,
           smooth=False)

    top_z = base_z + rim_h + rim_thick
    R2 = [(top_z, rim_out_r - rim_thick * 0.35),
          (top_z, wall_in_r),
          (top_z - basin_depth * 0.5, wall_in_r * 0.94),
          (top_z - basin_depth, floor_r)]
    faces2, _ = mb.loft([ring_z(n, z, r, r, 2.2) for z, r in R2], stone_d,
                        cap_a=False, cap_b=True, smooth=False)
    mb.face_list(faces2[:n], stone)               # the flat rim-top shelf
    if len(faces2) > 3 * n:
        mb.face_list([faces2[-1]], water)          # the basin floor reads as the water
    return top_z


# ============================================================================ 1/2. hedge

def hedge_arc():
    return hedge_wall("HedgeArc", 8.0, seed=301)


def hedge_arc_short():
    return hedge_wall("HedgeArcShort", 4.0, seed=302)


# ==================================================================== 3. the hedge pillar

def hedge_pillar():
    """A stone-and-topiary gatepost, 2.8 m, that caps a hedge run at a gap mouth."""
    mb = MB()
    # plinth: octagonal stone base with a bevelled top edge
    R = [(0.00, 0.46, 5.0), (0.06, 0.46, 4.6), (0.44, 0.44, 4.2), (0.50, 0.38, 3.8)]
    faces, _ = mb.loft([ring_z(10, z, r, r, e) for z, r, e in R], STONE, smooth=False)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.10), STONE_D)

    # shaft: a tapered octagonal column with two carved reveal bands
    S = [(0.50, 0.30, 4.4), (0.56, 0.29, 4.0), (1.10, 0.27, 4.0), (1.16, 0.245, 3.8),
         (1.22, 0.27, 4.0), (1.90, 0.24, 4.0), (1.96, 0.21, 3.6), (2.10, 0.205, 3.6)]
    faces2, _ = mb.loft([ring_z(10, z, r, r, e) for z, r, e in S], STONE_L, smooth=False)
    mb.face_list(pick_faces(faces2, lambda f: 1.08 < f.calc_center_median().z < 1.18 or
                            1.90 < f.calc_center_median().z < 2.0), STONE_JOINT)

    # finial collar, then the ball of clipped topiary that reads from every camera angle
    cyl(mb, STONE_D, (0, 0, 2.10), (0, 0, 2.20), 0.235, 0.27, n=10, e=3.4, smooth=False)
    sphere(mb, HEDGE, (0, 0, 2.55), (0.32, 0.32, 0.33), seg=10, rings=6, smooth=True)
    sphere(mb, HEDGE_TIP, (0, 0, 2.62), (0.30, 0.30, 0.26), seg=10, rings=5, smooth=True)
    return mb.finish("HedgePillar", MAT, smooth_angle=28.0)


# ======================================================================= 4. the ramp mound

def ramp_mound():
    """A drivable earth-and-timber ramp, origin at ground centre. 9 m across X, 7 m
    along Z, cresting at 1.5 m with zero slope at both approaches so no lip catches a
    wheel. Flat across X so the drivable lane never cross-slopes; timber retaining
    rails on both long edges do the work of reading 'built' instead of 'terrain'.
    """
    rnd = random.Random(401)
    mb = MB()
    HALF_X, HALF_Z, CREST = 4.5, 3.5, 1.5
    nx, nz = 7, 15

    def h(z):
        tz = 1.0 - min(1.0, abs(z) / HALF_Z)
        return CREST * ss(0.0, 1.0, tz)

    xs = [-HALF_X + HALF_X * 2.0 * i / (nx - 1) for i in range(nx)]
    zs = [-HALF_Z + HALF_Z * 2.0 * j / (nz - 1) for j in range(nz)]
    grid = [[Vector((x, z, h(z) + rnd.uniform(-0.01, 0.01))) for x in xs] for z in zs]

    # a position-keyed vertex cache: the perimeter skirt below reuses the SAME grid
    # points, and mb.v() mints a fresh bmesh vertex on every call, so without this the
    # top sheet and the skirt never actually share an edge -- forty isolated quads that
    # only LOOK welded (seal()'s remove_doubles cannot merge across islands that were
    # never asked to be the same vertex in the first place).
    vcache = {}
    def V(p):
        key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
        v = vcache.get(key)
        if v is None:
            v = mb.v(p)
            vcache[key] = v
        return v

    for j in range(nz - 1):
        for i in range(nx - 1):
            col = DIRT_L if grid[j][i].z + grid[j][i + 1].z > CREST * 1.1 else DIRT
            mb.f([V(grid[j][i]), V(grid[j][i + 1]),
                 V(grid[j + 1][i + 1]), V(grid[j + 1][i])], col)

    # perimeter skirt: one auto-oriented loft around the FULL boundary rather than two
    # hand-wound side walls. The long (X) edges genuinely drop to the ground at +/-4.6 m;
    # the short (Z) ends are already at height 0 (that is what "falls away" means), so
    # their "skirt" is a sliver pushed 0.1 m outward in Y -- zero visual footprint, but it
    # turns a boundary that used to be a degenerate straight line (which seal()'s
    # holes_fill could not close sanely) into a real, correctly-wound quad loop.
    perim = ([grid[0][i] for i in range(nx)] +
            [grid[j][nx - 1] for j in range(1, nz)] +
            [grid[nz - 1][i] for i in range(nx - 2, -1, -1)] +
            [grid[j][0] for j in range(nz - 2, 0, -1)])

    def outward(p):
        if abs(p.y + HALF_Z) < 1e-6:
            return Vector((p.x, p.y - 0.10, 0.0))
        if abs(p.y - HALF_Z) < 1e-6:
            return Vector((p.x, p.y + 0.10, 0.0))
        if p.x < 0:
            return Vector((p.x - 0.10, p.y, 0.0))
        return Vector((p.x + 0.10, p.y, 0.0))

    # perim traces the boundary CCW as seen from above (matches the top grid quads' own
    # CCW, +Z-normal winding). mb.loft's auto-orient decides a single flip for the WHOLE
    # skirt band from its own centroid and got this band wrong in both ring orders (the
    # long near-flat front/back slivers apparently swamp the heuristic), so the winding
    # is set by hand instead, verified analytically: for consecutive CCW points p0->p1
    # and their outward pair o0,o1, (p0, o0, o1, p1) is the outward-facing order.
    for p0, p1 in zip(perim, perim[1:] + perim[:1]):
        o0, o1 = outward(p0), outward(p1)
        mb.f([V(p0), V(o0), V(o1), V(p1)], DIRT_D)

    # timber retaining rails riding the crest profile along both long edges
    for i in (0, nx - 1):
        rx = xs[i] + (-0.18 if i == 0 else 0.18)
        pts = [Vector((rx, z, h(z) + 0.08)) for z in zs]
        tube(mb, WOOD, pts, 0.09, n=8, e=3.4, smooth=True)
        # a couple of cross braces per rail so it reads as built, not extruded
        for j in (nz // 4, nz // 2, 3 * nz // 4):
            z = zs[j]
            a = Vector((rx, z, h(z) + 0.03))
            b = Vector((xs[i] + (0.35 if i == 0 else -0.35), z, h(z) - 0.02))
            tube(mb, WOOD_D, [a, b], 0.045, n=6, smooth=False)
    # Deliberately open on the underside, exactly like rally props' garden_bed skirt: the
    # mound sits flush on the ground and nothing is ever below it to see. seal()'s
    # holes_fill would cap that invisible rim anyway, and its final recalc_face_normals
    # then mis-signs the whole closed result (confirmed empirically: this shape's global
    # winding comes out correct on its own -- verified analytically above -- and STAYS
    # correct with seal() skipped, but recalc_face_normals flips it negative every time
    # regardless of the input winding). The vertex cache above already welds every real
    # seam, so seal()'s remove_doubles is not needed either.
    return mb.finish("RampMound", MAT, smooth_angle=40.0, seal=False)


# ======================================================================= 5. the choke gate

def choke_gate():
    """A narrow garden archway, 5.5 m wide and 4 m tall, over a contested route.

    Timber posts, a wrought-iron arch bar with a lattice of drop pickets, and climbing
    roses scattered along the curve. Origin at ground centre, arch spanning local X.
    """
    rnd = random.Random(501)
    mb = MB()
    HALF_W, POST_H, RISE = 2.75, 2.70, 1.30

    # posts: tapered timber, flared base, collared top where the arch bar lands
    for sx in (-1, 1):
        x = sx * HALF_W
        P = [(0.00, 0.135, 5.0), (0.05, 0.115, 4.4), (0.90, 0.100, 4.2),
             (2.55, 0.095, 4.2), (2.62, 0.115, 3.8), (2.70, 0.105, 3.6)]
        faces, _ = mb.loft([ring_z(8, z, r, r, e, cx=x) for z, r, e in P], WOOD,
                           smooth=False)
        mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.10), WOOD_D)

    # arch bar: a sine-profile timber-and-iron beam from post top to post top
    n_arc = 14
    arc_pts = []
    for i in range(n_arc):
        t = i / (n_arc - 1)
        x = -HALF_W + 2 * HALF_W * t
        z = POST_H + RISE * math.sin(math.pi * t)
        arc_pts.append(Vector((x, 0.0, z)))
    tube(mb, WOOD, arc_pts, 0.075, n=8, e=3.0, smooth=True)
    inner_pts = [p + Vector((0, 0.02, -0.14)) for p in arc_pts]
    tube(mb, IRON, inner_pts, 0.028, n=6, smooth=True)

    # drop pickets: thin iron verticals hanging from the inner bar, a garden-gate lattice
    for i in range(1, n_arc - 1, 2):
        p = inner_pts[i]
        drop = min(0.55, (p.z - POST_H) * 0.9)
        if drop > 0.12:
            tube(mb, IRON_L, [p, p - Vector((0, 0, drop))], 0.012, n=5, smooth=False)

    # climbing roses: bloom/leaf clumps in twos and threes along the curve, big enough to
    # read as roses rather than scattered dots from the distance this stage is viewed at
    n_clusters = 8
    for i in range(n_clusters):
        t = lerp(0.05, 0.95, (i + 0.5) / n_clusters) + rnd.uniform(-0.02, 0.02)
        cx = -HALF_W + 2 * HALF_W * t
        cz = POST_H + RISE * math.sin(math.pi * t) + rnd.uniform(-0.18, 0.26)
        cy = rnd.uniform(-0.04, 0.08)
        for k in range(rnd.randint(2, 3)):
            x = cx + rnd.uniform(-0.14, 0.14)
            z = cz + rnd.uniform(-0.12, 0.12)
            y = cy + rnd.uniform(-0.05, 0.05)
            if rnd.random() < 0.6:
                r = rnd.uniform(0.09, 0.14)
                sphere(mb, ROSE_PINK if rnd.random() > 0.4 else ROSE_RED, (x, y, z),
                      (r, r * 0.85, r * 0.85), seg=6, rings=3, smooth=True)
            else:
                r = rnd.uniform(0.08, 0.13)
                sphere(mb, ROSE_LEAF if rnd.random() > 0.5 else ROSE_LEAF_D, (x, y, z),
                      (r, r * 0.75, r * 0.6), seg=6, rings=3, smooth=True)
    # a full spray up each post so the roses read as climbing FROM the ground
    for sx in (-1, 1):
        x = sx * HALF_W
        for i in range(5):
            z = 0.25 + i * 0.46 + rnd.uniform(-0.05, 0.05)
            r = rnd.uniform(0.08, 0.12)
            col = ROSE_LEAF if i % 3 else (ROSE_PINK if rnd.random() > 0.4 else ROSE_RED)
            sphere(mb, col, (x + sx * 0.11, rnd.uniform(-0.06, 0.06), z),
                  (r, r * 0.75, r * 0.65), seg=6, rings=3, smooth=True)
    return mb.finish("ChokeGate", MAT, smooth_angle=30.0)


# ==================================================================== 6. the plaza fountain

def plaza_fountain():
    """The hero prop: a tiered stone garden fountain, ~5 m across and 4.5 m tall.

    Bowl / column / bowl / column / a crown of stylised stone petals around a brass
    finial. Built to read from a low 40 m chase camera (a tall stacked silhouette that
    narrows as it rises) and from directly overhead (each tier is a distinct coloured
    ring, water blue at the centre of both basins).
    """
    mb = MB()
    # plinth: octagonal, the widest thing in the piece, anchors the silhouette from above
    PL = [(0.00, 1.20, 4.4), (0.08, 1.20, 4.2), (0.30, 1.10, 4.0), (0.34, 1.02, 3.8)]
    faces, _ = mb.loft([ring_z(12, z, r, r, e) for z, r, e in PL], STONE_D, smooth=False)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.06), STONE)

    top1 = tier_bowl(mb, base_z=0.34, stem_r=0.42, rim_out_r=2.45, rim_h=0.52,
                     rim_thick=0.12, wall_in_r=2.10, basin_depth=0.26, floor_r=1.85, n=22)
    cyl(mb, STONE_L, (0, 0, 0.34 + 0.52 + 0.12 - 0.30), (0, 0, top1 + 0.62),
       0.30, 0.21, n=12, e=3.6, smooth=False)
    torus(mb, STONE_JOINT, (0, 0, top1 + 0.30), (0, 0, 1), 0.245, 0.028, nmaj=14, nmin=6)

    top2 = tier_bowl(mb, base_z=top1 + 0.62, stem_r=0.21, rim_out_r=1.30, rim_h=0.30,
                     rim_thick=0.08, wall_in_r=1.08, basin_depth=0.15, floor_r=0.86, n=18)
    cyl(mb, STONE_L, (0, 0, top2 - 0.16), (0, 0, top2 + 0.55), 0.20, 0.135, n=10, e=3.6,
       smooth=False)

    # crown: eight stone petals fanning outward and up from a collar, plus a brass finial
    crown_base = top2 + 0.55
    cyl(mb, STONE_JOINT, (0, 0, crown_base - 0.05), (0, 0, crown_base + 0.05), 0.19, 0.16,
       n=12, e=3.4, smooth=False)
    for i in range(8):
        ang = TAU * i / 8.0
        d = Vector((math.cos(ang), math.sin(ang), 0.0))
        base = Vector((0, 0, crown_base)) + d * 0.10
        mid = base + d * 0.42 + Vector((0, 0, 0.42))
        tip = base + d * 0.62 + Vector((0, 0, 0.98))
        tube(mb, STONE_L, [base, mid, tip], [0.20, 0.15, 0.02], n=8, e=3.0, smooth=True)
    cyl(mb, STONE_L, (0, 0, crown_base), (0, 0, crown_base + 0.80), 0.10, 0.02, n=10,
       smooth=True)
    sphere(mb, BRASS, (0, 0, crown_base + 0.90), (0.09, 0.09, 0.10), seg=10, rings=6,
          smooth=True)
    return mb.finish("PlazaFountain", MAT, smooth_angle=26.0)


# ======================================================================== 7. the crown ring

def crown_ring():
    """The plaza's low stone kerb: outer radius 13 m, inner 12.2 m, ~0.35 m tall.

    A rectangular-bevelled cross-section swept as a closed loop around the mid radius,
    tinted in alternating segments so the single loft still reads as laid stone blocks.
    """
    mb = MB()
    mid_r, half_w, half_h = 12.6, 0.40, 0.175
    n_path = 72
    pts = [Vector((mid_r * math.cos(TAU * i / n_path), mid_r * math.sin(TAU * i / n_path),
                  half_h)) for i in range(n_path)]
    faces = tube(mb, STONE, pts, half_w, n=8, e=4.2, smooth=False, loop=True,
                squash=[(1.0, half_h / half_w)] * n_path)

    n_seg = 36
    for f in faces:
        c = f.calc_center_median()
        idx = int(((math.atan2(c.y, c.x) / TAU) + 0.5) * n_seg) % n_seg
        if idx % 6 == 0:
            f[mb.lay] = mb.cid(STONE_JOINT)
        elif idx % 2 == 0:
            f[mb.lay] = mb.cid(STONE_D)
    return mb.finish("CrownRing", MAT, smooth_angle=20.0)


# ======================================================================== 8. the bloom post

def bloom_post():
    """A slim 3.2 m marker post: lantern housing plus a small pennant. Origin at base."""
    mb = MB()
    P = [(0.00, 0.075, 5.0), (0.05, 0.062, 4.4), (2.35, 0.052, 4.0), (2.42, 0.062, 3.8),
         (2.50, 0.052, 3.8)]
    faces, _ = mb.loft([ring_z(8, z, r, r, e) for z, r, e in P], WOOD, smooth=False)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.10), WOOD_D)

    # lantern housing: a small glazed box under a pointed roof
    rbox(mb, IRON_L, (0, 0, 2.72), (0.30, 0.30, 0.34), r=0.03, n=8, k=1)
    for ry in (-1, 1):
        rbox(mb, GLASS_WARM, (0, ry * 0.135, 2.72), (0.20, 0.03, 0.24), r=0.01, n=6, k=1)
    for rx in (-1, 1):
        rbox(mb, GLASS_WARM, (rx * 0.135, 0, 2.72), (0.03, 0.20, 0.24), r=0.01, n=6, k=1)
    cyl(mb, IRON, (0, 0, 2.90), (0, 0, 3.16), 0.235, 0.02, n=8, e=3.4, smooth=False)
    sphere(mb, BRASS, (0, 0, 3.19), (0.045, 0.045, 0.05), seg=8, rings=4, smooth=True)

    # pennant: a small triangular slab hanging off the shaft below the lantern
    top, bot = [(0.0, 0.0, 2.55), (0.34, 0.05, 2.42), (0.30, -0.02, 2.28)], None
    a, b, c = (mb.v(p) for p in top)
    fA = mb.f((a, b, c), BRASS)
    fB = mb.f((c, b, a), STONE_JOINT)
    for f in (fA, fB):
        if f:
            f.smooth = False
    return mb.finish("BloomPost", MAT, smooth_angle=26.0)


# ==================================================================== 9. the trophy planter

def trophy_planter():
    """A 2 m ornamental planter urn for dressing the plaza and gate mouths."""
    rnd = random.Random(901)
    mb = MB()
    U = [(0.00, 0.36, 4.4), (0.06, 0.34, 4.0), (0.28, 0.28, 3.6), (0.62, 0.24, 3.6),
         (1.10, 0.40, 3.4), (1.46, 0.50, 3.2), (1.62, 0.545, 3.0), (1.72, 0.50, 2.8),
         (1.80, 0.53, 3.0)]
    faces, _ = mb.loft([ring_z(14, z, r, r, e) for z, r, e in U], STONE, smooth=False)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z < 0.10), STONE_D)
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z > 1.55), STONE_L)
    disc(mb, DIRT_D, (0, 0, 1.79), (0, 0, 1), 0.48, n=14, smooth=False)

    for i in range(5):
        ang = TAU * i / 5.0 + rnd.uniform(-0.2, 0.2)
        rr = rnd.uniform(0.10, 0.30)
        x, y = math.cos(ang) * rr, math.sin(ang) * rr
        z = 1.85 + rnd.uniform(0.0, 0.10)
        r = rnd.uniform(0.09, 0.14)
        col = (ROSE_PINK, ROSE_RED, ROSE_LEAF)[i % 3]
        sphere(mb, col, (x, y, z), (r, r * 0.85, r * (1.3 if col == ROSE_LEAF else 0.8)),
              seg=7, rings=4, smooth=True)
    return mb.finish("TrophyPlanter", MAT, smooth_angle=28.0)


# ============================================================================== assemble

def main():
    fresh_scene()
    objs = [hedge_arc(), hedge_arc_short(), hedge_pillar(), ramp_mound(), choke_gate(),
            plaza_fountain(), crown_ring(), bloom_post(), trophy_planter()]

    bpy.context.view_layer.update()
    for ob in objs:
        gsize = max(20.0, ob.dimensions.x + 4.0, ob.dimensions.y + 4.0)
        bake_ao([ob], floor=0.72, dist=0.35, samples=24, ground=True, ground_size=gsize)

    verify_meshes(objs, "TurfProps")
    total = report_tris(objs, "TurfProps")

    print("BOUNDS (metres, Blender Z-up; pivot should sit on the ground plane)")
    for o in objs:
        lo = Vector((min(v.co.x for v in o.data.vertices),
                     min(v.co.y for v in o.data.vertices),
                     min(v.co.z for v in o.data.vertices)))
        hi = Vector((max(v.co.x for v in o.data.vertices),
                     max(v.co.y for v in o.data.vertices),
                     max(v.co.z for v in o.data.vertices)))
        print("  %-16s size %6.3f x %6.3f x %6.3f   z %+.3f .. %+.3f   tris %5d"
              % (o.name, hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.z + o.location.z,
                 hi.z + o.location.z, tri_count(o)))
    print("TOTAL %d triangles" % total)

    layout = []
    for i, ob in enumerate(objs):
        layout.append((ob, ((i % 3) * 16.0, (i // 3) * 16.0, 0.0)))

    export_fbx(objs, os.path.join(L.MODELS_DIR, "TurfProps.fbx"))
    for ob, p in layout:
        ob.location = Vector(ob.location) + Vector(p)
    if "--preview" in sys.argv:
        render_gallery(objs, "turf", res=340, cols=3)
        render_gallery(objs, "turf", res=340, cols=3, sil=True)
    print("DONE TurfProps")


main()
