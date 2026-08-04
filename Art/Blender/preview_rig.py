# preview_rig.py — integration check: build the duck AND the mower in one scene,
# seat the duck at the mower's seat contact, and render.  This is what verifies
# the seat height, the footwell floor and the steering-wheel grip actually line up.
# Run: blender --background --python C:\Duck\Art\Blender\preview_rig.py
import bpy, math, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L
from duck_lib import make_empty, attach, render_previews, fresh_scene


def load(path):
    src = open(path, encoding="utf-8").read().replace("\nmain()\n", "\n")
    g = {"__name__": "rigmod", "__file__": path}
    exec(compile(src, path, "exec"), g)
    return g


def main():
    fresh_scene()
    D = load(os.path.join(HERE, "build_duck.py"))
    M = load(os.path.join(HERE, "build_mower.py"))

    mow = [M["build_body"](), M["build_deck"](), M["build_blade"](),
           M["build_wheel"]("W_FL", 0.300, M["FRONT_AXLE_Y"], M["FR"], 0.092, 0.012),
           M["build_wheel"]("W_FR", -0.300, M["FRONT_AXLE_Y"], M["FR"], 0.092, 0.012),
           M["build_wheel"]("W_RL", 0.372, M["REAR_AXLE_Y"], M["RR"], 0.134, 0.019),
           M["build_wheel"]("W_RR", -0.372, M["REAR_AXLE_Y"], M["RR"], 0.134, 0.019),
           M["build_steering"](), M["build_exhaust"](), M["build_bag"](),
           M["build_seat"]()]

    duck = [D["build_body"](), D["build_head"](), D["build_bill"](), D["build_cap"](),
            D["build_wing"](1, "D_WL"), D["build_wing"](-1, "D_WR"),
            D["build_tail"](), D["build_foot"](1, "D_FL"), D["build_foot"](-1, "D_FR")]

    seat = Vector((0.0, 0.10, 0.42))
    for o in duck:
        o.location = seat

    objs = mow + duck
    print("RIG duck top of cap  z =", round(seat.z + 0.484, 3))
    print("RIG duck foot bottom z =", round(seat.z - 0.138, 3), " (footwell floor 0.2825)")
    print("RIG wing grip centre  =", (0.0, round(seat.y - 0.264, 3), round(seat.z + 0.214, 3)),
          " (steering hub 0, -0.164, 0.636)")
    render_previews(objs, "rig", res=640,
                    extra_views=[("driver", (-0.85, -0.75, 0.30), 0.86),
                                 ("chase", (0.45, 1.0, 0.30), 0.95)])
    print("DONE rig")


main()
