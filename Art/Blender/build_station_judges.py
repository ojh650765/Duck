# build_station_judges.py — DUCK MOW: three seated judges for the RIVAL plots'
# judging stations.
# Run: blender --background --python C:\Duck\Art\Blender\build_station_judges.py
#
# Deliberately NOT the player's panel (goat / badger / heron) — these are a
# tortoise, a fox and an owl, so the far side of the field reads as its own
# competition.  They are only ever seen from 40-90 m, so they are built far
# simpler than the hero judges: under 700 tris each, two meshes apiece, no
# scorecards, no spectacles, no fine trim.  What survives at that distance is
# outline and one big colour, and that is all they carry.
#
# <Name>_Root / _Body / _Head.  Base z = 0 is the SEAT contact point, exactly as
# build_judges.py, so they drop straight onto a bench.  Head pivot at the neck.
# No rig — Unity animates the named transforms.
#
#   Cornelius (tortoise) 0.96 m  broad dome + a thin neck poking out low
#   Vesper    (fox)      1.06 m  narrow cone + tall pointed ears + brush tail
#   Ottilie   (owl)      0.94 m  barrel with NO neck and an enormous round head
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, TAU)

MAT = "M_StationJudges"
CREAM, CREAM_SH = "F6EBD2", "DCC9A4"
WHITE = "F1EDE0"
DARK, SLATE = "241F1E", "4A4F55"
WOOD, WOOD_D = "9A6B41", "6E4A2C"
DIRT, BRASS = "B99A6B", "C9A55A"
ORANGE, ORANGE_D = "F2A03D", "D3792A"
SAGE = "7FA8A0"
TIE_RED = "D8534E"


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


def collar(mb, ox, p, d, r):
    """The officials' collar every station judge wears — one cream disc across
    the neck.  It is the only thing tying the three together at 60 m."""
    p, d = Vector(p), Vector(d).normalized()
    cyl(mb, CREAM, p - d * 0.016, p + d * 0.012, r, r * 0.72, n=8, smooth=False)


# ================================================================== CORNELIUS
def cornelius(ox):
    P = {}
    global COR_PIV, COR_R, COR_AX
    COR_PIV = Vector((ox, -0.348, 0.784))      # inside the base of the skull
    COR_R = 0.068
    COR_AX = (0, -0.21, 0.978)
    mb = MB()
    SHELL = [(0.000, 0.252, 0.216, 3.6), (0.130, 0.302, 0.258, 3.0),
             (0.292, 0.298, 0.254, 2.6), (0.442, 0.238, 0.202, 2.3),
             (0.552, 0.146, 0.126, 2.1)]
    rings = [ring_z(12, z, rx, ry, e, ox, -0.012 * (z / 0.55))
             for (z, rx, ry, e) in SHELL]
    shell, _ = mb.pole_loft(rings, WOOD_D, pole_b=(ox, -0.020, 0.614),
                            smooth=False)
    # scutes: recolour alternate plates.  A per-face recess would cost 12 tris
    # each and none of it survives 60 m; the tonal break does.
    for i, f in enumerate(shell):
        if f.is_valid and f.normal.z > 0.05 and (i % 2 == 0):
            f[mb.lay] = mb.cid(WOOD)
    # plastron rim — the hard horizontal that says "shell", not "boulder"
    mb.loft([ring_z(12, 0.052, 0.268, 0.230, 3.4, ox),
             ring_z(12, 0.006, 0.250, 0.214, 3.6, ox)],
            DIRT, cap_a=False, cap_b=True, smooth=False)
    # Neck: long, THIN and craned forward-up.  It has to hold the head clear of
    # the dome or the whole thing reads as one brown boulder at 60 m.
    # It carries on THROUGH the head pivot and dies inside the skull's throat
    # socket; before, it stopped at the pivot and the cap swung into view.
    neck = [Vector((ox, -0.140, 0.396)), Vector((ox, -0.262, 0.560)),
            Vector((ox, -0.330, 0.700)), COR_PIV,
            COR_PIV + Vector(COR_AX) * 0.042]
    nr = [0.102, 0.078, 0.062, 0.056, 0.028]
    tube(mb, DIRT, neck, nr, n=8, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(COR_PIV, neck[-2:], nr[-2:], COR_R * 0.94, "Cornelius neck")
    for sx in (-1, 1):     # stumpy forelimbs resting on the bench
        cyl(mb, DIRT, (ox + sx * 0.238, -0.140, 0.300),
            (ox + sx * 0.286, -0.268, 0.212), 0.074, 0.058, n=6, smooth=False)
    collar(mb, ox, (ox, -0.244, 0.520), (0, -0.60, 0.80), 0.116)
    P["Body"] = (mb.finish("Cornelius_Body", MAT, 34.0), (ox, 0.0, 0.0))

    mb = MB()
    HEAD = [(-0.300, 0.086, 0.084, 0.818, 2.6),
            (-0.374, 0.098, 0.094, 0.848, 2.4),
            (-0.450, 0.076, 0.070, 0.852, 2.6),
            (-0.506, 0.050, 0.044, 0.844, 2.6)]
    hr = [ring_y(10, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    hd, _ = mb.pole_loft(hr, DIRT, pole_a=(ox, -0.258, 0.800),
                         pole_b=(ox, -0.542, 0.834), smooth=False)
    col_faces(mb, hd, lambda f: f.normal.z < -0.35, WOOD_D)
    # throat: spherical about the pivot below, buried in the skull above
    L.throat(mb, DIRT, COR_PIV, COR_AX, COR_R,
             [(ox, -0.392, 0.838)], [0.058],
             n=8, e=2.4, lats=(26, 62, 96), smooth=False)
    for sx in (-1, 1):
        sphere(mb, "F7F3E4", (ox + sx * 0.066, -0.412, 0.876),
               (0.030, 0.028, 0.030), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.070, -0.430, 0.874),
               (0.019, 0.018, 0.019), seg=6, rings=3)
    # heavy brow — an old, unimpressed tortoise
    rbox(mb, WOOD_D, (ox, -0.410, 0.912), (0.166, 0.086, 0.026),
         r=0.010, n=8, e=4.0, k=1)
    P["Head"] = (mb.finish("Cornelius_Head", MAT, 34.0), tuple(COR_PIV))
    return P


# ===================================================================== VESPER
def vesper(ox):
    P = {}
    global VES_PIV, VES_R, VES_AX
    VES_PIV = Vector((ox, -0.058, 0.700))      # inside the base of the skull
    VES_R = 0.072
    VES_AX = (0, -0.04, 0.999)
    mb = MB()
    # Narrow: she is the TALL THIN one of the three, or she collides with the owl.
    BODY = [
        (0.000, 0.184, 0.182, 0.176,  0.000, 3.6),
        (0.120, 0.190, 0.188, 0.180, -0.010, 3.2),
        (0.280, 0.172, 0.170, 0.160, -0.026, 2.8),
        (0.430, 0.140, 0.138, 0.128, -0.042, 2.5),
        (0.550, 0.104, 0.102, 0.094, -0.054, 2.3),
        (0.628, 0.074, 0.072, 0.066, -0.060, 2.2),
    ]
    rings = [egg_ring(12, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, ORANGE, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.058, DARK)
    # cream shirt front, cut in
    ch = [f for f in body if f.is_valid and f.normal.y < -0.42
          and 0.090 < f.calc_center_median().z < 0.500]
    recess(mb, ch, 0.018, -0.009, col=CREAM)
    # the brush tail, swept round to one side and up — the silhouette
    tube(mb, ORANGE, [Vector((ox + 0.078, 0.166, 0.120)),
                      Vector((ox + 0.216, 0.176, 0.212)),
                      Vector((ox + 0.298, 0.112, 0.378)),
                      Vector((ox + 0.296, 0.058, 0.498))],
         [0.100, 0.114, 0.096, 0.060], n=6, smooth=True)
    sphere(mb, CREAM, (ox + 0.292, 0.044, 0.530), (0.056, 0.056, 0.054),
           seg=8, rings=3)
    neck = [Vector((ox, -0.056, 0.572)), VES_PIV,
            VES_PIV + Vector(VES_AX) * 0.042]
    nr = [0.080, 0.062, 0.030]
    tube(mb, ORANGE, neck, nr, n=8, cap_a=False, cap_b=True, smooth=False)
    L.socket_fit(VES_PIV, neck[-2:], nr[-2:], VES_R * 0.94, "Vesper neck")
    collar(mb, ox, (ox, -0.062, 0.606), (0, -0.14, 0.99), 0.100)
    P["Body"] = (mb.finish("Vesper_Body", MAT, 34.0), (ox, 0.0, 0.0))

    mb = MB()
    HEAD = [(0.062, 0.084, 0.082, 0.744, 2.6),
            (-0.020, 0.104, 0.100, 0.748, 2.8),
            (-0.114, 0.072, 0.066, 0.726, 3.0),
            (-0.184, 0.042, 0.038, 0.710, 2.8)]
    hr = [ring_y(10, y, rx, rz, e, ox, cz) for (y, rx, rz, cz, e) in HEAD]
    hd, _ = mb.pole_loft(hr, ORANGE, pole_a=(ox, 0.104, 0.742),
                         pole_b=(ox, -0.224, 0.702), smooth=False)
    # ruff / throat: a piece of the sphere about the pivot, so the join under
    # her jaw is fixed at every angle
    L.throat(mb, ORANGE, VES_PIV, VES_AX, VES_R,
             [(ox, -0.070, 0.750)], [0.054],
             n=8, e=2.4, lats=(26, 62, 96), smooth=False)
    col_faces(mb, hd, lambda f: f.normal.z < -0.30
              and f.calc_center_median().y < -0.070, CREAM)
    sphere(mb, DARK, (ox, -0.220, 0.704), (0.024, 0.020, 0.019), seg=6, rings=3)
    for sx in (-1, 1):
        sphere(mb, "F7F3E4", (ox + sx * 0.058, -0.108, 0.762),
               (0.026, 0.023, 0.026), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.062, -0.124, 0.760),
               (0.016, 0.015, 0.016), seg=6, rings=3)
        # tall pointed ears — at 60 m these ARE the fox
        tube(mb, ORANGE, [Vector((ox + sx * 0.054, 0.014, 0.808)),
                          Vector((ox + sx * 0.076, 0.026, 0.926)),
                          Vector((ox + sx * 0.088, 0.018, 0.998))],
             [0.052, 0.032, 0.007], n=6, e=3.0,
             squash=[(0.62, 1.0)] * 3, smooth=False)
        tube(mb, DARK, [Vector((ox + sx * 0.048, -0.004, 0.812)),
                        Vector((ox + sx * 0.070, 0.008, 0.938))],
             [0.032, 0.012], n=5, e=3.0, squash=[(0.46, 1.0)] * 2, smooth=False)
    P["Head"] = (mb.finish("Vesper_Head", MAT, 34.0), tuple(VES_PIV))
    return P


# ==================================================================== OTTILIE
def ottilie(ox):
    P = {}
    global OTT_PIV
    # Deep inside the skull: an owl has no neck, so the socket is the skull's
    # own underside and the pivot has to sit far enough in that the underside
    # never lifts off the shoulders.
    OTT_PIV = Vector((ox, -0.048, 0.520))
    mb = MB()
    # Squat and NARROW-SHOULDERED, so the head can be wider than the body.
    BODY = [
        (0.000, 0.196, 0.174, 0.174,  0.000, 3.8),
        (0.104, 0.212, 0.188, 0.180, -0.008, 3.2),
        (0.234, 0.204, 0.180, 0.168, -0.016, 2.8),
        (0.348, 0.170, 0.150, 0.138, -0.024, 2.5),
        (0.420, 0.128, 0.114, 0.104, -0.028, 2.3),
        # One more, narrow section: it tucks the body's top INSIDE the ball the
        # head's underside describes about its own pivot, so no amount of head
        # rotation can bring the shoulder out from under that enormous skull.
        (0.490, 0.082, 0.074, 0.070, -0.030, 2.2),
    ]
    rings = [egg_ring(12, z, rx, rf, rb, cy, e) for (z, rx, rf, rb, cy, e) in BODY]
    for r in rings:
        for p in r:
            p.x += ox
    body, _ = mb.loft(rings, WOOD_D, cap_a=True, cap_b=True, smooth=False)
    col_faces(mb, body, lambda f: f.calc_center_median().z < 0.048, DARK)
    # folded wings cut into the flanks so she is not a barrel
    wg = [f for f in body if f.is_valid and abs(f.normal.x) > 0.40
          and 0.065 < f.calc_center_median().z < 0.360]
    recess(mb, wg, 0.018, -0.010, col=DARK)
    # pale barred breast
    br = [f for f in body if f.is_valid and f.normal.y < -0.45
          and 0.070 < f.calc_center_median().z < 0.340]
    recess(mb, br, 0.016, -0.008, col=CREAM_SH)
    collar(mb, ox, (ox, -0.050, 0.400), (0, -0.20, 0.98), 0.126)
    P["Body"] = (mb.finish("Ottilie_Body", MAT, 34.0), (ox, 0.0, 0.0))

    # ---- HEAD: no neck, and WIDER THAN THE BODY.  That is the whole owl. ----
    # A tall head with tall tufts would just be the fox again, so this one is a
    # broad flattened disc with two stubby nubs at the outer corners.
    mb = MB()
    sphere(mb, WOOD_D, (ox, -0.050, 0.590), (0.256, 0.188, 0.174),
           seg=12, rings=5)
    # The facial dish: big, white, the single brightest thing on the plot.  It
    # must sit PROUD of the skull (skull front is y -0.238) or the sphere eats
    # it and all that survives is a white moustache.
    sphere(mb, WHITE, (ox, -0.174, 0.582), (0.204, 0.088, 0.152),
           seg=12, rings=4)
    for sx in (-1, 1):
        sphere(mb, "F7F3E4", (ox + sx * 0.088, -0.252, 0.606),
               (0.062, 0.038, 0.062), seg=8, rings=3)
        sphere(mb, BRASS, (ox + sx * 0.088, -0.268, 0.606),
               (0.041, 0.027, 0.041), seg=8, rings=3)
        sphere(mb, DARK, (ox + sx * 0.088, -0.280, 0.606),
               (0.025, 0.019, 0.025), seg=6, rings=3)
        # stubby corner tufts, right out at the rim
        tube(mb, WOOD_D, [Vector((ox + sx * 0.184, -0.038, 0.700)),
                          Vector((ox + sx * 0.222, -0.028, 0.762))],
             [0.052, 0.007], n=6, e=3.0,
             squash=[(0.78, 1.0)] * 2, smooth=False)
    # beak
    hr = [ring_y(6, -0.238, 0.032, 0.038, 2.6, ox, 0.540),
          ring_y(6, -0.284, 0.021, 0.024, 2.6, ox, 0.522)]
    mb.pole_loft(hr, BRASS, pole_b=(ox, -0.314, 0.504), smooth=False)
    P["Head"] = (mb.finish("Ottilie_Head", MAT, 34.0), tuple(OTT_PIV))
    return P


# ======================================================================= BUILD
PARTS = ("Body", "Head")


def main():
    fresh_scene()
    specs = [("Cornelius", cornelius, -1.05), ("Vesper", vesper, 0.0),
             ("Ottilie", ottilie, 1.05)]
    roots, allobjs, allmesh, groups = [], [], [], []
    for name, fn, ox in specs:
        P = fn(ox)
        meshes = [P[k][0] for k in PARTS]
        bake_ao(meshes, floor=0.78, dist=0.22, samples=24, ground=True,
                ground_size=1.6)
        hi = max(max(v.co.z for v in ob.data.vertices) for ob in meshes)
        L.joint_audit(P["Body"][0], P["Head"][0], P["Head"][1], name, near=0.34,
                      tol=0.004)
        for k in PARTS:
            set_pivot(P[k][0], P[k][1])
        root = make_empty(name + "_Root", (ox, 0, 0))
        for k in PARTS:
            attach(P[k][0], root)
        roots.append((root, ox))
        groups.append((name, meshes, P["Head"][1]))
        allobjs += [root] + meshes
        allmesh += meshes
        report_tris(meshes, "%s (seat->top %.2f m)" % (name, hi))

    for root, ox in roots:
        root.location = (0, 0, 0)
    L.verify_meshes(allmesh, "StationJudges")
    export_fbx(allobjs, os.path.join(L.MODELS_DIR, "StationJudges.fbx"))
    for root, ox in roots:
        root.location = (ox, 0, 0)
    if L.want_tilt():
        for name, meshes, piv in groups:
            hd = [m for m in meshes if m.name.endswith("_Head")][0]
            L.tilt_sheet(hd, meshes, piv, None, "sjudge_" + name.lower())
    for name, meshes, piv in groups:
        render_previews(meshes, "sjudge_" + name.lower(), res=512, solo=True)
    render_previews(allmesh, "stationjudges", res=700)
    print("DONE StationJudges")


main()
