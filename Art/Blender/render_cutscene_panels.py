# render_cutscene_panels.py — the ten opening-cutscene panels, rendered from the
# game's own 3D assets.
#
# DuckCutsceneBuilder.cs owns the sequence: ten beats, each with a caption, a
# placeholder label and a Ken Burns move expressed as two UV rects. Those rects
# are the real brief. A panel is not "a nice picture of a duck" — it is a picture
# whose subject sits inside the rect the move ENDS on, because that is the frame
# the player is looking at when the beat lands. Panels 5 and 6 both end on
# Rect(0.30, 0.06, 0.56, 0.56), so both put their "$10,000" at the same place on
# the page; that repeat is the whole hinge of the story and it is composed for
# here, not in the C#.
#
# Everything in frame is built by the existing build scripts (duck, mower,
# foliage, props) through load(), so the cutscene cannot drift away from the game
# it introduces. The few things the story needs and the game does not own — the
# notice board, the excavator, the for-sale sign, the flyer, the shed — are
# modelled here with the same duck_lib toolkit and the same palette.
#
# The location is deliberately ONE pond, lit nine different ways: contentment,
# threat, grief, gold. A sequence that changes place every panel reads as ten
# unrelated pictures instead of one afternoon going wrong.
#
# Run:  blender --background --python C:\Duck\Art\Blender\render_cutscene_panels.py
#       ... --only=3,5     just those panels (iteration)
#       ... --crops        rebuild the crop-check sheets from the PNGs on disk
import bpy, bmesh, math, os, sys, types, random
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L
from duck_lib import (MB, sect, frame, ring_z, ring_y, cyl, tube, sphere, disc,
                      quad, torus, rbox, recess, flat_card, set_pivot, make_empty,
                      attach, bake_ao, fresh_scene, hexcol, TAU)

OUT_DIR = r"C:\Duck\Assets\Art\Textures\Cutscene"
RES = (1920, 1080)          # DuckCutsceneBuilder crops in UV; the art must be 16:9
MAT = "M_Panel"             # vertex-colour material for everything built here

# ------------------------------------------------------------------ palette
# Straight out of DuckSceneBuilder.P, so the panels and the playable scene are
# the same painting.
SKY_Z, SKY_H = "#3F90DA", "#CFE7F2"
GRASS, GRASS_LIT, GRASS_DK = "#2E7331", "#55A542", "#1B5220"
WATER, WATER_S = "#328DB6", "#68B0C4"
DIRT = "#B99A6B"
WOOD, WOOD_D = "#9A6B41", "#6E4A2C"
CREAM, CHALK, FENCE = "#F4E7CF", "#F7F3E4", "#F1EDE0"
RED, RED_D = "#DD3F38", "#A32E2D"
BRASS, GREY, DARK = "#C9A55A", "#4A4F55", "#241F1E"
YELLOW, YELLOW_D = "#D9A22B", "#A87A1E"   # the excavator: the one intruding colour
GLASS = "#9FD4E4"
DUCK_SH = "#DCC9A4"
SUNLIGHT = (1.0, 0.95, 0.84)

POND_CY = 8.5               # the pond sits BEYOND the near bank, so there is dry
POND_RX, POND_RY = 9.0, 7.0  # land at y < 0.5 for the duck, the signs and the camera
SKY_MIX = SKY_H             # what distance fades toward; each panel sets it


def mix_hex(a, b, t):
    """Blend two '#RRGGBB' strings in sRGB. Used to haze distant scenery toward
    the sky, which is what keeps a flat-shaded set from reading as cardboard."""
    a, b = a.lstrip("#"), b.lstrip("#")
    out = "#"
    for i in (0, 2, 4):
        va, vb = int(a[i:i + 2], 16), int(b[i:i + 2], 16)
        out += "%02X" % int(round(va + (vb - va) * t))
    return out


# ------------------------------------------------------------------ load()
def load(name):
    """Import a build script for its builders WITHOUT running its main().

    The build scripts end in a bare main() that would rebuild and re-export the
    whole asset (and in build_judges' case, one an agent is editing right now).
    Strip that call and exec the rest as a module.
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


DUCK = load("build_duck")
MOWER = load("build_mower")
FOL = load("build_foliage")
PROPS = load("build_props")


# ================================================================== materials
def vc_mat(name, rough=0.80, metal=0.0, spec=0.25, emit=None, emit_str=0.0):
    """duck_lib's vertex-colour material with the surface tuned."""
    m = L.get_mat(name)
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if "Specular" in b.inputs:
        b.inputs["Specular"].default_value = spec
    if emit is not None:
        for k in ("Emission Color", "Emission"):
            if k in b.inputs:
                b.inputs[k].default_value = hexcol(emit)
        b.inputs["Emission Strength"].default_value = emit_str
    return m


def fm(col, rough=0.80, emit_str=0.0):
    """Flat single-colour material, for lettering and light panels."""
    name = "FM_%s_%d_%d" % (col.lstrip("#"), int(rough * 100), int(emit_str * 10))
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = hexcol(col)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = 0.0
    if "Specular" in b.inputs:
        b.inputs["Specular"].default_value = 0.2
    if emit_str:
        for k in ("Emission Color", "Emission"):
            if k in b.inputs:
                b.inputs[k].default_value = hexcol(col)
        b.inputs["Emission Strength"].default_value = emit_str
    return m


# ================================================================== type
FONT_FILES = [r"C:\Windows\Fonts\ROCKB.TTF",      # Rockwell Bold: signwriter slab
              r"C:\Windows\Fonts\BOOKOSB.TTF",
              r"C:\Windows\Fonts\arialbd.ttf",
              os.path.join(HERE, "..", "..", "Assets", "TextMesh Pro", "Fonts",
                           "LiberationSans.ttf")]
_FONT = None


def display_font():
    global _FONT
    if _FONT is not None:
        return _FONT
    for p in FONT_FILES:
        if os.path.exists(p):
            try:
                _FONT = bpy.data.fonts.load(p)
                return _FONT
            except Exception as e:
                print("  font %s: %s" % (p, e))
    _FONT = False          # fall back to Blender's builtin
    return _FONT


def text3d(body, size, col, pos, rot=(90.0, 0.0, 0.0), align='CENTER',
           extrude=0.0035, bold=0.0, fit=None, spacing=1.0, name=None,
           rough=0.6):
    """A line of real 3D lettering. Everything printed in these panels — the
    notice, the price, the flyer — is geometry, so it takes the panel's own light
    instead of looking pasted on. `fit` scales the line to a width in metres,
    which keeps the layout right whichever of the candidate fonts is installed.
    """
    cu = bpy.data.curves.new(name or ("T_" + body[:10]), type='FONT')
    f = display_font()
    if f:
        cu.font = f
    cu.body = body
    cu.size = size
    cu.align_x = align
    cu.align_y = 'CENTER'
    cu.extrude = extrude
    cu.offset = bold * size          # real weight, not a fake outline
    cu.space_character = spacing
    ob = bpy.data.objects.new(name or ("T_" + body[:10]), cu)
    bpy.context.collection.objects.link(ob)
    ob.location = Vector(pos)
    ob.rotation_euler = tuple(math.radians(a) for a in rot)
    ob.data.materials.append(fm(col, rough))
    if fit:
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        w = ob.evaluated_get(dg).dimensions.x
        if w > 1e-5:
            cu.size *= fit / w
            cu.offset = bold * cu.size
    return ob


# ================================================================== scene rig
def sky(zen, hor, strength=1.0):
    w = bpy.data.worlds.new("PanelSky")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    mr = nt.nodes.new("ShaderNodeMapRange")
    rp = nt.nodes.new("ShaderNodeValToRGB")
    mr.inputs[1].default_value = -0.06
    mr.inputs[2].default_value = 0.38
    rp.color_ramp.elements[0].color = hexcol(hor)
    rp.color_ramp.elements[1].color = hexcol(zen)
    bg.inputs[1].default_value = strength
    nt.links.new(tc.outputs["Generated"], sp.inputs[0])
    nt.links.new(sp.outputs["Z"], mr.inputs[0])
    nt.links.new(mr.outputs[0], rp.inputs[0])
    nt.links.new(rp.outputs["Color"], bg.inputs[0])
    nt.links.new(bg.outputs[0], out.inputs[0])


def vignette(strength):
    """A soft corner falloff, identical on all ten panels.

    Applied in the render rather than left to the UI: the panel sits inside a
    painted card on a parchment page, and without it the render's corners butt
    straight up against the frame so every card reads as a window cut in the page
    instead of a picture lying on it. Kept mild — the Ken Burns move slides the
    crop around underneath it.
    """
    sc = bpy.context.scene
    sc.use_nodes = True
    nt = sc.node_tree
    nt.nodes.clear()
    rl = nt.nodes.new("CompositorNodeRLayers")
    comp = nt.nodes.new("CompositorNodeComposite")
    if strength <= 0:
        nt.links.new(rl.outputs["Image"], comp.inputs["Image"])
        return
    el = nt.nodes.new("CompositorNodeEllipseMask")
    el.x, el.y, el.width, el.height = 0.5, 0.5, 0.86, 0.96
    bl = nt.nodes.new("CompositorNodeBlur")
    bl.filter_type = 'FAST_GAUSS'
    bl.use_relative = True
    bl.factor_x = bl.factor_y = 26.0
    mx = nt.nodes.new("CompositorNodeMixRGB")
    mx.blend_type = 'MULTIPLY'
    mx.inputs[0].default_value = strength
    nt.links.new(el.outputs["Mask"], bl.inputs["Image"])
    nt.links.new(rl.outputs["Image"], mx.inputs[1])
    nt.links.new(bl.outputs["Image"], mx.inputs[2])
    nt.links.new(mx.outputs["Image"], comp.inputs["Image"])


def begin(zen=SKY_Z, hor=SKY_H, wstr=1.0, exposure=0.0, samples=48,
          vig=0.26, bloom=0.03):
    global _FONT
    fresh_scene()
    _FONT = None
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    sc.render.engine = 'BLENDER_EEVEE'
    ee = sc.eevee
    ee.taa_render_samples = samples
    ee.use_gtao = True
    ee.gtao_distance = 0.55
    ee.gtao_factor = 1.0
    ee.use_soft_shadows = True
    ee.shadow_cube_size = '2048'
    ee.shadow_cascade_size = '2048'
    ee.use_shadow_high_bitdepth = True
    ee.use_ssr = True
    ee.ssr_quality = 1.0
    ee.ssr_thickness = 0.4
    ee.use_bloom = bloom > 0.0
    ee.bloom_intensity = bloom
    ee.bloom_threshold = 0.95
    ee.bloom_radius = 5.0
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.exposure = exposure
    sky(zen, hor, wstr)
    vignette(vig)


def sun_lamp(dirv, energy=3.4, col=SUNLIGHT, angle=5.0, name="Sun"):
    """A sun pointing ALONG dirv — the direction the light travels."""
    d = bpy.data.lights.new(name, 'SUN')
    d.energy = energy
    d.color = col
    d.angle = math.radians(angle)
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    o.rotation_euler = Vector(dirv).to_track_quat('-Z', 'Y').to_euler()
    return o


def area_lamp(loc, look, energy, col=(1, 1, 1), size=3.0, name="Area"):
    d = bpy.data.lights.new(name, 'AREA')
    d.energy = energy
    d.color = col
    d.size = size
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    o.location = Vector(loc)
    o.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return o


def point_lamp(loc, energy, col=(1, 0.9, 0.7), radius=0.1, name="Point"):
    d = bpy.data.lights.new(name, 'POINT')
    d.energy = energy
    d.color = col
    d.shadow_soft_size = radius
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    o.location = Vector(loc)
    return o


def camera(center, frame_h, dirv, lens=50.0, u=0.5, v=0.5, roll=0.0,
           fstop=None, focus_at=None):
    """Frame `frame_h` metres of world height around `center`, from direction
    `dirv`, and put `center` at normalised screen position (u, v).

    The offset is a SENSOR SHIFT, not a re-aim: aiming off-subject to move it in
    frame tips the verticals, and a storybook page full of tipped verticals reads
    as a mistake. shift_x/shift_y are in units of the sensor's fit dimension,
    which at 1920x1080 is the width — hence the 9/16 on v.
    """
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("Cam")
    cd.lens = lens
    cd.sensor_fit = 'AUTO'
    cam = bpy.data.objects.new("Cam", cd)
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    ctr = Vector(center)
    vfov = 2.0 * math.atan(0.5 * cd.sensor_width * RES[1] / RES[0] / lens)
    dist = (frame_h * 0.5) / math.tan(vfov * 0.5)
    cam.location = ctr + Vector(dirv).normalized() * dist
    q = (ctr - cam.location).to_track_quat('-Z', 'Y')
    if roll:
        q = q @ Euler((0, 0, math.radians(roll)), 'XYZ').to_quaternion()
    cam.rotation_euler = q.to_euler()
    cd.shift_x = 0.5 - u
    cd.shift_y = (0.5 - v) * RES[1] / RES[0]
    if fstop:
        cd.dof.use_dof = True
        cd.dof.focus_distance = (Vector(focus_at) - cam.location).length \
            if focus_at else dist
        cd.dof.aperture_fstop = fstop
        cd.dof.aperture_blades = 6
    print("  cam at %.2f %.2f %.2f  dist %.2f  frame_h %.2f"
          % (cam.location.x, cam.location.y, cam.location.z, dist, frame_h))
    return cam


def render(idx):
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "panel_%02d.png" % idx)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("PANEL %02d -> %s" % (idx, path))


# ================================================================== set pieces
def group(name, objs, loc=(0, 0, 0), rot=(0, 0, 0)):
    """Rigid group: build at the origin, parent, THEN transform. attach() only
    behaves while the parent is still at the origin, which is exactly why the
    lettering has to be created before the sign is moved."""
    e = make_empty(name)
    for o in objs:
        attach(o, e)
    e.location = Vector(loc)
    e.rotation_euler = tuple(math.radians(a) for a in rot)
    return e


def face_toward(ob, n, up=(0, 0, 1)):
    """Point an object's printed face (its local -Y, the way flat_card builds it)
    along `n`, keeping its lettering level.

    Euler angles cannot do this by eye: (-64, 0, 30) tipped the flyer AND spun its
    type diagonally across the frame, because XYZ applies the Z last. Build the
    basis instead — right = up x normal, and the rest follows.
    """
    n = Vector(n).normalized()
    x = Vector(up).cross(n)
    if x.length < 1e-4:
        x = Vector((1.0, 0.0, 0.0))
    x.normalize()
    y = -n
    z = x.cross(y)
    ob.rotation_euler = Matrix((x, y, z)).transposed().to_euler()
    return ob


def plane(name, col, size, z=0.0, ctr=(0.0, 0.0), mat=MAT):
    mb = MB()
    x, y = ctr
    s = size * 0.5
    quad(mb, col, (x - s, y - s, z), (x + s, y - s, z),
         (x + s, y + s, z), (x - s, y + s, z))
    return mb.finish(name, mat, seal=False)


def lightbox(name, col, x, y0, y1, z0, z1, emit=6.0):
    """A blown-out rectangle of daylight, facing +X. Stands outside the shed's
    doorway: without it the opening reads as a grey hole, because there is no sky
    worth the name inside a dim interior's world."""
    mb = MB()
    quad(mb, col, (x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1))
    ob = mb.finish(name, "M_PanelDay", seal=False)
    vc_mat("M_PanelDay", rough=0.5, emit=col, emit_str=emit)
    return ob


def pond_polygon(seed=9, n=18):
    rnd = random.Random(seed)
    pts = []
    for i in range(n):
        a = TAU * i / n
        j = 1.0 + rnd.uniform(-0.12, 0.12)
        pts.append((math.cos(a) * POND_RX * j, POND_CY + math.sin(a) * POND_RY * j))
    return pts


def pond(water_col=WATER, bank_col=DIRT, wz=0.055, seed=9):
    """The pond: one water face and a muddy bank ring standing slightly proud of
    it. Returns (water, bank)."""
    pts = pond_polygon(seed)
    mb = MB()
    mb.f([mb.v((x, y, wz)) for (x, y) in pts], water_col)
    water = mb.finish("Pond_Water", "M_PanelWater", seal=False)
    # Rough enough that the screen-space reflections smear into a sheen instead
    # of drawing a second, upside-down, half-missing duck under the first one.
    vc_mat("M_PanelWater", rough=0.20, spec=0.55)

    mb = MB()
    inner = [(x * 1.005, POND_CY + (y - POND_CY) * 1.005) for (x, y) in pts]
    outer = [(x * 1.10, POND_CY + (y - POND_CY) * 1.13) for (x, y) in pts]
    lo = [mb.v((x, y, wz - 0.02)) for (x, y) in inner]
    hi = [mb.v((x, y, 0.10)) for (x, y) in outer]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        # wound so the slope faces UP and OUTWARD; the other order lights it
        # from underneath and the whole bank goes black
        mb.f((lo[j], lo[i], hi[i], hi[j]), bank_col)
    mb.bm.normal_update()
    bank = mb.finish("Pond_Bank", MAT, seal=False)
    return water, bank


def ripple(name, ctr, R, col=None, wz=0.060):
    col = col or WATER_S
    """Two hairline rings. They were three times this thick to begin with and
    read as hula hoops lying on the pond — a ripple is a highlight, not a pipe."""
    mb = MB()
    torus(mb, col, (ctr[0], ctr[1], wz), (0, 0, 1), R, 0.005, nmaj=24, nmin=4)
    torus(mb, col, (ctr[0], ctr[1], wz), (0, 0, 1), R * 1.38, 0.0035,
          nmaj=26, nmin=4)
    return mb.finish(name, MAT)


def sun_disc(name, loc, r=1.6, col="#FFF0C8", emit=9.0):
    """A low sun sitting on the horizon. Cheap, and it turns a graded sky into a
    time of day.

    A flat puck, not a squashed sphere: the squashed sphere's pole pinched into a
    drip that hung below the sun like a leak.
    """
    mb = MB()
    disc(mb, col, (0, 0, 0), (0, -1, 0), r, n=32, t=0.02)
    ob = mb.finish(name, "M_PanelSun")
    vc_mat("M_PanelSun", rough=0.5, emit=col, emit_str=emit)
    ob.location = Vector(loc)
    return ob


def dup(ob, loc, rotz=0.0, scale=1.0, name=None):
    """A second instance sharing the mesh — scenery, so no per-copy AO."""
    d = ob.copy()
    d.data = ob.data
    if name:
        d.name = name
    bpy.context.collection.objects.link(d)
    d.location = Vector(loc)
    d.rotation_euler = (0, 0, math.radians(rotz))
    d.scale = (scale, scale, scale)
    return d


def prop(fn, name, loc=(0, 0, 0), rotz=0.0, scale=1.0, rotx=0.0):
    mb = MB()
    fn(mb)
    ob = mb.finish(name, "M_Props", 30.0)
    ob.location = Vector(loc)
    ob.rotation_euler = (math.radians(rotx), 0, math.radians(rotz))
    ob.scale = (scale, scale, scale)
    return ob


def tall_grass(name, count, ctr, span, h=(0.28, 0.62), seed=4,
               cols=(GRASS, GRASS_LIT, GRASS_DK)):
    """A band of individual blades.

    Not decoration: the duck is modelled in a SEATED driving pose (legs forward,
    feet on the pedals), so every panel that stands it up has to bury the lower
    body in grass or water. This is the grass.
    """
    mb = MB()
    rnd = random.Random(seed)
    cx, cy = ctr
    sx, sy = span
    for k in range(count):
        bx = cx + rnd.uniform(-sx, sx)
        by = cy + rnd.uniform(-sy, sy)
        hh = rnd.uniform(*h)
        w = rnd.uniform(0.012, 0.024)
        lean = rnd.uniform(0.04, 0.16)
        la = rnd.uniform(0, TAU)
        tip = Vector((bx + lean * math.cos(la), by + lean * math.sin(la), hh))
        col = cols[k % 3]
        a0 = mb.v((bx - w, by, 0.0))
        b0 = mb.v((bx + w, by, 0.0))
        m0 = mb.v((bx + w * 0.4 + (tip.x - bx) * 0.5, by + (tip.y - by) * 0.5, hh * 0.55))
        m1 = mb.v((bx - w * 0.4 + (tip.x - bx) * 0.5, by + (tip.y - by) * 0.5, hh * 0.55))
        t0 = mb.v(tip)
        mb.f((a0, b0, m0, m1), col)
        mb.f((m1, m0, t0), col)
    mb.bm.normal_update()
    for f in mb.bm.faces:
        f.smooth = False
    return mb.finish(name, MAT, 26.0)


def hill_band(col, y=56.0, seed=2, n=7, span=90.0):
    """Distant hills, hazed toward the sky. Squashed spheres, because at that
    range a hill is a silhouette and nothing else."""
    mb = MB()
    rnd = random.Random(seed)
    for i in range(n):
        x = -span * 0.5 + span * (i + rnd.uniform(0.1, 0.9)) / n
        r = rnd.uniform(11.0, 19.0)
        h = rnd.uniform(3.4, 7.2)
        sphere(mb, col, (x, y + rnd.uniform(-6, 10), -h * 0.35),
               (r, r * 0.7, h), seg=14, rings=5, smooth=True)
    return mb.finish("Hills", MAT, 40.0)


def pond_set(grade=0.0, trees=True, hills=True, hedge=True):
    """The pond, dressed. Every outdoor panel is this same place — same reed
    clumps, same tree line, same bank.

    `grade` mixes the set's colours toward SKY_MIX, which is how dusk and
    overcast are made: no per-panel repaint, one number.
    """
    def g(c, f=1.0):
        return mix_hex(c, SKY_MIX, grade * f)

    plane("Ground", g(GRASS), 240.0, 0.0, (0.0, 24.0))
    pond(g(WATER, 0.6), g(mix_hex(DIRT, GRASS_DK, 0.30)))
    # Reeds only where the camera is close enough to see the stems. A clump at
    # fifteen metres renders as three floating brown dashes, because the bulrush
    # heads survive the distance and the blades they sit on do not.
    reeds = FOL.reeds()
    reeds.location = (-2.65, 1.05, 0.0)
    reeds.scale = (1.5, 1.5, 1.6)
    for (x, y, s, r) in [(-3.9, 2.4, 1.25, 40), (2.75, 1.30, 0.95, 130),
                         (4.4, 2.7, 0.85, 250), (-5.6, 5.0, 1.20, 70),
                         (6.1, 6.4, 1.05, 20), (2.35, 1.05, 0.90, 300),
                         (-4.8, 1.9, 1.30, 210)]:
        dup(reeds, (x, y, 0.0), rotz=r, scale=s)
    lily = FOL.lilypad()
    lily.location = (1.9, 6.4, 0.062)
    for p, r, s in (((-1.7, 8.0, 0.062), 40, 0.9), ((3.4, 9.6, 0.062), 200, 1.05),
                    ((-3.9, 10.2, 0.062), 120, 0.85)):
        dup(lily, p, rotz=r, scale=s)
    if hedge:
        # spaced to CLOSE, so the far bank reads as one hedge line instead of a
        # row of green boxes standing about
        h = FOL.hedge_straight()
        h.location = (-9.0, 18.4, 0.0)
        for i in range(12):
            dup(h, (-9.0 + i * 2.15, 18.4 + (i % 2) * 0.25, 0.0),
                rotz=(i * 7) % 30 - 15)
    if trees:
        oak = FOL.tree_oak()
        oak.location = (-11.0, 22.0, 0.0)
        dup(oak, (19.0, 29.0, 0.0), rotz=120, scale=0.85)
        pop = FOL.tree_poplar()
        pop.location = (12.5, 22.0, 0.0)
        dup(pop, (14.6, 24.6, 0.0), rotz=60, scale=0.86)
        ap = FOL.tree_apple()
        ap.location = (-7.6, 20.0, 0.0)
    if hills:
        hill_band(mix_hex("#7DB08A", SKY_MIX, 0.45 + 0.4 * grade))


# ================================================================== the duck
DUCK_EYE = Vector((0.082, -0.102, 0.406))
DUCK_NECK = (0.0, -0.050, 0.300)
FLOAT_Z = 0.052             # body at the waterline, legs hidden by the water
LAND_Z = 0.138              # webbed feet exactly on the ground

BILL_PIV = Vector((0.0, -0.140, 0.372))   # build_duck's bill root
# The bill's own mouth seam, as a plane through that root. build_duck lays every
# bill ring with rot=-pi/12 precisely so a face band sits on the mid-height
# extreme, and the ring centres fall from z=0.372 at the root to z=0.347 at the
# tip -- so the seam is the plane containing that 1-in-6 fall. Unit normal points
# up and very slightly back, i.e. out of the top of the beak.
BILL_SEAM_N = Vector((0.0, -0.025, 0.150)).normalized()
# Warm at the lip, near-black at the throat. Flat "#3A2A22" everywhere read as a
# hole punched in the duck; flat warm brown everywhere read as a slab of filling
# in a bun, because a cavity lit evenly from front to back is not a cavity. The
# panel-3 camera looks straight down into this, so it is a large area and its
# colour is a real decision, not a fill.
MOUTH_LIP, MOUTH_THROAT, TONGUE = "#6B3430", "#241615", "#A85C52"
BILL_TIP_Y, BILL_ROOT_Y = -0.306, -0.150       # the range the gradient runs over

# The gape hinges HERE, not at the bill root, and this is the line that decides
# whether the panel reads as an open beak at all. build_duck's bill root sits at
# y=-0.140, which is 30 mm INSIDE the skull (the head's front reaches y=-0.159 at
# bill height). Hinged there, both mandibles have already separated by the time
# they emerge from the head, so the opening is a parallel-sided slot and the beak
# reads as a bun with a filling in it. Hinged at the corner of the mouth -- where
# the bill actually leaves the skull -- the mandibles meet at the back and diverge
# toward the tip, which is the V a beak opens into.
def seam_pt(y):
    """The point on the mouth seam at depth y, on the bill's centre line."""
    return Vector((0.0, y, BILL_PIV.z
                   - (y - BILL_PIV.y) * BILL_SEAM_N.y / BILL_SEAM_N.z))


GAPE_PIV = seam_pt(-0.168)


def split_bill(bl, gape, squash=0.06, eps=0.0012):
    """Cut the one-piece bill into an upper and a lower mandible and open it by
    `gape` degrees. Returns (upper, lower).

    THE DUCK HAS NO MOUTH. build_bill() is a single closed spatula whose only
    "mouth" is a 3.2 mm groove recessed into the flanks, so rotating Duck_Bill
    does not open a beak -- it pitches the whole beak like a snout. The first pass
    at panels 3 and 8 posed bill=22/14 and parked a dark ellipsoid inside the head
    to be the throat. Measured: at bill=22 that ellipsoid's top cap stood 1 mm
    proud of the bill's dorsal surface at y=-0.180 and 38 mm proud at y=-0.270, so
    it rendered as a flat near-black hexagon lying ON TOP of the bill, dead centre
    of the two biggest close-ups in the sequence. Do not re-introduce a mouth
    interior that is not enclosed by geometry.

    Instead, a real gape, and WITHOUT changing topology: each mandible is the whole
    bill with everything on the far side of the seam squashed to 6% of its depth,
    so the far half becomes a shallow 1.6 mm shell that is the roof (or floor) of
    the mouth. Squashed rather than flattened onto the plane: projecting a closed
    half-shell onto its own cut plane leaves folded, overlapping, coplanar slivers
    that z-fight and speckle the inside of the mouth. Squashed, every face keeps
    its area and its outward normal.

    The two halves therefore still add up to the original silhouette when
    gape=0 -- they meet at the seam with a 0.24 mm slit inside a groove that is
    already there -- and the dark faces exist ONLY on those inner shells, so the
    mouth can only ever be seen through the opening it is behind.

    A duck's lower mandible does most of the moving, so the gape is split 35/65 --
    and it has to be, because the cap's peak reaches y=-0.180 at z=0.4255 and the
    upper mandible's root sits at z=0.410. Much past 15 deg of lift and the beak
    comes up through the peak of its own hat.
    """
    parts = []
    for sign, name, share in ((1.0, "Duck_Bill_Upper", -0.35),
                              (-1.0, "Duck_Bill_Lower", 0.65)):
        me = bl.data.copy()
        me.name = name
        inner = [False] * len(me.vertices)
        for v in me.vertices:
            # s > 0 is the half this mandible keeps
            s = (v.co - BILL_PIV).dot(BILL_SEAM_N) * sign
            if s < eps:
                s2 = eps + (s - eps) * squash
                v.co = v.co + BILL_SEAM_N * (sign * (s2 - s))
                inner[v.index] = True
        cols = me.color_attributes.get("Col")
        if cols:
            for poly in me.polygons:
                # ALL verts squashed: the lip faces that straddle the fold keep
                # their orange, which is what stops the mouth reading as a
                # painted-on hole
                if not all(inner[vi] for vi in poly.vertices):
                    continue
                y = sum(me.vertices[vi].co.y for vi in poly.vertices) \
                    / len(poly.vertices)
                t = (y - BILL_TIP_Y) / (BILL_ROOT_Y - BILL_TIP_Y)
                t = min(1.0, max(0.0, t)) ** 0.7
                c = hexcol(mix_hex(MOUTH_LIP, MOUTH_THROAT, t))
                for li in poly.loop_indices:
                    cols.data[li].color = c
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.clear()
        ob.data.materials.append(L.get_mat("M_Duck"))
        parts.append((ob, share))
    bpy.data.objects.remove(bl, do_unlink=True)

    # A tongue, riding the lower mandible. The mouth floor is the largest single
    # surface these two close-ups show, and flat dark across all of it read as a
    # scoop; one lighter shape in the middle of it is what says "mouth" instead.
    # Sunk 4 mm under the seam plane, so it can only be seen through the gape.
    mb = MB()
    sphere(mb, TONGUE, seam_pt(-0.212) - BILL_SEAM_N * 0.005,
           (0.030, 0.052, 0.007), seg=10, rings=5)
    parts.append((mb.finish("Duck_Tongue", "M_Duck"), parts[1][1]))
    return [(ob, share * gape) for (ob, share) in parts]


def duck(loc=(0, 0, 0), yaw=0.0, head=(0, 0), bill=0.0, gape=0.0, wings=(0, 0),
         wing_yaw=(0, 0), tail=0.0, cap=(0, 0, 0), face=None, ao=True):
    """Build the hero duck and pose it.

    The game animates named transforms, so the build script's pivots are exactly
    the handles a still needs: head pitch/yaw about the neck socket, the bill
    about its root, the wings about the shoulders. Nothing here re-models the duck.

    `bill` pitches the whole beak (positive = tip down). `gape` OPENS it, which
    the shipped one-piece bill cannot do on its own -- see split_bill().
    """
    body = DUCK.build_body()
    hd = DUCK.build_head()
    bl = DUCK.build_bill()
    cp = DUCK.build_cap()
    wl = DUCK.build_wing(1, "Duck_Wing_L")
    wr = DUCK.build_wing(-1, "Duck_Wing_R")
    tl = DUCK.build_tail()
    fl = DUCK.build_foot(1, "Duck_Foot_L")
    fr = DUCK.build_foot(-1, "Duck_Foot_R")
    # Split BEFORE the AO bake so both mandibles are baked shut, like the rest of
    # the duck; baking after the gape opens would burn the open pose into the paint.
    mandibles = split_bill(bl, gape) if gape else [(bl, 0.0)]
    meshes = [body, hd, cp, wl, wr, tl, fl, fr] + [m for (m, _) in mandibles]
    extra = duck_face(face) if face else []
    if ao:
        bake_ao(meshes, floor=0.78, dist=0.13, samples=24, ground=False)

    set_pivot(body, (0.0, 0.0, 0.0))
    set_pivot(hd, DUCK_NECK)
    for (m, dg) in mandibles:
        # A gaping beak hinges at the corner of the mouth; a closed one is still
        # the shipped Duck_Bill and keeps the shipped root.
        set_pivot(m, GAPE_PIV if gape else BILL_PIV)
    set_pivot(cp, DUCK.CROWN)
    set_pivot(wl, (0.146, -0.046, 0.198))
    set_pivot(wr, (-0.146, -0.046, 0.198))
    set_pivot(tl, (0.0, 0.066, 0.206))
    set_pivot(fl, (0.080, -0.192, -0.108))
    set_pivot(fr, (-0.080, -0.192, -0.108))
    for e in extra:
        if e.type == 'MESH':
            set_pivot(e, DUCK_NECK)      # face parts ride the head pivot

    root = make_empty("Duck_Root", (0, 0, 0))
    for o in (body, hd, wl, wr, tl, fl, fr):
        attach(o, root)
    for (m, _) in mandibles:
        attach(m, hd)
    attach(cp, hd)
    for e in extra:
        attach(e, hd)

    hd.rotation_euler = (math.radians(head[0]), 0.0, math.radians(head[1]))
    for (m, dg) in mandibles:
        m.rotation_euler = (math.radians(bill + dg), 0.0, 0.0)
    wl.rotation_euler = (math.radians(wings[0]), 0.0, math.radians(wing_yaw[0]))
    wr.rotation_euler = (math.radians(wings[1]), 0.0, math.radians(wing_yaw[1]))
    tl.rotation_euler = (math.radians(tail), 0.0, 0.0)
    cp.rotation_euler = tuple(math.radians(a) for a in cap)
    root.location = Vector(loc)
    root.rotation_euler = (0, 0, math.radians(yaw))
    bpy.context.view_layer.update()
    return dict(root=root, body=body, head=hd, bill=mandibles[0][0], cap=cp,
                yaw=yaw, mandibles=[m for (m, _) in mandibles],
                wings=(wl, wr), tail=tl, feet=(fl, fr), meshes=meshes + extra)


def duck_local(d, p):
    """A duck-local point in world space — for putting things in its wings."""
    R = Euler((0, 0, math.radians(d["yaw"])), 'XYZ').to_matrix()
    return d["root"].location + R @ Vector(p)


def duck_face(mode):
    """Expression the base mesh cannot hold.

    The duck's eye is a dark bulb with a catch-light: right for a driving
    close-up at 25 m, useless for a face that has to carry shock and greed at
    full frame. So a cream eyeball big enough to swallow the bulb, a small dark
    pupil on the front of it, and for greed a gold dollar where the pupil was.
    Built as its own object on the head pivot, so it turns with the head.
    """
    objs = []
    mb = MB()
    for sx in (1, -1):
        c = Vector((sx * DUCK_EYE.x, DUCK_EYE.y, DUCK_EYE.z))
        out = Vector((sx * 0.46, -0.87, 0.16)).normalized()
        # sunk 8 mm INTO the skull: a cream ball merely sat on the cheek reads as
        # an egg glued to the side of the head
        r = 0.036 if mode == "resolve" else 0.041     # wide for shock, not for resolve
        sphere(mb, "#FBF6EA", c - out * 0.010, (r, r - 0.001, r + 0.002),
               seg=14, rings=7)
        if mode != "greed":
            sphere(mb, "#221E1C", c + out * 0.022, (0.023, 0.023, 0.024),
                   seg=10, rings=5)
            sphere(mb, "#FFFFFF", c + out * 0.038 + Vector((sx * 0.008, -0.004, 0.009)),
                   (0.008, 0.008, 0.008), seg=6, rings=3)
        if mode == "resolve":
            # A brow. Nothing else in the duck's vocabulary says "decided": the
            # eye is a bulb, the bill cannot purse, and the body pose is fixed by
            # the driving rig. A heavy bar angled down toward the bill does it in
            # one shape, and it is the only cue that survives a 0.9 m frame.
            cyl(mb, "#C6AE85", c + Vector((-sx * 0.034, -0.016, 0.012)),
                c + Vector((sx * 0.030, -0.006, 0.044)), 0.013, 0.010, n=6)
    # NO mouth cavity here. There used to be a dark ellipsoid at (0, -0.196,
    # 0.352) for "the cavity the rotated bill uncovers" — but the bill is one
    # solid piece and rotating it uncovers nothing, so the ellipsoid's top cap
    # simply stood proud of the beak's dorsal surface (1 mm at its root, 38 mm at
    # its tip) and rendered as a flat black hexagon on top of the bill in panels
    # 3 and 8. The mouth now belongs to split_bill(), where it is enclosed.
    objs.append(mb.finish("Duck_Face", "M_Duck"))

    if mode == "greed":
        for sx in (1, -1):
            c = Vector((sx * DUCK_EYE.x, DUCK_EYE.y, DUCK_EYE.z))
            out = Vector((sx * 0.46, -0.87, 0.16)).normalized()
            t = text3d("$", 0.058, BRASS, c + out * 0.026, extrude=0.006,
                       rough=0.35, name="Duck_Dollar")
            t.rotation_euler = out.to_track_quat('Z', 'Y').to_euler()
            objs.append(t)
    return objs


def sparkles(name, ctr, n=7, R=0.22, seed=5, col=BRASS, size=0.020):
    """Four-point glints, built as a star and not as a cross.

    The first version was two equal crossed rods per glint, each tapered from one
    end to the other, and it rendered as seven literal PLUS SIGNS floating round
    the duck's head — the arms were the same length, they met at a right angle, and
    a rod that is fat at one end and thin at the other has no point on it at all.
    A glint is: arms of two different lengths, each built as two cones back to back
    from the centre so both tips come to a point, a bright core where they cross,
    and a per-glint roll so seven of them are not seven copies of one drawing.
    """
    mb = MB()
    rnd = random.Random(seed)
    for k in range(n):
        a = TAU * k / n + rnd.uniform(-0.3, 0.3)
        r = R * rnd.uniform(0.75, 1.25)
        c = Vector(ctr) + Vector((math.cos(a) * r, rnd.uniform(-0.05, 0.05),
                                  math.sin(a) * r * 0.8))
        s = size * rnd.uniform(0.6, 1.35)
        roll = rnd.uniform(-0.45, 0.45)
        # long axis, then a short one across it: 1.0 and 0.46
        for (ang, ln) in ((0.0, 1.0), (math.pi * 0.5, 0.46)):
            d = Vector((math.sin(ang + roll), 0.0,
                        math.cos(ang + roll))) * s * ln
            for e in (d, -d):
                cyl(mb, col, c, c + e, s * 0.12, s * 0.004, n=4)
        sphere(mb, col, c, (s * 0.16, s * 0.10, s * 0.16), seg=6, rings=3)
    ob = mb.finish(name, "M_PanelGlow")
    vc_mat("M_PanelGlow", rough=0.35, emit=col, emit_str=1.6)
    return ob


# ================================================================== new props
def board_on_posts(name, w, h, z_bot, col_face, col_frame, col_post=FENCE,
                   post_r=0.048, thick=0.07, inset=0.055):
    """A framed board on two posts, face toward -Y, built at the origin.

    Returns (object, face_y): face_y is the recessed panel's surface, so
    lettering placed a few millimetres in front of it sits INSIDE the frame
    instead of floating over it.
    """
    mb = MB()
    z_top = z_bot + h
    px = w * 0.5 - 0.11
    # The posts sit BEHIND the board. At y=0 a 48 mm post pokes 13 mm through the
    # board's face and draws two brown stripes straight down the lettering.
    py = thick * 0.5 + post_r * 0.55
    for sx in (-1, 1):
        mb.loft([ring_z(8, 0.0, post_r, post_r, 9.0, sx * px, py),
                 ring_z(8, z_top - 0.10, post_r, post_r, 9.0, sx * px, py),
                 ring_z(8, z_top - 0.02, post_r * 0.85, post_r * 0.85, 7.0,
                        sx * px, py)],
                col_post, cap_a=True, cap_b=True, smooth=False)
    faces = rbox(mb, col_frame, (0.0, 0.0, z_bot + h * 0.5), (w, thick, h),
                 r=0.016, n=10, e=6.0, k=1)
    front = [f for f in faces if f.is_valid and f.normal.y < -0.7]
    recess(mb, front, thickness=inset, depth=-0.010, col=col_face)
    ob = mb.finish(name, MAT, 30.0)
    return ob, -thick * 0.5 + 0.010


def hanging_placard(name, w, h, cz, hang_from, col_face=CHALK, col_frame=WOOD):
    """The price, hung under the for-sale board on two short chains."""
    mb = MB()
    for sx in (-1, 1):
        cyl(mb, GREY, (sx * w * 0.34, 0.0, hang_from),
            (sx * w * 0.34, 0.0, cz + h * 0.5 - 0.02), 0.012, n=6)
    faces = rbox(mb, col_frame, (0.0, 0.0, cz), (w, 0.06, h),
                 r=0.014, n=10, e=6.0, k=1)
    front = [f for f in faces if f.is_valid and f.normal.y < -0.7]
    recess(mb, front, thickness=0.045, depth=-0.009, col=col_face)
    return mb.finish(name, MAT, 30.0), -0.030 + 0.009


def flyer(name, w=0.60, h=0.84):
    """The championship flyer: a cream sheet with a red masthead band and two
    rules, printed in geometry so the light falls across the type."""
    mb = MB()
    flat_card(mb, CREAM, DUCK_SH, (0.0, 0.0, 0.0), w, h, 0.006)
    rbox(mb, RED, (0.0, -0.004, h * 0.5 - 0.085), (w * 0.985, 0.004, 0.17),
         r=0.004, n=6, e=8.0, k=1)
    # clear of the type: at the first spacing the upper rule ran straight through
    # the middle of "OPEN TO ALL COMERS"
    for z in (h * 0.5 - 0.375, -h * 0.5 + 0.165):
        rbox(mb, RED_D, (0.0, -0.004, z), (w * 0.80, 0.004, 0.008),
             r=0.002, n=4, e=8.0, k=1)
    return mb.finish(name, MAT, 30.0), -0.008


def excavator(name="Excavator"):
    """The threat.

    The game owns no machine that ruins things, so it is built here — in the
    palette, as one silhouette: a low tracked slab, a heavy house, and a boom
    that breaks the top of frame. Yellow is the only colour in the sequence the
    pond does not already contain, which is the point of it.
    """
    mb = MB()
    for sx in (-1, 1):
        rbox(mb, DARK, (sx * 0.95, 0.0, 0.42), (0.56, 3.5, 0.72),
             r=0.30, n=14, e=2.6, k=3)
        for k in range(11):
            y = -1.62 + 3.24 * k / 10.0
            rbox(mb, GREY, (sx * 0.95, y, 0.42), (0.62, 0.10, 0.78),
                 r=0.03, n=6, e=7.0, k=1)
        for y in (-1.42, 1.42):
            cyl(mb, GREY, (sx * 0.95 - 0.30, y, 0.42), (sx * 0.95 + 0.30, y, 0.42),
                0.30, 0.30, n=12)
    rbox(mb, YELLOW_D, (0.0, 0.0, 0.80), (2.10, 2.60, 0.26), r=0.05, n=8, e=6.0, k=1)
    hs = rbox(mb, YELLOW, (0.0, 0.28, 1.44), (1.86, 2.30, 1.05),
              r=0.10, n=12, e=5.0, k=2)
    side = [f for f in hs if f.is_valid and abs(f.normal.x) > 0.75
            and f.calc_center_median().y > 0.4]
    recess(mb, side, thickness=0.10, depth=-0.030, col=YELLOW_D)
    rbox(mb, GREY, (0.0, 1.44, 1.20), (1.80, 0.34, 0.72), r=0.06, n=8, e=6.0, k=1)
    cyl(mb, DARK, (0.42, 0.92, 1.95), (0.42, 0.92, 2.46), 0.10, 0.085, n=10)
    cyl(mb, DARK, (0.42, 0.92, 2.46), (0.42, 0.92, 2.54), 0.13, 0.13, n=10)
    cb = rbox(mb, YELLOW, (-0.52, -0.42, 2.00), (0.98, 1.16, 1.16),
              r=0.09, n=12, e=5.0, k=2)
    recess(mb, [f for f in cb if f.is_valid and f.normal.y < -0.7],
           thickness=0.10, depth=-0.020, col=GLASS)
    recess(mb, [f for f in cb if f.is_valid and f.normal.x < -0.7],
           thickness=0.10, depth=-0.020, col=GLASS)
    # The arm is DOWN, bucket resting on the ground it has come to move. Reaching
    # for the sky it stood 3.2 m tall, and at any framing that reads the notice
    # board the top of the boom is outside the picture — so the machine arrived as
    # a mystery yellow shape with its head cut off. Low, it also reads as waiting.
    tube(mb, YELLOW, [Vector((0.34, -0.95, 1.55)), Vector((0.34, -2.10, 2.24)),
                      Vector((0.34, -3.20, 2.05))], [0.26, 0.22, 0.19],
         n=8, e=3.4, smooth=False)
    tube(mb, YELLOW, [Vector((0.34, -3.20, 2.05)), Vector((0.34, -3.70, 1.35)),
                      Vector((0.34, -3.95, 0.62))], [0.19, 0.16, 0.13],
         n=8, e=3.4, smooth=False)
    cyl(mb, GREY, (0.34, -1.05, 1.30), (0.34, -2.30, 1.95), 0.10, 0.09, n=8)
    cyl(mb, BRASS, (0.34, -2.30, 1.95), (0.34, -2.80, 2.10), 0.055, 0.055, n=8)
    cyl(mb, GREY, (0.34, -3.02, 2.14), (0.34, -3.58, 1.42), 0.085, 0.075, n=8)
    # bucket: a scooped shell with teeth, tipped so it reads as a jaw
    prof = [(-0.34, 0.0), (0.34, 0.0), (0.34, 0.46), (0.20, 0.62),
            (-0.20, 0.62), (-0.34, 0.46)]
    bc = Vector((0.34, -4.05, 0.06))
    lo = [mb.v((bc.x + x, bc.y - 0.22, bc.z + y)) for (x, y) in prof]
    hi = [mb.v((bc.x + x * 0.92, bc.y + 0.30, bc.z + y * 0.86 + 0.10))
          for (x, y) in prof]
    for i in range(len(prof)):
        j = (i + 1) % len(prof)
        mb.f((lo[i], lo[j], hi[j], hi[i]), GREY)
    mb.f(list(reversed(lo)), GREY)
    mb.bm.normal_update()
    for k in range(5):
        x = -0.26 + 0.52 * k / 4.0
        cyl(mb, CHALK, (bc.x + x, bc.y - 0.22, bc.z + 0.06),
            (bc.x + x, bc.y - 0.44, bc.z - 0.02), 0.045, 0.018, n=5)
    return mb.finish(name, MAT, 34.0)


SHED_W, SHED_D, SHED_H = 2.9, 3.4, 2.55
DOOR = (1.50, 3.15)          # the gap in the left (-X) wall, in y
WINDOW = (-2.00, -1.05)      # the opening in the back (+Y) wall, in x


def shed(name="Shed"):
    """The shed interior: plank walls, a rafter roof, and a doorway at the far
    left that the panel's only daylight comes through. One mesh; the dressing is
    separate so it can be moved for framing."""
    mb = MB()
    W, D, Hh = SHED_W, SHED_D, SHED_H
    for k in range(13):                       # floor boards
        x = -W + (2 * W) * (k + 0.5) / 13.0
        c = WOOD_D if k % 2 else mix_hex(WOOD_D, DARK, 0.25)
        rbox(mb, c, (x, 0.0, -0.03), (2 * W / 13.0 - 0.012, 2 * D, 0.06),
             r=0.008, n=4, e=8.0, k=1)

    def wall(axis, sign, span, skip=()):
        n = 16
        for k in range(n):
            t = -span + (2 * span) * (k + 0.5) / n
            if any(a <= t <= b for (a, b) in skip):
                continue
            c = WOOD if k % 3 == 0 else (WOOD_D if k % 3 == 1 else
                                         mix_hex(WOOD, WOOD_D, 0.5))
            wpl = 2 * span / n - 0.014
            if axis == 'x':
                rbox(mb, c, (sign * W, t, Hh * 0.5), (0.10, wpl, Hh),
                     r=0.008, n=4, e=8.0, k=1)
            else:
                rbox(mb, c, (t, sign * D, Hh * 0.5), (wpl, 0.10, Hh),
                     r=0.008, n=4, e=8.0, k=1)
    # Back wall, with a window. The doorway that lights this panel sits behind the
    # left wall and is off-frame at any lens that also holds the mower, so the
    # room needs one bright opening the player can actually see.
    for k in range(16):
        t = -W + (2 * W) * (k + 0.5) / 16.0
        c = WOOD if k % 3 == 0 else (WOOD_D if k % 3 == 1 else
                                     mix_hex(WOOD, WOOD_D, 0.5))
        wpl = 2 * W / 16.0 - 0.014
        if WINDOW[0] <= t <= WINDOW[1]:
            for (z0, z1) in ((0.0, 1.32), (2.22, SHED_H)):
                rbox(mb, c, (t, D, (z0 + z1) * 0.5), (wpl, 0.10, z1 - z0),
                     r=0.008, n=4, e=8.0, k=1)
        else:
            rbox(mb, c, (t, D, SHED_H * 0.5), (wpl, 0.10, SHED_H),
                 r=0.008, n=4, e=8.0, k=1)
    for t in (WINDOW[0] - 0.09, WINDOW[1] + 0.09):
        rbox(mb, WOOD, (t, D, 1.77), (0.12, 0.15, 1.02), r=0.01, n=6, e=7.0, k=1)
    for z in (1.30, 2.24):
        rbox(mb, WOOD, ((WINDOW[0] + WINDOW[1]) * 0.5, D, z),
             (WINDOW[1] - WINDOW[0] + 0.28, 0.15, 0.11), r=0.01, n=6, e=7.0, k=1)
    rbox(mb, WOOD, ((WINDOW[0] + WINDOW[1]) * 0.5, D - 0.02, 1.77),
         (0.06, 0.10, 0.94), r=0.008, n=4, e=8.0, k=1)     # glazing bar
    wall('x', -1, D, skip=(DOOR,))           # left wall, with the doorway
    wall('x', 1, D)
    for t in (DOOR[0] - 0.10, DOOR[1] + 0.10):
        rbox(mb, WOOD, (-W, t, 1.15), (0.16, 0.16, 2.30), r=0.01, n=6, e=7.0, k=1)
    rbox(mb, WOOD, (-W, (DOOR[0] + DOOR[1]) * 0.5, 2.32),
         (0.16, DOOR[1] - DOOR[0] + 0.3, 0.16), r=0.01, n=6, e=7.0, k=1)
    for k in range(7):                       # rafters, then a boarded ceiling
        y = -D + (2 * D) * (k + 0.5) / 7.0
        rbox(mb, WOOD_D, (0.0, y, Hh + 0.20), (2 * W, 0.14, 0.18),
             r=0.012, n=4, e=8.0, k=1)
    rbox(mb, mix_hex(WOOD_D, DARK, 0.55), (0.0, 0.0, Hh + 0.42),
         (2 * W + 0.2, 2 * D + 0.2, 0.16), r=0.01, n=4, e=8.0, k=1)
    rbox(mb, WOOD, (0.4, D - 0.22, 1.42), (2.2, 0.36, 0.055),
         r=0.01, n=6, e=8.0, k=1)            # shelf
    for sx in (-0.5, 1.3):
        rbox(mb, WOOD_D, (sx, D - 0.16, 1.20), (0.07, 0.24, 0.42),
             r=0.008, n=4, e=8.0, k=1)
    return mb.finish(name, MAT, 32.0)


def motes(name, ctr, n=70, span=(1.1, 0.9, 1.2), seed=11):
    """Dust in the shaft: the difference between a dim room and a dim room with
    air in it."""
    mb = MB()
    rnd = random.Random(seed)
    for k in range(n):
        c = Vector(ctr) + Vector((rnd.uniform(-span[0], span[0]),
                                  rnd.uniform(-span[1], span[1]),
                                  rnd.uniform(-span[2], span[2])))
        r = rnd.uniform(0.0035, 0.009)
        sphere(mb, "#FFF0CF", c, (r, r, r), seg=6, rings=3)
    ob = mb.finish(name, "M_PanelMote")
    # 16 mm and 2.6 strength turned the shaft into falling snow
    vc_mat("M_PanelMote", rough=0.5, emit="#FFF0CF", emit_str=1.7)
    return ob


def crumbs(name, a, b, n=6, arc=0.22):
    """A handful of feed in the air, on a parabola from wing tip to water."""
    mb = MB()
    a, b = Vector(a), Vector(b)
    for k in range(n):
        t = (k + 0.5) / n
        p = a + (b - a) * t + Vector((0, 0, arc * math.sin(math.pi * t)))
        r = 0.019 + 0.008 * math.cos(k * 2.1)
        sphere(mb, "#E8D6A8", p, (r, r, r * 0.8), seg=8, rings=4)
    return mb.finish(name, MAT)


def fish(name, ctr, yaw=0.0, col="#E2803A"):
    """A koi breaking the surface — the thing being fed, and the reason panel 1
    has a subject rather than a landscape.

    Built at the origin and then MOVED. Building it at `ctr` and rotating the
    object spins it about the world origin instead of its own nose, which threw
    the first pair of fish several metres out of frame.
    """
    mb = MB()
    sphere(mb, col, (0, 0, 0), (0.052, 0.115, 0.048), seg=10, rings=5)
    tailp = [(0.0, 0.10), (0.055, 0.20), (0.0, 0.155), (-0.055, 0.20)]
    mb.f([mb.v((x, y, 0.012)) for (x, y) in tailp], col)
    mb.bm.normal_update()
    for sx in (1, -1):
        sphere(mb, "#241F1E", (sx * 0.036, -0.072, 0.022),
               (0.010, 0.010, 0.010), seg=6, rings=3)
    ob = mb.finish(name, MAT)
    ob.location = Vector(ctr)
    ob.rotation_euler = (0, 0, math.radians(yaw))
    return ob


def mower(loc=(0, 0, 0), yaw=0.0, ao=True):
    meshes = MOWER.build_all()
    if ao:
        bake_ao(meshes, floor=0.76, dist=0.22, samples=22, ground=True,
                ground_size=6.0)
    root = make_empty("Mower_Root", (0, 0, 0))
    for o in meshes:
        attach(o, root)
    root.location = Vector(loc)
    root.rotation_euler = (0, 0, math.radians(yaw))
    return root, meshes


# ==================================================================== PANELS
# Each panel notes the rect its move ENDS on, because that is the frame the
# composition is actually built for.

def panel_01():
    """THE POND · FEEDING THE FISH — ends on Rect(0.13, 0.11, 0.76, 0.76).

    An establishing shot that finishes on a duck who has no idea. Warm noon, the
    whole pond, foreground reeds to frame it, and the duck floating so the water
    hides a driving pose's forward legs.
    """
    global SKY_MIX
    SKY_MIX = SKY_H
    begin(SKY_Z, SKY_H, 1.05, samples=64, vig=0.24, bloom=0.035)
    pond_set(grade=0.0)
    sun_lamp((0.42, 0.62, -0.66), 3.6, SUNLIGHT, angle=4.0)
    area_lamp((-7, -6, 5), (0, 3, 0.4), 900, (0.72, 0.84, 1.0), 9.0)

    d = duck(loc=(0.15, 3.20, FLOAT_Z), yaw=38, head=(-16, -14), bill=6,
             wings=(46, 18), wing_yaw=(-8, 6), tail=-6)
    crumbs("Crumbs", duck_local(d, (0.10, -0.30, 0.24)), (1.35, 3.62, 0.10))
    # the fish sit high enough that their backs break the surface; the water is
    # opaque, so a koi at its own swimming depth is simply an invisible koi
    fish("Fish_A", (1.42, 3.86, 0.075), yaw=-140)
    fish("Fish_B", (1.05, 4.62, 0.062), yaw=150, col="#C96C33")
    ripple("Ripple_A", (1.42, 3.86), 0.19)
    ripple("Ripple_B", (1.02, 4.58), 0.15)
    ripple("Ripple_C", (0.15, 3.20), 0.34)
    tall_grass("Verge", 130, (0.0, 0.55), (4.4, 0.55), h=(0.16, 0.40), seed=6)

    # Nearly at the waterline: from any higher there is no horizon in frame, and
    # an establishing shot with no sky in it does not establish anything.
    camera((0.15, 3.26, 0.30), 2.45, (-0.52, -1.0, 0.085), lens=52,
           u=0.505, v=0.435, fstop=5.0, focus_at=(0.15, 3.26, 0.30))
    render(1)


def panel_02():
    """THE NOTICE · POND TO BE REMOVED — starts tight on the sign, Rect(0.14,
    0.22, 0.72, 0.72), and pulls back to nearly full frame.

    So the board has to be legible dead centre and the machine has to live in the
    margin the tight crop throws away. Overcast: the sun goes out here and does
    not properly come back until the flyer.
    """
    global SKY_MIX
    SKY_MIX = "#C6CBD0"
    begin("#8FA6B6", "#D3D8DA", 0.85, samples=64, vig=0.30, bloom=0.0)
    pond_set(grade=0.45)
    sun_lamp((0.30, 0.70, -0.62), 1.5, (0.90, 0.92, 0.96), angle=25.0)
    area_lamp((-7, -9, 8), (0, 2, 1), 1600, (0.86, 0.90, 0.96), 14.0)

    sign, fy = board_on_posts("Notice", 1.16, 0.80, 0.98, CREAM, FENCE,
                              col_post=WOOD)
    y = fy - 0.006
    t = [sign]
    t.append(text3d("NOTICE", 0.10, RED_D, (0.0, y, 1.655), fit=0.40, bold=0.015))
    t.append(text3d("POND TO BE", 0.10, DARK, (0.0, y, 1.505), fit=0.86, bold=0.012))
    t.append(text3d("REMOVED", 0.10, DARK, (0.0, y, 1.360), fit=0.74, bold=0.012))
    # ASCII only: the display font has no middle dot and draws a tofu box
    t.append(text3d("BY ORDER OF CITY WORKS", 0.05, GREY, (0.0, y, 1.225),
                    fit=0.74))
    t.append(text3d("7 DAYS", 0.11, RED, (0.0, y, 1.090), fit=0.46, bold=0.016))
    group("Notice", t, loc=(0.0, -0.20, 0.0), rot=(0, 0, -7))

    # Far enough back that the bucket cannot reach across the board: the machine
    # is a threat behind the words, and a bucket parked over "REMOVED" is just a
    # bucket. Its arm swings TOWARD the sign, which is all the menace needed.
    # Parked on the FAR bank, across the water, arm swung toward the pond. Close
    # in on the near side it either hid behind the reeds or stood outside the
    # frame entirely, because a 4 m machine one metre off the lens axis is not in
    # shot at all. At twenty metres it is a whole silhouette and still looming.
    ex = excavator()
    ex.location = (8.00, 16.50, 0.0)
    ex.rotation_euler = (0, 0, math.radians(-30))
    tall_grass("Verge", 90, (0.0, -0.75), (3.4, 0.30), h=(0.14, 0.34), seed=8)
    prop(PROPS.marker_stake, "Stake_A", (-1.65, 0.55, 0.0), rotz=20)
    prop(PROPS.marker_stake, "Stake_B", (1.35, 0.95, 0.0), rotz=-40)

    # 2.05 m of frame so the machine's cab clears the top edge: the frame grows
    # only 24 mm of headroom per metre of distance, so a 2.6 m tall excavator
    # cannot be fixed by pushing it further away alone.
    camera((0.0, -0.20, 1.36), 2.05, (-0.20, -1.0, 0.16), lens=48,
           u=0.50, v=0.575, fstop=9.0, focus_at=(0.0, -0.20, 1.36))
    render(2)


def panel_03():
    """SHOCK · THE DUCK'S FACE — ends on Rect(0.22, 0.20, 0.60, 0.60).

    Under two seconds with the page dark and the bars closed, so the only job is
    a face that reads instantly at 60% of frame: eyes wide, beak open, cap
    lifting, everything else thrown away by a long lens and a wide aperture.
    """
    global SKY_MIX
    # Dark, not pale. Graded toward a slate the first time round, the pond came
    # out the same value as a cream duck and the face had nothing to be read
    # against; the flash beat needs contrast more than it needs daylight.
    SKY_MIX = "#39505F"
    begin("#2B3F52", "#546C7E", 0.40, samples=72, vig=0.44, bloom=0.06)
    pond_set(grade=0.62, hills=False)
    sun_lamp((0.55, 0.55, -0.62), 0.8, (0.80, 0.84, 0.94), angle=20.0)
    # 110 W at nearly two metres. At 230 W in close the cream body blew out to
    # flat white and took the face's modelling with it.
    area_lamp((-1.5, 1.1, 1.5), (0, 2.5, 0.45), 110, (1.0, 0.95, 0.88), 1.1)
    area_lamp((2.1, 4.2, 1.0), (0, 2.6, 0.45), 90, (1.0, 0.72, 0.58), 1.4)

    # Turned to about 25 deg off the lens axis. At 47 deg the bill swings across
    # the frame and hides the far eye, and a shock the player cannot find the eyes
    # in is not a shock.
    # A gape, not a pitch. bill=22 alone swung the whole spatula down like a snout,
    # foreshortened it into an orange blob, and needed a fake throat to sell it.
    #
    # The beak points UP ten degrees in world (head -4 plus bill -6) because the
    # lens sits thirteen degrees ABOVE it. Level, the camera looks down onto the
    # FLOOR of the open mouth, which is a broad flat plate and reads as a bun with a
    # filling in it; tipped up to meet the lens, the camera looks ALONG the mouth
    # and the gape reads as a cavity with a tongue in it. Nearly all of that ten
    # degrees is spent on the BEAK rather than the head: at head=-10 the cap went
    # edge-on and the goggles climbed off the skull, and the panel lost the two
    # things it is composed on.
    d = duck(loc=(0.0, 2.60, FLOAT_Z), yaw=-6, head=(-4, 6), bill=-6, gape=26,
             wings=(-14, -8), wing_yaw=(16, -14), cap=(8, 0, -6), face="shock")
    d["cap"].location = d["cap"].location + Vector((0.0, 0.010, 0.016))
    ripple("Ripple", (0.0, 2.60), 0.24, col="#7FA8BE")

    camera((0.0, 2.50, 0.44), 0.60, (-0.30, -1.0, 0.24), lens=85,
           u=0.515, v=0.520, fstop=3.2, focus_at=(0.0, 2.48, 0.43))
    render(3)


def panel_04():
    """GRIEF · LOOKING OUT OVER THE WATER — drifts from Rect(0.16, 0.06, ...) to
    Rect(0.05, 0.11, ...): the same size at both ends.

    Nothing is discovered here, so nothing is pushed into. The duck is small,
    turned away, and the frame is mostly empty water at dusk — the negative space
    IS the beat. The only cold light in the sequence.
    """
    global SKY_MIX
    SKY_MIX = "#5C86C8"
    begin("#22335F", "#E8A574", 1.05, samples=72, vig=0.38, bloom=0.10)
    pond_set(grade=0.42)
    sun_lamp((0.16, -0.94, -0.20), 2.9, (1.0, 0.72, 0.46), angle=3.0)
    # Weak and far: at 300 W the foreground reeds lit up bright green in the
    # middle of a sunset, which is the one thing this panel cannot afford.
    area_lamp((-7, -5, 3.5), (0, 3, 0.3), 110, (0.44, 0.56, 1.0), 8.0)

    sun_disc("Sunset", (3.4, 44.0, 1.1), r=2.1, col="#FFD9A0", emit=7.0)
    d = duck(loc=(-0.30, 3.05, FLOAT_Z), yaw=201, head=(-20, 12), wings=(-6, -6),
             wing_yaw=(-4, 4), tail=-10)
    ripple("Ripple_A", (-0.30, 3.05), 0.19, col="#E9C0A8")
    ripple("Ripple_B", (0.35, 4.30), 0.13, col="#E9C0A8")
    # Painted dark, not lit dark: the blades face the camera into a backlight, and
    # no amount of turning the fill down stops the warm sky from making green
    # grass look like green grass at sunset.
    tall_grass("Verge", 120, (-0.4, 0.35), (3.0, 0.32), h=(0.16, 0.44), seed=3,
               cols=("#1B3324", "#24432E", "#132419"))

    # Down near the waterline so the empty half of the frame is water AND sky.
    # Looking down on it just makes the pond a floor.
    camera((-0.30, 3.10, 0.26), 1.85, (-0.30, -1.0, 0.075), lens=62,
           u=0.365, v=0.405, fstop=7.1, focus_at=(-0.30, 3.10, 0.26))
    render(4)


def panel_05():
    """THE PRICE · LAND FOR SALE — ends on Rect(0.30, 0.06, 0.56, 0.56).

    Half of the hinge. That rect is centred on (0.58, 0.34) of the frame, so the
    price placard is composed to land exactly there; panel 6 puts its prize in
    the same place with the same move, and the player joins them up before the
    duck does. Do not recompose one without the other.
    """
    global SKY_MIX
    SKY_MIX = "#E8DCC0"
    begin("#4E93C8", "#EEDFC0", 1.0, samples=64, vig=0.26, bloom=0.05)
    pond_set(grade=0.20)
    sun_lamp((-0.50, 0.58, -0.64), 3.3, (1.0, 0.90, 0.72), angle=4.0)
    area_lamp((7, -5, 5), (0, 2, 0.5), 800, (0.76, 0.86, 1.0), 9.0)

    sign, fy = board_on_posts("ForSale", 1.30, 0.62, 1.30, CREAM, FENCE)
    y = fy - 0.006
    t = [sign]
    t.append(text3d("LAND FOR SALE", 0.11, RED_D, (0.0, y, 1.72), fit=1.04,
                    bold=0.014))
    t.append(text3d("THE OLD POND - 2 ACRES", 0.05, GREY, (0.0, y, 1.52),
                    fit=0.86))
    plac, py = hanging_placard("Placard", 0.96, 0.34, 1.08, 1.30)
    t.append(plac)
    # bold, but not 0.018 bold: cu.offset grows every outline, and at that weight
    # the dollar sign swallowed the 1 next to it
    t.append(text3d("$10,000", 0.16, RED, (0.0, py - 0.006, 1.08), fit=0.74,
                    bold=0.006, spacing=1.06))
    group("Sign", t, loc=(0.85, 0.30, 0.0), rot=(0, 0, -13))

    # Floating out on the water, where the frame has room for it. On the near bank
    # the duck fell below the bottom of a frame composed on a price placard 1 m up.
    duck(loc=(-0.55, 3.60, FLOAT_Z), yaw=23, head=(11, 8), wings=(24, 30),
         wing_yaw=(-10, 8), tail=4)
    ripple("Ripple", (-0.55, 3.60), 0.22)
    tall_grass("Verge", 90, (-0.3, -1.05), (3.2, 0.30), h=(0.14, 0.36), seed=9)

    # Sized so "LAND FOR SALE" is still inside the frame the move ENDS on: the
    # crop stops at y=0.62 and the headline sits 0.64 m above the price.
    camera((0.85, 0.30, 1.08), 2.70, (-0.30, -1.0, 0.14), lens=50,
           u=0.575, v=0.345, fstop=6.3, focus_at=(0.85, 0.30, 1.08))
    render(5)


def panel_06():
    """THE PRIZE · CHAMPIONSHIP FLYER — ends on Rect(0.30, 0.06, 0.56, 0.56),
    identical to panel 5.

    Same move, same landing place, same size of lettering: the flyer's "$10,000"
    arrives exactly where the placard's "$10,000" just was. It is caught in the
    reeds at the water's edge, which is both how the duck finds it and why the
    beat's sound is paper fluttering.
    """
    global SKY_MIX
    SKY_MIX = "#F0DEB4"
    begin("#4E93C8", "#F3E2BC", 1.05, samples=64, vig=0.26, bloom=0.06)
    pond_set(grade=0.22)
    sun_lamp((-0.44, 0.50, -0.74), 3.6, (1.0, 0.91, 0.72), angle=4.0)
    area_lamp((5, -5, 4), (0, 2, 0.4), 700, (0.78, 0.87, 1.0), 8.0)

    fl, fy = flyer("Flyer", 0.62, 0.86)
    y = fy - 0.004
    t = [fl]
    # The event is GARDENER OF THE YEAR and lawn art is a CLASS within it, which is how a real
    # horticultural show bill reads: the show on the masthead, the discipline underneath in small
    # type. The flyer used to say "LAWN ART CHAMPIONSHIP", which made the duck's motive an art
    # contest — and left the whole defence half of the game ("a gardener protects their garden")
    # with nothing in the fiction to hang on. Naming the show this way earns the defence phase
    # without giving up lawn art as the thing that makes the game specific.
    #
    # The masthead is the only part that changed. GRAND PRIZE, $10,000 and the date keep their
    # exact z, because panel 6's "$10,000" has to land at the same screen position as panel 5's —
    # that repetition is the sequence's hinge and, since the caption plates were removed, the only
    # thing still carrying it.
    t.append(text3d("GARDENER", 0.10, CREAM, (0.0, y, 0.375), fit=0.36, bold=0.010))
    t.append(text3d("OF THE YEAR", 0.072, DARK, (0.0, y, 0.225), fit=0.40,
                    bold=0.008))
    t.append(text3d("LAWN ART CLASS", 0.038, GREY, (0.0, y, 0.128), fit=0.40))
    t.append(text3d("GRAND PRIZE", 0.05, RED_D, (0.0, y, -0.015), fit=0.32,
                    bold=0.006))
    t.append(text3d("$10,000", 0.15, DARK, (0.0, y, -0.155), fit=0.50, bold=0.006,
                    spacing=1.06))
    t.append(text3d("SATURDAY - THE GREEN", 0.032, GREY, (0.0, y, -0.335),
                    fit=0.38))
    # Nailed to a post on the bank, NOT hovering in the reeds. Snagged on two
    # stems it read as a poster floating over open water with a shadow under it,
    # which is the one thing a still cannot explain away.
    for sx in (-1, 1):
        mb = MB()
        disc(mb, BRASS, (sx * 0.22, -0.010, 0.355), (0, -1, 0), 0.016, n=8)
        t.append(mb.finish("Nail_%d" % sx, MAT, 30.0))
    # crooked on the board, but the POST stays upright: tilting the group tilted
    # the post with it and the whole thing read as falling over
    group("FlyerGrp", t, loc=(0.55, 1.18, 0.62), rot=(0, 0, -7))
    mb = MB()
    cyl(mb, WOOD_D, (0.55, 1.245, 0.0), (0.55, 1.245, 1.52), 0.055, 0.048, n=8,
        smooth=False)
    cyl(mb, WOOD, (0.55, 1.245, 1.52), (0.55, 1.245, 1.56), 0.062, 0.050, n=8,
        smooth=False)
    mb.finish("FlyerPost", MAT, 30.0)

    tall_grass("Fore", 110, (0.45, 0.66), (1.7, 0.30), h=(0.20, 0.52), seed=15)

    # Same landing place and the same headroom rule as panel 5: the masthead has
    # to survive the crop that ends on the prize.
    camera((0.55, 1.16, 0.47), 2.15, (-0.26, -1.0, 0.24), lens=58,
           u=0.575, v=0.345, fstop=3.5, focus_at=(0.55, 1.16, 0.50))
    render(6)


def panel_07():
    """REALISATION · LOOKING DOWN AT THE FLYER — ends on Rect(0.17, 0.17, 0.66,
    0.66), dead centre.

    The join the sequence has spent eleven seconds setting up, so the duck's face
    and the number have to be readable in the same frame: the camera is above and
    in front, and the flyer is tipped up toward it in the duck's own wings.
    """
    global SKY_MIX
    SKY_MIX = "#EADFC4"
    begin("#4E93C8", "#EFE2C4", 1.0, samples=72, vig=0.30, bloom=0.05)
    pond_set(grade=0.26)
    sun_lamp((-0.42, 0.44, -0.79), 3.4, (1.0, 0.92, 0.74), angle=4.0)
    area_lamp((-2.4, -2.0, 1.8), (0, 0.4, 0.4), 240, (1.0, 0.95, 0.86), 2.4)
    area_lamp((2.6, 2.6, 1.4), (0, 0.4, 0.4), 130, (0.80, 0.88, 1.0), 3.0)

    # Turned to FACE the lens, not away from it. Yawed +20 the duck's own forward
    # direction was 65% lateral to the camera, so the sheet it was holding in front
    # of its chest landed a foot to the right of it and read as a signpost it
    # happened to be standing next to.
    # Head down THIRTEEN degrees, not twenty-four. At full droop, with the camera
    # above the eyeline, the panel was the top of a cap and a bill pointing at the
    # floor: no eyes, no face, no realisation. The gaze only has to read, not
    # survey.
    d = duck(loc=(0.0, 0.30, LAND_Z), yaw=-14, head=(-13, 6),
             wings=(33, 29), wing_yaw=(-7, 5), tail=-4, face="wide")
    # Duck plus held sheet stands 0.61 m tall, and the move ends on a rect only
    # 0.66 of the frame high — at 0.94 m of frame the two fitted exactly, which
    # meant the prize line sat on the crop's bottom edge. 1.15 m gives it margin.
    cam = camera((0.0, 0.14, 0.35), 1.15, (-0.40, -1.0, 0.13), lens=62,
                 u=0.505, v=0.500, fstop=3.5, focus_at=(0.0, 0.12, 0.38))

    fl, fy = flyer("Flyer", 0.34, 0.46)
    y = fy - 0.004
    t = [fl]
    # Masthead matched to panel 6. No class line here: this flyer is 0.34 x 0.46 and the camera
    # ends tight on it, so a fourth line crowds the prize — and the prize is what this panel is
    # for. Panel 6 has already said which class it is.
    t.append(text3d("GARDENER", 0.07, CREAM, (0.0, y, 0.196), fit=0.21, bold=0.008))
    t.append(text3d("OF THE YEAR", 0.05, DARK, (0.0, y, 0.108), fit=0.24,
                    bold=0.006))
    t.append(text3d("GRAND PRIZE", 0.032, RED_D, (0.0, y, -0.012), fit=0.18,
                    bold=0.005))
    t.append(text3d("$10,000", 0.10, DARK, (0.0, y, -0.092), fit=0.27, bold=0.005,
                    spacing=1.06))
    t.append(text3d("SATURDAY", 0.024, GREY, (0.0, y, -0.184), fit=0.16))
    # Held low and well out in front. Any higher and the bill crosses the
    # masthead, so the two things the panel exists to show cover each other up.
    held = duck_local(d, (0.0, -0.34, 0.140))
    grp = group("FlyerGrp", t, loc=held)
    to_cam = (cam.location - Vector(held)).normalized()
    # TIPPED BACK, not held up. The lens sits only 8.5 deg above the held sheet, so
    # aiming the sheet's face 82% at the camera put its normal 20 deg off horizontal
    # — all but vertical — and the panel read as a duck presenting a placard to the
    # audience rather than reading one. At 0.42/0.58 the normal comes up to 41 deg
    # and the sheet leans back 41 deg, which is the angle a thing being READ sits at.
    #
    # This weight is a legibility trade and is why it is not simply 0.5/0.5: the type
    # foreshortens by the cosine of the angle between the sheet's normal and the lens
    # (33 deg here, so 0.84 of full height), and the prize line is the second subject
    # of the panel. Do not push past about 0.45 without checking $10,000 still reads.
    face_toward(grp, Vector((0, 0, 1)) * 0.42 + to_cam * 0.58)

    # Meadow at the SIDES only. A continuous band in front of the duck put blades
    # straight through the prize line — the sheet is the second subject of this
    # panel and nothing may cross it. The held flyer hides the legs by itself.
    for sx in (-1, 1):
        tall_grass("Fore_Grass_%d" % sx, 60, (sx * 1.25, -0.28), (0.65, 0.22),
                   h=(0.30, 0.58), seed=21 + sx)
    # BEHIND the sheet and shorter. Centred on y=0.12 with a 0.26 m span this band
    # still put its front row at y=-0.14, in front of a sheet held at y=-0.03, and
    # two 0.46 m blades came up through "GRAND PRIZE" and the prize line. Every
    # blade now starts behind the paper, where it cannot cross the type whatever
    # the seed does.
    tall_grass("Feet_Grass", 90, (0.0, 0.27), (0.85, 0.15), h=(0.20, 0.36),
               seed=27)
    render(7)


def panel_08():
    """GREED · DOLLAR SIGNS IN THE EYES — ends on Rect(0.25, 0.23, 0.50, 0.50),
    the tightest crop in the sequence.

    It rhymes with panel 3 on purpose: the same treatment for the thing that
    ruined the duck and the thing that saves it. Same lens, same eye height, warm
    instead of cold, gold instead of white.
    """
    global SKY_MIX
    SKY_MIX = "#D9BF88"
    # Two thirds of the light of the first attempt. Gold plus bloom plus a cream
    # duck went to flat white paper, and a blown highlight is not a mood. Still a
    # third of a stop hot after that: the breast was holding one flat value from the
    # wing line to the chin, so the key comes down as well as the exposure. Take any
    # more out and the gold goes to mud — the ceiling here is the CREAM, not the sky.
    begin("#A9702F", "#E7CE96", 0.60, exposure=-0.34, samples=72, vig=0.42,
          bloom=0.06)
    pond_set(grade=0.42, hills=False)
    # The headroom is bought back in SATURATION, not in stops. At -0.42 with the
    # lamps neutral-warm the breast held its modelling but the panel went grey-olive
    # and stopped being the gold beat at all, and this is the one panel in the
    # sequence whose whole job is gold. So: a third of a stop back, and the key and
    # the sun pushed further into amber instead.
    sun_lamp((-0.62, 0.42, -0.66), 1.6, (1.0, 0.81, 0.48), angle=6.0)
    area_lamp((-1.7, -0.6, 1.3), (0, 0.5, 0.50), 82, (1.0, 0.83, 0.52), 1.1)
    area_lamp((2.0, 2.6, 1.1), (0, 0.6, 0.50), 54, (1.0, 0.66, 0.42), 1.6)

    # Same lens, same angle, same distance as panel 3, because the two panels are
    # supposed to rhyme; only the colour and the eyes change.
    # A smaller gape than panel 3: greed is a grin, not a scream, and this panel's
    # crop is tighter so the beak eats more of the frame. Same beak-up-to-meet-the-
    # lens rule as panel 3 though (head -3 plus bill -7 = ten degrees up), and for
    # the same reason — this camera is the same thirteen degrees above the beak, so
    # a level gape shows the lens nothing but the floor of the mouth.
    d = duck(loc=(0.0, 0.60, LAND_Z), yaw=-6, head=(-3, 6), bill=-7, gape=13,
             wings=(30, 26), wing_yaw=(-12, 10), cap=(-4, 0, 3), face="greed")
    sparkles("Glints", duck_local(d, (0.0, -0.14, 0.50)), n=7, R=0.24,
             size=0.011)
    tall_grass("Fore_Grass", 90, (0.0, 0.10), (1.2, 0.20), h=(0.22, 0.40), seed=17)

    camera((0.0, 0.50, 0.525), 0.68, (-0.30, -1.0, 0.24), lens=85,
           u=0.505, v=0.480, fstop=3.2, focus_at=(0.0, 0.48, 0.515))
    render(8)


def panel_09():
    """RESOLVE · JAW SET, WINGS CLENCHED — ends on Rect(0.16, 0.13, 0.70, 0.70).

    It barely moves, so the frame does the work: low, hero height, the duck
    against sky rather than against scenery, back-lit so the silhouette is the
    read. The cap sits straight for the first time in the sequence.
    """
    global SKY_MIX
    SKY_MIX = "#F2D9A8"
    begin("#3E7FBE", "#F6DFAE", 0.95, samples=72, vig=0.32, bloom=0.09)
    pond_set(grade=0.30)
    # The sun travels TOWARD the camera, so it rims the duck instead of lighting
    # it flat. Pointed the other way this panel was a well-lit duck standing in
    # grass, which is not what "jaw set" looks like.
    sun_lamp((0.30, -0.86, -0.26), 5.0, (1.0, 0.85, 0.58), angle=3.0)
    sun_disc("LowSun", (4.6, 34.0, 1.4), r=1.7, col="#FFE1AA", emit=6.0)
    area_lamp((-2.4, -2.0, 1.5), (0, 0.4, 0.42), 130, (0.98, 0.93, 0.86), 2.6)
    area_lamp((2.6, 0.2, 0.9), (0, 0.4, 0.42), 70, (0.80, 0.88, 1.0), 3.0)

    duck(loc=(0.0, 0.40, LAND_Z), yaw=24, head=(-5, -14),
         wings=(34, 30), wing_yaw=(-26, 22), tail=-8, face="resolve")
    tall_grass("Fore_Grass", 190, (0.0, -0.22), (1.5, 0.26), h=(0.28, 0.52),
               seed=23)
    tall_grass("Feet_Grass", 60, (0.0, 0.12), (0.80, 0.20), h=(0.22, 0.40),
               seed=29)

    # 0.95 m of frame, not 0.80: the goggles on top of the cap reached v=0.88 and
    # the move ends at y=0.13..0.83, so the panel's hero was being decapitated by
    # its own crop. Every tight panel is sized against its end rect, not the frame.
    camera((0.0, 0.32, 0.34), 0.95, (-0.44, -1.0, 0.075), lens=66,
           u=0.505, v=0.455, fstop=4.0, focus_at=(0.0, 0.30, 0.34))
    render(9)


def panel_10():
    """THE SHED · WALKING TO THE MOWER — the only panel that pulls OUT, from
    Rect(0.21, 0.17, 0.60, 0.60) on the mower to nearly the whole frame.

    So the mower is composed at the centre of that opening rect and the shed has
    to be worth arriving at: it hands the game its first object and its first
    room in one move. The duck is a silhouette in the doorway light with
    somewhere to go.
    """
    global SKY_MIX
    SKY_MIX = "#6E5F4A"
    begin("#3B4A58", "#6E6252", 0.30, samples=96, vig=0.42, bloom=0.14)
    shed()
    # the doorway is the panel's light: one hard shaft down onto the mower, a
    # blown-out rectangle of daylight behind it, and just enough bounce that the
    # far corners are dim rather than black
    lightbox("Daylight", "#FFF3D8", -SHED_W - 0.22, DOOR[0] - 0.1, DOOR[1] + 0.1,
             0.0, 2.32, emit=7.0)
    # and the window's own pane of sky, seen from inside
    wb = MB()
    quad(wb, "#E8F1F6", (WINDOW[0] - 0.05, SHED_D + 0.14, 1.30),
         (WINDOW[1] + 0.05, SHED_D + 0.14, 1.30),
         (WINDOW[1] + 0.05, SHED_D + 0.14, 2.24),
         (WINDOW[0] - 0.05, SHED_D + 0.14, 2.24))
    wb.finish("WindowSky", "M_PanelWin", seal=False)
    vc_mat("M_PanelWin", rough=0.5, emit="#E8F1F6", emit_str=3.4)
    area_lamp((-1.5, SHED_D - 0.2, 1.8), (-0.4, 0.4, 0.9), 60,
              (0.82, 0.90, 1.0), 1.0)
    sun_lamp((0.82, -0.46, -0.34), 5.4, (1.0, 0.88, 0.66), angle=2.0)
    area_lamp((-SHED_W - 0.6, 2.3, 1.15), (0.4, 0.6, 0.5), 1100,
              (1.0, 0.90, 0.70), 2.2)
    area_lamp((0.0, -2.6, 2.0), (0.2, 1.0, 0.5), 70, (0.66, 0.74, 1.0), 4.0)
    point_lamp((0.6, 2.2, 2.05), 45, (1.0, 0.80, 0.52), 0.12)
    mb = MB()
    cyl(mb, "#3A3A3A", (0.6, 2.2, 2.42), (0.6, 2.2, 2.16), 0.012, 0.012, n=6)
    sphere(mb, "#FFE7B0", (0.6, 2.2, 2.10), (0.055, 0.055, 0.070), seg=10, rings=5)
    mb.finish("Bulb", "M_PanelBulb")
    vc_mat("M_PanelBulb", rough=0.3, emit="#FFE7B0", emit_str=3.0)

    mower(loc=(0.55, 0.35, 0.0), yaw=-118)
    motes("Motes", (-0.9, 1.4, 1.35), n=70, span=(1.6, 1.5, 1.1))

    prop(PROPS.hay_bale, "Bale", (2.05, 2.35, 0.0), rotz=18)
    prop(PROPS.wheelbarrow, "Barrow", (-2.15, 2.75, 0.0), rotz=-118)
    prop(PROPS.thermos, "Thermos", (-0.30, 3.02, 1.45), rotz=20)
    prop(PROPS.sprinkler, "Sprinkler", (1.15, 3.05, 1.45), rotz=-30)
    prop(PROPS.gnome, "Gnome", (1.62, 3.02, 1.45), rotz=196, scale=0.8)
    mb = MB()      # a rake and a shovel: vertical lines to break up the wall
    tube(mb, WOOD, [Vector((2.60, -1.05, 0.0)), Vector((2.42, -1.12, 1.70))],
         [0.026, 0.022], n=6, smooth=False)
    rbox(mb, GREY, (2.40, -1.13, 1.80), (0.34, 0.05, 0.10), r=0.01, n=6, e=8.0, k=1)
    for k in range(7):
        x = 2.40 - 0.15 + 0.30 * k / 6.0
        cyl(mb, GREY, (x, -1.13, 1.78), (x, -1.13, 1.66), 0.010, 0.006, n=4)
    tube(mb, WOOD_D, [Vector((2.58, -1.55, 0.0)), Vector((2.34, -1.62, 1.55))],
         [0.024, 0.020], n=6, smooth=False)
    mb.finish("Tools", MAT, 30.0)

    # Deeper into the room than it looks like it should be. A 35 mm lens this
    # close throws anything a metre to the left clean off the frame: at x=-1.10
    # the duck was half a duck against the edge. At 4.6 m it is a small figure in
    # the doorway shaft with the mower ahead of it, which is the shot.
    duck(loc=(-0.40, 1.80, LAND_Z), yaw=52, head=(0, -18), wings=(30, 22),
         wing_yaw=(-8, 8), tail=-4)
    # A crate and a sack in front of it: the duck is a driving pose stood up, and
    # in a shed there is no grass to bury the legs in.
    mb = MB()
    f = rbox(mb, WOOD, (-0.52, 1.35, 0.17), (0.52, 0.38, 0.34), r=0.02, n=8,
             e=6.0, k=1)
    recess(mb, [g for g in f if g.is_valid and g.normal.y < -0.7],
           thickness=0.035, depth=-0.008, col=WOOD_D)
    rbox(mb, mix_hex(DIRT, WOOD_D, 0.35), (-1.15, 1.15, 0.13), (0.40, 0.34, 0.26),
         r=0.11, n=10, e=3.0, k=2)
    mb.finish("Crate", MAT, 30.0)

    camera((0.55, 0.35, 0.45), 1.95, (-0.14, -1.0, 0.22), lens=35,
           u=0.505, v=0.465, fstop=6.3, focus_at=(0.55, 0.35, 0.45))
    render(10)


PANELS = [panel_01, panel_02, panel_03, panel_04, panel_05,
          panel_06, panel_07, panel_08, panel_09, panel_10]

# The moves out of DuckCutsceneBuilder.Beats, kept here so --crops can show the
# frame the player is actually looking at when each beat lands.
MOVES = [
    ((0.02, 0.03, 0.96, 0.96), (0.13, 0.11, 0.76, 0.76)),
    ((0.14, 0.22, 0.72, 0.72), (0.04, 0.03, 0.92, 0.92)),
    ((0.12, 0.12, 0.80, 0.80), (0.22, 0.20, 0.60, 0.60)),
    ((0.16, 0.06, 0.78, 0.78), (0.05, 0.11, 0.78, 0.78)),
    ((0.02, 0.06, 0.92, 0.92), (0.30, 0.06, 0.56, 0.56)),
    ((0.02, 0.06, 0.92, 0.92), (0.30, 0.06, 0.56, 0.56)),
    ((0.06, 0.10, 0.84, 0.84), (0.17, 0.17, 0.66, 0.66)),
    ((0.16, 0.16, 0.64, 0.64), (0.25, 0.23, 0.50, 0.50)),
    ((0.10, 0.08, 0.78, 0.78), (0.16, 0.13, 0.70, 0.70)),
    ((0.21, 0.17, 0.60, 0.60), (0.02, 0.02, 0.94, 0.94)),
]


def crop_sheets(cell=(608, 342)):
    """Two sheets showing every panel as the player sees it at the START and the
    END of its move. A panel that reads beautifully full-frame and badly inside
    Rect(0.25, 0.23, 0.50, 0.50) is a panel that does not work.
    """
    import numpy as np
    for which, tag in ((1, "to"), (0, "from")):
        cells = []
        for i in range(10):
            p = os.path.join(OUT_DIR, "panel_%02d.png" % (i + 1))
            if not os.path.exists(p):
                continue
            im = bpy.data.images.load(p)
            w, h = im.size
            a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
            bpy.data.images.remove(im)
            r = MOVES[i][which]
            x0, y0 = int(r[0] * w), int(r[1] * h)
            cw, ch = int(r[2] * w), int(r[3] * h)
            sub = a[y0:y0 + ch, x0:x0 + cw, :]
            ys = (np.arange(cell[1]) * sub.shape[0] // cell[1])
            xs = (np.arange(cell[0]) * sub.shape[1] // cell[0])
            cells.append(sub[ys][:, xs])
        if not cells:
            return
        cols, rows = 2, (len(cells) + 1) // 2
        sheet = np.ones((rows * cell[1], cols * cell[0], 4), dtype=np.float32)
        for i, c in enumerate(cells):
            rr, cc = divmod(i, cols)
            rr = rows - 1 - rr
            sheet[rr * cell[1]:(rr + 1) * cell[1],
                  cc * cell[0]:(cc + 1) * cell[0], :] = c
        out = os.path.join(L.PREVIEWS_DIR, "cutscene_crops_%s.png" % tag)
        im = bpy.data.images.new("sheet", cols * cell[0], rows * cell[1], alpha=True)
        im.pixels = sheet.reshape(-1).tolist()
        im.filepath_raw = out
        im.file_format = 'PNG'
        im.save()
        print("SHEET", out)


def main():
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = set(int(t) for t in a.split("=", 1)[1].split(",") if t.strip())
    if "--crops" in sys.argv:
        crop_sheets()
        return
    for i, fn in enumerate(PANELS, start=1):
        if only and i not in only:
            continue
        print("\n================ PANEL %02d  %s"
              % (i, fn.__doc__.split("\n")[0].strip()))
        fn()
    print("DONE cutscene panels")


main()
