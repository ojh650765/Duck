using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The duck reacting to being hit.
    ///
    /// The machine rocks, the camera kicks, the goose deforms — and the animal actually driving sat
    /// perfectly still through all of it. "Duck and NPC reaction animation" was on the goal's list with
    /// nothing behind it, and it is the one reaction the player is guaranteed to be looking at, because
    /// the duck is the thing the camera is framed on.
    ///
    /// Driven off <see cref="MowerController.OnImpact"/> rather than off the defence phase, so the duck
    /// reacts to a gnome in the middle of a round exactly as it reacts to a goose. A reaction that only
    /// existed during the raid would make the rest of the game look like the duck had stopped caring.
    ///
    /// Local pose only: this leans and squashes a child transform and never touches physics. The chassis
    /// recoil is already real torque on the rigidbody (see Bonk) and a passenger fighting that with forces
    /// of its own would produce a wobble nobody asked for.
    /// </summary>
    public class DuckRider : MonoBehaviour
    {
        [Tooltip("The duck. Found by name under this object if left empty.")]
        public Transform duck;

        [Tooltip("How far the duck is thrown, in degrees, by a full-strength hit.")]
        [Range(0f, 60f)] public float lean = 26f;

        [Tooltip("How quickly it recovers. Fast: control must read as recovered before the player " +
                 "believes they have lost it.")]
        [Range(1f, 12f)] public float recover = 4.6f;

        MowerController _mower;
        Vector3 _restPos;
        Quaternion _restRot;
        Vector3 _restScale;

        // Signed, so a blow from the left throws the duck right.
        float _kick, _side, _shock;

        void Awake()
        {
            _mower = GetComponentInParent<MowerController>();
            if (duck == null) duck = FindDuck(transform);
            if (duck == null) return;

            _restPos = duck.localPosition;
            _restRot = duck.localRotation;
            _restScale = duck.localScale;
        }

        static Transform FindDuck(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Duck") return t;
            return null;
        }

        void OnEnable() { if (_mower != null) _mower.OnImpact += OnImpact; }
        void OnDisable() { if (_mower != null) _mower.OnImpact -= OnImpact; }

        void OnImpact(float strength, Vector3 point)
        {
            if (duck == null) return;

            // Which way the blow came from, in the machine's own frame, so the duck is thrown AWAY from
            // it rather than in a fixed direction.
            Vector3 from = point - duck.position;
            from.y = 0f;
            float side = from.sqrMagnitude > 1e-4f
                ? Vector3.Dot(from.normalized, transform.right) : 0f;

            _kick = Mathf.Max(_kick, Mathf.Clamp01(strength));
            _side = side;
            _shock = Mathf.Max(_shock, Mathf.Clamp01(strength));
        }

        void Update()
        {
            if (duck == null) return;
            if (_kick <= 0f && _shock <= 0f) return;

            // UNSCALED, so the duck keeps flinching through a hit stop instead of freezing mid-recoil and
            // then snapping back when the world restarts. The freeze is meant to hold the pose, and a pose
            // that is still arriving has not been struck yet.
            float dt = Time.unscaledDeltaTime;
            _kick = Mathf.Max(0f, _kick - dt * recover);
            _shock = Mathf.Max(0f, _shock - dt * recover * 1.4f);

            // Thrown back and sideways, with a wobble on the way out so the recovery is not a slide.
            float wobble = Mathf.Sin(_kick * 26f) * _kick;
            duck.localRotation = _restRot
                               * Quaternion.Euler(-_kick * lean, wobble * 9f, -_side * _kick * lean * 0.8f);

            // Hunched: shorter and wider for a moment. The same read as the goose's squash, so an impact
            // looks like one event happening to two animals.
            duck.localScale = new Vector3(_restScale.x * (1f + _shock * 0.10f),
                                          _restScale.y * (1f - _shock * 0.14f),
                                          _restScale.z * (1f + _shock * 0.08f));

            duck.localPosition = _restPos + new Vector3(-_side * _kick * 0.05f, -_shock * 0.035f,
                                                        -_kick * 0.06f);

            if (_kick <= 0f && _shock <= 0f)
            {
                duck.localRotation = _restRot;
                duck.localScale = _restScale;
                duck.localPosition = _restPos;
            }
        }
    }
}
