# verify_goose.py — read Goose.fbx back and prove what is actually in the file.
#
# Run: blender --background --python C:\Duck\Art\Blender\verify_goose.py
#
# The build script running to completion proves nothing about the export: an FBX with the
# armature dropped, the skin unbound, or every animation take silently omitted because a
# strip was muted looks exactly as healthy from the build log. This opens the shipped file
# in a clean Blender and reports the objects, the bone hierarchy, the vertex and triangle
# counts, the material slots, the vertex-colour attribute, the actions and their lengths,
# and the vertex groups with their weight ranges.

import bpy, os, sys, math
from mathutils import Vector

PATH = r"C:\Duck\Assets\Art\Models\Goose.fbx"


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if not os.path.exists(PATH):
        print("MISSING", PATH)
        return
    print("FILE %s  %.1f KB" % (PATH, os.path.getsize(PATH) / 1024.0))
    bpy.ops.import_scene.fbx(filepath=PATH)

    print("\nOBJECTS")
    for o in bpy.data.objects:
        par = o.parent.name if o.parent else "-"
        print("  %-16s type=%-8s parent=%-16s loc=(%.3f %.3f %.3f) rot=(%.1f %.1f %.1f)"
              % (o.name, o.type, par, o.location.x, o.location.y, o.location.z,
                 math.degrees(o.rotation_euler.x), math.degrees(o.rotation_euler.y),
                 math.degrees(o.rotation_euler.z)))

    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        me = o.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        print("\nMESH %s" % o.name)
        print("  verts=%d  polys=%d  tris=%d" % (len(me.vertices), len(me.polygons), tris))
        print("  material slots: %s" % [m.name if m else None for m in me.materials])
        cnt = {}
        for p in me.polygons:
            cnt[p.material_index] = cnt.get(p.material_index, 0) + 1
        print("  polys per slot: %s" % cnt)
        print("  colour attributes: %s"
              % [(c.name, c.data_type, c.domain) for c in me.color_attributes])
        if me.color_attributes:
            ca = me.color_attributes[0]
            vals = set()
            for d in ca.data:
                vals.add(tuple(round(x, 3) for x in d.color[:3]))
            print("  distinct vertex colours: %d  e.g. %s"
                  % (len(vals), sorted(vals)[:4]))
        lo = Vector((min(v.co.x for v in me.vertices), min(v.co.y for v in me.vertices),
                     min(v.co.z for v in me.vertices)))
        hi = Vector((max(v.co.x for v in me.vertices), max(v.co.y for v in me.vertices),
                     max(v.co.z for v in me.vertices)))
        print("  bounds (Blender import axes) lo=(%.3f %.3f %.3f) hi=(%.3f %.3f %.3f)"
              % (lo.x, lo.y, lo.z, hi.x, hi.y, hi.z))
        print("  size = %.3f x %.3f x %.3f" % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
        print("  modifiers: %s" % [(m.type, getattr(m, "object", None) and m.object.name)
                                   for m in o.modifiers])
        # a closed manifold has no boundary edge; count them the cheap way
        edge_faces = {}
        for p in me.polygons:
            for e in p.edge_keys:
                edge_faces[e] = edge_faces.get(e, 0) + 1
        bnd = sum(1 for v in edge_faces.values() if v == 1)
        nmf = sum(1 for v in edge_faces.values() if v > 2)
        print("  boundary edges=%d  non-manifold edges=%d" % (bnd, nmf))
        print("  vertex groups (%d):" % len(o.vertex_groups))
        wmin, wmax, wcount = {}, {}, {}
        for v in me.vertices:
            for g in v.groups:
                n = o.vertex_groups[g.group].name
                wmin[n] = min(wmin.get(n, 9.9), g.weight)
                wmax[n] = max(wmax.get(n, 0.0), g.weight)
                wcount[n] = wcount.get(n, 0) + 1
        for n in sorted(wcount):
            print("    %-12s verts=%-4d weight %.3f .. %.3f"
                  % (n, wcount[n], wmin[n], wmax[n]))
        unweighted = [v.index for v in me.vertices if not v.groups]
        print("  vertices with no weight: %d" % len(unweighted))
        sums = [sum(g.weight for g in v.groups) for v in me.vertices if v.groups]
        if sums:
            print("  weight sums: min=%.4f max=%.4f" % (min(sums), max(sums)))

    for o in bpy.data.objects:
        if o.type != 'ARMATURE':
            continue
        print("\nARMATURE %s  (%d bones)" % (o.name, len(o.data.bones)))
        roots = [b for b in o.data.bones if b.parent is None]

        def walk(b, d=0):
            print("  %s%-12s len=%.3f head=(%.3f %.3f %.3f) tail=(%.3f %.3f %.3f)"
                  % ("    " * d, b.name, b.length,
                     b.head_local.x, b.head_local.y, b.head_local.z,
                     b.tail_local.x, b.tail_local.y, b.tail_local.z))
            for c in b.children:
                walk(c, d + 1)
        for r in roots:
            walk(r)

    print("\nACTIONS (%d)" % len(bpy.data.actions))
    for a in sorted(bpy.data.actions, key=lambda x: x.name):
        lo, hi = a.frame_range
        bones = sorted({fc.data_path.split('"')[1]
                        for fc in a.fcurves if '"' in fc.data_path})
        print("  %-24s frames %6.1f .. %6.1f  (%.3f s at 24 fps)  curves=%d  bones=%d"
              % (a.name, lo, hi, (hi - lo) / 24.0, len(a.fcurves), len(bones)))
        print("      %s" % ", ".join(bones))

    # DOES THE ANIMATION ACTUALLY DO ANYTHING IN THE FILE.
    #
    # Curve counts prove only that channels exist. Drive each imported action and measure
    # where the wingtip, bill tip and foot end up, in the units Unity will see. A clip that
    # exported as 159 flat curves would pass every check above and ship a bird that holds
    # one pose.
    rig = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if rig:
        print("\nPOSE SAMPLES (world position of each bone tip, metres)")
        if rig.animation_data is None:
            rig.animation_data_create()
        for a in sorted(bpy.data.actions, key=lambda x: x.name):
            rig.animation_data.action = a
            lo, hi = a.frame_range
            print("  %s" % a.name)
            for bone in ("Wing_L_2", "Head", "Leg_L_2", "Tail"):
                if bone not in rig.pose.bones:
                    continue
                rows, ext = [], []
                for f in range(int(lo), int(hi) + 1, max(1, int((hi - lo) / 5))):
                    bpy.context.scene.frame_set(f)
                    bpy.context.view_layer.update()
                    p = rig.matrix_world @ rig.pose.bones[bone].tail
                    ext.append(p)
                    rows.append("f%02d(%+.3f %+.3f %+.3f)" % (f, p.x, p.y, p.z))
                travel = max((a - b).length for a in ext for b in ext)
                print("    %-9s travel=%.3f m  %s" % (bone, travel, " ".join(rows)))
        rig.animation_data.action = None

    print("\nDONE verify")


main()
