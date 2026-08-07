using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One competitor's flowers and the fence in front of them.
    ///
    /// Binds baked scene objects; builds nothing. The beds and pickets are real GameObjects saved in
    /// GooseRally.unity by <c>DuckRallyBuilder</c>, and this only records what a round does to them —
    /// same split as <see cref="DefenceArena"/> and for the same reason.
    ///
    /// Integrity is the score. It starts at 1 and only ever falls, and it is what crosses back into
    /// the final judging: the picture the player mowed in round one is on this lawn, and a goose that
    /// gets through takes a bite out of it. Which is why a single crash costs a bounded fraction —
    /// see <see cref="bedsPerCrash"/>. A garden that can be wiped out reads as a game that stopped
    /// caring what you did for the first two rounds.
    /// </summary>
    public class RallyGarden : MonoBehaviour
    {
        public class Bed
        {
            public Transform body;
            public Vector3 standingPos;
            public Vector3 standingScale;
            public Quaternion standingRot;
            public bool alive = true;
        }

        [Header("Identity")]
        [Tooltip("Which corner this garden defends. Indexes RallyArena.")]
        public int slot;

        [Header("Baked layout")]
        [Tooltip("The flower beds. A goose that gets through flattens a couple of them.")]
        public Transform[] beds = new Transform[0];
        [Tooltip("Fence sections, left to right along the frontage. Smashed where the goose came through.")]
        public Transform[] fenceSections = new Transform[0];
        [Tooltip("The pennant over this quadrant. Leans and thrashes while a goose is inbound.")]
        public Transform flag;
        [Tooltip("Marker post carrying this competitor's colour, so the quadrant reads from anywhere.")]
        public Transform totem;
        [Tooltip("The totem's gauge bar. Emptied downward as the garden is destroyed, so every " +
                 "competitor's score is standing in the world at full size rather than only in a " +
                 "corner of the player's screen. In a four-way that matters: the decision the player " +
                 "makes forty times a match is WHO to send it at, and that decision is spatial.")]
        public Transform totemBar;

        [Header("Authored variants")]
        [Tooltip("Child object name holding a bed's intact mesh. Hidden when the bed is destroyed.")]
        public string intactChild = "Intact";
        [Tooltip("Child object name holding the wrecked mesh, shown in its place. When a bed has no " +
                 "such child the bed is squashed and repainted instead — which is the fallback, not " +
                 "the intention: a purpose-modelled wreck reads as damage, a flattened copy of the " +
                 "original reads as a bug.")]
        public string ruinedChild = "Ruined";

        [Header("Damage")]
        [Tooltip("Beds flattened by one goose getting through. Two of fifteen is about seven percent " +
                 "of a garden per crash, so a competitor has to be beaten repeatedly to be ruined.")]
        public int bedsPerCrash = 2;
        [Tooltip("Radius around the crash inside which beds are taken.")]
        public float crashRadius = 5.2f;
        [Tooltip("Fence sections knocked flat by one break-in.")]
        public int sectionsPerCrash = 2;
        [Tooltip("What a flattened bed is repainted with. A disk asset, never a runtime material.")]
        public Material trampledMaterial;

        public int BedsTotal { get; private set; }
        public int BedsLost { get; private set; }
        public int Breaches { get; private set; }

        /// <summary>1 for untouched, 0 for ruined. The whole of this mode's scoring.</summary>
        public float Integrity => BedsTotal > 0 ? Mathf.Clamp01(1f - BedsLost / (float)BedsTotal) : 1f;

        public RallyArena.Slot Slot => RallyArena.Get(slot);
        public Vector3 Centre => Slot.gardenCentre;

        readonly List<Bed> _beds = new(24);
        readonly List<(Transform t, Vector3 pos, Quaternion rot)> _fenceStanding = new(16);
        readonly HashSet<Transform> _smashed = new();
        Material _standingMaterial;
        float _flagAlarm;
        float _flagPhase;
        Vector3 _flagRest;

        void Awake()
        {
            foreach (var t in beds)
            {
                if (t == null) continue;
                var r = t.GetComponentInChildren<MeshRenderer>();
                if (r == null) continue;
                _standingMaterial = r.sharedMaterial;
                break;
            }
            if (flag != null) _flagRest = flag.localEulerAngles;
        }

        /// <summary>Stand everything back up. Called once at the start of a match.</summary>
        public void Bind()
        {
            _beds.Clear();
            BedsLost = 0;
            Breaches = 0;

            foreach (var t in beds)
            {
                if (t == null) continue;
                var b = new Bed
                {
                    body = t,
                    standingPos = t.position,
                    standingScale = t.localScale,
                    standingRot = t.rotation,
                };
                Stand(b);
                _beds.Add(b);
            }
            BedsTotal = _beds.Count;

            // Captured on the first bind only. Re-reading on a later bind would record the SMASHED
            // pose as standing, so one match would leave the fence permanently flat.
            if (_fenceStanding.Count == 0 && fenceSections != null)
                foreach (var t in fenceSections)
                    if (t != null) _fenceStanding.Add((t, t.position, t.rotation));

            foreach (var (t, pos, rot) in _fenceStanding)
            {
                if (t == null) continue;
                t.SetPositionAndRotation(pos, rot);
                t.gameObject.SetActive(true);
                var intact = t.Find(intactChild);
                var broken = t.Find(ruinedChild);
                if (intact != null) intact.gameObject.SetActive(true);
                if (broken != null) broken.gameObject.SetActive(false);
            }
            _smashed.Clear();

            if (BedsTotal == 0)
                Debug.LogWarning($"[Rally] garden {slot} has no beds bound — rebuild the scene with " +
                                 "'Duck/3 · Build goose rally scene'.");
        }

        void Stand(Bed b)
        {
            b.alive = true;
            if (b.body == null) return;
            b.body.SetPositionAndRotation(b.standingPos, b.standingRot);
            b.body.localScale = b.standingScale;
            b.body.gameObject.SetActive(true);

            var intact = b.body.Find(intactChild);
            var ruined = b.body.Find(ruinedChild);
            if (intact != null || ruined != null)
            {
                if (intact != null) intact.gameObject.SetActive(true);
                if (ruined != null) ruined.gameObject.SetActive(false);
                return;
            }

            if (_standingMaterial == null) return;
            foreach (var r in b.body.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = _standingMaterial;
        }

        // ------------------------------------------------------------------ damage

        /// <summary>
        /// A goose got through. Break the fence where it crossed, flatten what it landed on, and
        /// report how much was lost.
        /// </summary>
        /// <param name="from">Where the goose was a moment before it crossed the line.</param>
        /// <param name="impact">Where it came down in the beds.</param>
        public int Breach(Vector3 from, Vector3 impact)
        {
            Breaches++;
            SmashFence(from, impact);
            int killed = Flatten(impact, crashRadius, bedsPerCrash);
            Alarm(1f);
            return killed;
        }

        /// <summary>Flatten living beds near a point, up to a limit. Returns how many died.</summary>
        public int Flatten(Vector3 world, float radius, int limit)
        {
            if (limit <= 0) return 0;
            float r2 = radius * radius;
            int killed = 0;

            // Nearest first, so the damage clusters where the bird actually landed rather than
            // taking whichever beds happen to come first in the array.
            _beds.Sort((a, b) => (a.standingPos - world).sqrMagnitude
                       .CompareTo((b.standingPos - world).sqrMagnitude));

            for (int i = 0; i < _beds.Count && killed < limit; i++)
            {
                var b = _beds[i];
                if (!b.alive) continue;
                if ((b.standingPos - world).sqrMagnitude > r2) continue;

                b.alive = false;
                killed++;
                if (b.body == null) continue;

                // TIPPED OVER, not just swapped.
                //
                // Exchanging the intact mesh for the wrecked one is correct and it is not enough: a
                // bed that stays perfectly level and square to the grid reads as a texture change,
                // and the player looking at their own garden cannot tell at a glance how much of it
                // has gone. A bed that has been KNOCKED OVER is unmistakable from any angle and at
                // any distance, which is the whole job of this feedback.
                //
                // Tipped away from the impact and around the axis across that direction, so the
                // whole garden falls outward from wherever the goose came down — the damage points
                // back at the crash rather than being randomly scattered.
                Vector3 away = b.standingPos - world; away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = Slot.outward;
                away.Normalize();

                Vector3 axis = Vector3.Cross(Vector3.up, away);
                float tip = 62f + Random.value * 34f;
                b.body.rotation = Quaternion.AngleAxis(tip, axis)
                                * Quaternion.LookRotation(away, Vector3.up) * b.standingRot;
                // A bed rotated about its own centre leaves one edge buried and the other in the
                // air, so drop it onto the soil and shove it the way the bird went — the same
                // correction the fence needed for the same reason.
                b.body.position = new Vector3(b.standingPos.x, 0.04f, b.standingPos.z)
                                + away * Random.Range(0.15f, 0.5f);

                var intact = b.body.Find(intactChild);
                var ruined = b.body.Find(ruinedChild);
                if (intact != null || ruined != null)
                {
                    // The authored wreck: same footprint, same edging boards, same bloom positions —
                    // churned, flattened, with web prints pressed into the soil. It reads as THIS bed
                    // destroyed, where a scaled copy of the original only ever reads as a bug.
                    if (intact != null) intact.gameObject.SetActive(false);
                    if (ruined != null) ruined.gameObject.SetActive(true);
                    continue;
                }

                // Fallback, for a garden built before the wrecked mesh existed. Flattened, not
                // hidden: bare soil reads as never having been planted, and the player has to see
                // that something was DESTROYED rather than that something is missing.
                b.body.localScale = new Vector3(b.standingScale.x * 1.25f,
                                                b.standingScale.y * 0.14f,
                                                b.standingScale.z * 1.35f);
                if (trampledMaterial != null)
                    foreach (var r in b.body.GetComponentsInChildren<MeshRenderer>())
                        r.sharedMaterial = trampledMaterial;
            }

            BedsLost += killed;
            return killed;
        }

        /// <summary>
        /// Knock the fence flat where the goose crossed it.
        ///
        /// Measured against the SEGMENT flown, not against where the bird ended up: a goose that
        /// tumbles into the middle of the garden broke in somewhere on the perimeter, and it is that
        /// crossing the player watched. Toppled by rotation rather than by physics — these are
        /// static collider-free boards, and a dozen fresh rigidbodies on the exact frame a hit stop
        /// has already made expensive buys nothing anyone can see.
        /// </summary>
        public int SmashFence(Vector3 from, Vector3 to)
        {
            if (fenceSections == null || fenceSections.Length == 0 || sectionsPerCrash <= 0) return 0;

            Vector3 seg = to - from; seg.y = 0f;
            float segLenSq = Mathf.Max(seg.sqrMagnitude, 1e-4f);
            Vector3 push = seg.sqrMagnitude > 1e-4f ? seg.normalized : Slot.outward;
            Vector3 axis = Vector3.Cross(Vector3.up, push);

            // Rank by distance to the flight line and take the closest, so a break is always a
            // contiguous hole rather than whichever boards the loop reached first.
            var ranked = new List<(Transform t, float d)>(fenceSections.Length);
            foreach (var t in fenceSections)
            {
                if (t == null || _smashed.Contains(t)) continue;
                Vector3 p = t.position; p.y = 0f;
                Vector3 a = from; a.y = 0f;
                float k = Mathf.Clamp01(Vector3.Dot(p - a, seg) / segLenSq);
                ranked.Add((t, (p - (a + seg * k)).sqrMagnitude));
            }
            ranked.Sort((x, y) => x.d.CompareTo(y.d));

            int hit = 0;
            for (int i = 0; i < ranked.Count && hit < sectionsPerCrash; i++)
            {
                var t = ranked[i].t;

                // Swap to the splintered panel if the section has one, so the hole in the fence is
                // BROKEN rather than merely lying down. A whole picket fence tipped over at 78
                // degrees reads as somebody having pushed it; a shattered one reads as impact.
                var intact = t.Find(intactChild);
                var broken = t.Find(ruinedChild);
                if (intact != null) intact.gameObject.SetActive(false);
                if (broken != null) broken.gameObject.SetActive(true);

                // Tipped either way. A broken panel falls less far — it has already come apart.
                float lean = broken != null ? 22f + Random.value * 16f : 74f + Random.value * 14f;
                t.rotation = Quaternion.AngleAxis(lean, axis) * t.rotation;
                // A board rotated about its own centre leaves one end buried and the other in the
                // air, so drop it onto the ground and shove it the way the bird went.
                t.position = new Vector3(t.position.x, broken != null ? 0.02f : 0.07f, t.position.z)
                           + push * 0.22f;
                _smashed.Add(t);
                hit++;
            }
            return hit;
        }

        // ------------------------------------------------------------------ threat reaction

        /// <summary>
        /// Raise the alarm over this quadrant. The flag thrashes and the totem pulses, so a player
        /// looking anywhere in the arena can see which garden is about to be hit.
        /// </summary>
        public void Alarm(float amount) => _flagAlarm = Mathf.Clamp01(Mathf.Max(_flagAlarm, amount));

        public float AlarmLevel => _flagAlarm;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _flagAlarm = Mathf.Max(0f, _flagAlarm - dt * 0.8f);
            _flagPhase += dt * (2.2f + _flagAlarm * 11f);

            if (flag != null)
            {
                // Leans away from the garden as the alarm rises: the pole is being pushed by whatever
                // is coming, which points at the threat without drawing an arrow. Kept small, because
                // this is a flagpole flexing rather than a pennant on a string — the authored prop is
                // one piece and a thirty-degree lean reads as the mast snapping.
                float lean = 2f + _flagAlarm * 9f;
                float flutter = Mathf.Sin(_flagPhase) * (1f + _flagAlarm * 4f);
                flag.localRotation = Quaternion.Euler(_flagRest.x + lean + flutter,
                                                      _flagRest.y,
                                                      _flagRest.z + flutter * 0.6f);
            }

            // The gauge. Every competitor's remaining garden, standing in the world at full size —
            // so "who is winning" is answerable by looking across the arena rather than only by
            // reading a corner of the HUD. Eased rather than snapped, so losing two beds is a bar
            // visibly dropping and not a value that was already different.
            if (totemBar != null)
            {
                float want = Mathf.Clamp01(Integrity);
                var s = totemBar.localScale;
                totemBar.localScale = new Vector3(s.x, Mathf.MoveTowards(s.y, want, dt * 0.8f), s.z);
            }

            if (totem != null && _flagAlarm > 0.001f)
            {
                float pulse = 1f + Mathf.Sin(_flagPhase * 1.7f) * 0.05f * _flagAlarm;
                totem.localScale = new Vector3(1f, pulse, 1f);
            }
        }
    }
}
