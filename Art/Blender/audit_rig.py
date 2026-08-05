# audit_rig.py — baseline geometry sweep of the mower + seated duck.
import bpy, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import duck_lib as L
import audit_lib as A
from duck_lib import fresh_scene


def load(path):
    src = open(path, encoding="utf-8").read().replace("\nmain()\n", "\n")
    g = {"__name__": "rigmod", "__file__": path}
    exec(compile(src, path, "exec"), g)
    return g


def main():
    fresh_scene()
    D = load(os.path.join(HERE, "build_duck.py"))
    M = load(os.path.join(HERE, "build_mower.py"))

    mow = M["build_all"]()
    A.audit(mow, "MOWER", ignore=M["SOCKETS"])
    A.audit_self(mow, "MOWER")

    duck = [D["build_body"](), D["build_head"](), D["build_bill"](), D["build_cap"](),
            D["build_wing"](1, "Duck_Wing_L"), D["build_wing"](-1, "Duck_Wing_R"),
            D["build_tail"](), D["build_foot"](1, "Duck_Foot_L"),
            D["build_foot"](-1, "Duck_Foot_R")]
    A.audit(duck, "DUCK", ignore=D["DUCK_SOCKETS"])

    seat = Vector((0.0, 0.10, 0.42))
    for o in duck:
        o.location = seat
    # rider sockets: rump in the seat dish, soles on the pedals, fists on the rim
    RIDE = tuple(D["DUCK_SOCKETS"]) + tuple(M["SOCKETS"]) + (
        ("Duck_Body", "Mower_Seat"), ("Duck_Foot_L", "Mower_Body"),
        ("Duck_Foot_R", "Mower_Body"),
        ("Duck_Wing_L", "Mower_Steering"), ("Duck_Wing_R", "Mower_Steering"))
    A.audit(mow + duck, "RIG (duck seated)", ignore=RIDE)


main()
