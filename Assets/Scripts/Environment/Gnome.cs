using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// A garden gnome. Stands its ground until a mower hits it, at which point it should leave
    /// the scene with as much dignity as physics allows, and slowly right itself afterwards so
    /// the lawn does not fill up with fallen gnomes over a long session.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Gnome : MonoBehaviour
    {
        // Retuned when the gnome went from 0.6 m to 1.2 m tall — roughly 2.5x linear and 15x volume.
        //
        // The size change was a gameplay fix, not dressing: the mower's chassis box only occupies
        // y 0.24 to 0.76, so a 0.6 m ornament presented just 0.36 m of itself to be hit and glancing
        // contacts slipped past it entirely. It was also shorter than the ~0.5 m grass it stood in,
        // so the player could not see it coming. At 1.2 m the mower's whole collider height is
        // inside the capsule and any contact registers.
        //
        // These numbers had to come down as a result, because this class sets linearVelocity and
        // angularVelocity DIRECTLY — mass never enters the launch. So a bigger object thrown at the
        // same speed reads as LIGHTER, which is backwards for something that just got heavier. The
        // spin is the worst of it: 9 rad/s is 1.4 revolutions a second, which on a 1.2 m figure
        // looks frantic rather than struck.
        public float launchBoost = 2.8f;
        public float spinBoost = 5.5f;

        public static event System.Action<Vector3, float> OnKnocked;

        /// <summary>
        /// Every gnome currently in the scene, so a round can stand them all up without a search.
        /// </summary>
        static readonly System.Collections.Generic.List<Gnome> All = new System.Collections.Generic.List<Gnome>(8);

        /// <summary>
        /// Put every gnome back on its plinth. Called once when a round begins.
        ///
        /// They used to right themselves on a six-second timer, creeping home on their own. That
        /// was the only thing restoring them — nothing else ever called ResetGnome — and it undid
        /// the best joke on the lawn: you flatten an ornament, and by the time you come back round
        /// it has tidied itself up as though you had imagined it. A knocked gnome now stays exactly
        /// where it landed for the rest of the round, so the overhead reveal shows the wreckage
        /// along with the artwork, and the plot is only tidied when a new round starts.
        /// </summary>
        public static void ResetAll()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null) All[i].ResetGnome();
        }

        Rigidbody _rb;
        Vector3 _homePosition;
        Quaternion _homeRotation;
        float _knockedAt = -999f;
        bool _knocked;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.maxAngularVelocity = 10f;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            Sleep();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Sleep()
        {
            _rb.isKinematic = true;
            _knocked = false;
        }

        public void ResetGnome()
        {
            _rb.isKinematic = true;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _knocked = false;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_knocked) return;
            var mower = c.collider.GetComponentInParent<MowerController>();
            if (mower == null) return;

            float speed = Mathf.Abs(mower.ForwardSpeed);

            // The gate is a crawl, not a jog.
            //
            // It used to be 1.5 m/s, which is a fifth of top speed and well inside the range a
            // player creeps at while placing a careful edge — nudge a gnome at 1.4 and it simply
            // was not there, so the same obstacle blocked you or did not depending on a threshold
            // nothing on screen expressed. Anything above a genuine idle now counts.
            if (speed < 0.45f) return;

            _knocked = true;
            _knockedAt = Time.time;

            Vector3 contact = c.contactCount > 0 ? c.GetContact(0).point : transform.position;

            Vector3 away = transform.position - c.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = mower.transform.forward;
            away.Normalize();

            // Hit the mower FIRST, while the gnome is still kinematic.
            //
            // Waking the body before staging the reaction is what made this obstacle unreliable:
            // a rigidbody that turns dynamic inside a collision callback swallows the contact
            // instead of resisting it, so roughly half the time the mower drove through an
            // ornament that visibly went flying. Ordering it this way means the mower's reaction
            // never depends on how the solver happened to resolve the frame.
            mower.Bonk(contact, Mathf.Clamp01(speed / 8f));

            _rb.isKinematic = false;
            _rb.linearVelocity = away * (speed * 0.65f + launchBoost) + Vector3.up * (2.6f + speed * 0.25f);
            _rb.angularVelocity = Random.onUnitSphere * spinBoost;

            OnKnocked?.Invoke(transform.position, Mathf.Clamp01(speed / 10f));
        }

        // No recovery loop. A knocked gnome lies where it fell until the next round begins, which
        // is what ResetAll is for. See the comment on it.
    }
}
