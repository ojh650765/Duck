# build_duck.py — DUCK MOW hero: the duck, seated driving pose.
# Run: blender --background --python C:\Duck\Art\Blender\build_duck.py
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc, quad,
                      torus, rbox, recess, set_pivot, make_empty, attach,
                      report_tris, export_fbx, render_previews, bake_ao,
                      fresh_scene, tri_count, TAU)

MAT = "M_Duck"
CREAM, CREAM_SH = "duck_cream", "duck_cream_sh"
ORANGE, ORANGE_SH = "duck_orange", "duck_orange_sh"
CAP_RED, CAP_DARK, CAP_TRIM = "tent_red", "red_deep", "tent_cream"
EYE, BRASS, LENS, STRAP = "eye", "brass", "water_shallow", "grey"
SPARK = "white"          # the catch-light that stops the eye reading as a hole

CROWN = Vector((0.0, -0.048, 0.440))
CAP_TILT = Euler((math.radians(-7.0), math.radians(13.0), math.radians(6.0)), 'XYZ')

N = 14   # main ring resolution

# The head is a separately animated transform: MowerVisuals spins Duck_Head
# about this point every frame (steer look, speed crane, airborne jolt).  The
# skull's underside is therefore built as a piece of the SPHERE OF RADIUS
# NECK_R CENTRED ON NECK_PIV, which is invariant under rotation about that
# pivot, and the neck column runs up through the pivot and dies inside it.  It
# used to be a flat cap butted onto a flat-topped column: two solids that
# merely touched, so any head turn opened the seam.
NECK_PIV = Vector((0.0, -0.050, 0.300))
NECK_R = 0.046


# --------------------------------------------------------------- helpers
def egg_ring(n, z, rx, ryF, ryB, cy, e=2.0):
    """Ring with different front (-Y) and back (+Y) depth -> teardrop chest."""
    pts = []
    k = 2.0 / e
    for i in range(n):
        a = TAU * (i + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        x = math.copysign(abs(c) ** k, c) * rx
        y = math.copysign(abs(s) ** k, s)
        y *= ryB if y >= 0 else ryF
        pts.append(Vector((x, cy + y, z)))
    return pts


def extrude_profile(mb, col, pts2d, z0, z1, col_top=None, smooth=False):
    """Flat outline in XY extruded in Z. pts2d is CCW."""
    bot = [mb.v((x, y, z0)) for (x, y) in pts2d]
    top = [mb.v((x, y, z1)) for (x, y) in pts2d]
    faces = []
    n = len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        faces.append(mb.f((bot[i], bot[j], top[j], top[i]), col))
    faces.append(mb.f(list(reversed(bot)), col))
    faces.append(mb.f(top, col_top or col))
    faces = [f for f in faces if f]
    for f in faces:
        f.smooth = smooth
    mb.bm.normal_update()
    return faces


# --------------------------------------------------------------- BODY + NECK + LEGS
def build_body():
    mb = MB()
    BODY = [
        # z,     rx,    ryF,   ryB,   cy,     e
        (-0.024, 0.058, 0.072, 0.060, -0.012, 2.9),
        (0.012,  0.114, 0.152, 0.114, -0.014, 2.5),
        (0.052,  0.147, 0.188, 0.139, -0.018, 2.3),
        (0.096,  0.159, 0.197, 0.145, -0.026, 2.1),
        (0.140,  0.153, 0.185, 0.137, -0.034, 2.1),
        (0.182,  0.137, 0.161, 0.119, -0.042, 2.0),
        (0.220,  0.115, 0.135, 0.099, -0.048, 2.0),
        (0.250,  0.095, 0.109, 0.079, -0.054, 2.0),
        (0.270,  0.081, 0.091, 0.065, -0.057, 2.0),
    ]
    rings = [egg_ring(N, *s) for s in BODY]
    body_faces, _ = mb.pole_loft(rings, CREAM, pole_a=(0, -0.012, -0.036))
    # shoulder shelf: the body stops FLAT so the thin neck reads as a real step
    mb.loft([rings[-1], egg_ring(N, 0.276, 0.073, 0.082, 0.059, -0.057, 2.0)],
            CREAM, cap_a=False, cap_b=True)

    # --- distinct neck step: a narrow column, clearly thinner than the
    # shoulders, running up THROUGH the head pivot and dying 32 mm above it
    # inside the skull's socket
    neck = [
        ring_z(N, 0.268, 0.036, 0.038, 2.0, 0.0, -0.056),
        ring_z(N, 0.300, 0.034, 0.034, 2.0, 0.0, -0.050),
        ring_z(N, 0.322, 0.029, 0.029, 2.0, 0.0, -0.050),
        ring_z(N, 0.332, 0.015, 0.015, 2.0, 0.0, -0.050),
    ]
    mb.loft(neck, CREAM, cap_a=False, cap_b=True)

    # --- breast shadow: darker cream low on the belly (tier-3 read, keeps it grounded)
    for f in body_faces:
        if not f.is_valid:
            continue
        c = f.calc_center_median()
        if c.z < 0.055 and c.y < -0.02 and f.normal.z < 0.2:
            f[mb.lay] = mb.cid(CREAM_SH)

    # --- legs (orange), body-owned; feet are separate objects pivoted at the ankle.
    # Short and chunky: a seated duck has almost no visible leg.
    for sx in (-1, 1):
        path = [Vector((sx * 0.070, -0.100, 0.036)),
                Vector((sx * 0.076, -0.140, -0.018)),
                Vector((sx * 0.080, -0.172, -0.066)),
                Vector((sx * 0.080, -0.196, -0.108))]
        tube(mb, ORANGE, path, [0.050, 0.043, 0.036, 0.031], n=8,
             cap_a=False, cap_b=False)

    return mb.finish("Duck_Body", MAT)


# --------------------------------------------------------------- HEAD
def build_head():
    mb = MB()
    # Big, chunky skull.  One dominant mass; the bill is the secondary.
    HEAD = [
        (0.334, 0.075, 0.086, 0.075, -0.052, 2.1),
        (0.358, 0.089, 0.104, 0.092, -0.053, 2.2),
        (0.386, 0.092, 0.107, 0.097, -0.052, 2.2),
        (0.415, 0.085, 0.094, 0.090, -0.050, 2.2),
        (0.440, 0.062, 0.066, 0.066, -0.048, 2.1),
    ]
    rings = [egg_ring(N, *s) for s in HEAD]
    # the skull's underside IS the socket: four sections lying exactly on the
    # sphere about the head pivot, lofted straight on into the skull
    # Built with ring_z on egg_ring's half-step phase, NOT with sweep_rings:
    # sweep_rings frames a vertical path as rt=-X, so its rings wind the other
    # way round from egg_ring and lofting the two index-to-index twists the
    # skin 180 deg through its own axis.
    sp, sr = L.socket_path(NECK_PIV, (0, 0, 1), NECK_R, lats=(24, 55, 84, 104))
    sock = [ring_z(N, p.z, r, r, 2.0, p.x, p.y, TAU * 0.5 / N)
            for p, r in zip(sp, sr)]
    head_faces, _ = mb.pole_loft(sock + rings, CREAM,
                                 pole_a=tuple(NECK_PIV - Vector((0, 0, NECK_R))),
                                 pole_b=(0.0, -0.046, 0.456))
    # cheek shading under the jaw so the head does not float off the neck
    for f in head_faces:
        if f.is_valid and f.calc_center_median().z < 0.345 and f.normal.z < 0.1:
            f[mb.lay] = mb.cid(CREAM_SH)

    # --- EYES.  The duck's earnestness lives here, so the eye has to survive a
    # 25 m camera: a BIG round bulb, wide-set on the flank of the skull where
    # both the front and the chase camera can see it, with a bright catch-light
    # aimed at the key so it never reads as a dark slit.  Nothing sits over it:
    # the goggles now live on the cap, 50 mm higher.
    for sx in (-1, 1):
        c = Vector((sx * 0.082, -0.102, 0.406))
        sphere(mb, EYE, c, (0.030, 0.029, 0.032), seg=10, rings=5)
        d = Vector((sx * 0.60, -0.66, 0.45)).normalized()
        sphere(mb, SPARK, c + d * 0.0225, (0.0125, 0.0125, 0.0125), seg=6, rings=3)

    # brow: a band of shadow cream over each eye so the bulb has a socket to
    # sit in rather than floating on a flat cheek
    for f in head_faces:
        if not f.is_valid:
            continue
        p = f.calc_center_median()
        if p.z > 0.420 and p.y < -0.060 and abs(p.x) > 0.030:
            f[mb.lay] = mb.cid(CREAM_SH)

    return mb.finish("Duck_Head", MAT)


# --------------------------------------------------------------- BILL
def build_bill():
    mb = MB()
    # broad, flat, spatulate.  Readable at 25 m: 0.15 m wide against a 0.17 m head.
    BILL = [
        # y,      rx,    rz,    cz,     e
        # deliberately WIDER than the skull (0.092) so the bill breaks the
        # head-on silhouette - this is the whole silhouette gate for the duck
        (-0.140, 0.072, 0.036, 0.372, 2.6),
        (-0.186, 0.088, 0.030, 0.365, 3.2),
        (-0.228, 0.098, 0.026, 0.358, 3.8),
        (-0.264, 0.100, 0.022, 0.352, 4.2),
        (-0.290, 0.086, 0.019, 0.347, 3.6),
    ]
    rings = []
    for (y, rx, rz, cz, e) in BILL:
        # rot so a face band sits exactly on the ±X mid-height extreme -> mouth seam
        rings.append(ring_y(12, y, rx, rz, e, 0.0, cz, rot=-math.pi / 12))
    faces, _ = mb.pole_loft(rings, ORANGE, pole_b=(0.0, -0.306, 0.345))
    mb.loft([ring_y(12, -0.128, 0.058, 0.038, 2.4, 0.0, 0.374, rot=-math.pi / 12),
             rings[0]], ORANGE, cap_a=True, cap_b=False)

    # lower mandible darker so the bill has a read from below
    for f in faces:
        if f.is_valid and f.normal.z < -0.35:
            f[mb.lay] = mb.cid(ORANGE_SH)

    # --- mouth line CUT IN, not glued on (SKILL.md technique 2)
    mouth = [f for f in faces if f.is_valid and abs(f.normal.z) < 0.45
             and abs(f.calc_center_median().x) > 0.045]
    recess(mb, mouth, thickness=0.0032, depth=-0.0035, col=ORANGE_SH)

    # nostrils, cut in near the root
    for sx in (-1, 1):
        n0 = [f for f in faces if f.is_valid and f.normal.z > 0.6
              and -0.200 < f.calc_center_median().y < -0.160
              and (f.calc_center_median().x * sx) > 0.012]
        recess(mb, n0[:1], thickness=0.010, depth=-0.006, col=ORANGE_SH)

    return mb.finish("Duck_Bill", MAT)


# --------------------------------------------------------------- CAP
def build_cap():
    mb = MB()
    # flat cap, built upright about the crown then rotated askew (ART_BIBLE §5).
    # The GOGGLES ride the cap now, up on the brim, clear of the eyes -- and the
    # strap wraps the CAP, so nothing passes through the skull any more.
    rings = [
        ring_z(12, 0.432, 0.106, 0.114, 2.3, 0.0, -0.046),
        ring_z(12, 0.448, 0.109, 0.117, 2.3, 0.0, -0.048),
        ring_z(12, 0.463, 0.094, 0.100, 2.2, 0.0, -0.052),
        ring_z(12, 0.473, 0.060, 0.064, 2.1, 0.0, -0.056),
    ]
    faces, _ = mb.pole_loft(rings, CAP_RED, pole_b=(0.0, -0.058, 0.479))
    # underside band (darker) closing the crown
    mb.loft([ring_z(12, 0.432, 0.106, 0.114, 2.3, 0.0, -0.046),
             ring_z(12, 0.424, 0.097, 0.104, 2.3, 0.0, -0.046)],
            CAP_DARK, cap_a=False, cap_b=True)
    # peak / brim, forward
    # narrow peak: must NOT hide the bill in the head-on silhouette
    brim = [
        (0.058, -0.120), (0.044, -0.158), (0.022, -0.176), (0.0, -0.180),
        (-0.022, -0.176), (-0.044, -0.158), (-0.058, -0.120),
        (-0.052, -0.092), (0.052, -0.092),
    ]
    extrude_profile(mb, CAP_DARK, brim, 0.4255, 0.4375, col_top=CAP_RED)
    # top button
    sphere(mb, CAP_TRIM, (0.0, -0.058, 0.4805), (0.014, 0.014, 0.010), seg=8, rings=3)

    # --- tilt the CAP askew now, so the goggles below can be authored in final
    # coordinates.  Rotating them with the cap instead threw one lens 100 mm
    # off-centre; the cap is allowed to sit crooked, the goggles are not.
    R = CAP_TILT.to_matrix()
    bmesh.ops.rotate(mb.bm, verts=list(mb.bm.verts), cent=CROWN, matrix=R)
    Mrot = (Matrix.Translation(CROWN) @ R.to_4x4() @ Matrix.Translation(-CROWN))

    # --- goggles, sitting on the brim, 50 mm above the eye ------------------
    GC = [Vector((sx * 0.056, -0.150, 0.480)) for sx in (1, -1)]
    for sx, c in zip((1, -1), GC):
        ax = Vector((sx * 0.16, -0.66, 0.735)).normalized()
        torus(mb, BRASS, c, ax, 0.031, 0.0085, nmaj=10, nmin=5)
        # a shallow CONE, not a cylinder: a cylindrical lens inside a torus rim
        # gives two coaxial surfaces of revolution 2 mm apart, which z-fights
        cyl(mb, LENS, c - ax * 0.010, c + ax * 0.010, 0.035, 0.020, n=10)
    # strap: a band round the CAP crown, following its askew surface
    # sunk ~4 mm into the crown so the tube MERGES with it; a strap laid exactly
    # on the surface is a coplanar pair and z-fights in engine
    band = [(0.078, -0.131, 0.446), (0.104, -0.048, 0.442), (0.078, 0.036, 0.440),
            (0.0, 0.064, 0.438), (-0.078, 0.036, 0.440), (-0.104, -0.048, 0.442),
            (-0.078, -0.131, 0.446)]
    strap = [GC[0]] + [Mrot @ Vector(p) for p in band] + [GC[1]]
    tube(mb, STRAP, strap, 0.0085, n=5, cap_a=True, cap_b=True)

    return mb.finish("Duck_Cap", MAT)


# --------------------------------------------------------------- WINGS
def build_wing(sx, name):
    mb = MB()
    SH = Vector((sx * 0.146, -0.046, 0.198))
    # A reaching arm, not a folded wing: swings out from the wide shoulder, down
    # and forward, then in and up to the wheel, ending in a rounded "fist".
    PATH = [
        # y,      cx,     cz,    rx,    rz
        (-0.016, 0.146, 0.198, 0.042, 0.078),
        (-0.072, 0.156, 0.176, 0.040, 0.068),
        (-0.132, 0.152, 0.168, 0.037, 0.056),
        (-0.192, 0.130, 0.182, 0.034, 0.046),
        (-0.268, 0.104, 0.202, 0.033, 0.041),
        (-0.298, 0.090, 0.212, 0.036, 0.040),
    ]
    rings = []
    for (y, cx, cz, rx, rz) in PATH:
        rings.append(ring_y(10, y, rx, rz, 2.05, sx * cx, cz))
    faces, _ = mb.pole_loft(rings, CREAM, pole_b=(sx * 0.082, -0.312, 0.217))
    # shoulder cap buried inside the body
    mb.loft([ring_y(10, 0.028, 0.038, 0.074, 2.05, sx * 0.132, 0.204), rings[0]],
            CREAM, cap_a=True, cap_b=False)
    # underside shading
    for f in faces:
        if f.is_valid and f.normal.z < -0.4:
            f[mb.lay] = mb.cid(CREAM_SH)
    # two primary-feather grooves cut into the outer face
    outer = [f for f in faces if f.is_valid and (f.normal.x * sx) > 0.55]
    outer.sort(key=lambda f: f.calc_center_median().y)
    if len(outer) >= 4:
        recess(mb, outer[1:2], thickness=0.006, depth=-0.004, col=CREAM_SH)
        recess(mb, outer[2:3], thickness=0.006, depth=-0.004, col=CREAM_SH)
    return mb.finish(name, MAT)


# --------------------------------------------------------------- TAIL
def build_tail():
    mb = MB()
    T = [
        (0.078, 0.076, 0.058, 0.212, 2.2),
        (0.126, 0.070, 0.044, 0.238, 2.4),
        (0.170, 0.056, 0.030, 0.268, 2.6),
        (0.200, 0.038, 0.020, 0.292, 2.6),
    ]
    rings = [ring_y(10, y, rx, rz, e, 0.0, cz) for (y, rx, rz, cz, e) in T]
    faces, _ = mb.pole_loft(rings, CREAM, pole_b=(0.0, 0.220, 0.304))
    mb.loft([ring_y(10, 0.056, 0.074, 0.060, 2.2, 0.0, 0.200), rings[0]],
            CREAM, cap_a=True, cap_b=False)
    for f in faces:
        if f.is_valid and f.normal.z < -0.3:
            f[mb.lay] = mb.cid(CREAM_SH)
    return mb.finish("Duck_Tail", MAT)


# --------------------------------------------------------------- FEET
def build_foot(sx, name):
    mb = MB()
    X = sx * 0.080
    # big webbed paddle - three toe lobes, wide enough to read at 25 m
    out = [
        (0.032, -0.176), (0.052, -0.214), (0.066, -0.252), (0.064, -0.302),
        (0.038, -0.320), (0.027, -0.290), (0.011, -0.328), (-0.011, -0.328),
        (-0.027, -0.290), (-0.038, -0.320), (-0.064, -0.302), (-0.066, -0.252),
        (-0.052, -0.214), (-0.032, -0.176),
    ]
    pts = [(X + x, y) for (x, y) in out]
    extrude_profile(mb, ORANGE_SH, pts, -0.138, -0.114, col_top=ORANGE)
    # ankle stub so the paddle joins the leg
    cyl(mb, ORANGE, (X, -0.196, -0.092), (X, -0.188, -0.126), 0.028, 0.032, n=8)
    return mb.finish(name, MAT)


# Deliberate sockets: limbs and head plug into the body, the bill into the
# skull, the cap over the skull.  Everything else must not intersect.
DUCK_SOCKETS = (
    ("Duck_Body", "Duck_Head"), ("Duck_Body", "Duck_Tail"),
    ("Duck_Body", "Duck_Wing_L"), ("Duck_Body", "Duck_Wing_R"),
    ("Duck_Body", "Duck_Foot_L"), ("Duck_Body", "Duck_Foot_R"),
    ("Duck_Head", "Duck_Bill"), ("Duck_Head", "Duck_Cap"),
)


# =============================================================== BUILD
def main():
    fresh_scene()

    body = build_body()
    head = build_head()
    bill = build_bill()
    cap = build_cap()
    wl = build_wing(1, "Duck_Wing_L")
    wr = build_wing(-1, "Duck_Wing_R")
    tail = build_tail()
    fl = build_foot(1, "Duck_Foot_L")
    fr = build_foot(-1, "Duck_Foot_R")

    meshes = [body, head, bill, cap, wl, wr, tail, fl, fr]

    # AO baked in world space while everything is still at the origin
    bake_ao(meshes, floor=0.76, dist=0.13, samples=32, ground=False)
    L.joint_audit(body, head, (0.0, -0.050, 0.300), "Duck", near=0.22)

    # pivots -------------------------------------------------------------
    set_pivot(body, (0.0, 0.0, 0.0))              # seat contact
    set_pivot(head, (0.0, -0.050, 0.300))         # neck joint
    set_pivot(bill, (0.0, -0.140, 0.372))         # bill root
    set_pivot(cap,  (0.0, -0.048, 0.440))         # head top / crown
    set_pivot(wl,   (0.146, -0.046, 0.198))       # shoulder
    set_pivot(wr,   (-0.146, -0.046, 0.198))
    set_pivot(tail, (0.0, 0.066, 0.206))          # tail root
    set_pivot(fl,   (0.080, -0.192, -0.108))      # ankle
    set_pivot(fr,   (-0.080, -0.192, -0.108))

    if L.want_tilt():
        L.tilt_sheet(head, meshes, NECK_PIV, None, "duck")

    root = make_empty("Duck_Root", (0, 0, 0))
    for o in (body, head, wl, wr, tail, fl, fr):
        attach(o, root)
    attach(bill, head)
    attach(cap, head)

    objs = [root] + meshes
    report_tris(meshes, "Duck")
    L.verify_meshes(meshes, "Duck")
    try:
        import audit_lib
        audit_lib.audit(meshes, "DUCK", ignore=DUCK_SOCKETS)
        audit_lib.audit_self(meshes, "DUCK")
    except Exception as e:
        print("audit skipped:", e)
    export_fbx(objs, os.path.join(L.MODELS_DIR, "Duck.fbx"))
    render_previews(meshes, "duck", res=512,
                    extra_views=[("backq", (0.70, 1.0, 0.34))])
    print("DONE Duck")


main()
