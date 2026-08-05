# preview_judge_rig.py — render the exported judges in real poses from their own
# clips, so the skinning can be LOOKED AT rather than assumed.
#
# Exporting cleanly proves the file is well formed. It says nothing about whether
# the weights deform sensibly — a shoulder that collapses or a neck that pinches
# exports exactly as happily as one that works. This is the check that catches it.
#
# Run: blender --background --python C:\Duck\Art\Blender\preview_judge_rig.py
import bpy, os, sys, math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L

OUT = os.path.join(HERE, "previews", "rig")
JUDGES = ["Mildred", "Boris", "Priscilla"]


def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def main():
    for judge in JUDGES:
        clean()
        fbx = os.path.join(L.MODELS_DIR, "Judge_%s.fbx" % judge)
        bpy.ops.import_scene.fbx(filepath=fbx)

        rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
        meshes = [o for o in bpy.data.objects if o.type == "MESH"]

        # Every clip, sampled where it is most extreme — the frame most likely to
        # show a weighting fault.
        acts = sorted(bpy.data.actions, key=lambda a: a.name)
        print("JUDGE", judge, "clips", [a.name for a in acts])

        for a in acts:
            rig.animation_data.action = a
            lo, hi = a.frame_range
            frame = int(lo + (hi - lo) * 0.45)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            short = a.name.split("|")[-1]
            print("  POSE", short, "frame", frame)
            L.render_previews(meshes, "rig_%s" % short, res=520,
                              extra_views=[("face", (-0.25, -1.0, 0.18), 0.55)])

    print("DONE rig previews")


main()
