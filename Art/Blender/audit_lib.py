# audit_lib.py — geometry defect sweep for DUCK MOW.
# Finds, between every pair of separate objects in a rig:
#   * intersecting triangles (surfaces chewing into each other)
#   * near-coplanar surface pairs (z-fighting in engine)
#   * parts closer than the 5 mm minimum separation
# Run from any build script, or via audit_all.py.
import bpy, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

MIN_GAP = 0.005          # ART brief: overlap-or-merge, never a hair's gap
COPLANAR_DOT = 0.985     # |n1.n2| above this and nearly touching = z-fight
COPLANAR_DIST = 0.0040


def _tris(ob):
    me = ob.data
    mw = ob.matrix_world
    vs = [mw @ v.co for v in me.vertices]
    tris, nrm = [], []
    for p in me.polygons:
        vi = list(p.vertices)
        for k in range(1, len(vi) - 1):
            tris.append((vi[0], vi[k], vi[k + 1]))
    for t in tris:
        a, b, c = vs[t[0]], vs[t[1]], vs[t[2]]
        n = (b - a).cross(c - a)
        nrm.append(n.normalized() if n.length > 1e-9 else Vector((0, 0, 1)))
    return vs, tris, nrm


def _bbox(vs):
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return lo, hi


def _bb_far(a, b, pad):
    (l0, h0), (l1, h1) = a, b
    for i in range(3):
        if l0[i] - pad > h1[i] or l1[i] - pad > h0[i]:
            return True
    return False


def audit(objs, label="", min_gap=MIN_GAP, verbose=True, ignore=()):
    """Return a list of (severity, message) findings."""
    bpy.context.view_layer.update()
    data = {}
    for ob in objs:
        if ob.type != 'MESH' or not ob.data.polygons:
            continue
        vs, tris, nrm = _tris(ob)
        bvh = BVHTree.FromPolygons([tuple(v) for v in vs], tris, all_triangles=True)
        data[ob.name] = (ob, vs, tris, nrm, bvh, _bbox(vs))

    names = sorted(data)
    findings = []
    ign = set(frozenset(p) for p in ignore)
    for i, na in enumerate(names):
        for nb in names[i + 1:]:
            if frozenset((na, nb)) in ign:
                continue
            _, vsA, trA, nrA, bvhA, bbA = data[na]
            _, vsB, trB, nrB, bvhB, bbB = data[nb]
            if _bb_far(bbA, bbB, min_gap * 3):
                continue

            pairs = bvhA.overlap(bvhB)
            copl = 0
            for ia, ib in pairs:
                if abs(nrA[ia].dot(nrB[ib])) > COPLANAR_DOT:
                    copl += 1
            if pairs:
                findings.append((
                    "COPLANAR" if copl > max(2, len(pairs) * 0.25) else "INTERSECT",
                    "%s <-> %s : %d tri pairs intersect (%d near-coplanar)"
                    % (na, nb, len(pairs), copl)))
                continue

            # not intersecting: how close do they come, and is it a z-fight plane?
            best = 1e9
            bestn = 0.0
            for ti, t in enumerate(trA):
                c = (vsA[t[0]] + vsA[t[1]] + vsA[t[2]]) / 3.0
                hit = bvhB.find_nearest(c, min_gap * 6.0)
                if hit[0] is None:
                    continue
                d = (hit[0] - c).length
                if d < best:
                    best = d
                    bestn = abs(nrA[ti].dot(hit[1]))
            if best < COPLANAR_DIST and bestn > COPLANAR_DOT:
                findings.append(("COPLANAR",
                                 "%s <-> %s : parallel surfaces %.1f mm apart"
                                 % (na, nb, best * 1000)))
            elif best < min_gap:
                findings.append(("TIGHT",
                                 "%s <-> %s : closest approach %.1f mm (< %.0f mm)"
                                 % (na, nb, best * 1000, min_gap * 1000)))

    if verbose:
        print("=" * 66)
        print("AUDIT %s — %d object(s), %d finding(s)" % (label, len(data), len(findings)))
        for sev, msg in findings:
            print("  [%-9s] %s" % (sev, msg))
        if not findings:
            print("  clean.")
        print("=" * 66)
    return findings


def self_coplanar(ob, gap=0.0028, dot=0.985, verbose=True):
    """Faces of ONE object lying almost on top of each other.  Same-mesh
    coplanar pairs z-fight in engine exactly like cross-object ones do, and the
    audit's pairwise sweep cannot see them."""
    vs, tris, nrm = _tris(ob)
    if not tris:
        return 0
    bvh = BVHTree.FromPolygons([tuple(v) for v in vs], tris, all_triangles=True)
    hits = 0
    worst = []
    for i, t in enumerate(tris):
        a, b, c3 = vs[t[0]], vs[t[1]], vs[t[2]]
        c = (a + b + c3) / 3.0
        n = nrm[i]
        # a neighbouring face on a smooth loft is also near-parallel and near-by;
        # only count a hit that sits DIRECTLY over this triangle, not beside it
        rad = max((a - c).length, (b - c).length, (c3 - c).length)
        for s in (1.0, -1.0):
            o = c + n * (s * gap * 0.6)
            hit = bvh.find_nearest(o, gap)
            if hit[0] is None:
                continue
            u = hit[0] - c
            dp = abs(u.dot(n))
            lat = (u - n * u.dot(n)).length
            if dp < 0.0004 or dp > gap or lat > rad * 0.55:
                continue
            if abs(n.dot(hit[1])) > dot:
                hits += 1
                worst.append((dp, c.copy(), n.copy()))
                break
    if verbose and hits:
        worst.sort(key=lambda w: w[0])
        loc = "  ".join("%.2fmm@(%.3f %.3f %.3f)n(%.2f %.2f %.2f)"
                        % (w[0] * 1000, w[1].x, w[1].y, w[1].z, w[2].x, w[2].y, w[2].z)
                        for w in worst[:4])
        print("    %-22s %3d coplanar face(s)\n        %s"
              % (ob.name, hits, loc.replace("  ", "\n        ")))
    return hits


def audit_self(objs, label=""):
    print("COPLANAR-SELF %s" % label)
    tot = 0
    for ob in objs:
        if ob.type == 'MESH':
            tot += self_coplanar(ob)
    if not tot:
        print("    clean.")
    return tot


def clearance(ob_a, ob_b):
    """Signed-ish minimum surface gap between two objects, in mm (None if hit)."""
    bpy.context.view_layer.update()
    vsA, trA, nrA = _tris(ob_a)
    vsB, trB, nrB = _tris(ob_b)
    bvhB = BVHTree.FromPolygons([tuple(v) for v in vsB], trB, all_triangles=True)
    bvhA = BVHTree.FromPolygons([tuple(v) for v in vsA], trA, all_triangles=True)
    if bvhA.overlap(bvhB):
        return None
    best = 1e9
    for v in vsA:
        hit = bvhB.find_nearest(v, 1.0)
        if hit[0] is not None:
            best = min(best, (hit[0] - v).length)
    return best * 1000.0
