# render_title.py — the game's masthead, as real signwriting.
#
# The project's only font is Liberation Sans, and the art bible lists default
# sans UI as an automatic rejection. Every attempt to make a title out of it —
# weight, tracking, a hard offset drop, a rosette pinned to the corner — was
# still a heading in a UI font with decoration around it.
#
# The cutscene flyer already solved this: its masthead is Rockwell Bold set as
# real 3D geometry and lit, and it reads as a printed show bill. This script does
# the same thing for the title block on the menu, so the two agree — the player
# sees the same lettering on the front page that they see on the flyer that
# starts the story.
#
# The lettering is arched, because arched is what a fairground sign does and a
# straight baseline is what a heading does. Each glyph is placed and rolled
# individually on that arch rather than bent through a curve modifier, so the
# spacing is measured kerning and the roll is a number in this file rather than
# whatever a deform happens to produce.
#
# Three passes, back to front, which is how a signwriter actually lays this up:
#   drop      a dark offset shadow, hard-edged, down and to the right
#   keyline   a fattened cream copy, which is the band that separates the
#             lettering from whatever it is standing on
#   face      the brick red letters themselves
# Every pass is extruded and bevelled, so the top edge catches the key light and
# the whole thing sits on the board instead of being printed on it.
#
# Run:  blender --background --python C:\Duck\Art\Blender\render_title.py
# Out:  Assets/Art/Textures/Title/title_masthead.png   (RGBA, premultiplied off)
#
# DuckMenuBuilder.cs imports that PNG and lays it on the title card. If it is
# missing the menu falls back to TMP lettering, so a machine without Rockwell
# still builds a menu — it just builds the old, weaker one.

import bpy
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L                                   # noqa: E402
from duck_lib import fresh_scene, hexcol               # noqa: E402

OUT = r"C:\Duck\Assets\Art\Textures\Title\title_masthead.png"

# Same candidate chain as the cutscene panels, and for the same reason: the look
# is authored for Rockwell Bold, and a machine without it must still produce
# something rather than fail the build.
FONT_FILES = [r"C:\Windows\Fonts\ROCKB.TTF",           # Rockwell Bold: signwriter slab
              r"C:\Windows\Fonts\BOOKOSB.TTF",
              r"C:\Windows\Fonts\arialbd.ttf",
              os.path.join(HERE, "..", "..", "Assets", "TextMesh Pro", "Fonts",
                           "LiberationSans.ttf")]

NAME = "DUCK MOW"
SHOW = "COUNTY GARDENER OF THE YEAR"

FACE = "#B8332B"        # UI brick, the colour the menu's plates and ribbon use
KEYLINE = "#F7EEDC"     # UI cream
DROP = "#4E1A16"        # deep brick, not black: the drop is paint, not a hole

ARC_RISE = 0.15         # units the middle glyph rides above the ends, at size 1
GLYPH_ROLL = 5.2        # degrees the outermost glyph fans off vertical
TRACK = 0.062           # extra units between glyphs, on top of measured kerning.
                        # Wide enough that the fattened keylines of adjacent glyphs do
                        # not touch: overlapping keylines are the one thing in this
                        # treatment that reads as a mistake rather than as hand-painted.

# The show line sits under the name, straight, small, wide-tracked. Two tiers is
# how a horticultural show bill reads: the show on the masthead, the class in
# small type underneath. The class line itself lives on the menu's ribbon.
SHOW_SIZE = 0.155
SHOW_TRACK = 0.095
# Below the name's baseline. The arch drops the outer glyphs to z = 0, so this is
# measured from those rather than from the middle of the run.
SHOW_DROP = 0.40

WIDTH_PX = 1536         # downsampled on screen at every sane window size


# ------------------------------------------------------------------ materials
def flat(col, rough=0.62):
    key = "TM_" + col.lstrip("#")
    m = bpy.data.materials.get(key)
    if m:
        return m
    m = bpy.data.materials.new(key)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = hexcol(col)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = 0.0
    return m


_font = None


def display_font():
    global _font
    if _font is not None:
        return _font
    for p in FONT_FILES:
        if os.path.exists(p):
            try:
                _font = bpy.data.fonts.load(p)
                print("  font: %s" % p)
                return _font
            except Exception as e:
                print("  font %s: %s" % (p, e))
    _font = False
    return _font


def letter(body, size, col, pos, roll, weight, extrude, bevel, name, spacing=1.0):
    """One piece of lettering standing in the XZ plane, facing -Y.

    `weight` is Blender's curve offset, which grows the outline outward in real
    geometry. That is what makes the keyline pass a keyline rather than a second
    copy of the same letters hidden behind the first.
    """
    cu = bpy.data.curves.new(name, type='FONT')
    f = display_font()
    if f:
        cu.font = f
    cu.body = body
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'BOTTOM_BASELINE'
    cu.space_character = spacing
    cu.extrude = extrude
    cu.offset = weight
    cu.bevel_depth = bevel
    cu.bevel_resolution = 2
    ob = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(ob)
    ob.location = (pos[0], pos[1], pos[2])
    # X first puts the text upright in the XZ plane; Y after it is the in-plane
    # roll, because Blender composes euler XYZ as Rz*Ry*Rx.
    ob.rotation_euler = (math.radians(90.0), math.radians(roll), 0.0)
    ob.data.materials.append(flat(col))
    return ob


def measure(body, size, track):
    """Ink width of a string, including the tracking this script will add.

    Measured by building the real curve and reading its evaluated dimensions,
    because the only reliable source of a font's kerning is the font.
    """
    if not body:
        return 0.0
    cu = bpy.data.curves.new("M", type='FONT')
    f = display_font()
    if f:
        cu.font = f
    cu.body = body
    cu.size = size
    cu.space_character = 1.0
    ob = bpy.data.objects.new("M", cu)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    w = ob.evaluated_get(dg).dimensions.x
    bpy.data.objects.remove(ob, do_unlink=True)
    return w + track * max(len(body) - 1, 0)


# ------------------------------------------------------------------ the block
def build():
    fresh_scene()

    # Cumulative advance to each glyph, from prefix widths. A space has no ink,
    # so its width has to be asked for separately and added by hand — measuring
    # "DUCK " and "DUCK" gives the same number.
    space = measure("n", 1.0, 0.0) * 0.62
    advance = [0.0]
    for i, ch in enumerate(NAME):
        step = space if ch == " " else measure(ch, 1.0, 0.0) + TRACK
        advance.append(advance[-1] + step)
    total = advance[-1] - TRACK

    # The drop carries the SAME weight as the keyline, not more. Fattened past it
    # the drop's outline pokes out on the up-left side of every glyph, where a
    # shadow cannot be, and the whole masthead reads as a printing misregistration.
    # The counters are deliberately left open in both: a drop showing through the
    # inside of a D is what signwriting actually does.
    passes = [
        # colour,   weight, extrude, bevel,  x,      z,      depth
        (DROP,      0.036,  0.008,   0.000,  0.062, -0.064,  0.10),
        (KEYLINE,   0.036,  0.014,   0.002,  0.000,  0.000,  0.05),
        (FACE,      0.000,  0.020,   0.003,  0.000,  0.000,  0.00),
    ]

    for ci, ch in enumerate(NAME):
        if ch == " ":
            continue
        # Centre of this glyph's advance slot, as a -1..1 position across the run.
        mid = (advance[ci] + advance[ci + 1] - TRACK) * 0.5 - total * 0.5
        u = mid / (total * 0.5) if total > 1e-5 else 0.0
        rise = ARC_RISE * (1.0 - u * u)
        roll = -u * GLYPH_ROLL

        for col, weight, extrude, bevel, dx, dz, depth in passes:
            letter(ch, 1.0, col, (mid + dx, depth, rise + dz), roll, weight,
                   extrude, bevel, "L%02d_%s" % (ci, col.lstrip("#")))

    # ---- the show line ----
    show_spacing = 1.0 + SHOW_TRACK / SHOW_SIZE
    show_w = measure(SHOW, SHOW_SIZE, SHOW_TRACK)
    for col, weight, extrude, bevel, dx, dz, depth in passes[1:]:
        letter(SHOW, SHOW_SIZE, col, (dx, depth, -SHOW_DROP + dz), 0.0,
               weight * 0.42, extrude * 0.5, bevel * 0.5, "SHOW_" + col.lstrip("#"),
               show_spacing)

    return total, show_w


# ------------------------------------------------------------------ shot
def shoot(total, show_w):
    sc = bpy.context.scene
    bpy.context.view_layer.update()

    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    lo = [1e9, 1e9]
    hi = [-1e9, -1e9]
    for ob in sc.objects:
        if ob.type != 'FONT':
            continue
        ev = ob.evaluated_get(dg)
        for corner in ev.bound_box:
            w = ev.matrix_world @ __import__("mathutils").Vector(corner)
            lo[0] = min(lo[0], w.x); hi[0] = max(hi[0], w.x)
            lo[1] = min(lo[1], w.z); hi[1] = max(hi[1], w.z)

    pad = 0.10
    cx = (lo[0] + hi[0]) * 0.5
    cz = (lo[1] + hi[1]) * 0.5
    w = (hi[0] - lo[0]) + pad * 2.0
    h = (hi[1] - lo[1]) + pad * 2.0
    print("  ink %.3f x %.3f  ->  frame %.3f x %.3f" % (hi[0] - lo[0], hi[1] - lo[1], w, h))

    # Height rounded to a multiple of four: DXT compresses in 4x4 blocks, and a
    # sprite whose dimensions are not multiples of four falls back to uncompressed
    # RGBA — four times the download for the same picture.
    res_y = max(4, int(round(WIDTH_PX * h / w / 4.0)) * 4)
    aspect = WIDTH_PX / float(res_y)

    cam_d = bpy.data.cameras.new("TitleCam")
    cam_d.type = 'ORTHO'
    # Ortho scale applies to the longer axis, which is x here.
    cam_d.ortho_scale = max(w, h * aspect)
    cam = bpy.data.objects.new("TitleCam", cam_d)
    bpy.context.collection.objects.link(cam)
    cam.location = (cx, -6.0, cz)
    cam.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    sc.camera = cam

    def sun(direction, energy, col=(1.0, 0.96, 0.88)):
        d = bpy.data.lights.new("S", 'SUN')
        d.energy = energy
        d.color = col
        d.angle = math.radians(12.0)
        o = bpy.data.objects.new("S", d)
        bpy.context.collection.objects.link(o)
        o.rotation_euler = __import__("mathutils").Vector(direction).to_track_quat('-Z', 'Y').to_euler()
        return o

    # Key from the upper left and in front, which is where the venue's own sun is
    # relative to the menu camera, so the title is lit the same way as the set
    # behind it. Fill from below-right keeps the extrusion's underside off black.
    sun((0.42, 0.78, -0.46), 3.1)
    sun((-0.30, 0.62, 0.72), 1.15, (0.86, 0.90, 1.0))

    world = bpy.data.worlds.new("TitleWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.86, 0.88, 0.92, 1.0)
    bg.inputs[1].default_value = 0.55
    sc.world = world

    sc.render.engine = 'BLENDER_EEVEE'
    sc.eevee.taa_render_samples = 96
    sc.eevee.use_gtao = True
    sc.eevee.gtao_distance = 0.12
    sc.eevee.use_soft_shadows = True
    # Transparent film, so the masthead can sit on the painted card the menu
    # already has rather than carrying its own background and fighting it.
    sc.render.film_transparent = True
    sc.render.resolution_x = WIDTH_PX
    sc.render.resolution_y = res_y
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.compression = 90
    sc.view_settings.view_transform = 'Standard'

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sc.render.filepath = OUT
    bpy.ops.render.render(write_still=True)
    print("TITLE %dx%d -> %s" % (WIDTH_PX, res_y, OUT))


if __name__ == "__main__":
    total, show_w = build()
    shoot(total, show_w)
