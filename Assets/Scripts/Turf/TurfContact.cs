using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Reports one mower bumping another.
    ///
    /// It exists because of where Unity delivers collisions. <see cref="TurfCompetitor"/> lives on
    /// its own object beside the machine — that is the project's pattern, and it is the right one,
    /// because it keeps everything about one gardener selectable in a single click and keeps the
    /// shared Mower prefab free of mode-specific components. But OnCollisionEnter is delivered to
    /// the object carrying the Rigidbody, and nowhere else. A collision handler written on the
    /// competitor compiles, reads correctly, and is never called once.
    ///
    /// So the handler lives here, on the machine, and hands the event to the gardener driving it.
    /// This is also what lets one mower identify another: the collider it hit belongs to a machine,
    /// and the machine carries the only pointer back to whoever is sitting on it.
    ///
    /// Both parties are told independently, by their own contact. Nothing here reaches across to
    /// shove the other machine — <see cref="MowerController.Bonk"/> is applied by each competitor to
    /// its own chassis, so a shunt is two machines reacting to the same event rather than one
    /// machine applying an outcome to another.
    /// </summary>
    [DefaultExecutionOrder(-4)]
    public class TurfContact : MonoBehaviour
    {
        [Tooltip("Whose machine this is. Wired by the scene builder.")]
        public TurfCompetitor competitor;

        void Awake()
        {
            if (competitor == null)
                competitor = GetComponentInParent<TurfCompetitor>();
        }

        void OnCollisionEnter(Collision c) => Report(c);
        void OnCollisionStay(Collision c) => Report(c);

        void Report(Collision c)
        {
            if (competitor == null) return;
            var other = c.collider.GetComponentInParent<TurfContact>();
            if (other == null || other == this || other.competitor == null) return;
            competitor.Shunted(other.competitor, c);
        }
    }
}
