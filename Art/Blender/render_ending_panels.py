# render_ending_panels.py — the six ENDING panels: three for the duck who keeps
# the pond and three for the duck who does not.
#
# DuckEndingBuilder.cs owns the two sequences. As with the opening, the Ken Burns
# rects in that file are the real brief: a panel is not "a nice picture", it is a
# picture whose subject sits inside the rect its move ENDS on, because that is the
# frame the player is looking at when the beat lands. Every panel below states its
# end rect in its docstring and is composed for it.
#
# NOTHING IS RE-MODELLED HERE.
#
# render_cutscene_panels.py already owns the pond, the duck, the sign boards, the
# excavator, the flyer, the lighting rig and the camera helper, and it owns them in
# a form that has already survived a full review pass. So this script imports that
# module rather than copying it — same strip-the-trailing-main() trick it uses on
# the build scripts, for the same reason (its last line renders ten panels). The
# endings are therefore guaranteed to be the same pond, in the same palette, lit
# with the same rig as the story that opened the game. That is the entire point of
# an ending: it has to be recognisably the same place.
#
# What is new here is only what the endings need and the opening did not: a
# presentation cheque, ducklings, a deck chair, construction hoarding, concrete
# pipe, a SOLD sash, and a pond with the water taken out of it.
#
# TWO PLACES, NOT ONE. The opening's rule was one pond lit nine ways. The endings
# break it exactly once, for the win's first panel, because the prize is handed
# over at the show and not at the pond — and the whole point of the win is that the
# duck GOES BACK. One panel away and two panels home reads as a journey; three
# panels at the bench would read as a different game's ending.
#
# Run:  blender --background --python C:\Duck\Art\Blender\render_ending_panels.py
#       ... --only=win1,lose3     just those panels (iteration)
import bpy, math, os, sys, types, random
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L
from duck_lib import (MB, cyl, tube, sphere, disc, quad, rbox, recess, flat_card,
                      make_empty, attach, bake_ao, hexcol, TAU)


def load(name):
    """Import a script for its builders WITHOUT running its main().

    Identical to render_cutscene_panels.load(), and duplicated rather than
    imported because this is the function that does the importing — there is
    nowhere to import it FROM until it has run once.
    """
    path = os.path.join(HERE, name + ".py")
    with open(path, "r", encoding="utf-8") as fh:
        src = fh.read().rstrip()
    if src.endswith("main()"):
        src = src[:-len("main()")]
    mod = types.ModuleType(name)
    mod.__file__ = path
    sys.modules[name] = mod
    exec(compile(src + "\n", path, "exec"), mod.__dict__)
    return mod


# The opening's renderer, as a library. Importing it also imports build_duck,
# build_mower, build_foliage and build_props through its own load(), so every
# builder those own is reachable as C.DUCK, C.MOWER, C.FOL, C.PROPS.
C = load("render_cutscene_panels")
JUDGES = load("build_judges")

OUT_DIR = C.OUT_DIR          # one cutscene texture folder; the names cannot collide
RES = C.RES
MAT = C.MAT

# Palette, straight off the opening's, which is straight off DuckSceneBuilder.P.
GRASS, GRASS_LIT, GRASS_DK = C.GRASS, C.GRASS_LIT, C.GRASS_DK
WATER, DIRT = C.WATER, C.DIRT
WOOD, WOOD_D = C.WOOD, C.WOOD_D
CREAM, CHALK, FENCE = C.CREAM, C.CHALK, C.FENCE
RED, RED_D = C.RED, C.RED_D
BRASS, GREY, DARK = C.BRASS, C.GREY, C.DARK
YELLOW, YELLOW_D = C.YELLOW, C.YELLOW_D
SUNLIGHT = C.SUNLIGHT
POND_CY, POND_RX, POND_RY = C.POND_CY, C.POND_RX, C.POND_RY
FLOAT_Z, LAND_Z = C.FLOAT_Z, C.LAND_Z

# The three colours the endings add, and each one is a decision.
#
# DUCKLING is the only unmixed yellow in the win, so it is the thing the eye finds
# on a panel that is otherwise all amber water — a duckling painted in the duck's
# own cream would simply be a small duck-shaped smudge at that distance.
#
# HOARD and CONCRETE are the loss's equivalents and they are chosen to be WRONG for
# this game: a flat industrial blue and a dead grey, neither of which appears
# anywhere in DuckSceneBuilder.P. The excavator's yellow was already the one
# intruding colour in the opening; these two are what it brought with it.
DUCKLING, DUCKLING_SH = "#F2C64B", "#DCA731"
HOARD, HOARD_D = "#4F6E86", "#3B5568"
CONCRETE, CONCRETE_D = "#A5A49C", "#6E6E68"


def render(tag, idx):
    """Write one panel where DuckEndingBuilder.PanelPath expects it."""
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "ending_%s_%02d.png" % (tag, idx))
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("ENDING %s %02d -> %s" % (tag, idx, path))


# ================================================================== new props

def cheque(name, w=0.86, h=0.40):
    """The presentation cheque: a cream sheet with a brass band along the top and
    a rule under the payee line, printed in geometry like the flyer so the light
    falls across the type rather than the type sitting on top of the light.

    Built to the flyer's proportions on purpose — a duck holding a cream rectangle
    with a coloured band across it is the same shape the player last saw blowing
    into the reeds. That rhyme is free and it is worth having.
    """
    mb = MB()
    flat_card(mb, CREAM, C.DUCK_SH, (0.0, 0.0, 0.0), w, h, 0.006)
    rbox(mb, BRASS, (0.0, -0.004, h * 0.5 - 0.052), (w * 0.985, 0.004, 0.104),
         r=0.004, n=6, e=8.0, k=1)
    rbox(mb, C.mix_hex(BRASS, DARK, 0.4), (0.0, -0.004, -h * 0.5 + 0.088),
         (w * 0.80, 0.004, 0.006), r=0.002, n=4, e=8.0, k=1)
    return mb.finish(name, MAT, 30.0), -0.008


def sold_sash(name, w, h, roll=-16.0):
    """The red band that goes across a for-sale board once it has gone.

    Rotated about the board's own face normal rather than tilted in space: the
    band is a slab lying flat on the board, so its roll is a rotation about -Y and
    nothing else. Given any pitch at all it lifts a corner off the board and casts
    a shadow that says "a plank leaning against a sign".
    """
    mb = MB()
    rbox(mb, RED, (0.0, 0.0, 0.0), (w, 0.022, h), r=0.008, n=8, e=6.0, k=1)
    ob = mb.finish(name, MAT, 30.0)
    ob.rotation_euler = (0.0, math.radians(roll), 0.0)
    return ob


def duckling(name, loc, yaw=0.0, s=1.0, float_z=True):
    """One duckling, floating.

    Not a scaled-down duck. build_duck's bird is a seated driving pose with a cap
    and goggles on it, and shrunk to a third it reads as a toy of the hero rather
    than as its child. Three primitives with the head set well forward of the body
    is what reads as a duckling at four metres, which is the only distance this
    ever has to survive.
    """
    mb = MB()
    z = 0.030 if float_z else 0.052
    sphere(mb, DUCKLING, (0, 0.012, z), (0.062, 0.082, 0.052), seg=10, rings=5)
    sphere(mb, DUCKLING_SH, (0, 0.052, z - 0.012), (0.050, 0.048, 0.030),
           seg=8, rings=4)                                  # the tail end, darker
    sphere(mb, DUCKLING, (0, -0.058, z + 0.058), (0.044, 0.044, 0.046),
           seg=10, rings=5)                                 # head
    cyl(mb, C.mix_hex("#F9A331", DUCKLING, 0.35), (0, -0.082, z + 0.052),
        (0, -0.116, z + 0.048), 0.016, 0.012, n=6)          # bill
    for sx in (1, -1):
        sphere(mb, "#241F1E", (sx * 0.019, -0.082, z + 0.070),
               (0.006, 0.006, 0.006), seg=6, rings=3)
    ob = mb.finish(name, MAT, 30.0)
    ob.location = Vector(loc)
    ob.rotation_euler = (0, 0, math.radians(yaw))
    ob.scale = (s, s, s)
    return ob


def deck_chair(name, seat_col=RED, stripe_col=CREAM):
    """A striped deck chair, built at the origin facing -Y.

    The canvas is individual stripe slabs rather than one striped texture, because
    nothing in this project is textured — every colour in every panel is a face
    colour, and a deck chair is the one prop where the stripes ARE the silhouette.
    """
    mb = MB()
    # Two side frames: a back rail and a seat rail crossing, the way a real one
    # folds. Built as tubes so the frame keeps a round section at the pivot.
    for sx in (-1, 1):
        tube(mb, WOOD, [Vector((sx * 0.28, 0.30, 0.0)), Vector((sx * 0.26, 0.02, 0.62)),
                        Vector((sx * 0.25, -0.05, 0.86))], [0.020, 0.018, 0.016],
             n=6, smooth=False)
        tube(mb, WOOD_D, [Vector((sx * 0.27, -0.34, 0.0)), Vector((sx * 0.26, 0.06, 0.40))],
             [0.020, 0.018], n=6, smooth=False)
        cyl(mb, WOOD_D, (sx * 0.27, -0.34, 0.0), (sx * 0.27, -0.34, 0.03), 0.024, n=6)
    # The canvas: from the seat rail's top up the back rail, in eight stripes.
    a0, a1 = Vector((0.0, 0.05, 0.38)), Vector((0.0, -0.04, 0.84))
    NS = 8
    for k in range(NS):
        t0, t1 = k / NS, (k + 1) / NS
        p0 = a0 + (a1 - a0) * t0
        p1 = a0 + (a1 - a0) * t1
        col = seat_col if k % 2 == 0 else stripe_col
        quad(mb, col, (-0.25, p0.y, p0.z), (0.25, p0.y, p0.z),
             (0.25, p1.y, p1.z), (-0.25, p1.y, p1.z))
    for k in range(4):        # the seat itself, flat, in the same stripes
        t0, t1 = k / 4.0, (k + 1) / 4.0
        y0, y1 = 0.05 - 0.38 * t0, 0.05 - 0.38 * t1
        col = seat_col if k % 2 == 0 else stripe_col
        quad(mb, col, (-0.25, y0, 0.38), (0.25, y0, 0.38),
             (0.25, y1, 0.36), (-0.25, y1, 0.36))
    mb.bm.normal_update()
    return mb.finish(name, MAT, 30.0)


def hoarding(name, x0, x1, y, h=1.35, post_every=1.55, col=HOARD):
    """A run of site hoarding along a line in x: boarded panels on posts, with a
    dark rail behind them.

    Deliberately taller than the duck and deliberately unbroken. The loss's second
    and third panels both need something that says the pond is not the duck's any
    more without anybody having to say it, and a barrier you cannot see the bottom
    of does that; a fence with gaps in it reads as a boundary, which is not the
    same thing at all.
    """
    mb = MB()
    n = max(2, int(round((x1 - x0) / post_every)))
    for k in range(n + 1):
        x = x0 + (x1 - x0) * k / n
        rbox(mb, C.mix_hex(WOOD_D, DARK, 0.3), (x, y + 0.08, h * 0.5),
             (0.09, 0.09, h + 0.10), r=0.010, n=6, e=7.0, k=1)
    NB = max(6, int((x1 - x0) / 0.62))
    for k in range(NB):
        bx0 = x0 + (x1 - x0) * k / NB + 0.004
        bx1 = x0 + (x1 - x0) * (k + 1) / NB - 0.004
        c = col if k % 3 else HOARD_D          # a panel here and there, replaced
        rbox(mb, c, ((bx0 + bx1) * 0.5, y, h * 0.5),
             (bx1 - bx0, 0.05, h), r=0.006, n=4, e=8.0, k=1)
    return mb.finish(name, MAT, 32.0)


def pipe(name, ctr, length=1.9, r=0.34, yaw=0.0, tilt=0.0):
    """A concrete pipe section.

    An open cylinder with a dark disc sunk a quarter of a radius inside each mouth,
    rather than a capped cylinder with a recess in the end. A capped one is a grey
    log: the read that makes a pipe a pipe is that you can see INTO it, and at
    these framings the mouth is only ever a few dozen pixels across — enough for a
    dark ellipse inside a bright rim, not enough for a modelled wall thickness.
    """
    mb = MB()
    hl = length * 0.5
    cyl(mb, CONCRETE, (0, -hl, 0), (0, hl, 0), r, r, n=16,
        cap_a=False, cap_b=False)
    for s in (1, -1):
        # Sunk deep and painted much darker than the shell. At r*0.30 in and only
        # one shade down, the bore caught the same light as the rim and the whole
        # section rendered as a smooth grey log with a flat disc on the end.
        cyl(mb, "#3A3A38", (0, s * (hl - 0.02), 0), (0, s * (hl - r * 0.70), 0),
            r * 0.86, r * 0.86, n=16, cap_a=False, cap_b=True)
    for s in (1, -1):        # the thickened collar every real section has
        cyl(mb, C.mix_hex(CONCRETE, CHALK, 0.25), (0, s * hl, 0),
            (0, s * (hl - 0.10), 0), r * 1.07, r * 1.07, n=16,
            cap_a=False, cap_b=False)
    ob = mb.finish(name, MAT, 34.0)
    ob.location = Vector(ctr)
    ob.rotation_euler = (math.radians(tilt), 0.0, math.radians(yaw))
    return ob


def drain_pond(level=0.52, mud=None):
    """Take the water out of the pond that pond_set() just built.

    pond_set() does not hand its water back, so this reaches for it by name and
    shrinks it about the pond's own centre — which is exactly right, because a
    draining pond does not drop like a bath, it RETREATS toward the middle and
    leaves a ring of wet mud behind. Building a second, smaller pond would have
    put its bank in the wrong place; scaling the one that is there keeps the
    shoreline the opening established and simply moves the water away from it.
    """
    mud = mud or C.mix_hex(DIRT, DARK, 0.42)
    pts = C.pond_polygon()
    mb = MB()
    mb.f([mb.v((x * 1.02, POND_CY + (y - POND_CY) * 1.02, 0.030))
          for (x, y) in pts], mud)
    mb.bm.normal_update()
    bed = mb.finish("Pond_Bed", MAT, seal=False)

    water = bpy.data.objects.get("Pond_Water")
    if water is not None:
        water.scale = (level, level, 1.0)
        water.location = (0.0, POND_CY * (1.0 - level), 0.008)
    return bed, water


def judges(loc=(0.0, 0.0, 0.0), clap=True, ao=True):
    """The three judges, seated and applauding.

    build_judges builds each of them from its own seat contact at z = 0, which is
    what makes them droppable straight onto the bench's seat plank. The applause
    is done by moving the MITTS, because that is all these characters have — they
    have no arms at all (see build_judges' own note), so a clap is two floating
    hands brought together and raised, and there is no shoulder to disagree with.

    The scorecards are deleted rather than posed. A judge holding a card while
    applauding is a judge who has not finished judging, and this panel is after.
    """
    P = [fn(ox) for (fn, ox) in ((JUDGES.mildred, -0.95), (JUDGES.boris, 0.0),
                                 (JUDGES.priscilla, 0.95))]
    meshes = []
    for p in P:
        card = p["Card"][0]
        bpy.data.objects.remove(card, do_unlink=True)
        del p["Card"]
        meshes += [p[k][0] for k in ("Body", "Head", "Arm_L", "Arm_R")]
    if ao:
        bake_ao(meshes, floor=0.78, dist=0.16, samples=20, ground=False)

    off = Vector(loc)
    for p in P:
        for k in ("Body", "Head", "Arm_L", "Arm_R"):
            p[k][0].location = off
        if not clap:
            continue
        # Inward, forward and — above all — UP.
        #
        # The lift is the whole trick and it is not a matter of taste: these judges
        # sit BEHIND a table whose top is at 0.785, and their mitts rest at about
        # that height. Clapping at rest height is clapping underneath the furniture,
        # which is what the first pass rendered — three animals sitting perfectly
        # still with a pair of brown blobs on the table in front of one of them.
        # Raised to the chest they clear the table top and the applause is the first
        # thing the panel reads. The inward figure stays small because any more
        # crosses the centre line and reads as one animal with its hands clasped.
        p["Arm_L"][0].location = off + Vector((-0.085, -0.090, 0.355))
        p["Arm_R"][0].location = off + Vector((0.085, -0.090, 0.330))
    return P, meshes


# ==================================================================== THE WIN

def win_1():
    """THE PRIZE · THE CHEQUE AT THE BENCH — ends on Rect(0.16, 0.14, 0.68, 0.68).

    That rect is centred on (0.50, 0.48) of the frame, so the cheque is composed
    dead centre and everything the panel opens on — the awning, the applause, the
    trophy — lives in the margin the crop throws away. The duck faces the lens with
    its back to the bench, which is how a prize photograph is actually taken and
    also the only arrangement where the face and the money share a frame.
    """
    C.SKY_MIX = C.SKY_H
    C.begin(C.SKY_Z, C.SKY_H, 1.05, samples=64, vig=0.26, bloom=0.05)
    C.plane("Ground", GRASS, 240.0, 0.0, (0.0, 10.0))
    C.hill_band(C.mix_hex("#7DB08A", C.SKY_H, 0.55), y=48.0)
    hedge = C.FOL.hedge_straight()
    hedge.location = (-8.0, 9.0, 0.0)
    for i in range(9):
        C.dup(hedge, (-8.0 + i * 2.15, 9.0 + (i % 2) * 0.2, 0.0), rotz=(i * 7) % 24 - 12)
    oak = C.FOL.tree_oak()
    oak.location = (-9.5, 14.0, 0.0)
    C.dup(oak, (10.5, 16.0, 0.0), rotz=140, scale=0.88)

    C.sun_lamp((0.36, 0.66, -0.66), 3.5, SUNLIGHT, angle=4.0)
    C.area_lamp((-5, -6, 4), (0, 0.5, 0.6), 520, (0.80, 0.88, 1.0), 8.0)
    # A warm key from the front left so the duck's face is not lit only by sky.
    # The bench and the awning shade everything behind it, and without this the
    # subject of the panel was a silhouette against a bright canopy.
    C.area_lamp((-2.2, -3.0, 1.7), (0, -1.1, 0.5), 190, (1.0, 0.94, 0.84), 2.4)

    # The bench is offset in x rather than centred, and the duck is not. Centred on
    # the same line, Boris — the widest of the three and the one directly opposite —
    # sat exactly behind the winner and was reduced to a pair of ears either side of
    # a cheque. Sliding the furniture a third of a metre puts the duck in the gap
    # between two judges instead of on top of one, which costs nothing and is the
    # difference between three judges applauding and two.
    BX = 0.35
    C.prop(C.PROPS.bench, "Bench", (BX, 0.0, 0.0))
    C.prop(C.PROPS.awning, "Awning", (BX, 0.10, 0.0))
    C.prop(C.PROPS.trophy, "Trophy", (BX + 0.66, 0.02, 0.785))    # on the table top
    for sx in (-1, 1):    # bunting posts, well outside the crop, to close the ends
        C.prop(C.PROPS.bunting_post, "Bunting_%d" % sx, (sx * 3.3, -0.9, 0.0))
    judges(loc=(BX, 0.52, 0.481))      # the seat plank's top face

    # THE DUCK IS ON A PLINTH, and that is a framing decision before it is a
    # staging one. He is half a metre tall and the judges' heads are at 1.75 m on
    # the far side of a table; on the ground there is no lens that holds the money
    # big enough to be the subject AND the applause without cutting three animals
    # off at the neck, which reads as a mistake rather than as a crop. Stood on the
    # show's own presentation plinth the whole picture lives in a 1.2 m band and
    # every head is inside the frame — and a winner up on a plinth is what a prize
    # photograph looks like anyway.
    #
    # DX is where the plinth stands, and it is -0.49 rather than 0 for a reason
    # that is pure parallax. The duck is 1.57 m in front of the judges and the lens
    # sits 1.19 m off the aim axis, so a duck standing dead centre does not COVER
    # centre on the judges' plane — it covers x = +0.36, which is where Boris is.
    # Solving the other way round (aim shifts with the duck, so the offset feeds
    # back) puts the duck's silhouette in the gap between Mildred and Boris, and
    # three visible judges is the difference between applause and a bystander.
    DX = -0.49
    C.prop(C.PROPS.trophy_plinth, "Plinth", (DX, -1.05, 0.0))
    # Turned to the lens. Panel 7 of the opening learned this the hard way: a duck
    # yawed away from the camera holds whatever it is holding off to one side, and
    # the sheet stops belonging to it.
    d = C.duck(loc=(DX, -1.05, 0.76 + LAND_Z), yaw=-12, head=(-6, 8),
               wings=(36, 32), wing_yaw=(-8, 6), tail=-3, face="wide")
    cam = C.camera((DX, -1.20, 1.12), 1.80, (-0.24, -1.0, 0.13), lens=58,
                   u=0.500, v=0.480, fstop=4.5, focus_at=(DX, -1.32, 1.00))

    ch, cy = cheque("Cheque")
    y = cy - 0.004
    t = [ch]
    t.append(C.text3d("GARDENER OF THE YEAR", 0.036, DARK, (0.0, y, 0.148),
                      fit=0.52, bold=0.004))
    t.append(C.text3d("PAY THE BEARER", 0.030, GREY, (0.0, y, 0.052), fit=0.34))
    t.append(C.text3d("$10,000", 0.13, RED_D, (0.0, y, -0.062), fit=0.46,
                      bold=0.005, spacing=1.06))
    # Low and well out in front: the sheet has to cover the feet, because the duck
    # is a seated driving pose stood upright and on a plinth there is no grass to
    # bury the legs in.
    held = C.duck_local(d, (0.0, -0.32, 0.100))
    grp = C.group("ChequeGrp", t, loc=held)
    to_cam = (cam.location - Vector(held)).normalized()
    # Same 0.42/0.58 lean as the opening's flyer panel, and for the same reason:
    # tipped any further toward the lens the duck reads as presenting a placard to
    # an audience rather than holding a cheque, and tipped any flatter the figure
    # foreshortens past reading.
    C.face_toward(grp, Vector((0, 0, 1)) * 0.42 + to_cam * 0.58)

    # Sides only. A continuous band in front would put blades through the figure,
    # and the held cheque already hides the legs the driving pose cannot straighten.
    for sx in (-1, 1):
        C.tall_grass("Fore_%d" % sx, 60, (DX + sx * 1.15, -1.55), (0.55, 0.20),
                     h=(0.22, 0.44), seed=31 + sx)
    render("win", 1)


def win_2():
    """THE POND, BOUGHT · SOLD ACROSS THE SIGN — starts on Rect(0.20, 0.16, 0.64,
    0.64) and pulls back to nearly the whole frame.

    So the board has to be legible dead centre AND the pond has to be worth
    arriving at, which is the same brief the opening's notice panel had. It is
    deliberately the same shot with the weather turned back on: the sun that went
    out on the notice comes back here, on the same board, on the same bank.
    """
    C.SKY_MIX = "#E8DCC0"
    C.begin("#4E93C8", "#EEDFC0", 1.02, samples=64, vig=0.26, bloom=0.05)
    C.pond_set(grade=0.16)
    C.sun_lamp((-0.48, 0.60, -0.64), 3.4, (1.0, 0.92, 0.76), angle=4.0)
    C.area_lamp((6, -5, 5), (0, 2, 0.5), 760, (0.78, 0.87, 1.0), 9.0)

    sign, fy = C.board_on_posts("ForSale", 1.30, 0.62, 1.24, CREAM, FENCE)
    y = fy - 0.006
    t = [sign]
    t.append(C.text3d("LAND FOR SALE", 0.11, RED_D, (0.0, y, 1.66), fit=1.04,
                      bold=0.014))
    t.append(C.text3d("THE OLD POND - 2 ACRES", 0.05, GREY, (0.0, y, 1.46),
                      fit=0.86))
    # The sash goes across the LOWER half of the board, not the middle.
    #
    # First pass it was 1.46 x 0.30 across the centre and it ate the board: the
    # headline vanished under it and the panel became a red diagonal with a word on
    # it. The beat only works if both things can be read at once — the pond is for
    # sale, and the pond has been sold — so the sash is sized to cover the small
    # print and nothing else, and the headline survives above it.
    #
    # The geometry is tight and the roll is what makes it tight: a band of width w
    # rolled by a lifts its far corner by (w/2)·sin a, so a 1.34 band at -16 deg
    # reached 0.19 above its own centre and swallowed the last three letters of the
    # headline. The board is only 0.62 tall with type at 1.66 and 1.46, which leaves
    # the sash a 0.36 m window between the board's bottom edge and the headline's
    # descenders. 1.28 x 0.18 at -7 deg spans 1.25 to 1.59 and fits it exactly.
    band = sold_sash("Sold_Band", 1.28, 0.18, roll=-7.0)
    band.location = (0.0, y - 0.028, 1.42)
    t.append(band)
    # The word rides the band and is rolled by the same angle, so the two are one
    # object as far as the eye is concerned. Rolled about world Y after the upright
    # rotation, which for a face already pointing -Y is a roll in its own plane —
    # tilting it any other way lifts the lettering off the sash.
    t.append(C.text3d("SOLD", 0.11, CHALK, (0.0, y - 0.036, 1.42),
                      rot=(90.0, -7.0, 0.0), fit=0.52, bold=0.009, spacing=1.08))
    C.group("Sign", t, loc=(0.95, 0.35, 0.0), rot=(0, 0, -13))

    # Under its own sign, on the bank, looking up at it. The duck is small here on
    # purpose: this panel belongs to the board, and the next one belongs to him.
    C.duck(loc=(-0.15, 0.72, LAND_Z), yaw=48, head=(16, -26), wings=(22, 26),
           wing_yaw=(-10, 8), tail=2)
    C.tall_grass("Verge", 120, (-0.2, 0.30), (3.2, 0.28), h=(0.16, 0.40), seed=12)
    # A patch AT the feet, in front of and behind them. The verge band runs along
    # y = 0.3 and the duck stands at y = 0.72, so it hides nothing at all — and the
    # duck is a seated driving pose stood upright, which means anything that does
    # not bury the legs renders two orange feet splayed out on the bank.
    C.tall_grass("Feet", 120, (-0.15, 0.60), (0.50, 0.26), h=(0.30, 0.52), seed=19)
    C.ripple("Ripple_A", (0.7, 4.2), 0.20)
    C.ripple("Ripple_B", (-1.6, 5.6), 0.15)

    # Wider than the opening's notice shot, because the crop this one ENDS on is
    # nearly the whole frame and the pond has to fill it. The board still has to
    # survive the 0.64 crop it starts on, which is what fixes the height at 2.6 m.
    C.camera((0.55, 0.30, 1.28), 2.60, (-0.26, -1.0, 0.15), lens=48,
             u=0.520, v=0.480, fstop=8.0, focus_at=(0.95, 0.35, 1.40))
    render("win", 2)


def win_3():
    """GOLDEN HOUR · A LIFE ON THE WATER — drifts from Rect(0.14, 0.10, ...) to
    Rect(0.06, 0.14, ...): the same size at both ends.

    Nothing is discovered, so nothing is pushed into, and the panel has to be worth
    six seconds of looking. It is composed as a spread rather than a subject: the
    duck and the ducklings a little right of centre, the deck chair on the near
    bank at the left, the mower parked behind it, and the sun low across the whole
    thing. The drift walks LEFT, from the birds onto the chair and the mower, so
    the last thing the game shows is the tools put away.
    """
    C.SKY_MIX = "#F2D9A8"
    # The horizon carries this panel, not the lamps. Everything the water shows is
    # a reflection of the sky — the sun is behind the pond and the surface is a
    # mirror at this angle — so a pale cream horizon renders a pale grey pond no
    # matter how warm the fill is. Pushing the horizon into amber is what turns the
    # water gold, and it is the only control that does.
    C.begin("#2E5C93", "#FFC98A", 1.12, exposure=0.05, samples=72, vig=0.30,
            bloom=0.10)
    # Graded harder than the opening's dusk panel. At 0.30 the water came back a
    # pale slate — the sun is BEHIND the pond here, so the surface reflects sky and
    # not sun, and a sunset panel whose water is grey is not a sunset panel.
    C.pond_set(grade=0.42)
    # Low and along the water, from behind the far bank. The same rig as the
    # opening's grief panel with the fill turned warm instead of blue — that panel
    # and this one are the same time of day and the difference between them is
    # entirely who is in the water.
    C.sun_lamp((0.20, -0.92, -0.22), 3.4, (1.0, 0.78, 0.50), angle=3.0)
    C.area_lamp((-6, -5, 3.6), (0, 3, 0.4), 190, (1.0, 0.82, 0.62), 8.0)
    C.sun_disc("Sunset", (2.2, 42.0, 1.2), r=2.0, col="#FFD9A0", emit=7.0)

    d = C.duck(loc=(0.55, 3.05, FLOAT_Z), yaw=214, head=(-6, 18), bill=2,
               wings=(12, 8), wing_yaw=(-6, 6), tail=-4)
    C.ripple("Ripple_D", (0.55, 3.05), 0.30, col="#F0CFA2")
    # Strung out behind their parent on a curve, not in a line. Three is the right
    # number: two reads as a pair and four starts to read as a flock, and a flock
    # is a nature documentary rather than somebody's afternoon.
    for k, (x, yy, a, s) in enumerate(((1.32, 2.86, 206, 1.20),
                                       (1.86, 3.24, 198, 1.10),
                                       (1.58, 3.72, 224, 1.00))):
        duckling("Duckling_%d" % k, (x, yy, 0.0), yaw=a, s=s)
        C.ripple("Ripple_%d" % k, (x, yy), 0.11 * s, col="#F0CFA2")

    chair = deck_chair("DeckChair")
    chair.location = (-1.85, 0.30, 0.0)
    chair.rotation_euler = (0, 0, math.radians(-28))
    C.prop(C.PROPS.thermos, "Thermos", (-2.30, 0.02, 0.0), rotz=24)
    # Parked, not driven: turned side-on so the silhouette is the whole machine.
    #
    # ACROSS the water, and every nearer position was tried first. On the left bank
    # at -3.3 it fell outside the render, and a move crops INTO a still — anything
    # not in the frame is not in the panel however far the drift travels. On the
    # near right at +1.45 it was in the render but not in the CROP: the move ends at
    # x = 0.82 and the machine spanned 0.80 to 1.00, so the panel would have shown a
    # red sliver at the edge. Behind the chair at -0.85 it was four metres from the
    # lens and swallowed the pond, the duck and all three ducklings.
    #
    # The near bank simply is not deep enough to hold a two-metre machine at this
    # framing, and it should not be: the near bank is where the chair is, and the
    # panel is about somebody sitting in it. On the far bank the mower is small,
    # whole, and unmistakably parked — which is the only thing it has to say here.
    C.mower(loc=(2.90, 16.30, 0.0), yaw=204)

    C.tall_grass("Verge", 150, (-0.4, 0.45), (4.0, 0.40), h=(0.18, 0.46), seed=33,
                 cols=("#4C5E2A", "#6A7C33", "#38491F"))

    # Near the waterline, so the empty half of the frame is water and sky rather
    # than a floor. Wide enough to hold the chair, the birds and the mower at once,
    # because the drift crosses all three.
    C.camera((-0.30, 2.30, 0.40), 2.55, (-0.34, -1.0, 0.085), lens=46,
             u=0.500, v=0.440, fstop=8.0, focus_at=(0.55, 3.05, 0.20))
    render("win", 3)


# =================================================================== THE LOSS

def lose_1():
    """SOLD · THE NOTICE AT THE WATER'S EDGE — starts tight on the board,
    Rect(0.16, 0.24, 0.68, 0.68), and pulls back to nearly full frame.

    The opening's second panel, again, and the framing is copied from it almost
    exactly — same board height, same lens, same 0.575 sensor lift — because the
    beat is that this is the same sign. What has changed is the word on it and the
    fact that the machine behind it is no longer waiting.
    """
    C.SKY_MIX = "#C6CBD0"
    C.begin("#8FA6B6", "#D3D8DA", 0.80, samples=64, vig=0.32, bloom=0.0)
    C.pond_set(grade=0.50)
    C.sun_lamp((0.30, 0.70, -0.62), 1.3, (0.90, 0.92, 0.96), angle=25.0)
    C.area_lamp((-7, -9, 8), (0, 2, 1), 1500, (0.86, 0.90, 0.96), 14.0)

    sign, fy = C.board_on_posts("Sold", 1.16, 0.80, 0.98, CREAM, FENCE,
                                col_post=WOOD)
    y = fy - 0.006
    t = [sign]
    # The word appears once. It was the headline AND the verb of the sentence under
    # it — "SOLD / THIS LAND HAS BEEN / SOLD FOR DEVELOPMENT" — which read as a
    # stutter rather than as a stamp. The headline keeps the word and the body says
    # what it means for the duck instead, which is that he is not allowed in.
    t.append(C.text3d("SOLD", 0.13, RED_D, (0.0, y, 1.640), fit=0.44, bold=0.016))
    t.append(C.text3d("THIS SITE IS NOW CLOSED", 0.075, DARK, (0.0, y, 1.470),
                      fit=0.86, bold=0.010))
    t.append(C.text3d("TO THE PUBLIC", 0.075, DARK, (0.0, y, 1.345),
                      fit=0.56, bold=0.010))
    # ASCII only: the display font has no middle dot and draws a tofu box.
    t.append(C.text3d("BY ORDER OF CITY WORKS", 0.05, GREY, (0.0, y, 1.215),
                      fit=0.74))
    t.append(C.text3d("WORKS BEGIN MONDAY", 0.05, GREY, (0.0, y, 1.100),
                      fit=0.68))
    C.group("Sold", t, loc=(0.0, -0.20, 0.0), rot=(0, 0, -6))

    # The machine has come round the pond, and that is the only thing in this panel
    # that differs from the opening's: it is not parked across the water waiting any
    # more, it is on this side of it. It stays a long way off all the same. At 8 m
    # a 4 m excavator is not "looming", it is a yellow shape with its head cut off
    # by the top of the frame — the opening found the same thing and settled on
    # twenty metres, where the whole silhouette reads and the menace is that it is
    # coming rather than that it is close.
    ex = C.excavator()
    ex.location = (7.40, 15.20, 0.0)
    ex.rotation_euler = (0, 0, math.radians(-52))
    # On the FAR bank. At y = 2.1 the run stood in the middle of the water, which
    # reads as a blue wall floating on the pond rather than as a fence around it.
    hoarding("Hoard", -9.0, -0.5, 16.2, h=1.7)
    C.tall_grass("Verge", 90, (0.0, -0.75), (3.4, 0.30), h=(0.14, 0.34), seed=8)
    C.prop(C.PROPS.marker_stake, "Stake_A", (-1.70, 0.60, 0.0), rotz=20)
    C.prop(C.PROPS.marker_stake, "Stake_B", (1.45, 1.05, 0.0), rotz=-40)

    C.camera((0.0, -0.20, 1.36), 2.05, (-0.20, -1.0, 0.16), lens=48,
             u=0.50, v=0.575, fstop=9.0, focus_at=(0.0, -0.20, 1.36))
    render("lose", 1)


def lose_2():
    """THE WORKS · DIGGERS, HOARDING, PIPE — ends on Rect(0.22, 0.18, 0.60, 0.60).

    That rect is centred on (0.52, 0.48), so the pipe mouth and the retreating
    waterline are composed dead centre and the excavator lives in the half of the
    frame the push throws away. The machine is what arrives; the water going is
    what the beat is about, and it is the only thing here that is still moving.
    """
    C.SKY_MIX = "#C0C4C6"
    C.begin("#7F94A4", "#CBD0D2", 0.78, samples=64, vig=0.34, bloom=0.0)
    C.pond_set(grade=0.56, trees=True, hills=True)
    drain_pond(level=0.50)
    C.sun_lamp((0.26, 0.70, -0.64), 1.1, (0.88, 0.90, 0.94), angle=28.0)
    C.area_lamp((-6, -8, 7), (0, 2, 0.8), 1400, (0.84, 0.88, 0.96), 14.0)

    ex = C.excavator()
    # Standing on the exposed mud ring, not in the water and not at the frame's
    # edge. The move ends at x = 0.22, so anything in the left fifth of the render
    # is not in the panel — parked out at -4.9 the machine kept its arm and lost its
    # cab to the crop, which is a beat about "diggers" delivered by a yellow stick.
    # The ring is only wide enough for it this far back, where the water has already
    # pulled in to a 2.9 m half-width.
    ex.location = (-3.60, 11.20, 0.0)
    ex.rotation_euler = (0, 0, math.radians(38))
    hoarding("Hoard_Far", -9.0, 9.0, 15.0, h=1.9)


    # The outfall: one pipe laid down the mud into what is left of the water, and
    # two more stacked on the bank waiting for it. Stacked rather than scattered —
    # a site delivers pipe in a stack, and three of them lying about separately
    # would read as debris, which is a different and much sadder panel.
    pipe("Pipe_Out", (0.55, 4.30, 0.30), length=3.2, r=0.34, yaw=-8, tilt=-4)
    # The stack goes well back down the bank. At y = 1.2 it was three metres from
    # the lens and two sections filled the bottom-right corner, so the panel's
    # subject — the water leaving — was competing with a pile of stock.
    pipe("Pipe_A", (3.60, 6.10, 0.34), length=2.2, r=0.34, yaw=76)
    pipe("Pipe_B", (3.47, 5.51, 0.34), length=2.2, r=0.34, yaw=76)
    pipe("Pipe_C", (3.54, 5.81, 0.94), length=2.2, r=0.34, yaw=76)

    # A wet stain running out of the mouth, painted rather than lit: at this grade
    # nothing in the panel is bright enough for a specular to read, so the only way
    # to say "water is leaving here" is a darker patch of mud with an edge on it.
    mb = MB()
    for k, (rr, cc) in enumerate(((1.05, C.mix_hex(DIRT, DARK, 0.55)),
                                  (0.72, C.mix_hex(DIRT, DARK, 0.70)))):
        disc(mb, cc, (0.55, 5.95 + k * 0.10, 0.034 + k * 0.002), (0, 0, 1), rr, n=18)
    mb.finish("Outfall_Stain", MAT, seal=False)

    C.prop(C.PROPS.marker_stake, "Stake_A", (-1.30, 1.15, 0.0), rotz=14)
    C.prop(C.PROPS.marker_stake, "Stake_B", (1.90, 2.30, 0.0), rotz=-32)
    C.tall_grass("Verge", 70, (0.0, -0.55), (3.6, 0.26), h=(0.12, 0.30), seed=41,
                 cols=("#4A5A3C", "#5C6B49", "#38452F"))

    # A SURVEY, from back and above. The first pass framed 3.1 m from four metres
    # away and the result was a close-up of one pipe: at that distance a 4 m
    # excavator does not fit in the frame at all and the pond it is emptying was a
    # blue puddle behind a foreground prop. This panel is the only one in either
    # ending that has to hold four things at once — machine, hoarding, pipe and the
    # water that is going — so it is framed like a site photograph and not like a
    # portrait: six metres of frame from twelve metres back, high enough to see the
    # far bank over the near one.
    C.camera((0.40, 5.40, 0.70), 6.20, (-0.22, -1.0, 0.26), lens=40,
             u=0.520, v=0.480, fstop=11.0, focus_at=(0.55, 4.30, 0.35))
    render("lose", 2)


def lose_3():
    """ALONE · WATCHING FROM THE BANK — ends on Rect(0.24, 0.22, 0.54, 0.54), the
    tightest crop in either ending.

    One small figure, seen from behind, with the page dimming around it. Everything
    that could soften it has been taken out: no reeds in front of him, no ripples,
    no far-bank trees in the light. What is left is a duck, a hoarding, and the mud
    where the water used to reach.

    Melancholy and not cruel, which is a composition decision and not a caption:
    the camera is at his height rather than above him, so the panel is standing
    next to him instead of looking down at him, and the water he is watching is
    still there. It is just further away than it was.
    """
    C.SKY_MIX = "#B9BFC4"
    C.begin("#77899A", "#C3C9CC", 0.72, samples=72, vig=0.42, bloom=0.0)
    C.pond_set(grade=0.62, trees=True, hills=False)
    drain_pond(level=0.44)
    C.sun_lamp((0.22, 0.72, -0.62), 0.9, (0.86, 0.89, 0.94), angle=30.0)
    C.area_lamp((-5, -7, 6), (0, 2, 0.6), 1100, (0.82, 0.87, 0.96), 13.0)
    # Lower and further off than the works panel's. A 72 mm lens compresses the far
    # bank enormously — at 1.9 m and sixteen metres out the hoarding filled the top
    # two thirds of the frame and there was no sky in the panel at all, which took
    # the air out of a shot whose whole subject is somebody standing in the open.
    hoarding("Hoard_Far", -9.0, 9.0, 15.6, h=1.45)

    # YAW 188: back to the lens. This is the opening's grief pose turned the other
    # way round, and the yaw matters more than it looks — the duck's forward is -Y,
    # so yaw 8 faces the CAMERA, which is a duck staring blankly down the lens and
    # is the single most wrong thing this panel could show. Facing away, the player
    # is standing behind him looking at what he is looking at.
    C.duck(loc=(0.10, 1.35, LAND_Z), yaw=188, head=(-3, -12), wings=(-4, -4),
           wing_yaw=(-3, 3), tail=-8)
    # Just enough grass to stand the driving pose in, and painted down. Anything
    # taller here would hide the one figure the panel has.
    C.tall_grass("Feet", 70, (0.10, 1.15), (0.70, 0.16), h=(0.14, 0.26), seed=47,
                 cols=("#4E5A46", "#5C6852", "#3C463A"))
    C.prop(C.PROPS.marker_stake, "Stake", (-1.45, 2.30, 0.0), rotz=22)

    # At his eye height, not above it, and long enough that the far hoarding is
    # compressed up behind him rather than laid out flat.
    C.camera((0.10, 1.42, 0.36), 1.35, (-0.16, -1.0, 0.055), lens=72,
             u=0.505, v=0.470, fstop=5.0, focus_at=(0.10, 1.42, 0.34))
    render("lose", 3)


PANELS = [("win1", win_1), ("win2", win_2), ("win3", win_3),
          ("lose1", lose_1), ("lose2", lose_2), ("lose3", lose_3)]


def main():
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = set(t.strip() for t in a.split("=", 1)[1].split(",") if t.strip())
    for tag, fn in PANELS:
        if only and tag not in only:
            continue
        print("\n================ ENDING %s  %s"
              % (tag, fn.__doc__.split("\n")[0].strip()))
        fn()
    print("DONE ending panels")


main()
