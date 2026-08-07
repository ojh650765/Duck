using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Tells the lawn where not to grow: the four defending strips and the four gardens.
    ///
    /// Answered from <see cref="RallyArena"/> rather than from a list of rectangles dropped in the
    /// inspector, so the bare ground and the ground it is bare FOR can never disagree. Move a garden
    /// in the layout and the grass moves with it; there is no second copy of the arena's geometry to
    /// keep in step.
    ///
    /// The margins are generous on purpose. A blade rooted at the exact edge of a dirt strip leans
    /// into it under wind and lays over it under the mower's layover, so a hairline boundary reads as
    /// grass creeping onto the earth — which is the thing this exists to stop.
    /// </summary>
    public class RallyBareGround : MonoBehaviour, IBareGround
    {
        [Tooltip("Metres of clearance around a defending strip. Wind and layover push a blade about " +
                 "a third of its height sideways, so the margin has to be bigger than that.")]
        public float bandMargin = 0.9f;
        [Tooltip("Metres of clearance around a garden's soil patch.")]
        public float gardenMargin = 1.6f;
        [Tooltip("Metres of clearance around the fence line, so pickets do not stand in a tuft.")]
        public float fenceMargin = 1.2f;

        public bool IsBare(Vector3 world)
        {
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var s = RallyArena.Get(i);

                if (RallyArena.InsideBand(s, world, bandMargin)) return true;

                // The soil patch is a wobbled ellipse a little larger than the bed grid — treated as
                // an ellipse here rather than the exact outline, because the grass only has to stop
                // before it, not trace it.
                Vector3 d = world - s.gardenCentre;
                float across = Vector3.Dot(d, s.right) / (RallyArena.GardenHalfWidth + gardenMargin);
                float along = Vector3.Dot(d, s.outward) / (RallyArena.GardenHalfDepth + gardenMargin);
                if (across * across + along * along <= 1f) return true;

                Vector3 f = world - s.fenceCentre;
                if (Mathf.Abs(Vector3.Dot(f, s.outward)) <= fenceMargin &&
                    Mathf.Abs(Vector3.Dot(f, s.right)) <= RallyArena.FenceHalfWidth + 0.6f)
                    return true;
            }
            return false;
        }
    }
}
