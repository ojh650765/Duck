# duck_lib.py — shared modelling / colour / AO / export / preview toolkit for DUCK MOW
# Blender 4.0.1.  No image textures anywhere: all colour lives in a CORNER BYTE_COLOR
# attribute named "Col".  Method follows SKILL.md: loft cross-sections, cut detail in,
# three-tier detail hierarchy.  Art direction follows Docs/ART_BIBLE.md.

import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree
import numpy as np

TAU = math.pi * 2.0

MODELS_DIR   = r"C:\Duck\Assets\Art\Models"
PREVIEWS_DIR = r"C:\Duck\Art\Blender\previews"

# ----------------------------------------------------------------------------- palette
P = {
    # grass
    "grass_uncut_base": "2F6B33", "grass_uncut_tip": "4E9440",
    "grass_cut_base": "6E9E37",   "grass_cut_tip": "A8CB55",
    "cut_edge": "24512A",
    # duck
    "duck_cream": "F6EBD2", "duck_cream_sh": "DCC9A4",
    "duck_orange": "F2A03D", "duck_orange_sh": "D3792A",
    # mower
    "red": "D6423C", "red_deep": "A32E2D", "cream": "F4E7CF",
    "grey": "4A4F55", "brass": "C9A55A",
    # world
    "fence_white": "F1EDE0", "tent_red": "D8534E", "tent_cream": "F5EAD6",
    "wood": "9A6B41", "wood_dark": "6E4A2C",
    "water": "3E86A8", "water_shallow": "68B0C4",
    "chalk": "F7F3E4", "hedge": "2A5A34", "dirt": "B99A6B",
    "hill": "7FA8A0",
    # derived working tones (kept inside the bible's hue family)
    "black": "241F1E", "white": "F7F3E4", "eye": "2B2521",
    "tyre": "3A3B3E", "glass": "9FD4E4",
}

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def hexcol(h):
    """'#RRGGBB' or palette key -> linear RGBA tuple."""
    h = P.get(h, h).lstrip("#")
    r, g, b = (int(h[i:i+2], 16) / 255.0 for i in (0, 2, 4))
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)

def tint(h, f):
    """Darken (f<1) / lighten (f>1) a hex in sRGB space, return hex string."""
    h = P.get(h, h).lstrip("#")
    out = ""
    for i in (0, 2, 4):
        v = int(h[i:i+2], 16) / 255.0
        v = max(0.0, min(1.0, v * f))
        out += "%02X" % int(round(v * 255))
    return out

# ----------------------------------------------------------------------------- scene
def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    os.makedirs(MODELS_DIR, exist_ok=True)
    os.makedirs(PREVIEWS_DIR, exist_ok=True)
    random.seed(7)

# ----------------------------------------------------------------------------- builder
class MB:
    """bmesh wrapper that tracks a per-face palette index."""
    def __init__(self):
        self.bm = bmesh.new()
        self.lay = self.bm.faces.layers.int.new("cidx")
        self.cols = []
        self.cmap = {}

    def cid(self, h):
        h = P.get(h, h)
        if h not in self.cmap:
            self.cmap[h] = len(self.cols)
            self.cols.append(h)
        return self.cmap[h]

    def v(self, co):
        return self.bm.verts.new(Vector(co))

    def f(self, verts, col):
        try:
            fa = self.bm.faces.new(verts)
        except ValueError:
            return None
        fa[self.lay] = self.cid(col)
        return fa

    def face_list(self, faces, col):
        c = self.cid(col)
        for fa in faces:
            if fa:
                fa[self.lay] = c

    # -- generic loft over a list of rings (each ring = list of 3D points, equal len)
    def loft(self, rings, col, cap_a=True, cap_b=True, smooth=True, closed=True):
        vr = [[self.v(p) for p in ring] for ring in rings]
        faces = []
        for A, B in zip(vr, vr[1:]):
            n = len(A)
            m = n if closed else n - 1
            for i in range(m):
                j = (i + 1) % n
                faces.append(self.f((A[i], A[j], B[j], B[i]), col))
        if cap_a and len(vr[0]) > 2:
            faces.append(self.f(list(reversed(vr[0])), col))
        if cap_b and len(vr[-1]) > 2:
            faces.append(self.f(vr[-1], col))
        faces = [f for f in faces if f]
        for f in faces:
            f.smooth = smooth
        # TRAP: bm.faces.new() leaves face.normal at (0,0,0).  Every predicate that
        # picks faces by normal silently matches nothing until this is called.
        self.bm.normal_update()
        self._orient(faces)
        return faces, vr

    def _orient(self, faces):
        """Force a lofted shell's normals OUTWARD.

        A ring_z loft comes out outward; a ring_y loft comes out INWARD, because
        (x,z) counter-clockwise swept along +Y winds the other way.  That single
        sign is why parts of this set rendered inside-out, and worse, why every
        `f.normal.z > 0.55` predicate on a Y-lofted body was quietly selecting
        the far side -- the mower's footwell was being cut into the underside of
        the tub.  Decide by the shell's own centroid, then flip in place so the
        caller's face list stays valid and in order.
        """
        faces = [f for f in faces if f and f.is_valid]
        if not faces:
            return faces
        ctr = Vector((0, 0, 0))
        for f in faces:
            ctr += f.calc_center_median()
        ctr /= len(faces)
        score = 0.0
        for f in faces:
            d = f.calc_center_median() - ctr
            score += d.dot(f.normal) * f.calc_area()
        if score < 0.0:
            bmesh.ops.reverse_faces(self.bm, faces=faces)
            self.bm.normal_update()
        return faces

    def upd(self):
        self.bm.normal_update()

    def pole_loft(self, rings, col, pole_a=None, pole_b=None, smooth=True):
        """Loft with optional single-vertex caps (spheres, cones)."""
        faces, vr = self.loft(rings, col, cap_a=False, cap_b=False, smooth=smooth)
        if pole_a is not None:
            pa = self.v(pole_a)
            A = vr[0]; n = len(A)
            for i in range(n):
                faces.append(self.f((A[(i + 1) % n], A[i], pa), col))
        if pole_b is not None:
            pb = self.v(pole_b)
            B = vr[-1]; n = len(B)
            for i in range(n):
                faces.append(self.f((B[i], B[(i + 1) % n], pb), col))
        faces = [f for f in faces if f]
        for f in faces:
            f.smooth = smooth
        self.bm.normal_update()
        self._orient(faces)
        # the pole fans were wound against the ORIGINAL ring order; if _orient
        # flipped the shell they now disagree with it.  Settle each one against
        # the shell centroid so predicates see the truth.
        ctr = Vector((0, 0, 0))
        for f in faces:
            ctr += f.calc_center_median()
        ctr /= max(1, len(faces))
        flip = [f for f in faces if f.is_valid and len(f.verts) == 3
                and (f.calc_center_median() - ctr).dot(f.normal) < 0]
        if flip:
            bmesh.ops.reverse_faces(self.bm, faces=flip)
            self.bm.normal_update()
        return faces, vr

    def slab(self, pts, col, offset, col_bot=None, smooth=False):
        """A closed thin solid from one 3D outline swept by `offset`.

        Use this for canvas: canopies, valances, flags.  The pattern it replaces
        -- mb.f(a,b,c,d) followed by mb.f(d,c,b,a) -- CANNOT work, because bmesh
        refuses a second face on the same vertices and mb.f() swallows the
        error.  What shipped was a single one-sided face that disappears under
        back-face culling.
        """
        off = Vector(offset)
        top = [self.v(p) for p in pts]
        bot = [self.v(Vector(p) + off) for p in pts]
        n = len(pts)
        faces = [self.f(top, col), self.f(list(reversed(bot)), col_bot or col)]
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.f((top[j], top[i], bot[i], bot[j]), col))
        faces = [f for f in faces if f]
        for f in faces:
            f.smooth = smooth
        self.bm.normal_update()
        return faces

    def double_side(self, faces, offset=0.0005):
        """Give a deliberately flat piece (flag, canopy, leaf blade) real back
        faces: a reversed copy pushed 0.5 mm along -normal.  A single face that
        you hope the engine renders from both sides is the bug that made parts
        of this set read as transparent under back-face culling."""
        self.bm.normal_update()
        faces = [f for f in faces if f and f.is_valid]
        out = []
        for f in faces:
            n = f.normal.copy()
            if n.length < 1e-9:
                continue
            ci = f[self.lay]
            vs = [self.bm.verts.new(v.co - n * offset) for v in reversed(f.verts)]
            nf = self.bm.faces.new(vs)
            nf[self.lay] = ci
            nf.smooth = f.smooth
            out.append(nf)
        self.bm.normal_update()
        return out

    def seal(self, name="", weld=1e-4, sheet=0.0012):
        """DEFECT 6 fix, applied to every mesh this toolkit builds.

        bmesh.faces.new() leaves face.normal at (0,0,0) and hand-wound faces
        drift, so a triangle can end up facing inward -- back-face culling then
        turns it into a hole you can see straight through.  Recalc alone is not
        enough: it can only decide "outward" for a CLOSED island.  So:
          1  weld coincident seams (two lofts that share a ring)
          2  cap the leftover open tube ends
          3  solidify genuinely flat pieces (flags, blades) into thin slabs so
             they have real back faces instead of relying on two-sided drawing
          4  recalculate, now that every island is a closed solid
        """
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=weld)
        bm.normal_update()
        bm.faces.index_update()

        seen, islands = set(), []
        for f in bm.faces:
            if f.index in seen:
                continue
            stack, isl = [f], []
            seen.add(f.index)
            while stack:
                g = stack.pop()
                isl.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h.index not in seen:
                            seen.add(h.index)
                            stack.append(h)
            islands.append(isl)

        fill_edges, flat_faces = set(), []
        for isl in islands:
            bnd = [e for g in isl for e in g.edges if len(e.link_faces) == 1]
            if not bnd:
                continue
            n0 = isl[0].normal
            if n0.length > 1e-9 and all(abs(g.normal.dot(n0)) > 0.985
                                        for g in isl if g.normal.length > 1e-9):
                flat_faces.extend(isl)          # a sheet: give it thickness
            else:
                fill_edges.update(bnd)          # an open tube: cap it
        if fill_edges:
            try:
                bmesh.ops.holes_fill(bm, edges=list(fill_edges), sides=0)
            except Exception as e:
                print("  holes_fill %s: %s" % (name, e))
        if flat_faces:
            try:
                bmesh.ops.solidify(bm, geom=flat_faces, thickness=-sheet)
            except Exception as e:
                print("  solidify %s: %s" % (name, e))
        bm.normal_update()
        try:
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        except Exception as e:
            print("  recalc_face_normals %s: %s" % (name, e))
        bm.normal_update()

    def finish(self, name, mat_name, smooth_angle=38.0, seal=True):
        me = bpy.data.meshes.new(name)
        if seal:
            self.seal(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        # carry face palette index onto corner colours
        idx_attr = me.attributes.get("cidx")
        cols = me.color_attributes.new(name="Col", type='BYTE_COLOR', domain='CORNER')
        lin = [hexcol(c) for c in self.cols]
        if not lin:
            lin = [hexcol("F6EBD2")]
        for poly in me.polygons:
            ci = idx_attr.data[poly.index].value if idx_attr else 0
            c = lin[min(ci, len(lin) - 1)]
            for li in poly.loop_indices:
                cols.data[li].color = c
        if idx_attr:
            me.attributes.remove(me.attributes["cidx"])
        me.use_auto_smooth = True
        me.auto_smooth_angle = math.radians(smooth_angle)
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.append(get_mat(mat_name))
        self.bm.free()
        return ob

# ------------------------------------------------------------------- profile helpers
def sect(n, rx, ry, e=2.0, rot=0.0):
    """Superellipse outline in 2D. e=2 circle, e>2 squarer."""
    pts = []
    k = 2.0 / e
    for i in range(n):
        a = TAU * i / n + rot
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** k, c) * rx
        y = math.copysign(abs(s) ** k, s) * ry
        pts.append((x, y))
    return pts

def frame(pts2d, origin, right, up):
    o, r, u = Vector(origin), Vector(right), Vector(up)
    return [o + r * x + u * y for (x, y) in pts2d]

def ring_z(n, z, rx, ry, e=2.0, cx=0.0, cy=0.0, rot=0.0):
    """Horizontal ring at height z (X = width, Y = depth)."""
    return [Vector((cx + x, cy + y, z)) for (x, y) in sect(n, rx, ry, e, rot)]

def ring_y(n, y, rx, rz, e=2.0, cx=0.0, cz=0.0, rot=0.0):
    """Ring facing along Y (X = width, Z = height)."""
    return [Vector((cx + x, y, cz + z)) for (x, z) in sect(n, rx, rz, e, rot)]

# -------------------------------------------------------------------- solid helpers
def rbox(mb, col, center, size, r=0.014, n=12, e=5.0, k=2, smooth=False, taper=None):
    """Rounded box as a Z-loft. taper = (top_scale_x, top_scale_y) for a wedge."""
    cx, cy, cz = center
    W, D, H = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    r = min(r, W * 0.9, D * 0.9, H * 0.9)
    rings = []
    for end in (-1, 1):
        steps = range(k, -1, -1) if end > 0 else range(0, k + 1)
        seq = []
        for i in steps:
            a = math.pi * 0.5 * i / k
            dz = r * (1 - math.cos(a))
            sh = r * (1 - math.sin(a))
            z = cz + end * (H - dz)
            sx, sy = 1.0, 1.0
            if taper:
                t = (z - (cz - H)) / max(1e-6, 2 * H)
                sx = 1 + (taper[0] - 1) * t
                sy = 1 + (taper[1] - 1) * t
            seq.append(ring_z(n, z, (W - sh) * sx, (D - sh) * sy, e, cx, cy))
        rings.extend(seq if end < 0 else seq)
    # order: bottom rounding then top rounding (already ascending)
    rings.sort(key=lambda rg: rg[0].z)
    return mb.loft(rings, col, smooth=smooth)[0]

def cyl(mb, col, p0, p1, r0, r1=None, n=12, e=2.0, cap_a=True, cap_b=True,
        smooth=True, up_hint=(0, 0, 1)):
    p0, p1 = Vector(p0), Vector(p1)
    if r1 is None:
        r1 = r0
    d = (p1 - p0)
    L = d.length
    if L < 1e-7:
        return []
    d.normalize()
    up = Vector(up_hint)
    if abs(d.dot(up)) > 0.98:
        up = Vector((0, 1, 0))
    rt = d.cross(up).normalized()
    up = rt.cross(d).normalized()
    A = frame(sect(n, r0, r0, e), p0, rt, up)
    B = frame(sect(n, r1, r1, e), p1, rt, up)
    return mb.loft([A, B], col, cap_a=cap_a, cap_b=cap_b, smooth=smooth)[0]

def tube(mb, col, pts, radii, n=8, e=2.0, cap_a=True, cap_b=True, smooth=True,
         up_hint=(0, 0, 1), squash=None, loop=False):
    """Loft a round section along a polyline, frames averaged at the joints so
    bends do not show a seam disc.  squash = per-point (sx, sy) scale.

    loop=True closes the path properly.  Repeating the first point at the end
    instead gives two rings at the same place with DIFFERENT frames, and after
    the 0.1 mm weld some of those vertices merge and some do not -- which is
    exactly how a mesh ends up inconsistently wound.
    """
    pts = [Vector(p) for p in pts]
    if loop and len(pts) > 1 and (pts[0] - pts[-1]).length < 1e-6:
        pts = pts[:-1]
    m = len(pts)
    tans = []
    for i in range(m):
        if loop:
            a, b = pts[(i - 1) % m], pts[(i + 1) % m]
        else:
            a, b = pts[max(0, i - 1)], pts[min(m - 1, i + 1)]
        t = (b - a)
        tans.append(t.normalized() if t.length > 1e-7 else Vector((0, 0, 1)))
    rings = []
    for i, (p, t) in enumerate(zip(pts, tans)):
        up = Vector(up_hint)
        if abs(t.dot(up)) > 0.97:
            up = Vector((0, 1, 0))
        rt = t.cross(up).normalized()
        up = rt.cross(t).normalized()
        r = radii[i] if hasattr(radii, "__len__") else radii
        sx, sy = squash[i] if squash else (1.0, 1.0)
        rings.append(frame(sect(n, r * sx, r * sy, e), p, rt, up))
    if loop:
        rings.append(rings[0])
        cap_a = cap_b = False
    return mb.loft(rings, col, cap_a=cap_a, cap_b=cap_b, smooth=smooth)[0]


def sweep_rings(pts, radii, n=8, e=2.0, up_hint=(0, 0, 1), squash=None,
                tangents=None):
    """The ring list tube() would build, handed back instead of lofted.

    Use it when the swept skin has to end in a POLE rather than a cap — heads,
    beaks, muzzles — so a neck socket, a skull and a snout can be one unbroken
    lofted surface instead of three solids stacked on each other.
    """
    pts = [Vector(p) for p in pts]
    m = len(pts)
    rings = []
    for i, p in enumerate(pts):
        a, b = pts[max(0, i - 1)], pts[min(m - 1, i + 1)]
        t = (b - a)
        t = t.normalized() if t.length > 1e-7 else Vector((0, 0, 1))
        # A socket ring MUST stay perpendicular to the pivot axis: if the path
        # bends away just past it, the averaged tangent tips that ring off the
        # sphere and the "invariant" surface stops being invariant.  Horace
        # opened an 8 mm gap under his jaw from exactly this.
        if tangents is not None and tangents[i] is not None:
            t = Vector(tangents[i]).normalized()
        up = Vector(up_hint)
        if abs(t.dot(up)) > 0.97:
            up = Vector((0, 1, 0))
        rt = t.cross(up).normalized()
        up = rt.cross(t).normalized()
        r = radii[i] if hasattr(radii, "__len__") else radii
        sx, sy = squash[i] if squash else (1.0, 1.0)
        rings.append(frame(sect(n, r * sx, r * sy, e), p, rt, up))
    return rings


def sphere(mb, col, center, radii, seg=12, rings=6, smooth=True, squash=None):
    cx, cy, cz = center
    rx, ry, rz = radii
    rgs = []
    for i in range(1, rings):
        t = math.pi * i / rings
        s = math.sin(t)
        z = cz + rz * math.cos(t)
        rgs.append(ring_z(seg, z, rx * s, ry * s, 2.0, cx, cy))
    return mb.pole_loft(rgs, col, pole_a=(cx, cy, cz + rz),
                        pole_b=(cx, cy, cz - rz), smooth=smooth)[0]

def disc(mb, col, center, normal, r, n=12, smooth=False, t=0.0016):
    """A CLOSED sliver puck whose visible face sits at `center` facing `normal`.

    This used to emit a single one-sided n-gon.  Under back-face culling a lone
    face is a hole you can see through from behind, and it z-fights whatever it
    was laid on.  The puck has real thickness, so neither happens.
    """
    c, d = Vector(center), Vector(normal).normalized()
    return cyl(mb, col, c - d * t, c, r, r, n=n, e=2.0,
               cap_a=True, cap_b=True, smooth=smooth)

def quad(mb, col, a, b, c, d, smooth=False):
    vs = [mb.v(p) for p in (a, b, c, d)]
    f = mb.f(vs, col)
    if f:
        f.smooth = smooth
    return [f]

def arc_sweep(mb, col, cx, cy, cz, r, a0, a1, steps, prof, taper=None,
              smooth=False, cap_a=True, cap_b=True):
    """Sweep a closed 2D profile around an arc lying in the YZ plane.

    prof entries are (u, v): u = offset along X (the arc's axis), v = radial
    offset outward from radius r.  The arc runs from a0 to a1 degrees measured
    from +Y toward +Z about the centre (cy, cz).  taper(t) -> (u_scale, v_scale,
    u_shift, r_shift) lets the section feather into the bodywork at the ends
    instead of terminating in a blunt paddle -- the flap failure mode.
    """
    rings = []
    for i in range(steps + 1):
        t = i / steps
        a = math.radians(a0 + (a1 - a0) * t)
        cy_, cz_ = math.cos(a), math.sin(a)
        us, vs, ush, rsh = taper(t) if taper else (1.0, 1.0, 0.0, 0.0)
        rings.append([Vector((cx + u * us + ush,
                              cy + (r + rsh + v * vs) * cy_,
                              cz + (r + rsh + v * vs) * cz_)) for (u, v) in prof])
    return mb.loft(rings, col, cap_a=cap_a, cap_b=cap_b, smooth=smooth)


def fender_prof(u0, u1, t, ch=0.008):
    """Closed section for arc_sweep: a strip u0..u1 wide, t thick radially,
    with the outboard-top corner knocked off so it catches a highlight."""
    ch = min(ch, (u1 - u0) * 0.25, t * 0.45)
    return [(u0, 0.0), (u1 - ch, 0.0), (u1, ch), (u1, t - ch),
            (u1 - ch, t), (u0, t)]


def poly_area(pts):
    a = 0.0
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        a += x0 * y1 - x1 * y0
    return a * 0.5


def ccw(pts):
    """Force a 2D outline counter-clockwise (so a +Z cap faces up)."""
    return pts if poly_area(pts) > 0 else list(reversed(pts))


def mirror_half(half):
    """half: (x,y) with x>=0, from the centreline round one side and back to the
    centreline.  Returns the full closed outline, CCW."""
    return ccw(list(half) + [(-x, y) for (x, y) in reversed(half[1:-1])])


def scale_outline(pts, s, cx=None, cy=None):
    if cx is None:
        cx = sum(p[0] for p in pts) / len(pts)
    if cy is None:
        cy = sum(p[1] for p in pts) / len(pts)
    return [(cx + (x - cx) * s, cy + (y - cy) * s) for (x, y) in pts]


def torus(mb, col, center, axis, R, r, nmaj=14, nmin=6, smooth=True):
    c = Vector(center); ax = Vector(axis).normalized()
    up = Vector((0, 0, 1))
    if abs(ax.dot(up)) > 0.98:
        up = Vector((0, 1, 0))
    e1 = ax.cross(up).normalized(); e2 = ax.cross(e1).normalized()
    rings = []
    for i in range(nmaj + 1):
        a = TAU * (i % nmaj) / nmaj
        dirv = e1 * math.cos(a) + e2 * math.sin(a)
        ctr = c + dirv * R
        rings.append(frame(sect(nmin, r, r), ctr, dirv, ax))
    return mb.loft(rings, col, cap_a=False, cap_b=False, smooth=smooth)[0]

# ------------------------------------------------------------------ inset / recess
def recess(mb, faces, thickness, depth, col=None, use_even=True):
    """Cut a panel into the skin (SKILL.md technique 2). Returns the inner faces."""
    mb.bm.normal_update()
    faces = [f for f in faces if f and f.is_valid]
    if not faces:
        return []
    res = bmesh.ops.inset_region(mb.bm, faces=faces, thickness=thickness,
                                 depth=0.0, use_even_offset=use_even,
                                 use_boundary=True, use_interpolate=True)
    inner = [f for f in faces if f.is_valid]
    if depth:
        vs = set()
        for f in inner:
            vs.update(f.verts)
        # move along averaged normal of the inner faces
        nrm = Vector((0, 0, 0))
        for f in inner:
            nrm += f.normal
        if nrm.length > 1e-6:
            nrm.normalize()
            bmesh.ops.translate(mb.bm, verts=list(vs), vec=nrm * depth)
    if col:
        mb.face_list(inner, col)
    mb.bm.normal_update()
    return inner

def pick_faces(faces, fn):
    return [f for f in faces if f and f.is_valid and fn(f)]

# ----------------------------------------------------------------------- materials
def get_mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    ca.location = (-320, 180)
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.78
    bsdf.inputs["Metallic"].default_value = 0.0
    if "Specular" in bsdf.inputs:
        bsdf.inputs["Specular"].default_value = 0.25
    return m

# ------------------------------------------------------------------------- AO bake
def _hemi_dirs(n):
    dirs = []
    for i in range(n):
        u = (i + 0.5) / n
        b, f, v = i, 0.5, 0.0
        while b:
            v += f * (b & 1); b >>= 1; f *= 0.5
        r = math.sqrt(u); phi = TAU * v
        dirs.append(Vector((r * math.cos(phi), r * math.sin(phi),
                            math.sqrt(max(0.0, 1.0 - u)))))
    return dirs

def bake_ao(objs, floor=0.75, dist=0.30, samples=28, ground=False, ground_size=40.0,
            ground_z=0.0, bias=0.0025):
    """Multiply a soft raycast AO term into the Col vertex colours (RGB only)."""
    verts, tris = [], []
    for ob in objs:
        if ob.type != 'MESH':
            continue
        mw = ob.matrix_world
        base = len(verts)
        me = ob.data
        verts.extend([mw @ v.co for v in me.vertices])
        for p in me.polygons:
            vi = list(p.vertices)
            for k in range(1, len(vi) - 1):
                tris.append((base + vi[0], base + vi[k], base + vi[k + 1]))
    if ground:
        b = len(verts); s = ground_size
        verts.extend([Vector((-s, -s, ground_z)), Vector((s, -s, ground_z)),
                      Vector((s, s, ground_z)), Vector((-s, s, ground_z))])
        tris.append((b, b + 1, b + 2)); tris.append((b, b + 2, b + 3))
    if not tris:
        return
    bvh = BVHTree.FromPolygons([tuple(v) for v in verts], tris, all_triangles=True)
    dirs = _hemi_dirs(samples)

    for ob in objs:
        if ob.type != 'MESH':
            continue
        me = ob.data
        mw = ob.matrix_world
        nm = mw.to_3x3().inverted_safe().transposed()
        cols = me.color_attributes.get("Col")
        if not cols:
            continue
        vao = [1.0] * len(me.vertices)
        for vi, v in enumerate(me.vertices):
            n = (nm @ v.normal).normalized()
            if n.length < 1e-6:
                continue
            up = Vector((0, 0, 1))
            if abs(n.dot(up)) > 0.95:
                up = Vector((0, 1, 0))
            t = n.cross(up).normalized(); b2 = n.cross(t)
            o = (mw @ v.co) + n * bias
            occ = 0.0
            for d in dirs:
                wd = (t * d.x + b2 * d.y + n * d.z).normalized()
                hit = bvh.ray_cast(o, wd, dist)
                if hit[0] is not None:
                    occ += 1.0 - (hit[3] / dist) ** 1.5
            vao[vi] = max(0.0, 1.0 - occ / samples)
        # smooth once over edges to kill single-vertex speckle
        acc = list(vao); cnt = [1] * len(vao)
        for e in me.edges:
            a, b = e.vertices
            acc[a] += vao[b]; cnt[a] += 1
            acc[b] += vao[a]; cnt[b] += 1
        vao = [acc[i] / cnt[i] for i in range(len(vao))]

        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                m = floor + (1.0 - floor) * vao[vi]
                c = cols.data[li].color
                cols.data[li].color = (c[0] * m, c[1] * m, c[2] * m, c[3])

# ------------------------------------------------------------------ hierarchy/pivots
def set_pivot(ob, pivot):
    """Move the object's origin to `pivot` (world), keeping the geometry in place."""
    p = Vector(pivot)
    ob.data.transform(Matrix.Translation(-p))
    ob.location = p
    ob.data.update()
    return ob

def make_empty(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.08
    e.empty_display_type = 'PLAIN_AXES'
    bpy.context.collection.objects.link(e)
    e.location = Vector(loc)
    return e

def _world_loc(ob):
    """Accumulated location up the parent chain (all our rigs are unrotated),
    computed without relying on a depsgraph update of matrix_world."""
    p = Vector((0, 0, 0))
    while ob:
        p = p + ob.location
        ob = ob.parent
    return p


def attach(child, parent):
    """Parent with identity matrix_parent_inverse (SKILL.md trap)."""
    child.parent = parent
    child.matrix_parent_inverse.identity()
    child.location = child.location - _world_loc(parent)
    return child

def card_uv(ob, facing=(0, -1, 0), tol=0.92):
    """Give the flat card face a clean 0..1 UV rect so a number can be rendered
    onto it.  Every other loop is parked at (0,0)."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for li in range(len(me.loops)):
        uv.data[li].uv = (0.0, 0.0)
    f = Vector(facing).normalized()
    cand = [p for p in me.polygons if Vector(p.normal).dot(f) >= tol]
    if not cand:
        print("  card_uv %s: NO face found" % ob.name)
        return 0
    # only the largest matching face is the printable panel; a recessed border
    # produces many co-planar slivers that must NOT each get a 0..1 rect
    best = max(cand, key=lambda p: p.area)
    hits = 0
    for p in [best]:
        co = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        x0, x1 = min(c.x for c in co), max(c.x for c in co)
        z0, z1 = min(c.z for c in co), max(c.z for c in co)
        dx, dz = max(x1 - x0, 1e-6), max(z1 - z0, 1e-6)
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = ((v.x - x0) / dx, (v.z - z0) / dz)
        hits += 1
    print("  card_uv %s: %d face(s)" % (ob.name, hits))
    return hits


def flat_card(mb, col_face, col_edge, center, w, h, t):
    """A scorecard whose -Y face is a SINGLE quad (clean UV rect)."""
    cx, cy, cz = center
    x0, x1 = cx - w * 0.5, cx + w * 0.5
    z0, z1 = cz - h * 0.5, cz + h * 0.5
    yf, yb = cy - t * 0.5, cy + t * 0.5
    F = [mb.v((x0, yf, z0)), mb.v((x1, yf, z0)), mb.v((x1, yf, z1)), mb.v((x0, yf, z1))]
    B = [mb.v((x0, yb, z0)), mb.v((x1, yb, z0)), mb.v((x1, yb, z1)), mb.v((x0, yb, z1))]
    faces = [mb.f((F[0], F[1], F[2], F[3]), col_face)]      # the printable face, -Y
    faces.append(mb.f((B[3], B[2], B[1], B[0]), col_edge))
    for i in range(4):
        j = (i + 1) % 4
        faces.append(mb.f((F[j], F[i], B[i], B[j]), col_edge))
    faces = [f for f in faces if f]
    for f in faces:
        f.smooth = False
    mb.bm.normal_update()
    return faces


# =========================================================== neck / head joints
#
# The game animates NAMED TRANSFORMS, not skinned bones: at runtime Unity spins
# <Name>_Head about its own pivot.  JudgeCharacter.cs alone reaches 16 deg of
# gesture pitch on top of 4 idle + 12 look, and 30 deg of gesture yaw on top of
# 9 idle + 30 look -- so the joint has to survive a good deal more than the
# +-20/+-25 the brief quotes.
#
# A neck and a head built as two closed solids meeting at a butt joint CANNOT
# survive that.  Whatever the overlap, rotation slides one cap out from under
# the other and the character visibly becomes two stacked primitives.  Enlarging
# the head only moves the angle at which it breaks.
#
# The fix is geometric, not cosmetic: make the head's lower surface a piece of
# the SPHERE OF RADIUS R CENTRED EXACTLY ON THE HEAD'S PIVOT.  That surface is
# invariant under rotation about the pivot, so the junction between neck and
# head does not move AT ALL, at any angle, ever -- the head turns inside its own
# silhouette.  The neck then only has to stay inside that ball near the joint
# for its end cap to be permanently buried.
#
# Rules, all three needed:
#   1  head lower skin = socket_path(pivot, axis, R) rings, lofted STRAIGHT ON
#      into the skull -- one continuous lofted skin, no sphere sat on a tube.
#   2  neck axis runs through the pivot, and every neck vertex within the joint
#      is closer to the pivot than R (socket_fit() checks this).
#   3  the neck keeps going ABOVE the pivot and its cap dies inside the ball.
# joint_audit() then proves it by rotating the head and looking for any body
# surface that was hidden at rest and is not hidden any more.

def socket_path(pivot, axis, R, lats=(22.0, 52.0, 80.0, 102.0)):
    """Path points + radii tracing the lower cap of the sphere of radius `R`
    centred on `pivot`, along `axis` (which points from the neck INTO the head).

    Feed these straight into tube()/loft() as the first sections of the head and
    then carry on into the skull: every vertex they produce sits exactly R from
    the pivot, which is what makes the joint rotation-proof.  `lats` are degrees
    from the far pole; keep the last one just past 90 so the socket closes over
    the pivot's equator before the skull takes over.
    """
    P, ax = Vector(pivot), Vector(axis).normalized()
    pts, radii = [], []
    for a in lats:
        t = math.radians(a)
        pts.append(P - ax * (R * math.cos(t)))
        radii.append(R * math.sin(t))
    return pts, radii


def throat(mb, col, pivot, axis, R, into, into_r, n=10, e=2.0,
           lats=(24.0, 55.0, 84.0, 104.0), squash=None, smooth=False):
    """A THROAT: the socket cap of radius R about `pivot`, swept on up `into`
    the skull's interior so the joint reads as a jaw and throat rather than a
    ball on a stick.

    Use this on characters whose skull is lofted along its own axis (muzzles,
    snouts, beaks pointing forward) where the socket cannot simply be the first
    sections of the skull.  The part that shows below the jaw is a piece of a
    sphere centred on the pivot, so it does not move when the head turns; the
    rest is buried inside the skull.  `into` must stay inside the skull.
    """
    pts, rad = socket_path(pivot, axis, R, lats)
    tg = [Vector(axis).normalized()] * len(pts) + [None] * len(into)
    pts = list(pts) + [Vector(p) for p in into]
    rad = list(rad) + list(into_r)
    rings = sweep_rings(pts, rad, n=n, e=e, squash=squash, tangents=tg)
    return mb.pole_loft(rings, col, smooth=smooth,
                        pole_a=tuple(Vector(pivot)
                                     - Vector(axis).normalized() * R))


def socket_fit(pivot, pts, radii, R, label=""):
    """Largest distance from `pivot` reached by a tube(pts, radii) — i.e. how
    much of the neck the socket has to swallow.  Must come out under R."""
    P = Vector(pivot)
    worst = 0.0
    n = len(pts)
    for i, (p, r) in enumerate(zip(pts, radii)):
        a = Vector(pts[max(0, i - 1)])
        b = Vector(pts[min(n - 1, i + 1)])
        t = (b - a)
        t = t.normalized() if t.length > 1e-7 else Vector((0, 0, 1))
        d = Vector(p) - P
        perp = (d - t * d.dot(t)).length
        worst = max(worst, math.sqrt(d.dot(t) ** 2 + (perp + r) ** 2))
    ok = "OK" if worst < R else "*** OVER ***"
    print("  socket_fit %-18s reach=%.4f  R=%.4f  %s" % (label, worst, R, ok))
    return worst


def _island_polys(me):
    """Group polygons into connected shells.  A head is a UNION of closed
    solids (skull, beak, eyes, cap); ray parity over all of them at once
    computes the XOR instead, and reports a point that is inside two of them as
    outside.  That false positive is not a modelling defect, so the union has to
    be evaluated shell by shell."""
    adj = {}
    for p in me.polygons:
        for e in p.edge_keys:
            adj.setdefault(e, []).append(p.index)
    seen, out = set(), []
    for p in me.polygons:
        if p.index in seen:
            continue
        stack, isl = [p.index], []
        seen.add(p.index)
        while stack:
            i = stack.pop()
            isl.append(i)
            for e in me.polygons[i].edge_keys:
                for j in adj.get(e, ()):
                    if j not in seen:
                        seen.add(j)
                        stack.append(j)
        out.append(isl)
    return out


def _shells(ob, xform=None):
    """[(bvh, lo, hi)] — one entry per connected shell, in world space."""
    me = ob.data
    mw = ob.matrix_world
    vs = [mw @ v.co for v in me.vertices]
    if xform is not None:
        vs = [xform(v) for v in vs]
    out = []
    for isl in _island_polys(me):
        idx, tris = {}, []
        for pi in isl:
            vi = list(me.polygons[pi].vertices)
            for v in vi:
                if v not in idx:
                    idx[v] = len(idx)
            for k in range(1, len(vi) - 1):
                tris.append((idx[vi[0]], idx[vi[k]], idx[vi[k + 1]]))
        pts = [None] * len(idx)
        for v, i in idx.items():
            pts[i] = tuple(vs[v])
        lo = Vector((min(p[0] for p in pts), min(p[1] for p in pts),
                     min(p[2] for p in pts)))
        hi = Vector((max(p[0] for p in pts), max(p[1] for p in pts),
                     max(p[2] for p in pts)))
        out.append((BVHTree.FromPolygons(pts, tris, all_triangles=True), lo, hi))
    return out


def _in_union(shells, p):
    for bvh, lo, hi in shells:
        if (lo.x - 1e-6 <= p.x <= hi.x + 1e-6 and lo.y - 1e-6 <= p.y <= hi.y + 1e-6
                and lo.z - 1e-6 <= p.z <= hi.z + 1e-6 and _inside(bvh, p)):
            return True
    return False


_RAY_D = [Vector(v).normalized() for v in
          ((0.4127, 0.6151, 0.6721), (-0.7311, 0.2213, 0.6452),
           (0.2887, -0.8091, 0.5122))]


def _inside(bvh, p):
    """Ray-parity point-in-mesh, best of three directions.

    One ray is not enough: a ray that grazes an edge miscounts, and a single
    stray sample then reads as a hole in the model that is not there.
    """
    votes = 0
    for d in _RAY_D:
        o, n = Vector(p), 0
        for _ in range(64):
            hit = bvh.ray_cast(o, d, 30.0)
            if hit[0] is None:
                break
            n += 1
            o = hit[0] + d * 3e-6
        votes += (n % 2)
    return votes >= 2


AUDIT_POSES = [("rest", 0, 0), ("up", 24, 0), ("down", -24, 0),
               ("yawL", 0, 42), ("yawR", 0, -42),
               ("upL", 24, 42), ("dnL", -24, 42),
               ("upR", 24, -42), ("dnR", -24, -42)]


def joint_audit(body, head, pivot, label, near=0.34, poses=AUDIT_POSES,
                quiet=False, tol=0.0015):
    """Rotate `head` about `pivot` exactly as the engine does and prove that no
    body surface which is hidden at rest is ever revealed.

    Call it BEFORE set_pivot(), while both objects are still in world space.
    Reports, per pose, how many sampled body surface points come out from under
    the head.  Anything but 0 is the stacked-primitive defect.
    """
    P = Vector(pivot)
    # sample the body's skin near the joint: verts, face centres and edge mids
    me = body.data
    mw = body.matrix_world
    S = []
    for v in me.vertices:
        w = mw @ v.co
        if (w - P).length < near:
            S.append(w)
    for f in me.polygons:
        w = mw @ f.center
        if (w - P).length < near:
            S.append(w)
    for e in me.edges:
        w = mw @ ((me.vertices[e.vertices[0]].co + me.vertices[e.vertices[1]].co) * 0.5)
        if (w - P).length < near:
            S.append(w)
    if not S:
        print("  joint_audit %s: nothing near the pivot" % label)
        return 0

    rest = _shells(head)
    # Only count surface that is properly buried.  The emergence line itself --
    # where the neck comes out of the socket -- always sits within a facet
    # width of the head's skin, and a sub-millimetre flicker exactly on that
    # line is not what makes a character look like stacked primitives.
    hidden = []
    for p in S:
        if not _in_union(rest, p):
            continue
        d = min(bvh.find_nearest(p)[3] or 0.0 for bvh, _, _ in rest)
        if d > tol:
            hidden.append(p)

    # how big a ball around the pivot is guaranteed to stay inside the head
    hm = head.data
    hw = head.matrix_world
    rball = min([(hw @ v.co - P).length for v in hm.vertices]
                + [(hw @ f.center - P).length for f in hm.polygons])
    reach = max([(p - P).length for p in hidden], default=0.0)

    worst, rows, minburied = 0, [], len(hidden)
    for nm, pit, yaw in poses:
        R = Euler((math.radians(pit), 0.0, math.radians(yaw)), 'XYZ').to_matrix()
        sh = _shells(head, xform=lambda v: P + R @ (v - P))
        ins = [p for p in S if _in_union(sh, p)]
        # A revealed point is only a defect in proportion to HOW FAR it comes
        # out: the join line always jitters by about one facet width, which is
        # sub-millimetre.  Measure the exposure depth and judge on that.
        out, deep = [], 0.0
        for p in hidden:
            if _in_union(sh, p):
                continue
            d = min(b.find_nearest(p)[3] for b, _, _ in sh)
            if d > tol:
                out.append((p, d))
                deep = max(deep, d)
        worst = max(worst, len(out))
        minburied = min(minburied, len(ins))
        rows.append("      %-5s pitch%+4d yaw%+4d  revealed=%-4d buried=%-4d "
                    "exposure=%.1f mm" % (nm, pit, yaw, len(out), len(ins),
                                          deep * 1000.0))
        if out and os.environ.get("JOINT_DUMP"):
            for p, d in out[:6]:
                v = p - P
                rows.append("            at dz=%+.4f radial=%.4f |d|=%.4f  "
                            "out by %.1f mm"
                            % (v.z, math.hypot(v.x, v.y), v.length, d * 1000))
    verdict = "CLEAN"
    if worst:
        verdict = "*** BREAKS: %d revealed ***" % worst
    elif minburied == 0:
        # nothing of the neck is ever inside the head: the two solids only touch,
        # which is the butt joint this whole apparatus exists to eliminate
        verdict = "*** BUTT JOINT: no overlap ***"
    if not quiet:
        print("  JOINT %-18s hiddenSamples=%-4d socketBall=%.4f  neckReach=%.4f  %s"
              % (label, len(hidden), rball, reach, verdict))
        for r in rows:
            print(r)
    return worst if worst else (0 if minburied else -1)


def tilt_sheet(head, show, focus, rad, asset, res=360, poses=None,
               views=(("side", (-1, 0.04, 0.05)), ("tq", (-0.70, -1.0, 0.26)),
                      ("front", (0, -1, 0.04)))):
    """Render the head through the engine's rotation range — the only honest
    test of a neck joint.  `show` = the objects to keep visible."""
    poses = poses or AUDIT_POSES
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    if rad is None:                     # frame the whole head plus its joint
        c, r = _bbox([head])
        focus = (Vector(focus) + c) * 0.5
        rad = ((Vector(focus) - c).length + r) * 1.20
    if "PW" not in bpy.data.worlds:
        _setup_lights()
    keep = set(show) | {head}
    hidden = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and o not in keep and not o.hide_render:
            o.hide_render = True
            hidden.append(o)
    cd = bpy.data.cameras.new("TCam")
    cd.lens = 68
    cam = bpy.data.objects.new("TCam", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    sc.render.engine = 'BLENDER_EEVEE'
    sc.eevee.taa_render_samples = 24
    sc.eevee.use_gtao = True
    sc.eevee.gtao_distance = 0.10
    sc.view_settings.view_transform = 'Standard'
    r0 = head.rotation_euler.copy()
    paths = []
    for nm, pit, yaw in poses:
        head.rotation_euler = (math.radians(pit), 0.0, math.radians(yaw))
        bpy.context.view_layer.update()
        for vn, d in views:
            _place_cam(cam, Vector(focus), rad, d, pad=1.0)
            p = os.path.join(PREVIEWS_DIR, "%s_tilt_%s_%s.png" % (asset, nm, vn))
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            paths.append(p)
    head.rotation_euler = r0
    bpy.context.view_layer.update()
    for o in hidden:
        o.hide_render = False
    bpy.data.objects.remove(cam, do_unlink=True)
    contact_sheet(paths, os.path.join(PREVIEWS_DIR, "%s_tiltsheet.png" % asset),
                  cols=len(views))
    return paths


def want_tilt():
    """--tilt on the blender command line turns the render sweep on."""
    return "--tilt" in sys.argv


# ------------------------------------------------------- winding / manifold check
# Mirrors the engine-side audit exactly: weld by position at 0.1 mm, then count
# DIRECTED edge traversals.  Consistently wound closed manifold => every
# directed edge is traversed exactly once and the signed volume is positive.
WELD_Q = 1e-4


def mesh_report(ob):
    me = ob.data
    q = {}
    remap = []
    for v in me.vertices:
        k = (round(v.co.x / WELD_Q), round(v.co.y / WELD_Q), round(v.co.z / WELD_Q))
        if k not in q:
            q[k] = len(q)
        remap.append(q[k])

    dirs = {}
    vol = 0.0
    for p in me.polygons:
        vi = [remap[i] for i in p.vertices]
        raw = list(p.vertices)
        for k in range(len(vi)):
            a, b = vi[k], vi[(k + 1) % len(vi)]
            if a == b:
                continue
            dirs[(a, b)] = dirs.get((a, b), 0) + 1
        for k in range(1, len(raw) - 1):
            p0 = me.vertices[raw[0]].co
            p1 = me.vertices[raw[k]].co
            p2 = me.vertices[raw[k + 1]].co
            vol += p0.dot(p1.cross(p2)) / 6.0

    bnd = nmf = bad = 0
    done = set()
    for (a, b), n in dirs.items():
        e = (a, b) if a < b else (b, a)
        if e in done:
            continue
        done.add(e)
        f = dirs.get((e[0], e[1]), 0)
        r = dirs.get((e[1], e[0]), 0)
        tot = f + r
        if tot == 1:
            bnd += 1
        elif tot > 2:
            nmf += 1
        if f > 1 or r > 1:
            bad += 1

    # Per-island signed volume.  For a CLOSED island this is exact: negative
    # means the island is wound inside-out.  Open islands are listed separately.
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=WELD_Q)
    bm.faces.index_update()
    seen, inverted, islands, open_islands = set(), 0, 0, 0
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        islands += 1
        if any(len(e.link_faces) != 2 for g in isl for e in g.edges):
            open_islands += 1
            continue
        o = isl[0].verts[0].co
        v = 0.0
        for g in isl:
            vs = [w.co for w in g.verts]
            for k in range(1, len(vs) - 1):
                v += (vs[0] - o).dot((vs[k] - o).cross(vs[k + 1] - o)) / 6.0
        if v <= 0.0:
            inverted += 1
    bm.free()
    return dict(boundary=bnd, nonmanifold=nmf, bad=bad, vol=vol,
                inverted=inverted, islands=islands, open=open_islands)


def verify_meshes(objs, label=""):
    print("WINDING %s  (weld 0.1 mm, directed-edge method)" % label)
    bad = []
    tb = tn = tw = 0
    for ob in objs:
        if ob.type != 'MESH' or not ob.data.polygons:
            continue
        r = mesh_report(ob)
        tb += r["boundary"]; tn += r["nonmanifold"]; tw += r["bad"]
        flag = ""
        if r["bad"]:
            flag += " BAD-WINDING:%d" % r["bad"]
        if r["inverted"]:
            flag += " INSIDE-OUT-ISLANDS:%d" % r["inverted"]
        if r["open"]:
            flag += " OPEN-ISLANDS:%d" % r["open"]
        if r["nonmanifold"]:
            flag += " NONMANIFOLD:%d" % r["nonmanifold"]
        if r["vol"] <= 0:
            flag += " NEG-VOLUME"
        if flag:
            bad.append(ob.name)
        print("    %-24s tris=%-5d islands=%-3d badWinding=%-3d boundary=%-4d "
              "nonManifold=%-3d volume=%+.5f%s"
              % (ob.name, tri_count(ob), r["islands"], r["bad"], r["boundary"],
                 r["nonmanifold"], r["vol"], flag))
    print("  TOTAL badWinding=%d boundaryEdges=%d nonManifold=%d -> %d object(s) "
          "need attention%s" % (tw, tb, tn, len(bad),
                                (": " + ", ".join(bad)) if bad else ""))
    return bad


def tri_count(ob):
    if ob.type != 'MESH':
        return 0
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)

def report_tris(objs, label=""):
    tot = 0
    lines = []
    for o in objs:
        t = tri_count(o)
        tot += t
        if t:
            lines.append("    %-24s %5d" % (o.name, t))
    print("TRIS %s total=%d" % (label, tot))
    for l in lines:
        print(l)
    return tot

# ---------------------------------------------------------------------- export
def export_fbx(objs, filepath):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    for o in objs:
        if o.type == 'MESH':
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=filepath, use_selection=True, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        colors_type='SRGB', path_mode='COPY', object_types={'MESH', 'EMPTY'})
    print("EXPORTED", filepath)

# ---------------------------------------------------------------------- previews
def _bbox(objs):
    pts = []
    for o in objs:
        if o.type != 'MESH':
            continue
        for c in o.bound_box:
            pts.append(o.matrix_world @ Vector(c))
    if not pts:
        return Vector((0, 0, 0)), 1.0
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) * 0.5
    rad = max((p - ctr).length for p in pts)
    return ctr, rad

def _setup_lights():
    for n, loc, rot, energy, col, size in [
        ("Key",  (2.2, -2.6, 3.4), (0, 0, 0), 4.2, (1.0, 0.95, 0.83), 3.0),
        ("Fill", (-3.0, -2.2, 1.3), (0, 0, 0), 1.5, (0.72, 0.84, 1.0), 4.0),
        ("Rim",  (-0.8, 3.4, 2.4), (0, 0, 0), 2.6, (0.85, 1.0, 0.9), 3.0),
    ]:
        d = bpy.data.lights.new(n, 'AREA')
        d.energy = energy * 60
        d.color = col
        d.size = size
        o = bpy.data.objects.new(n, d)
        bpy.context.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 0.4)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    w = bpy.data.worlds.new("PW")
    bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.70, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.55

def _place_cam(cam, ctr, rad, d, pad=1.18):
    d = Vector(d).normalized()
    dist = rad / math.tan(cam.data.angle * 0.5) * pad
    cam.location = ctr + d * dist
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()

VIEWS = [
    ("front", (0, -1, 0.06)),
    ("tq",    (-0.78, -1.0, 0.42)),
    ("side",  (-1, 0.02, 0.10)),
    ("top",   (0, -0.30, 1.0)),
]
SIL_VIEWS = [("silf", (0, -1, 0.05)), ("silq", (-0.80, -1.0, 0.35))]

def render_previews(objs, asset, res=512, extra_views=None, sheet=True, solo=False):
    sc = bpy.context.scene
    bpy.context.view_layer.update()      # matrix_world is stale after parenting
    hidden = []
    if solo:
        keep = set(objs)
        for o in bpy.data.objects:
            if o.type == 'MESH' and o not in keep and not o.hide_render:
                o.hide_render = True
                hidden.append(o)
    ctr, rad = _bbox(objs)
    if "PW" not in bpy.data.worlds:
        _setup_lights()
    cd = bpy.data.cameras.new("PCam")
    cd.lens = 62
    cam = bpy.data.objects.new("PCam", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'

    paths = []
    # --- lit passes (EEVEE)
    sc.render.engine = 'BLENDER_EEVEE'
    sc.eevee.taa_render_samples = 32
    sc.eevee.use_gtao = True
    sc.eevee.gtao_distance = 0.25
    sc.eevee.use_soft_shadows = True
    sc.view_settings.view_transform = 'Standard'
    views = list(VIEWS) + list(extra_views or [])
    for vw in views:
        name, d = vw[0], vw[1]
        _place_cam(cam, ctr, rad, d, pad=(vw[2] if len(vw) > 2 else 1.18))
        p = os.path.join(PREVIEWS_DIR, "%s_%s.png" % (asset, name))
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)

    # --- silhouette pass (Workbench, flat black on white)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'FLAT'
    sh.color_type = 'SINGLE'
    sh.single_color = (0, 0, 0)
    sh.background_type = 'VIEWPORT'
    sh.background_color = (1, 1, 1)
    sh.show_specular_highlight = False
    sc.display.render_aa = '8'
    for name, d in SIL_VIEWS:
        _place_cam(cam, ctr, rad, d)
        p = os.path.join(PREVIEWS_DIR, "%s_%s.png" % (asset, name))
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)

    sc.render.engine = 'BLENDER_EEVEE'
    for o in hidden:
        o.hide_render = False
    if sheet:
        contact_sheet(paths, os.path.join(PREVIEWS_DIR, "%s_sheet.png" % asset), cols=3)
    return paths

def render_gallery(objs, asset, res=300, cols=5, d=(-0.62, -1.0, 0.34), sil=False):
    """One tightly framed render per object, composited into a single sheet.
    Use for kits (props / landmarks / foliage) where a group shot is useless."""
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    if "PW" not in bpy.data.worlds:
        _setup_lights()
    cd = bpy.data.cameras.new("GCam")
    cd.lens = 62
    cam = bpy.data.objects.new("GCam", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    if sil:
        sc.render.engine = 'BLENDER_WORKBENCH'
        sh = sc.display.shading
        sh.light = 'FLAT'; sh.color_type = 'SINGLE'; sh.single_color = (0, 0, 0)
        sh.background_type = 'VIEWPORT'; sh.background_color = (1, 1, 1)
        sc.display.render_aa = '8'
    else:
        sc.render.engine = 'BLENDER_EEVEE'
        sc.eevee.taa_render_samples = 24
        sc.eevee.use_gtao = True
        sc.view_settings.view_transform = 'Standard'
    paths = []
    for ob in objs:
        hidden = []
        for o in bpy.data.objects:
            if o.type == 'MESH' and o is not ob and not o.hide_render:
                o.hide_render = True
                hidden.append(o)
        ctr, rad = _bbox([ob])
        _place_cam(cam, ctr, rad, d, pad=1.14)
        p = os.path.join(PREVIEWS_DIR, "%s_g_%s.png" % (asset, ob.name))
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)
        for o in hidden:
            o.hide_render = False
    sc.render.engine = 'BLENDER_EEVEE'
    contact_sheet(paths, os.path.join(PREVIEWS_DIR, "%s_gallery%s.png"
                                      % (asset, "_sil" if sil else "")), cols=cols)
    return paths


def contact_sheet(paths, out, cols=3):
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        imgs.append(a)
        bpy.data.images.remove(im)
    if not imgs:
        return
    h, w = imgs[0].shape[0], imgs[0].shape[1]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.ones((rows * h, cols * w, 4), dtype=np.float32)
    for i, a in enumerate(imgs):
        r, c = divmod(i, cols)
        r = rows - 1 - r          # blender images are bottom-up
        sheet[r * h:(r + 1) * h, c * w:(c + 1) * w, :] = a
    im = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    im.pixels = sheet.reshape(-1).tolist()
    im.filepath_raw = out
    im.file_format = 'PNG'
    im.save()
    print("SHEET", out)
