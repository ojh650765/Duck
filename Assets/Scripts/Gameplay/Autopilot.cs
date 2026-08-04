using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Drives the mower along a boustrophedon fill of the target picture — the same back-and-forth
    /// pattern a person mowing a lawn would use.
    ///
    /// This exists for two reasons. It produces deterministic, repeatable gameplay to capture and
    /// review (you cannot art-direct cut grass you have to hand-drive every time), and it is an
    /// honest reference for what a good run looks like, which is what the judges' scoring curve
    /// gets calibrated against.
    /// </summary>
    public class Autopilot : MonoBehaviour
    {
        public MowerController mower;
        public RoundTarget target;

        [Header("Path")]
        [Tooltip("Spacing between passes. Slightly under the deck width so passes overlap.")]
        public float passSpacing = 1.12f;
        [Tooltip("How far ahead the mower aims. Larger is smoother but cuts corners.")]
        public float lookAheadDistance = 3.2f;
        public float waypointRadius = 2.0f;
        [Tooltip("Take every Nth strip, then come back for the ones in between. 1 is undrivable.")]
        public int lineSkip = 2;

        [Header("Driving")]
        public float steerGain = 0.055f;
        public float cornerSlowdown = 0.62f;
        [Tooltip("Throttle used on a straight. Full throttle cannot hold a line at this field scale.")]
        [Range(0.2f, 1f)] public float cruiseThrottle = 0.78f;
        [Tooltip("Distance over which to bleed off speed before a turn, in metres.")]
        public float slowdownDistance = 11f;
        [Tooltip("Use boost on long straights.")]
        public bool useBoost = false;
        public float boostStraightLength = 14f;

        public bool Active { get; private set; }
        public int WaypointCount => _path.Count;
        public int WaypointIndex => _index;

        readonly List<Vector3> _path = new List<Vector3>();
        int _index;
        float _stuckTimer;
        float _wedgedTimer;
        Vector3 _lastPos;

        public void Begin()
        {
            if (mower == null || target == null) return;
            BuildPath();
            _index = 0;
            _stuckTimer = 0f;
            _lastPos = mower.transform.position;
            Active = _path.Count > 0;
            if (InputReader.Instance != null) InputReader.Instance.OverrideActive = Active;
        }

        public void Stop()
        {
            Active = false;
            if (InputReader.Instance != null)
            {
                InputReader.Instance.OverrideActive = false;
                InputReader.Instance.SetOverride(0f, 0f, false, false);
            }
        }

        /// <summary>
        /// Scan the picture in strips, keep the spans that fall inside it, and drive them in an
        /// interleaved order.
        ///
        /// The obvious pattern — mow each strip then U-turn into the one next to it — is not
        /// drivable: adjacent passes are about a metre apart and the mower's tightest turn is
        /// over a metre in radius, so it just orbits the next waypoint forever. Taking every
        /// <see cref="lineSkip"/>th strip and then coming back for the ones in between gives each
        /// turn several metres of room, which is exactly why real mowing patterns look like this.
        /// </summary>
        void BuildPath()
        {
            _path.Clear();
            float r = target.shapeRadius;

            // First pass: collect every drivable span, grouped by strip.
            var lines = new List<List<Vector2>>();
            var lineZ = new List<float>();

            for (float z = -r; z <= r; z += passSpacing)
            {
                var spans = new List<Vector2>();
                bool inSpan = false;
                float spanStart = 0f;

                const float step = 0.35f;
                for (float x = -r; x <= r; x += step)
                {
                    float d = TargetShapes.Sdf(target.Shape, new Vector2(x / r, z / r));
                    bool inside = d < 0f;
                    if (inside && !inSpan) { inSpan = true; spanStart = x; }
                    else if (!inside && inSpan) { inSpan = false; spans.Add(new Vector2(spanStart, x)); }
                }
                if (inSpan) spans.Add(new Vector2(spanStart, r));

                // Drop slivers the mower cannot usefully enter.
                spans.RemoveAll(sp => sp.y - sp.x < 1.2f);
                if (spans.Count == 0) continue;

                lines.Add(spans);
                lineZ.Add(z);
            }

            // Second pass: emit in interleaved order so consecutive strips are far enough apart
            // to turn between, alternating direction so it reads as a mowing pattern.
            bool flip = false;
            int k = Mathf.Max(1, lineSkip);
            for (int offset = 0; offset < k; offset++)
            {
                for (int i = offset; i < lines.Count; i += k)
                {
                    foreach (var sp in lines[i])
                    {
                        // Overrun each end so the blade clears the edge of the shape.
                        float a = sp.x - 0.45f, b = sp.y + 0.45f;
                        if (flip) { (a, b) = (b, a); }
                        _path.Add(new Vector3(a, 0f, lineZ[i]));
                        _path.Add(new Vector3(b, 0f, lineZ[i]));
                        flip = !flip;
                    }
                }
            }
        }

        void FixedUpdate()
        {
            if (SimClock.Scripted) return;
            TickPhysics(Time.fixedDeltaTime);
        }

        public void TickPhysics(float dt)
        {
            if (!Active || mower == null) return;
            var input = InputReader.Instance;
            if (input == null) return;

            if (_index >= _path.Count)
            {
                input.SetOverride(0f, 0f, false, false);
                return;
            }

            Vector3 pos = mower.transform.position;
            Vector3 wp = _path[_index];
            wp.y = pos.y;

            if ((wp - pos).sqrMagnitude < waypointRadius * waypointRadius)
            {
                _index++;
                _stuckTimer = 0f;
                if (_index >= _path.Count) { input.SetOverride(0f, 0f, false, false); return; }
                wp = _path[_index];
                wp.y = pos.y;
            }

            // Aim a fixed distance along the segment toward the waypoint rather than at the
            // waypoint itself, which stops the mower fishtailing as it arrives.
            Vector3 toWp = wp - pos;
            float dist = toWp.magnitude;
            Vector3 aim = dist > lookAheadDistance
                ? pos + toWp.normalized * lookAheadDistance
                : wp;

            Vector3 toAim = aim - pos;
            toAim.y = 0f;
            float angle = Vector3.SignedAngle(mower.transform.forward, toAim.normalized, Vector3.up);

            float steer = Mathf.Clamp(angle * steerGain, -1f, 1f);
            float throttle = Mathf.Lerp(cruiseThrottle, cruiseThrottle * cornerSlowdown,
                                        Mathf.Clamp01(Mathf.Abs(angle) / 55f));

            // Bleed off speed well before the turn. Steering authority falls away with speed, so
            // arriving fast means orbiting a waypoint that sits inside your own turning circle.
            throttle *= Mathf.Lerp(0.30f, 1f, Mathf.Clamp01(dist / slowdownDistance));

            // Reverse out of a hard corner instead of grinding forward into it.
            if (Mathf.Abs(angle) > 110f && dist < 4f) { throttle = -0.6f; steer = -steer; }

            // A waypoint that has ended up behind us is not worth chasing round a full circle.
            if (Mathf.Abs(angle) > 140f && dist < 2.5f) { _index++; _stuckTimer = 0f; }

            bool boost = useBoost && dist > boostStraightLength && Mathf.Abs(angle) < 10f
                         && mower.BoostFuel > 0.45f;

            // Unstick. Two failure modes: wedged against something, and orbiting a waypoint
            // that is inside our turning circle. Track time spent on one waypoint rather than
            // distance moved, so both are caught.
            _stuckTimer += dt;
            if ((pos - _lastPos).sqrMagnitude > 0.0004f) _wedgedTimer = 0f; else _wedgedTimer += dt;
            _lastPos = pos;
            if (_stuckTimer > 9f || _wedgedTimer > 1.6f) { _index++; _stuckTimer = 0f; _wedgedTimer = 0f; }

            input.SetOverride(steer, throttle, false, boost);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            for (int i = 1; i < _path.Count; i++)
                Gizmos.DrawLine(_path[i - 1] + Vector3.up * 0.2f, _path[i] + Vector3.up * 0.2f);
        }
    }
}
