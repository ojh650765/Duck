# rig_judges.py — give the three judges a real skeleton, skin them, and author
# their performance as keyframed actions instead of runtime noise.
#
# Run: blender --background --python C:\Duck\Art\Blender\rig_judges.py
#
# WHY THIS IS NOT AN AUTOMATIC-WEIGHTS JOB
#
# The judges' geometry is built for rotation about named pivots. duck_lib's neck
# section explains it at length: the head's lower surface is a piece of the sphere
# centred exactly on the head's pivot, so rotating the head about that pivot leaves
# the neck/head junction mathematically invariant — the head turns inside its own
# silhouette and the joint cannot come apart at any angle. joint_audit() proves it.
#
# Envelope or automatic weights would throw that away: the socket would deform,
# the invariance would be gone, and the result would be WORSE than the transform
# hierarchy it replaced. So the head is bound rigidly, weight 1.0, to a bone whose
# head sits exactly on that pivot. Rotating that bone is then arithmetically the
# same operation the old code did, and the socket still holds.
#
# Skinning earns its place everywhere the socket rule does not apply: the body can
# bend and breathe through a spine chain, and the arms get an elbow. That is what
# makes a keyframed performance read as a character rather than as parts pivoting.

import bpy, os, sys, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L
from duck_lib import fresh_scene, make_empty


def load(path):
    """Import a build script's functions without running its main(). Same trick
    audit_rig.py and preview_rig.py use."""
    src = open(path, encoding="utf-8").read().replace("\nmain()\n", "\n")
    g = {"__name__": "rigmod", "__file__": path}
    exec(compile(src, path, "exec"), g)
    return g


# --------------------------------------------------------------------- helpers

def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def arm_axis(arm_obj, shoulder_ref):
    """Shoulder end and hand end of an arm mesh, worked out from the geometry rather
    than typed in — the three judges have very different arms and hard numbers would
    silently rot the moment one of them is remodelled.

    The end nearest SHOULDER_REF is the shoulder, where shoulder_ref is a point on
    the torso's centre line at chest height. Two simpler rules were tried and both
    were wrong on one judge each, in the same way:

      "nearest the body's centre line in x" — Mildred and Priscilla have arms that
      hang, so their long axis is vertical and both ends share an x. Tie every time,
      lower end won by default, and every arm came out rigged upside down: the
      shoulder bone's origin at the wrist, upper-arm and forearm weights swapped,
      and the shoulder rotating the arm about the hand.

      "the higher end" — fixes those two and breaks Boris, whose arms are modelled
      straight out to the sides for his broad-triangle silhouette. Both ends at one
      height, tie again, and his arm ended up folded horizontally at head height.

    A 3D distance to an actual point on the torso has no tie to lose.
    """
    # Real vertices, not bounding-box corners.
    #
    # A box corner is not on the model. Putting the shoulder bone there placed the
    # pivot outside the arm entirely, so any rotation swung the whole limb away from
    # the torso and opened a gap — which is what "the arms are detached" was. These
    # judges are assembled from separate closed solids (Body, Head, Arm_L, Arm_R), so
    # an arm only stays attached if it turns about the point where it actually meets
    # the body. That is the same trick duck_lib uses for the neck: a joint that does
    # not move cannot come apart.
    mw = arm_obj.matrix_world
    pts = [mw @ v.co for v in arm_obj.data.vertices]
    if not pts:
        lo, hi = world_bounds(arm_obj)
        return lo, hi

    shoulder = min(pts, key=lambda p: (p - shoulder_ref).length_squared)
    hand = max(pts, key=lambda p: (p - shoulder).length_squared)
    return shoulder, hand


def mitt_centre(obj):
    """A hand's centre, from its own vertices. The mitt is a single closed blob, so its
    vertex average IS its centre — no bounding-box corner, which is never on the model,
    and no nearest-vertex rule, which is what mis-sited the old shoulder pivots."""
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    if not pts:
        return Vector((0.0, 0.0, 0.0))
    return sum(pts, Vector((0.0, 0.0, 0.0))) / len(pts)


def mitt_radius(obj, centre):
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    return max((p - centre).length for p in pts) if pts else 0.04


def local_offset(rig, bone, world_vec):
    """WORLD_VEC expressed in BONE's local space, for use as a pose location.

    Pose translation happens along the bone's own axes, and those axes are not the ones
    the eye expects — the previous round of this work burned three iterations guessing
    at a bone's local X. Converting a stated world direction through the bone's rest
    matrix removes the guess: the caller says "move the hands toward each other" in
    world terms and this works out what that is for each bone.
    """
    rest = rig.data.bones[bone].matrix_local.to_3x3()
    return rest.inverted() @ (rig.matrix_world.to_3x3().inverted() @ Vector(world_vec))


def shoulder_reference(body_obj, ox, chest_z):
    """A point on the torso's centre line at chest height, for arm_axis to measure
    against."""
    lo, hi = world_bounds(body_obj)
    return Vector((ox, (lo.y + hi.y) * 0.5, chest_z))


def add_bone(edit_bones, name, head, tail, parent=None, connect=False):
    b = edit_bones.new(name)
    b.head = head
    b.tail = tail
    b.roll = 0.0
    if parent is not None:
        b.parent = parent
        b.use_connect = connect
    return b


def ensure_group(obj, name):
    return obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)


def smoothstep(a, b, x):
    if b - a < 1e-6:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


# ----------------------------------------------------------------- the skeleton

def build_armature(name, ox, pivot, arm_l, arm_r, body):
    """One armature per judge. Bone positions are derived from the judge's own
    geometry so the three different silhouettes each get a skeleton that fits."""
    lo, hi = world_bounds(body)
    top = pivot.z                     # the neck dies at the head pivot
    base = lo.z

    arm = bpy.data.armatures.new(name + "_Rig")
    rig = bpy.data.objects.new(name + "_Rig", arm)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones

    # Spine chain up the body. Three links is enough for a seated character to
    # lean and breathe; more would only be weight painting nobody can see.
    z0 = base
    z1 = base + (top - base) * 0.34
    z2 = base + (top - base) * 0.66
    root = add_bone(eb, "Root", Vector((ox, 0, z0)), Vector((ox, 0, z0 + 0.06)))
    spine = add_bone(eb, "Spine", Vector((ox, 0, z0)), Vector((ox, 0, z1)), root)
    chest = add_bone(eb, "Chest", Vector((ox, 0, z1)), Vector((ox, 0, z2)), spine, True)
    neck = add_bone(eb, "Neck", Vector((ox, 0, z2)), pivot.copy(), chest, True)

    # THE HEAD BONE. Its head is exactly the pivot the geometry was built around;
    # everything above is bound to it rigidly. Move this and you break the socket.
    add_bone(eb, "Head", pivot.copy(), pivot + Vector((0, 0, 0.14)), neck, True)

    # ONE BONE PER HAND, not a shoulder-and-elbow chain.
    #
    # The hands are detached mitts (see build_judges.mitts), so there is no limb to
    # articulate and nothing that has to stay joined to the torso. The bone sits inside
    # the mitt and the mitt is bound to it rigidly, which means the rig can translate a
    # hand and spin it in place and no arrangement of keys can tear anything.
    for side, obj in (("L", arm_l), ("R", arm_r)):
        c = mitt_centre(obj)
        r = mitt_radius(obj, c)
        add_bone(eb, "Hand_" + side, c - Vector((0, 0, r * 0.5)),
                 c + Vector((0, 0, r * 0.5)), chest)

    bpy.ops.object.mode_set(mode='OBJECT')
    rig["pivot_z"] = pivot.z
    return rig, (z0, z1, z2, top)


# -------------------------------------------------------------------- skinning

def skin(rig, body, head, arm_l, arm_r, ox, zs):
    """Explicit weights. Automatic ones would smear the head socket across the
    neck, which is the one thing that must not happen here."""
    z0, z1, z2, top = zs

    # ---- head: rigid, weight 1. This is what preserves the ball joint.
    g = ensure_group(head, "Head")
    g.add([v.index for v in head.data.vertices], 1.0, 'REPLACE')

    # ---- body: blended up the spine so it bends instead of hinging.
    gs = {n: ensure_group(body, n) for n in ("Spine", "Chest", "Neck")}
    for v in body.data.vertices:
        z = (body.matrix_world @ v.co).z
        wn = smoothstep(z1, top, z)                       # neck takes over high up
        wc = smoothstep(z0, z2, z) * (1.0 - wn)           # chest through the middle
        ws = max(0.0, 1.0 - wn - wc)
        for n, w in (("Spine", ws), ("Chest", wc), ("Neck", wn)):
            if w > 1e-4:
                gs[n].add([v.index], w, 'REPLACE')

    # ---- hands: rigid, weight 1, exactly like the head.
    #
    # A mitt is a solid prop, not a deforming limb: there is no elbow to fold and no
    # socket to preserve, so a weight gradient could only distort it. Binding it whole
    # to one bone is what makes the hands structurally incapable of tearing, stretching
    # or drifting away from the body — the failure that jointed arms kept producing.
    for side, obj in (("L", arm_l), ("R", arm_r)):
        g = ensure_group(obj, "Hand_" + side)
        g.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')

    for obj in (body, head, arm_l, arm_r):
        obj.parent = rig
        obj.matrix_parent_inverse = rig.matrix_world.inverted()
        m = obj.modifiers.new("Armature", 'ARMATURE')
        m.object = rig


# ------------------------------------------------------------------- animation
#
# Everything below is authored timing. The runtime version layered two octaves of
# Perlin noise per axis and fired gestures on a random schedule, which never
# repeated and never landed — noise has no anticipation, no accent and no settle,
# so the judges read as drifting rather than as behaving. Keyframes have all
# three, and a curve that holds still for a beat is something noise cannot do at
# any amplitude.

# WHERE THE HANDS LIVE IS SETTLED BY THE MODEL, NOT BY A TABLE HERE.
#
# There used to be a hand-written per-side (swing, drop, lower) correction that key()
# added to every arm keyframe, because the judges were modelled with jointed arms held
# out for silhouette. It never worked, and the reason is worth keeping: the angles were
# chosen by driving a bone and reading the tail's Y, and in this rig Y is depth, not
# height. The number meant to report "the hand came down" was measuring the arm swinging
# forwards, so every re-guess of its sign was checked against the wrong axis. Three
# rounds went by that way, then two more on pivots and elbow bends.
#
# The arms are gone. build_judges now models the hands as detached mitts sitting at desk
# height, so the resting pose is simply where they are modelled and there is nothing to
# correct. The offsets below stay at zero and exist only so a clip can still be written
# as a deviation from rest.
#
# The rule worth keeping from all of it: if a pose can be measured off the model,
# measure it. Nothing about a bone's local axes after an FBX round trip is recoverable
# by looking at a render — see local_offset() for how translation avoids the same trap.

_ARM_BONES = ("Hand_L", "Hand_R")
_rest_for = {b: (0.0, 0.0, 0.0) for b in _ARM_BONES}


def hand_key(rig, side, frame, inward=0.0, lift=0.0, fwd=0.0, spin=0.0):
    """Move a mitt, stated in WORLD terms.

    INWARD is toward the judge's own centre line, LIFT is up, FWD is out away from the
    body toward the camera; all in Blender units, all small. The conversion into the
    bone's own axes goes through local_offset, so no caller ever has to know which way a
    given bone's local X happens to point — which is the mistake that cost this file
    five rounds when the arms were jointed.

    This is why the hands are mitts. Clapping is now "the hands move toward each other",
    which is what clapping is, rather than a chain of shoulder and elbow rotations that
    has to keep a limb attached to a torso while it happens.
    """
    bone = "Hand_" + side
    sx = -1.0 if side == "L" else 1.0        # the L mitt is modelled at +x
    world = Vector((sx * inward, -fwd, lift))
    key(rig, bone, frame, rot=(0.0, spin, 0.0), loc=local_offset(rig, bone, world))


def bounce(rig, frames):
    """A vertical bob on the torso. FRAMES is [(frame, lift), ...], in Blender units.

    Keyed on Spine rather than Root so the judge's base stays planted behind the bench
    while everything above it — chest, neck, head and both mitts, which hang off Chest —
    rides up and down together. It is the cheapest thing that makes a low-poly character
    read as alive, and with no arms to animate it carries most of the idle.

    AMPLITUDES HAVE TO BE READ AGAINST THE JUDGE'S SIZE, not against the numbers used for
    rotation elsewhere in this file. These characters are 0.75 to 1.25 units tall and the
    export is 1:1 to metres, so the first pass at 4 mm was a bob of half a percent of body
    height — arithmetically present and invisible on screen. Centimetres, not millimetres.
    """
    for f, lift in frames:
        key(rig, "Spine", f, rot=None, loc=local_offset(rig, "Spine", (0.0, 0.0, lift)))


def key(rig, bone, frame, rot=None, loc=None):
    pb = rig.pose.bones[bone]
    pb.rotation_mode = 'XYZ'
    if rot is not None:
        r = list(rot)
        if bone in _ARM_BONES:
            # Every arm key is stated as a DEVIATION from the resting pose, so a clip
            # written as (0,0,0) means "arms where they belong" rather than "arms in the
            # modelled bind pose". That distinction is the whole reason Lean used to
            # play with the arms stuck out: it keys the spine and head only, and a bone
            # with no keys holds its bind pose.
            base = _rest_for[bone]
            r[0] += base[0]
            r[1] += base[1]
            r[2] += base[2]
        pb.rotation_euler = [math.radians(a) for a in r]
        pb.keyframe_insert("rotation_euler", frame=frame)
    if loc is not None:
        pb.location = loc
        pb.keyframe_insert("location", frame=frame)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    return act


def cycle(rig, name, length, tracks):
    """tracks: {bone: [(frame, (rx,ry,rz)), ...]}. First and last key are made
    identical so the clip loops without a hitch."""
    act = new_action(rig, name)
    for bone, keys in tracks.items():
        for f, rot in keys:
            key(rig, bone, f, rot)
        first = keys[0][1]
        key(rig, bone, length, first)
    act.use_fake_user = True
    return act


def idle_severe(rig):
    # Mildred: upright, minimal, chews. The stillness is the performance, so her bob is
    # the shallowest of the three and her hands barely register.
    act = cycle(rig, "Idle_Severe", 120, {
        "Spine": [(1, (0, 0, 0)), (40, (1.2, 0, 0.6)), (80, (-0.8, 0, -0.5))],
        "Chest": [(1, (0, 0, 0)), (55, (1.0, 1.4, 0)), (95, (-0.6, -1.0, 0))],
        "Neck":  [(1, (0, 0, 0)), (30, (-1.5, 2.0, 0)), (70, (1.0, -2.4, 0))],
        "Head":  [(1, (0, 0, 0)), (18, (2.6, 0, 0)), (26, (-1.8, 0, 0)),
                  (34, (2.2, 0, 0)), (44, (0, 0, 0)), (86, (0, 5.0, -1.2))],
    })
    # Slowest and shallowest of the three, but no longer invisible: she breathes.
    bounce(rig, [(1, 0.0), (30, 0.020), (60, 0.0), (90, 0.016), (120, 0.0)])
    for side in ("L", "R"):
        off = 0 if side == "L" else 8
        hand_key(rig, side, 1, 0.0, 0.0)
        hand_key(rig, side, 44 + off, 0.0, 0.003)
        hand_key(rig, side, 120, 0.0, 0.0)
    return act


def idle_boisterous(rig):
    # Boris: never still, bounces on the beat. The bob is his whole character, and the
    # mitts ride it a couple of frames late so they read as loose rather than bolted on.
    act = cycle(rig, "Idle_Boisterous", 90, {
        "Spine": [(1, (0, 0, 0)), (22, (3.4, 0, 2.2)), (45, (0, 0, 0)), (68, (3.0, 0, -2.4))],
        "Chest": [(1, (0, 0, 0)), (22, (-2.0, 3.0, 0)), (45, (0, 0, 0)), (68, (-1.6, -3.2, 0))],
        "Neck":  [(1, (0, 0, 0)), (30, (2.4, -3.0, 0)), (62, (-2.0, 3.4, 0))],
        "Head":  [(1, (0, 0, 0)), (22, (4.0, 0, 2.0)), (45, (-2.0, 0, 0)), (68, (3.4, 0, -2.2))],
    })
    # The big one. Boris bounces on the beat and it is his whole read from across the lawn.
    bounce(rig, [(1, 0.0), (22, 0.062), (45, -0.014), (68, 0.054), (90, 0.0)])
    for side in ("L", "R"):
        off = 0 if side == "L" else 3
        hand_key(rig, side, 1, 0.0, 0.0)
        hand_key(rig, side, 25 + off, 0.006, 0.012, spin=6.0)
        hand_key(rig, side, 48 + off, 0.0, -0.003, spin=-3.0)
        hand_key(rig, side, 71 + off, 0.005, 0.010, spin=5.0)
        hand_key(rig, side, 90, 0.0, 0.0)
    return act


def idle_aloof(rig):
    # Priscilla: unnervingly still, then a fast snap of the head, then still again. Her
    # hands do not move at all between snaps, which is most of why she reads as cold.
    act = cycle(rig, "Idle_Aloof", 150, {
        "Spine": [(1, (0, 0, 0)), (75, (0.6, 0, 0))],
        "Chest": [(1, (0, 0, 0)), (75, (0.8, 0, 0))],
        "Neck":  [(1, (0, 0, 0)), (60, (0, 0, 0)), (66, (0, -16.0, 0)),
                  (110, (0, -16.0, 0)), (120, (0, 0, 0))],
        "Head":  [(1, (0, 0, 0)), (60, (0, 0, 0)), (65, (-3.0, -12.0, 2.0)),
                  (112, (-3.0, -12.0, 2.0)), (122, (0, 0, 0))],
    })
    # Barely there, and very slow. Enough that she is not a statue; not enough to be warm.
    bounce(rig, [(1, 0.0), (48, 0.013), (100, 0.004), (150, 0.0)])
    for side in ("L", "R"):
        hand_key(rig, side, 1, 0.0, 0.0)
        # One tiny adjustment on the head snap, and nothing else for five seconds.
        hand_key(rig, side, 64, 0.0, 0.0)
        hand_key(rig, side, 70, 0.001, 0.002, spin=-4.0)
        hand_key(rig, side, 96, 0.0, 0.0)
        hand_key(rig, side, 150, 0.0, 0.0)
    return act


def act_lean(rig):
    # Studying the picture. A pose, not a cycle -- Unity holds it by weight. The hands
    # slide forward onto the desk as the judge comes in over the lawn.
    act = new_action(rig, "Lean")
    for f, s, c, n, h in ((1, 0, 0, 0, 0), (24, 7, 6, 4, 5)):
        key(rig, "Spine", f, (s, 0, 0))
        key(rig, "Chest", f, (c, 0, 0))
        key(rig, "Neck", f, (n, 0, 0))
        key(rig, "Head", f, (h, 0, 0))
    for side in ("L", "R"):
        hand_key(rig, side, 1, 0.0, 0.0)
        hand_key(rig, side, 24, -0.004, 0.0, fwd=0.014)
    act.use_fake_user = True
    return act


# ---------------------------------------------------------- reactions, per judge
#
# ONE APPLAUD AND ONE SHAKE FOR ALL THREE WAS THE PROBLEM.
#
# The judges had distinct idles from the start but shared a single reaction pair, so the
# only thing telling them apart at the moment they reacted was body shape. The goose read
# as different because a long neck cannot help but read as different; the goat and the
# badger played identical performances, and the panel came across as one character
# wearing three heads.
#
# What follows is not one curve at three amplitudes -- that is the same note played
# louder. Each judge reacts with a different PART of the body, on a different rhythm, for
# a different number of beats:
#
#   MILDRED   withholds. Two slow claps a long way apart, barely leaving the desk, and a
#             curt chin-lift instead of any body movement. Her refusal is one turn away
#             and a very long hold -- unimpressed at you rather than about the lawn.
#   BORIS     commits everything. Five fast claps off a big wind-up, mitts leaving the
#             bench entirely, chest and head thrown through the beat, and an overshoot he
#             has to recover from. His refusal is a whole-body slump.
#   PRISCILLA does it all in the neck. One slow imperial nod over a hand-flutter far too
#             fast to count, and a long serpentine S with the hands held perfectly still.

def act_applaud_mildred(rig):
    act = new_action(rig, "Applaud")
    # Two claps only, and slow. The gaps are the performance -- a third beat would make
    # her generous, which she is not.
    for f, inw, lift in ((1, 0.0, 0.0), (7, -0.010, 0.004), (13, 0.030, 0.010),
                         (19, 0.008, 0.004), (27, 0.032, 0.011), (33, 0.006, 0.003),
                         (52, 0.0, 0.0)):
        for side in ("L", "R"):
            hand_key(rig, side, f, inw, lift)
    # The chin does the approving, not the body. Spine deliberately almost dead.
    for f, pitch in ((1, 0), (12, -3.4), (20, -1.0), (30, -3.0), (52, 0)):
        key(rig, "Head", f, (pitch, 0, 0))
    for f, pitch in ((1, 0), (16, 0.8), (52, 0)):
        key(rig, "Chest", f, (pitch, 0, 0))
    act.use_fake_user = True
    return act


def act_applaud_boris(rig):
    act = new_action(rig, "Applaud")
    # Five beats, fast, off an enormous wind-up. He does not settle cleanly either: the
    # second-to-last key overshoots past rest and has to come back.
    beats = ((1, 0.0, 0.0), (4, -0.034, 0.010), (8, 0.052, 0.046), (11, 0.010, 0.030),
             (14, 0.058, 0.052), (17, 0.012, 0.032), (20, 0.060, 0.055),
             (23, 0.014, 0.034), (26, 0.054, 0.048), (29, 0.010, 0.026),
             (32, 0.044, 0.038), (36, -0.012, 0.004), (48, 0.0, 0.0))
    for f, inw, lift in beats:
        for side in ("L", "R"):
            hand_key(rig, side, f, inw, lift, spin=inw * 90.0)
    for f, pitch, roll in ((1, 0, 0), (5, 3.0, 0), (9, -6.0, 2.0), (15, -7.5, -2.4),
                           (21, -8.0, 2.6), (27, -6.0, -1.8), (38, 1.5, 0), (48, 0, 0)):
        key(rig, "Chest", f, (pitch, 0, roll))
        key(rig, "Spine", f, (pitch * 0.5, 0, roll * 0.6))
    # Head thrown back on the accents, arriving late each time.
    for f, pitch in ((1, 0), (7, 4.5), (12, -7.0), (18, -8.5), (24, -7.0), (32, -3.0),
                     (41, 2.0), (48, 0)):
        key(rig, "Head", f, (pitch, 0, 0))
    act.use_fake_user = True
    return act


def act_applaud_priscilla(rig):
    act = new_action(rig, "Applaud")
    # A flutter, not a clap: tiny, and much faster than either of the others can move.
    flutter = ((1, 0.0), (4, 0.011), (7, 0.002), (10, 0.010), (13, 0.001),
               (16, 0.008), (19, 0.001), (22, 0.005), (30, 0.0))
    for f, v in flutter:
        for side in ("L", "R"):
            hand_key(rig, side, f, v, v * 0.5, spin=v * 260.0)
    # Over the top of it, one slow imperial nod that takes the whole clip.
    for f, pitch in ((1, 0), (18, -7.0), (34, -5.5), (54, 0)):
        key(rig, "Neck", f, (pitch, 0, 0))
    for f, pitch in ((1, 0), (22, -4.0), (38, -3.0), (54, 0)):
        key(rig, "Head", f, (pitch, 0, 0))
    act.use_fake_user = True
    return act


def act_shake_mildred(rig):
    act = new_action(rig, "Shake")
    # Barely a shake. One turn away, held far longer than is comfortable, then back.
    for f, y in ((1, 0), (6, 4), (13, -19), (34, -17), (44, -6), (56, 0)):
        key(rig, "Head", f, (0, y, 0))
    for f, y in ((1, 0), (8, 2), (16, -9), (36, -8), (48, -2), (56, 0)):
        key(rig, "Neck", f, (0, y, 0))
    for f, pitch in ((1, 0), (16, -2.6), (36, -2.2), (56, 0)):
        key(rig, "Chest", f, (pitch, 0, 0))
    # The hands do not participate. They stay flat on the desk for the whole clip, which
    # is the point: she has not been moved enough to move.
    for side in ("L", "R"):
        hand_key(rig, side, 1, 0.0, 0.0)
        hand_key(rig, side, 56, 0.0, 0.0)
    act.use_fake_user = True
    return act


def act_shake_boris(rig):
    act = new_action(rig, "Shake")
    # A big slow wag, and the whole body goes with it. He is disappointed FOR you.
    head = ((1, 0), (5, 9), (13, -26), (22, 21), (31, -15), (39, 8), (47, -3), (58, 0))
    for f, y in head:
        key(rig, "Head", f, (0, y, 0))
    for f, y in head:
        key(rig, "Neck", f + 3, (0, y * 0.55, 0))
    # The shoulders drop and stay dropped -- the slump is what sells it.
    for f, pitch in ((1, 0), (10, -3.0), (24, -6.5), (40, -6.0), (58, 0)):
        key(rig, "Chest", f, (pitch, 0, 0))
        key(rig, "Spine", f, (pitch * 0.6, 0, 0))
    # Mitts come off the desk and turn over in a helpless shrug, then go back.
    for f, inw, lift, spin in ((1, 0.0, 0.0, 0.0), (14, -0.014, 0.020, -22.0),
                               (30, -0.018, 0.026, -30.0), (44, -0.006, 0.010, -10.0),
                               (58, 0.0, 0.0, 0.0)):
        for side in ("L", "R"):
            hand_key(rig, side, f, inw, lift, spin=spin)
    act.use_fake_user = True
    return act


def act_shake_priscilla(rig):
    act = new_action(rig, "Shake")
    # A serpentine S travelling up the neck, with one sharp accent near the end. The
    # hands never move at all, which is the entire joke.
    for f, y in ((1, 0), (9, -14), (20, 12), (30, -9), (38, 16), (44, -4), (60, 0)):
        key(rig, "Neck", f, (0, y, 0))
    for f, y, roll in ((1, 0, 0), (13, -17, 3), (25, 15, -3), (35, -12, 2),
                       (42, 19, -4), (50, -5, 0), (60, 0, 0)):
        key(rig, "Head", f, (0, y, roll))
    for side in ("L", "R"):
        hand_key(rig, side, 1, 0.0, 0.0)
        hand_key(rig, side, 60, 0.0, 0.0)
    act.use_fake_user = True
    return act


TEMPERAMENT = {"Mildred": idle_severe, "Boris": idle_boisterous, "Priscilla": idle_aloof}
REACTIONS = {
    "Mildred":   (act_applaud_mildred, act_shake_mildred),
    "Boris":     (act_applaud_boris, act_shake_boris),
    "Priscilla": (act_applaud_priscilla, act_shake_priscilla),
}


def author(rig, name):
    """Every clip this judge ships with, pushed onto its own NLA tracks.

    Named per judge because one FBX carries three armatures and Unity would
    otherwise see three clips called 'Idle'.

    The NLA part is not decoration. Exporting with bake_anim_use_all_actions
    applies EVERY action in the file to EVERY armature: three rigs times twelve
    actions came out as thirty-six clips, and thirty of them were nonsense —
    Mildred's severe idle driving Boris's skeleton. Only the eight bones they
    happen to share made it even look plausible. One strip per action per rig
    exports exactly the twelve that mean something.
    """
    # No resting correction. The mitts are modelled where they belong, so rest is rest
    # and every clip is written as a deviation from it.
    for b in _ARM_BONES:
        _rest_for[b] = (0.0, 0.0, 0.0)

    made = [TEMPERAMENT[name](rig)]

    def ensure_arm_rest(act):
        """Every clip must STATE where the arms are, even if it does not move them.

        A bone with no keys holds the bind pose, and the bind pose is the modelled
        one with the arms held out for silhouette. So baking the rest correction into
        the keyframes fixed nothing on the clips that never touched the arms — Lean
        keys only the spine and head, and it was the pose the judging beat spends
        most of its time in. The judges leaned in beautifully with their arms still
        stuck out sideways.
        """
        rig.animation_data.action = act
        lo, hi = act.frame_range
        keyed = {fc.data_path for fc in act.fcurves}
        for b in _ARM_BONES:
            if 'pose.bones["%s"].rotation_euler' % b in keyed:
                continue
            key(rig, b, int(lo), (0, 0, 0))
            key(rig, b, int(hi), (0, 0, 0))
    applaud, shake = REACTIONS[name]
    for fn in (act_lean, applaud, shake):
        a = fn(rig)
        a.name = name + "_" + a.name
        made.append(a)
    made[0].name = name + "_" + made[0].name

    for a in made:
        ensure_arm_rest(a)

    rig.animation_data.action = None
    for a in made:
        track = rig.animation_data.nla_tracks.new()
        track.name = a.name
        strip = track.strips.new(a.name, 1, a)
        strip.name = a.name
        # Left UNMUTED, deliberately. Muting these to stop them blending on the
        # timeline seemed tidy and produced an FBX with no animation at all: the
        # exporter walks the strips and skips anything muted, so the whole
        # performance silently vanished and the file still looked healthy.
        track.mute = False

    return made


# ----------------------------------------------------------------------- export

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
        # Strips, not "all actions" — see author(). all_actions cross-applies every
        # action to every armature and produced thirty-six clips where twelve were
        # wanted.
        bake_anim=True, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True, bake_anim_simplify_factor=0.0)
    print("EXPORTED", filepath)


def main():
    fresh_scene()
    B = load(os.path.join(HERE, "build_judges.py"))

    specs = [("Mildred", B["mildred"], -0.95), ("Boris", B["boris"], 0.0),
             ("Priscilla", B["priscilla"], 0.95)]

    allobjs, allmesh, previews = [], [], []

    for name, fn, ox in specs:
        P = fn(ox)
        parts = {k: P[k][0] for k in ("Body", "Head", "Arm_L", "Arm_R", "Card")}
        meshes = list(parts.values())

        B["bake_ao"](meshes, floor=0.76, dist=0.20, samples=28, ground=True,
                     ground_size=1.6)
        B["card_uv"](parts["Card"])

        # Prove the socket still holds BEFORE anything is bound to a skeleton, so
        # a failure here is a modelling problem and not a weighting one.
        L.joint_audit(parts["Body"], parts["Head"], P["Head"][1], name)

        pivot = Vector(P["Head"][1])
        for k in ("Body", "Head", "Arm_L", "Arm_R", "Card"):
            B["set_pivot"](parts[k], P[k][1])

        # THIS UPDATE IS LOAD-BEARING, and its absence is what "the arms are all
        # detached" actually was.
        #
        # set_pivot translates the mesh data by -pivot and assigns obj.location =
        # pivot, so the geometry does not move in world space. But assigning location
        # does not refresh matrix_world; that happens on the next depsgraph
        # evaluation. Everything below — build_armature and skin — positions itself by
        # reading obj.matrix_world @ v.co, and without this call it read the translated
        # mesh through the OLD matrix, measuring every part a whole pivot offset away
        # from where it sits. Back when the judges had jointed arms that put each
        # shoulder bone out in empty space, and rotating a bone whose origin is not
        # inside the limb swings the limb off the torso. It matters just as much now:
        # a mitt bone would be built next to the mitt rather than inside it.
        #
        # Measured: Boris's arm-to-body gap read 0.042 through the stale matrix and
        # 0.018 after this line, on a judge two thirds of a unit tall.
        bpy.context.view_layer.update()

        rig, zs = build_armature(name, ox, pivot, parts["Arm_L"], parts["Arm_R"],
                                 parts["Body"])
        skin(rig, parts["Body"], parts["Head"], parts["Arm_L"], parts["Arm_R"], ox, zs)

        # The card stays an unskinned child. Unity tips it up off the desk as a
        # prop, and binding it to the arm would drag the number about with every
        # gesture — the one thing on the bench that has to stay rock steady.
        parts["Card"].parent = rig

        clips = author(rig, name)
        print("RIGGED", name, "bones:", len(rig.data.bones),
              "clips:", [c.name for c in clips])

        # Each judge ships centred on its OWN origin, not on its place in the row.
        #
        # The three are built side by side at x = -0.95, 0 and +0.95 so they can be
        # previewed together, and the old build then parented each one to a root empty
        # and moved that empty to the origin — which carried the meshes with it. This
        # script parents to the armature instead, and the armature was already AT the
        # origin, so setting its location to zero did nothing at all and two of the
        # three judges exported with their offset baked in. Unity then placed them on
        # the bench correctly and they sat a metre to one side of where they belonged.
        #
        # Shifting the rig by -ox moves the bones and every parented mesh together.
        rig.location = (-ox, 0, 0)
        allobjs.append((name, [rig] + meshes))
        allmesh += meshes
        previews.append((name, meshes))

    # One file per judge, and it is not a stylistic preference.
    #
    # Blender's FBX exporter builds one take per animation strip and writes EVERY
    # selected armature into EVERY take. With three rigs in one file that turned
    # twelve clips into thirty-six, and twenty-four of them were Mildred's severe
    # idle driving Boris's skeleton — plausible-looking rubbish, because the three
    # rigs share bone names. Splitting the export is the only way to get each
    # judge's four clips and nothing else.
    for name, objs in allobjs:
        export(objs, os.path.join(L.MODELS_DIR, "Judge_%s.fbx" % name))

    print("DONE Judges rig")


main()
