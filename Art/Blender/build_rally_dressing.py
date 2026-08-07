# build_rally_dressing.py — DUCK MOW: the things that stop 92 m of lawn reading as nothing.
#
# Run: blender --background --python C:\Duck\Art\Blender\build_rally_dressing.py
#      (add --preview to also render the gallery + silhouette sheets)
#
# Twelve objects in two families:
#
#   GROUND COVER (GrassTuft_A/B/C, CloverPatch_A/B, Daisies_A/B) — scattered by the
#   hundred across the mowable field.  These are the whole reason this file exists: an
#   arena floor that is one flat green disc has no scale cue in it, so the goose reads as
#   sliding rather than travelling.  A tuft every few metres gives the eye something to
#   measure speed against.  Every one of them is under ~230 triangles and none of them
#   stands high enough to hide the mower.
#
#   ARENA DRESSING (Molehill, Rock_A/B, WaterTrough, HayBale, Bunting_Span, Floodlight) —
#   placed by hand, a handful each, to break the empty ground and the empty sky.
#
# THE TWO RULES THAT SHAPED IT.
#
#   1. CHUNKY, NOT FINE.  These are seen from a 25 m chase camera and a 90 m overhead.  A
#      grass blade 8 mm wide is a sub-pixel shimmer and costs the same as one 60 mm wide
#      that actually reads.  Every blade here is a swept THREE-SIDED prism with a ridge
#      along its spine — flat-bottomed, apex up — so it has a lit face and a shaded face
#      no matter which way the sun is.  Six extruded planes would be cheaper and would
#      vanish edge-on; a cylinder would have no ridge and read as a wire.
#
#   2. THE COLOUR IS THE TEXTURE.  Duck/Prop has no albedo map: vertex colour IS albedo.
#      So every object gets THREE separate sources of value range baked into "Col" —
#      per-face palette at build time, a continuous height grade (grade()), and raycast
#      AO (bake_ao).  An object authored in one flat hex arrives in Unity as a silhouette
#      full of nothing, which is exactly the failure this file is fixing.
#
# Conventions, identical to build_rally_props.py: colour in a CORNER BYTE_COLOR attribute
# named "Col", one material M_RallyProps so Unity reuses the existing one, pivots at the
# ground contact point, metres, exported Y-up.

import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, fresh_scene, bake_ao, report_tris, render_gallery, get_mat,
                      set_pivot, export_fbx, ring_z, sect, frame, cyl, tube, sphere,
                      rbox, disc, recess, pick_faces, TAU, tint, hexcol)

MAT = "M_RallyProps"

# ------------------------------------------------------------------------------ colours
# Grass tones come straight from the bible's grass table; the standing tufts are UNCUT
# grass, so they must sit darker than the cut lawn they are scattered on or they read as
# bald patches from the overhead reveal.
G_ROOT  = "24512A"      # cut_edge — the darkest green in the bible, used at the crown
G_BASE  = "2F6B33"      # grass_uncut_base
G_TIP   = "4E9440"      # grass_uncut_tip
G_DRY   = "7E9B45"      # a sun-bleached blade: the only tone that is not in the table,
                        # mixed from uncut_tip toward cut_tip so it stays in the family
LEAF    = "3E7A42"      # clover leaf body
LEAF_L  = "5C9B4C"      # clover leaf, lit
SEED    = "C4A85C"      # seed head, between brass and straw
STRAW   = "D9BE7E"      # hay
STRAW_D = "A98D52"
SOIL    = "6B5233"      # turned earth
SOIL_D  = "43331F"      # the inside of a molehill
STONE   = "9A8C72"      # warm dun stone (NOT grey — the bible bans grey outright)
STONE_D = "6E6350"
STONE_L = "B5A98E"
LICHEN  = "8FA46B"
WOOD    = "9A6B41"
WOOD_D  = "6E4A2C"
IRON    = "3A2E26"      # dark warm iron, not black
WHITE   = "F1EDE0"
CREAM   = "F5EAD6"
RED     = "D8534E"
RED_D   = "A32E2D"
YELLOW  = "E8B84B"
BLUE    = "3E86A8"
WATER   = "3E86A8"
WATER_L = "68B0C4"
CHALK   = "F7F3E4"
BRASS   = "C9A55A"
LAMP    = "FFF3D0"      # sun disc / bloom — a lit floodlight lens is the same warm white


def mixhex(a, b, t):
    """Blend two palette hexes in sRGB. tint() can only scale; this can move hue."""
    a = L.P.get(a, a).lstrip("#")
    b = L.P.get(b, b).lstrip("#")
    out = ""
    for i in (0, 2, 4):
        va, vb = int(a[i:i + 2], 16), int(b[i:i + 2], 16)
        out += "%02X" % max(0, min(255, int(round(va + (vb - va) * t))))
    return out


def ss(a, b, x):
    if b - a < 1e-9:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


# ------------------------------------------------------------------------ colour grading
def grade(ob, z0, z1, lo=0.74, hi=1.18, axis='Z'):
    """Multiply a continuous value ramp along an axis into the Col attribute.

    Per-face palette gives you steps; this gives you the ramp between them, and it is
    what turns a tuft from 'green shape' into 'grass catching the light at the tips'.
    Runs BEFORE bake_ao so the AO term darkens the graded colour rather than being
    flattened by it.  Clamped, because BYTE_COLOR cannot hold >1.
    """
    me = ob.data
    cols = me.color_attributes.get("Col")
    if not cols:
        return
    i = {'X': 0, 'Y': 1, 'Z': 2}[axis]
    span = max(1e-6, z1 - z0)
    for poly in me.polygons:
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co[i]
            t = max(0.0, min(1.0, (v - z0) / span))
            m = lo + (hi - lo) * (t * t * (3.0 - 2.0 * t))
            c = cols.data[li].color
            cols.data[li].color = (min(1.0, c[0] * m), min(1.0, c[1] * m),
                                   min(1.0, c[2] * m), c[3])


# ============================================================== the grass blade primitive

def blade(mb, base, ang, h, w, bend, cols, stations=4, curl=0.16, thick=0.34,
          twist=0.0):
    """One blade of grass: a three-sided prism swept along an arc, ridge up, tip pinched.

    Section is sect(3, w, w*thick, rot=90deg) — a flat-bottomed triangle with its apex on
    top.  That single choice is doing three jobs: it is WIDE (1.73*w across) so it reads
    from 30 m, it has a SPINE so a lit blade and a shaded blade are different values at
    any sun angle, and it costs 3 quads per band instead of the 4 a diamond would.

    The bands are lofted one at a time so each can carry its own colour — root, body, tip
    — which is where most of the object's value range comes from.  seal()'s 0.1 mm weld
    fuses the shared rings back into one shell afterwards.
    """
    base = Vector(base)
    side = Vector((-math.sin(ang), math.cos(ang), 0.0))
    lean = Vector((math.cos(ang), math.sin(ang), 0.0))

    pts, radii = [], []
    for i in range(stations):
        t = i / (stations - 1.0)
        lat = bend * (t ** 1.65)
        z = h * (t - curl * t * t)
        pts.append(base + lean * lat + Vector((0, 0, z)))
        radii.append(max(0.0026, w * (1.0 - t) ** 0.75))

    rings = []
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]
        b = pts[min(stations - 1, i + 1)]
        tan = (b - a)
        tan = tan.normalized() if tan.length > 1e-7 else Vector((0, 0, 1))
        # width axis is horizontal and square to the lean, so the blade arches in its own
        # plane instead of corkscrewing; thickness axis follows from it.
        sd = side.copy()
        if twist:
            sd = (Matrix.Rotation(twist * (i / (stations - 1.0)), 3, tan) @ sd)
        sd = (sd - tan * sd.dot(tan))
        sd = sd.normalized() if sd.length > 1e-6 else Vector((1, 0, 0))
        up = tan.cross(sd).normalized()
        r = radii[i]
        rings.append(frame(sect(3, r, r * thick, 2.0, math.pi * 0.5), p, sd, up))

    nb = stations - 1
    for i in range(nb):
        c = cols[min(len(cols) - 1, int(i * len(cols) / nb))]
        mb.loft(rings[i:i + 2], c, cap_a=(i == 0), cap_b=(i == nb - 1), smooth=False)
    return pts[-1]


def crown(mb, r, col, n=6, h=0.030, seed=0):
    """The clod of root and thatch a tuft grows out of.

    Without it a tuft is a fan of blades whose bases float a few millimetres above a lawn
    that is never perfectly flat, and the gap is visible from the chase camera as a
    hovering shadow.  n-gon, jittered, dark.
    """
    rnd = random.Random(seed)
    lo, hi = [], []
    for i in range(n):
        a = TAU * i / n + rnd.uniform(-0.10, 0.10)
        rr = r * rnd.uniform(0.82, 1.15)
        lo.append(Vector((math.cos(a) * rr, math.sin(a) * rr, 0.0)))
        hi.append(Vector((math.cos(a) * rr * 0.74, math.sin(a) * rr * 0.74,
                          h * rnd.uniform(0.7, 1.0))))
    mb.loft([lo, hi], col, cap_a=True, cap_b=True, smooth=False)


def seed_head(mb, tip, tan, length, r, col_a, col_b, n=5):
    """A spindle of seed on the end of a stem — a pointed loft, not a sphere.

    A sphere on a stick is a lollipop and reads as a dandelion clock; timothy grass is a
    long tapered spike, and that vertical accent is the only thing that separates
    GrassTuft_C's silhouette from GrassTuft_A's at range.
    """
    tan = Vector(tan).normalized()
    up = Vector((0, 0, 1))
    if abs(tan.dot(up)) > 0.95:
        up = Vector((0, 1, 0))
    rt = tan.cross(up).normalized()
    up = rt.cross(tan).normalized()
    rings = []
    for t in (0.16, 0.42, 0.72):
        rings.append(frame(sect(n, r * math.sin(t * math.pi) ** 0.55,
                                r * math.sin(t * math.pi) ** 0.55, 2.0),
                           Vector(tip) + tan * (length * t), rt, up))
    mb.pole_loft(rings, col_a, pole_a=Vector(tip) - tan * length * 0.06,
                 pole_b=Vector(tip) + tan * length, smooth=False)


# ================================================================== 1. the grass tufts

def grass_tuft_a():
    """UPRIGHT.  Narrow, tall, near-vertical — the default tuft, and the one that has to
    look like it is standing up out of the lawn rather than lying on it."""
    mb = MB()
    rnd = random.Random(11)
    crown(mb, 0.115, G_ROOT, n=6, h=0.028, seed=1)
    for k in range(6):                                   # six long blades, 4 stations
        a = TAU * k / 6 + rnd.uniform(-0.32, 0.32)
        rr = rnd.uniform(0.03, 0.08)
        h = rnd.uniform(0.44, 0.58)
        dry = rnd.random() < 0.28
        cols = [G_ROOT,
                mixhex(G_BASE, G_TIP, rnd.uniform(0.15, 0.45)),
                mixhex(G_TIP, G_DRY if dry else G_TIP, 0.7) if dry else G_TIP]
        blade(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.012), a, h,
              rnd.uniform(0.030, 0.040), rnd.uniform(0.10, 0.19), cols,
              stations=4, curl=rnd.uniform(0.10, 0.22), twist=rnd.uniform(-0.5, 0.5))
    for k in range(4):                                   # four short fillers, 3 stations
        a = TAU * k / 4 + 0.7 + rnd.uniform(-0.4, 0.4)
        rr = rnd.uniform(0.055, 0.10)
        blade(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.010), a,
              rnd.uniform(0.20, 0.31), rnd.uniform(0.026, 0.034),
              rnd.uniform(0.10, 0.19), [G_ROOT, G_BASE, G_TIP], stations=3,
              curl=rnd.uniform(0.14, 0.26))
    ob = mb.finish("GrassTuft_A", MAT, smooth_angle=22.0)
    grade(ob, 0.0, 0.52, lo=0.70, hi=1.20)
    return ob


def grass_tuft_b():
    """SPLAYED.  A fountain — long blades thrown well out sideways and arched over, so it
    is wide and low.  Same palette as A, deliberately opposite proportion: scattering two
    tufts of the same silhouette at different scales is the 'repeated identical prop' the
    bible rejects, and rotation alone does not hide it."""
    mb = MB()
    rnd = random.Random(23)
    crown(mb, 0.135, G_ROOT, n=6, h=0.024, seed=2)
    for k in range(4):                                   # long arcs, 5 stations
        a = TAU * k / 4 + rnd.uniform(-0.30, 0.30)
        rr = rnd.uniform(0.02, 0.05)
        blade(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.010), a,
              rnd.uniform(0.46, 0.56), rnd.uniform(0.034, 0.044),
              rnd.uniform(0.26, 0.36), [G_ROOT, G_BASE,
                                        mixhex(G_TIP, G_DRY, rnd.uniform(0.0, 0.5)),
                                        G_TIP],
              stations=5, curl=rnd.uniform(0.20, 0.30), twist=rnd.uniform(-0.7, 0.7))
    for k in range(5):                                   # mid fillers
        a = TAU * k / 5 + 0.5 + rnd.uniform(-0.35, 0.35)
        rr = rnd.uniform(0.03, 0.09)
        blade(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.008), a,
              rnd.uniform(0.22, 0.34), rnd.uniform(0.030, 0.040),
              rnd.uniform(0.16, 0.28), [G_ROOT, G_BASE, G_TIP], stations=3,
              curl=rnd.uniform(0.22, 0.36))
    ob = mb.finish("GrassTuft_B", MAT, smooth_angle=22.0)
    grade(ob, 0.0, 0.34, lo=0.72, hi=1.18)
    return ob


def grass_tuft_c():
    """SEEDED.  Fewer blades, two tall seed spikes standing clear above them.  This is the
    accent tuft — one in six or so — and it is the only one with a vertical line in it, so
    it is what stops a scattered field reading as one repeated blob."""
    mb = MB()
    rnd = random.Random(37)
    crown(mb, 0.105, G_ROOT, n=6, h=0.026, seed=3)
    for k in range(6):
        a = TAU * k / 6 + rnd.uniform(-0.35, 0.35)
        rr = rnd.uniform(0.015, 0.06)
        blade(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.010), a,
              rnd.uniform(0.22, 0.36), rnd.uniform(0.028, 0.038),
              rnd.uniform(0.10, 0.22), [G_ROOT, G_BASE, G_TIP], stations=3,
              curl=rnd.uniform(0.18, 0.30))
    for k, (a, h) in enumerate(((0.6, 0.44), (3.5, 0.37))):
        lean = rnd.uniform(0.06, 0.12)
        p0 = Vector((math.cos(a) * 0.03, math.sin(a) * 0.03, 0.010))
        p1 = p0 + Vector((math.cos(a) * lean * 0.35, math.sin(a) * lean * 0.35, h * 0.55))
        p2 = p0 + Vector((math.cos(a) * lean, math.sin(a) * lean, h))
        tube(mb, mixhex(G_BASE, G_DRY, 0.45), [p0, p1, p2], [0.011, 0.008, 0.006],
             n=3, e=2.0, smooth=False)
        seed_head(mb, p2, (p2 - p1), 0.135 + 0.02 * k, 0.026,
                  SEED, tint(SEED, 0.8), n=5)
    ob = mb.finish("GrassTuft_C", MAT, smooth_angle=22.0)
    grade(ob, 0.0, 0.62, lo=0.72, hi=1.20)
    return ob


# =============================================================== 2. clover and daisies

def clover_leaf(mb, ctr, r, col, tilt=0.0, spin=0.0, lobes=3, thick=0.010, inner=0.70):
    """A trefoil as ONE lobed slab.  Three separate leaflets cost three times the tris and
    at the 20 pixels this ever occupies they read as the same outline with more noise.

    `inner` is the waist between lobes and it wants to be HIGH.  At 0.42 the outline is a
    three-pointed star, and a star on a stalk 60 mm off the ground is a parasol, not a
    leaf -- that is exactly what the first preview showed.  At 0.70 it is a round leaf
    with three soft bumps, which is what clover reads as from any distance that matters.
    """
    c = Vector(ctr)
    R = Matrix.Rotation(tilt, 3, 'Y') @ Matrix.Rotation(spin, 3, 'Z')
    pts = []
    for i in range(lobes * 2):
        a = TAU * i / (lobes * 2)
        rr = r if i % 2 == 0 else r * inner
        pts.append(c + R @ Vector((math.cos(a) * rr, math.sin(a) * rr, 0.0)))
    mb.slab(pts, col, R @ Vector((0, 0, -thick)), col_bot=tint(col, 0.72), smooth=False)


def _clover(name, seed, n_leaf, spread, flowers):
    """NO GROUND PAD.  The first version had one -- a low seven-sided slab under the whole
    patch -- and the silhouette test killed it instantly: a solid dark hexagon sitting on a
    lawn is a hole, not a plant, and at 30 m that is all you saw.  The patch is now nothing
    but overlapping leaves at three different heights, so its outline is scalloped and its
    interior has gaps of lawn showing through.  Two small thatch clumps under the densest
    clusters do the bedding job the pad was supposed to do.
    """
    mb = MB()
    rnd = random.Random(seed)
    for k in range(2):
        a = TAU * k / 2 + 0.6
        rr = spread * 0.36
        pad = []
        for i in range(6):
            b = TAU * i / 6
            q = spread * rnd.uniform(0.26, 0.38)
            pad.append(Vector((math.cos(a) * rr + math.cos(b) * q,
                               math.sin(a) * rr + math.sin(b) * q, 0.0)))
        mb.slab(pad, tint(LEAF, 0.66), (0, 0, 0.010), col_bot=tint(LEAF, 0.55),
                smooth=False)
    for k in range(n_leaf):
        a = TAU * k / n_leaf + rnd.uniform(-0.4, 0.4)
        rr = spread * rnd.uniform(0.18, 0.92)
        z = rnd.uniform(0.012, 0.046)          # LOW.  Clover is a mat, not an understorey
        col = mixhex(LEAF, LEAF_L, rnd.uniform(0.0, 1.0))
        clover_leaf(mb, (math.cos(a) * rr, math.sin(a) * rr, z),
                    rnd.uniform(0.078, 0.112), col,
                    tilt=rnd.uniform(-0.20, 0.20), spin=rnd.uniform(0, TAU),
                    thick=0.011)
        if k % 4 == 1:      # a stalk on one leaf in four is enough to say 'growing'
            cyl(mb, tint(LEAF, 0.72), (math.cos(a) * rr, math.sin(a) * rr, 0.003),
                (math.cos(a) * rr, math.sin(a) * rr, z - 0.003), 0.008, 0.006, n=3,
                smooth=False)
    for k in range(flowers):
        a = rnd.uniform(0, TAU)
        rr = spread * rnd.uniform(0.2, 0.7)
        x, y = math.cos(a) * rr, math.sin(a) * rr
        cyl(mb, tint(LEAF, 0.72), (x, y, 0.010), (x, y, 0.070), 0.008, 0.006, n=3,
            smooth=False)
        sphere(mb, CREAM, (x, y, 0.086), (0.034, 0.034, 0.030), seg=5, rings=3,
               smooth=False)
    ob = mb.finish(name, MAT, smooth_angle=24.0)
    grade(ob, 0.0, 0.11, lo=0.70, hi=1.18)
    return ob


def clover_patch_a():
    """Ground-hugging leaf mat, 0.9 m across.  Nothing above 120 mm: this dresses the
    lawn the mower drives straight over, so it must never look like an obstacle."""
    return _clover("CloverPatch_A", 51, 10, 0.42, 2)


def clover_patch_b():
    """The same mat, smaller and denser, with three flower heads instead of two."""
    return _clover("CloverPatch_B", 67, 9, 0.33, 3)


def _daisies(name, seed, n, spread, petal_col, heart_col):
    """Flower heads are DOMED, not flat.

    The first pass built each head as a flat lobed slab and it read, from the only camera
    angles this game has, as a hat on a stick -- a disc seen at 20 degrees is a line.  A
    head is now a lobed ring fanned to a raised centre and a dropped underside (a lobed
    bicone), which costs FEWER triangles than the slab did and has a profile from every
    angle.  The petal tips also sit lower than the centre, which is what a daisy does.
    """
    mb = MB()
    rnd = random.Random(seed)
    # base rosette of leaves — a flower without one is a wire stuck in a lawn.  No ground
    # pad here either; see the note on _clover().
    for k in range(6):
        a = TAU * k / 6 + 0.4
        rr = spread * rnd.uniform(0.30, 0.62)
        clover_leaf(mb, (math.cos(a) * rr, math.sin(a) * rr, 0.014),
                    rnd.uniform(0.062, 0.086), mixhex(LEAF, LEAF_L, rnd.uniform(0, 0.7)),
                    tilt=0.12, spin=a, lobes=3, thick=0.010, inner=0.72)
    for k in range(n):
        a = TAU * k / n + rnd.uniform(-0.5, 0.5)
        rr = spread * rnd.uniform(0.15, 0.80)
        h = rnd.uniform(0.15, 0.215)
        x, y = math.cos(a) * rr, math.sin(a) * rr
        tipx = x + rnd.uniform(-0.03, 0.03)
        tipy = y + rnd.uniform(-0.03, 0.03)
        tube(mb, mixhex(LEAF, LEAF_L, 0.25),
             [(x, y, 0.008), ((x + tipx) * 0.5, (y + tipy) * 0.5, h * 0.55),
              (tipx, tipy, h)], [0.008, 0.007, 0.006], n=3, e=2.0, smooth=False)
        tilt = rnd.uniform(0.16, 0.40)
        spin = rnd.uniform(0, TAU)
        R = Matrix.Rotation(tilt, 3, 'Y') @ Matrix.Rotation(spin, 3, 'Z')
        up = R @ Vector((0, 0, 1))
        c = Vector((tipx, tipy, h + 0.008))
        ring = []
        for i in range(10):
            aa = TAU * i / 10
            q = 0.062 if i % 2 == 0 else 0.030
            drop = 0.008 if i % 2 == 0 else 0.002      # petal tips droop a little
            ring.append(c + R @ Vector((math.cos(aa) * q, math.sin(aa) * q, -drop)))
        # The dome is SHALLOW.  At pole_a = 16 mm over a 52 mm ring the head was a cone
        # and every daisy read as a sun hat; 5 mm over a 62 mm ring is a flower that still
        # has a profile from the chase camera.
        mb.pole_loft([ring], petal_col, pole_a=c + up * 0.005,
                     pole_b=c - up * 0.012, smooth=False)
        # the heart: a squat button, wide enough to read as the centre of a flower rather
        # than a crown on top of one
        sphere(mb, heart_col, c + up * 0.004, (0.024, 0.024, 0.013), seg=6, rings=3,
               smooth=False)
    ob = mb.finish(name, MAT, smooth_angle=26.0)
    grade(ob, 0.0, 0.26, lo=0.72, hi=1.16)
    return ob


def daisies_a():
    """Five white daisies on a leaf rosette, 0.25 m.  White heads on a green field are the
    highest-contrast thing this file makes — used sparingly, they read as sparkle."""
    return _daisies("Daisies_A", 83, 5, 0.20, CHALK, YELLOW)


def daisies_b():
    """Buttercups: the same build, yellow petals and a DEEP ORANGE heart.

    The first pass gave this one a paler-yellow heart and the two tones collapsed into one
    blob under bloom.  Contrast in a 40-pixel flower has to come from value, not hue."""
    return _daisies("Daisies_B", 97, 4, 0.17, YELLOW, "D3792A")


# ================================================================= 3. arena dressing

def molehill():
    """Turned earth, 0.70 m across, 180 mm tall — with the hole actually cut into it.

    A smooth dome is a lump of mud.  What says 'mole' is (a) the summit being a small dark
    crater rather than a peak, and (b) the loose clods thrown clear of the base, so the
    mound has a scatter of debris around it instead of a clean waterline against the lawn.
    """
    mb = MB()
    rnd = random.Random(5)
    R, H = 0.35, 0.175
    rings = []
    for (t, rs, zs) in ((0.00, 1.00, 0.00), (0.42, 0.74, 0.42), (0.72, 0.44, 0.74),
                        (1.00, 0.20, 1.00)):
        rg = []
        for i in range(10):
            a = TAU * i / 10
            rr = R * rs * (1.0 + 0.17 * math.sin(a * 3.0 + t * 2.0)) * \
                rnd.uniform(0.88, 1.12)
            rg.append(Vector((math.cos(a) * rr, math.sin(a) * rr,
                              H * zs * rnd.uniform(0.86, 1.14))))
        rings.append(rg)
    faces, _ = mb.loft(rings, SOIL, cap_a=True, cap_b=True, smooth=False)
    # the crown is darker, freshly turned; the skirt is drier
    mb.face_list(pick_faces(faces, lambda f: f.calc_center_median().z > H * 0.62),
                 tint(SOIL, 0.86))
    # the summit cap only -- see the note in water_trough(); the band under it also has
    # normal.z > 0.85 and including it turns the crater into a raised chimney
    top = pick_faces(faces, lambda f: len(f.verts) > 6 and
                     f.calc_center_median().z > H * 0.90)
    if top:
        inner = recess(mb, top, 0.020, -0.055, col=SOIL_D)
        recess(mb, inner, 0.016, -0.030, col=tint(SOIL_D, 0.55))
    for k in range(4):                       # clods, thrown clear
        a = rnd.uniform(0, TAU)
        rr = R * rnd.uniform(1.02, 1.30)
        s = rnd.uniform(0.030, 0.055)
        sphere(mb, mixhex(SOIL, SOIL_D, rnd.uniform(0.0, 0.6)),
               (math.cos(a) * rr, math.sin(a) * rr, s * 0.52),
               (s, s * rnd.uniform(0.7, 1.1), s * 0.62), seg=5, rings=3, smooth=False)
    ob = mb.finish("Molehill", MAT, smooth_angle=20.0)
    grade(ob, 0.0, H, lo=0.80, hi=1.14)
    return ob


def _rock(name, seed, R, H, lean, lichen_col=LICHEN, pebbles=0):
    """A faceted glacial erratic: four jittered rings to a pole, hard-shaded.

    Deliberately NOT a subdivided sphere.  The rings are offset sideways as they rise
    (`lean`) and each vertex is pushed in or out, so the silhouette has real corners and
    one dominant face that catches the key light.  Under 20-degree auto-smooth every one
    of those planes stays a plane.
    """
    mb = MB()
    rnd = random.Random(seed)
    rings = []
    prof = ((0.00, 1.00), (0.34, 1.06), (0.68, 0.82), (0.90, 0.46))
    for (t, rs) in prof:
        off = Vector((math.cos(lean) * t * R * 0.22, math.sin(lean) * t * R * 0.22, 0))
        rg = []
        for i in range(8):
            a = TAU * i / 8
            rr = R * rs * rnd.uniform(0.80, 1.18)
            zz = H * t * rnd.uniform(0.93, 1.07)
            rg.append(Vector((math.cos(a) * rr, math.sin(a) * rr, zz)) + off)
        rings.append(rg)
    faces, vr = mb.pole_loft(rings, STONE,
                             pole_b=Vector((math.cos(lean) * R * 0.24,
                                            math.sin(lean) * R * 0.24, H)),
                             smooth=False)
    # base cap, so the rock is a closed solid rather than an open shell on the ground
    mb.f(list(reversed(vr[0])), STONE_D)
    mb.upd()
    for f in faces:
        if not f.is_valid:
            continue
        c = f.calc_center_median()
        n = f.normal
        if n.z > 0.55 and c.z > H * 0.45:
            f[mb.lay] = mb.cid(lichen_col if rnd.random() < 0.45 else STONE_L)
        elif c.z < H * 0.28:
            f[mb.lay] = mb.cid(STONE_D)
    for k in range(pebbles):
        a = rnd.uniform(0, TAU)
        rr = R * rnd.uniform(1.05, 1.5)
        s = R * rnd.uniform(0.16, 0.26)
        sphere(mb, mixhex(STONE, STONE_D, rnd.uniform(0.2, 0.8)),
               (math.cos(a) * rr, math.sin(a) * rr, s * 0.45),
               (s, s * 0.82, s * 0.55), seg=5, rings=3, smooth=False)
    ob = mb.finish(name, MAT, smooth_angle=18.0)
    grade(ob, 0.0, H, lo=0.78, hi=1.16)
    return ob


def rock_a():
    """The big one, 0.70 m across, leaning, lichen on the sunward shoulder."""
    return _rock("Rock_A", 13, 0.33, 0.42, 0.9, pebbles=2)


def rock_b():
    """A low flat slab, 0.46 m — the kind that sits half-sunk in a lawn."""
    return _rock("Rock_B", 29, 0.24, 0.19, 3.6, pebbles=3)


def water_trough():
    """1.62 m of watering trough, on two sleepers, HOLLOW, with water in it.

    The basin is cut into the solid (inset + push down, twice: rim then floor) rather than
    modelled as four walls, so the rim has real thickness and a dark inner line all the way
    round.  That line is the entire read at range — a trough without it is a bench.  The
    water sits 60 mm under the rim and is a separate slab in pond blue so it can catch a
    specular that the timber does not.
    """
    mb = MB()
    LEN, WID, TOP, BOT = 1.62, 0.48, 0.54, 0.16
    body = rbox(mb, WOOD, (0, 0, (TOP + BOT) * 0.5), (LEN, WID, TOP - BOT),
                r=0.030, n=12, k=2, e=4.2)
    # TRAP, and it cost a rebuild: a rounded box's top CHAMFER band has normal.z ~ 0.93,
    # so `normal.z > 0.9` grabs thirteen faces, not one.  inset_region on that region
    # insets its outer boundary and then the depth translate carries the whole cap ring
    # with it -- the trough came out 236 mm too tall with no basin at all.  Pick the cap
    # by BOTH its normal and its height and the region is unambiguous.
    top = pick_faces(body, lambda f: f.normal.z > 0.98 and
                     f.calc_center_median().z > TOP - 0.01)
    inner = recess(mb, top, 0.055, -0.045, col=WOOD_D)      # the rim, with real thickness
    recess(mb, inner, 0.030, -0.190, col=tint(WOOD_D, 0.78))  # the basin floor
    mb.upd()
    # water surface
    wpts = []
    for i in range(12):
        a = TAU * i / 12
        wpts.append(Vector((math.cos(a) * (LEN * 0.5 - 0.10),
                            math.sin(a) * (WID * 0.5 - 0.085), TOP - 0.052)))
    mb.slab(wpts, WATER_L, (0, 0, -0.016), col_bot=WATER, smooth=False)

    # iron straps around the tub, and the bolt heads that hold them
    for sx in (-1, 1):
        rbox(mb, IRON, (sx * 0.50, 0, (TOP + BOT) * 0.5 - 0.02),
             (0.055, WID + 0.020, TOP - BOT - 0.070), r=0.008, n=8, k=1)
        for sy in (-1, 1):
            cyl(mb, BRASS, (sx * 0.50, sy * (WID * 0.5 + 0.004), 0.40),
                (sx * 0.50, sy * (WID * 0.5 + 0.028), 0.40), 0.022, 0.018, n=6,
                smooth=False)
    # sleepers: splayed feet, not posts, so the trough looks bedded on the turf
    for sx in (-1, 1):
        rbox(mb, WOOD_D, (sx * 0.54, 0, BOT * 0.5), (0.19, WID + 0.08, BOT),
             r=0.014, n=8, k=1, taper=(0.72, 0.80))
    ob = mb.finish("WaterTrough", MAT, smooth_angle=28.0)
    grade(ob, 0.0, TOP, lo=0.76, hi=1.14)
    return ob


def hay_bale():
    """A round bale on its side, 1.20 m across, 1.10 m long.

    Two details do all the work.  The ENDS are recessed twice into concentric rings, which
    is what a coiled bale actually looks like end-on and what stops it reading as a barrel.
    The BARREL is slightly fatter in the middle and flat-shaded at 14 sides, so the
    silhouette has facets and the top catches a bleached highlight while the underside
    stays in shadow.
    """
    mb = MB()
    R, HL = 0.60, 0.55
    rings = []
    for (y, rs) in ((-1.00, 0.86), (-0.72, 0.98), (0.0, 1.00), (0.72, 0.98),
                    (1.00, 0.86)):
        rings.append([Vector((x, y * HL, z + R)) for (x, z) in
                      sect(14, R * rs, R * rs, 2.0)])
    faces, vr = mb.loft(rings, STRAW, cap_a=True, cap_b=True, smooth=False)
    for f in faces:
        if not f.is_valid:
            continue
        if f.calc_center_median().z < R * 0.55:
            f[mb.lay] = mb.cid(STRAW_D)
    caps = pick_faces(faces, lambda f: abs(f.normal.y) > 0.9)
    for c in caps:
        i1 = recess(mb, [c], 0.085, -0.020, col=tint(STRAW, 0.92))
        recess(mb, i1, 0.085, -0.034, col=STRAW_D)
    # twine: two bands, three-sided so they cost almost nothing but still catch a highlight
    for y in (-0.26, 0.26):
        pts = [Vector((math.cos(TAU * i / 12) * (R + 0.012), y * HL / 0.55 * 0.55,
                       math.sin(TAU * i / 12) * (R + 0.012) + R)) for i in range(12)]
        tube(mb, tint(STRAW_D, 0.72), pts, 0.014, n=3, e=2.0, smooth=False, loop=True)
    # loose straw at the contact line, so the bale is settled into the grass
    rnd = random.Random(71)
    for k in range(5):
        a = rnd.uniform(0, TAU)
        x = rnd.uniform(-HL, HL) * 0.9
        d = Vector((math.cos(a), math.sin(a) * 0.4, 0.12)).normalized()
        b = Vector((x, rnd.choice((-1, 1)) * (HL - 0.04), 0.012))
        tube(mb, mixhex(STRAW, STRAW_D, 0.3), [b, b + d * 0.06, b + d * 0.13],
             [0.010, 0.007, 0.003], n=3, smooth=False)
    ob = mb.finish("HayBale", MAT, smooth_angle=24.0)
    grade(ob, 0.0, R * 2, lo=0.72, hi=1.18)
    return ob


def bunting_span():
    """4.00 m of pennant line on a catenary, PIVOT AT THE LEFT ATTACHMENT POINT.

    This is the one object in the file whose pivot is not on the ground, and it has to be:
    the span is chained end to end round a barrier, so instance n+1 sits at instance n's
    far end at the SAME height.  Pivot at the cord's left eye, geometry hanging below it,
    right eye exactly 4.000 m along +X at z = 0.  Anything else and a run of these
    staircases.

    The droop is a real catenary (cosh), not a parabola, because a chain of parabolas has a
    visible corner at every post and a chain of catenaries does not.
    """
    mb = MB()
    SPAN, SAG, N = 4.00, 0.46, 22
    a = 2.05                                        # catenary parameter, tuned to SAG
    def zc(t):                                      # t in 0..1 across the span
        u = (t - 0.5) * SPAN
        return -(math.cosh(SPAN * 0.5 / a) - math.cosh(u / a)) * a * \
            (SAG / ((math.cosh(SPAN * 0.5 / a) - 1.0) * a))
    pts = [Vector((t * SPAN, 0.0, zc(t))) for t in (i / (N - 1.0) for i in range(N))]
    tube(mb, CHALK, pts, 0.011, n=3, e=2.0, smooth=False)

    cols = [RED, CREAM, YELLOW, BLUE, CREAM, RED, YELLOW, CREAM, BLUE, RED, CREAM]
    n_flag = 11
    for k in range(n_flag):
        t = (k + 0.5) / n_flag
        i = t * (N - 1.0)
        p = pts[int(i)].lerp(pts[min(N - 1, int(i) + 1)], i - int(i))
        # the cord's local slope, so a pennant near the post hangs off a tilted line
        j = max(0, min(N - 2, int(i)))
        tan = (pts[j + 1] - pts[j]).normalized()
        wob = math.sin(k * 1.9) * 0.045                 # each flag turned a little
        hw, ht = 0.115, 0.290
        c = cols[k % len(cols)]
        # outline: two top corners on the cord, one apex below, plus a dipped mid-top so
        # the cloth looks slack rather than stretched over a wire
        tl = p - tan * hw
        tr = p + tan * hw
        tm = (tl + tr) * 0.5 + Vector((0, 0, -0.022))
        ap = (tl + tr) * 0.5 + Vector((wob, 0.012 + wob * 0.4, -ht))
        mb.slab([tl, tm, tr, ap], c, (0, 0.006, 0), col_bot=tint(c, 0.80), smooth=False)
        # the knot over the cord: three tris, and it is what makes a flag hang from the
        # line instead of intersecting it
        cyl(mb, tint(c, 0.86), p + Vector((0, -0.014, 0.004)),
            p + Vector((0, 0.014, 0.004)), 0.017, 0.017, n=3, smooth=False)
    # eyes at both ends so the span visibly terminates at something
    for x in (0.0, SPAN):
        cyl(mb, BRASS, (x, -0.016, 0.0), (x, 0.016, 0.0), 0.020, 0.020, n=5,
            smooth=False)
    ob = mb.finish("Bunting_Span", MAT, smooth_angle=26.0)
    grade(ob, -SAG - 0.29, 0.02, lo=0.80, hi=1.12)
    return ob


def floodlight():
    """A 7.05 m mast, to put something on the skyline behind the stands.

    Built the way SKILL.md says a mast should be: a STEPPED loft, not a smooth taper — a
    fat splayed foot, a parallel shank, a collar where the ladder ends, a thinner upper
    shank, a head.  The steps are what the eye measures 7 m against; a smooth cone reads as
    the same object at any height.

    The head is deliberately asymmetric (four lamps on a cranked yoke, all aimed down and
    inboard) and carries a small pennant, so the silhouette against the sky is not a T.
    """
    mb = MB()
    H = 7.05
    # --- foot: a splayed plinth with four gussets
    rbox(mb, STONE_D, (0, 0, 0.075), (0.78, 0.78, 0.15), r=0.024, n=8, k=1,
         taper=(0.80, 0.80))
    for k in range(4):
        a = TAU * k / 4 + math.pi * 0.25
        d = Vector((math.cos(a), math.sin(a), 0))
        mb.slab([Vector((0, 0, 0.15)) + d * 0.085 + Vector((0, 0, 0.52)),
                 Vector((0, 0, 0.15)) + d * 0.085,
                 Vector((0, 0, 0.15)) + d * 0.310],
                RED_D, (d.y * 0.028, -d.x * 0.028, 0), smooth=False)
    # --- mast: stepped profile
    # Beefed up by half after the first preview: at 7 m a 70 mm mast is two pixels of
    # candy stripe and the whole prop reads as a wire.  Arcade scale wants a pole you
    # could not put your arms round.
    prof = [(0.150, 0.155, 4.6), (0.230, 0.128, 4.2), (0.320, 0.116, 3.4),
            (2.560, 0.106, 3.0), (2.640, 0.136, 3.0), (2.720, 0.127, 3.0),
            (2.800, 0.096, 2.8), (5.560, 0.082, 2.6), (5.640, 0.110, 2.6),
            (5.720, 0.101, 2.6), (5.800, 0.075, 2.4), (6.240, 0.068, 2.4)]
    rings = [ring_z(8, z, r, r, e) for (z, r, e) in prof]
    faces, _ = mb.loft(rings, RED, cap_a=True, cap_b=True, smooth=False)
    for f in faces:
        if not f.is_valid:
            continue
        z = f.calc_center_median().z
        if 2.55 < z < 2.81 or 5.55 < z < 5.81:
            f[mb.lay] = mb.cid(CREAM)                 # the collars, painted out
        elif z > 4.0:
            f[mb.lay] = mb.cid(tint(RED, 1.06))
    # --- ladder: two stringers and rungs up the back, to 2.6 m
    for sx in (-1, 1):
        cyl(mb, IRON, (sx * 0.105, 0.155, 0.28), (sx * 0.092, 0.140, 2.62),
            0.015, 0.015, n=4, smooth=False)
    for k in range(8):
        z = 0.42 + k * 0.30
        cyl(mb, IRON, (-0.102, 0.148, z), (0.102, 0.148, z), 0.013, 0.013, n=3,
            smooth=False)
    # --- yoke: a cranked crossbar carrying four lamps, plus two stays back to the mast
    yz = 6.02
    rbox(mb, CREAM, (0, -0.14, yz), (1.70, 0.115, 0.115), r=0.018, n=8, k=1)
    for sx in (-1, 1):
        cyl(mb, CREAM, (sx * 0.76, -0.14, yz), (sx * 0.075, 0.02, yz + 0.40),
            0.034, 0.026, n=5, smooth=False)
    for k in range(4):
        x = (-1.5 + k) * 0.48
        # housing: a short cone aimed down and inboard
        base = Vector((x, -0.14, yz - 0.03))
        aim = Vector((-x * 0.22, -0.62, -0.75)).normalized()
        cyl(mb, IRON, base, base + aim * 0.070, 0.042, 0.072, n=6, smooth=False)
        cyl(mb, CREAM, base + aim * 0.070, base + aim * 0.275, 0.120, 0.160, n=8,
            e=2.4, smooth=False)
        # the lens, set a touch inside the hood so the rim casts a line across it
        disc(mb, LAMP, base + aim * 0.262, aim, 0.138, n=8, smooth=False, t=0.016)
        # hood peak, so a lamp is not a plain cone
        mb.slab([base + aim * 0.275 + Vector((0, 0, 0.105)),
                 base + aim * 0.275 + Vector((-0.14, -0.06, 0.04)),
                 base + aim * 0.275 + Vector((0.14, -0.06, 0.04))],
                tint(CREAM, 0.88), (0, 0.045, 0.016), smooth=False)
    # --- crown: finial and a pennant, so the top is not a bare stub
    cyl(mb, BRASS, (0, 0, 6.24), (0, 0, 6.42), 0.058, 0.038, n=6, smooth=False)
    sphere(mb, BRASS, (0, 0, 6.47), (0.068, 0.068, 0.082), seg=6, rings=3, smooth=False)
    cyl(mb, CREAM, (0, 0, 6.50), (0, 0, H - 0.08), 0.024, 0.018, n=5, smooth=False)
    mb.slab([Vector((0.010, 0.0, H - 0.08)), Vector((0.42, 0.06, H - 0.22)),
             Vector((0.010, 0.0, H - 0.36))], RED, (0, 0.012, 0),
            col_bot=RED_D, smooth=False)
    ob = mb.finish("Floodlight", MAT, smooth_angle=26.0)
    grade(ob, 0.0, H, lo=0.80, hi=1.12)
    return ob


# ============================================================================== assemble

BUILDERS = [grass_tuft_a, grass_tuft_b, grass_tuft_c,
            clover_patch_a, clover_patch_b, daisies_a, daisies_b,
            molehill, rock_a, rock_b, water_trough, hay_bale,
            bunting_span, floodlight]

NO_GROUND = {"Bunting_Span"}     # hangs below its own pivot; a ground plane at z=0 would
                                 # be ABOVE most of it and occlude the wrong faces


def main():
    fresh_scene()
    objs = [fn() for fn in BUILDERS]

    bpy.context.view_layer.update()
    for ob in objs:
        bake_ao([ob], floor=0.70, dist=0.28, samples=24,
                ground=ob.name not in NO_GROUND, ground_size=6.0)

    L.verify_meshes(objs, "RallyDressing")
    total = report_tris(objs, "RallyDressing")

    print("BOUNDS (metres, Blender Z-up; pivot at origin unless noted)")
    for o in objs:
        lo = Vector((min(v.co.x for v in o.data.vertices),
                     min(v.co.y for v in o.data.vertices),
                     min(v.co.z for v in o.data.vertices)))
        hi = Vector((max(v.co.x for v in o.data.vertices),
                     max(v.co.y for v in o.data.vertices),
                     max(v.co.z for v in o.data.vertices)))
        ca = o.data.color_attributes[0]
        print("  %-16s tris %4d  size %5.3f x %5.3f x %5.3f  z %+.3f..%+.3f  "
              "pivot %s  col(%s,%s,%s)"
              % (o.name, L.tri_count(o), hi.x - lo.x, hi.y - lo.y, hi.z - lo.z,
                 lo.z, hi.z, tuple(round(x, 3) for x in o.location),
                 ca.name, ca.domain, ca.data_type))
    print("TOTAL %d triangles (budget 9000)" % total)

    export_fbx(objs, os.path.join(L.MODELS_DIR, "RallyDressing.fbx"))

    if "--preview" in sys.argv:
        for i, ob in enumerate(objs):
            ob.location = Vector(ob.location) + Vector(((i % 5) * 3.0,
                                                        (i // 5) * 3.0, 0.0))
        render_gallery(objs, "dressing", res=340, cols=5)
        render_gallery(objs, "dressing", res=340, cols=5, sil=True)
    print("DONE RallyDressing")


main()
