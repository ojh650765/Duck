using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Where does Bloom Rush actually stop you?
    ///
    /// The arena is a wheel of numbers — a walled core at 8 m, a hedge wall from 30 to 33, a
    /// touchline at 42 and a perimeter wall of box colliders at 45.9 — and a mower reported being
    /// shoved back off open ground a long way short of the wall. That report is impossible to
    /// settle by driving at the fence once and looking: a boundary fault can be one bad bearing out
    /// of three hundred and sixty, and a fix that works where somebody happened to test is not a
    /// fix. Both halves of the question have to be asked all the way round.
    ///
    /// So this asks them separately, because they are different questions with different failure
    /// modes:
    ///
    ///   * the SWEEP asks where the geometry is. No play mode, no mower, no physics stepping — just
    ///     a sphere the width of the machine marched outward along seventy-two bearings, recording
    ///     every solid thing it meets and how far out it was. That finds a phantom collider ring,
    ///     and it finds it whether or not anything is currently being pushed by one.
    ///   * the REACHABILITY PROBE asks where the MOWER can get to, which is not the same thing at
    ///     all: the fault being chased is a FORCE, applied by <see cref="TurfCompetitor"/>'s own
    ///     recovery push, and a force leaves no geometry for a sphere to find. The only way to
    ///     measure it is to drive the real machine at the real boundary and watch where it stops.
    ///
    /// The probe therefore runs through Unity's actual player loop rather than stepping physics by
    /// hand the way <see cref="DuckSimulator"/> does. That is deliberate and it is the whole point:
    /// the recovery push lives in a FixedUpdate, and a scripted step calls Physics.Simulate without
    /// calling anybody's FixedUpdate — so a hand-stepped probe would measure an arena with the bug
    /// switched off and report a clean pass every time.
    ///
    /// Neither mode edits anything. The sweep is queries only, and the probe puts the machine, the
    /// clock, the director and the input wiring back where it found them.
    /// </summary>
    public static class DuckTurfBoundaryProbe
    {
        const string Dir = "Captures/Bloom";

        // ------------------------------------------------------------------ sweep tuning

        /// <summary>Bearings sampled by the collider sweep. Seventy-two is every five degrees.</summary>
        const int SweepAzimuths = 72;
        /// <summary>How far the sphere moves between tests. Finer than any collider in the arena.</summary>
        const float SweepStep = 0.25f;

        /// <summary>
        /// The probe sphere, which is the mower rather than a point.
        ///
        /// Mower.prefab's body box is 0.92 m across, so 0.5 m of radius is the machine's half-width
        /// with a little to spare — a gap this sphere fits through is a gap the mower fits through.
        /// </summary>
        const float ProbeRadius = 0.5f;

        /// <summary>
        /// Height of the sphere's centre. Chosen so it CANNOT touch the floor.
        ///
        /// The arena is dead flat (TurfArena.RampHeight is zero), so the ground mesh is the plane
        /// y = 0 and a sphere centred at 0.62 with a radius of 0.5 clears it by 12 cm. That is the
        /// first of two defences against the ground answering every single query; the second is
        /// <see cref="IsGround"/>, which throws the ground collider out by name if it is ever met
        /// anyway. Two, because if the ramps are ever switched back on the height defence silently
        /// stops working and the sweep would report a solid arena.
        ///
        /// The top of the sphere reaches 1.12 m, which is inside the hedge boxes (2.6 m), the
        /// perimeter wall (3 m) and the core wall (0.95 m), and its waist is wide enough at 34 cm
        /// to catch the plaza kerb as well. Nothing solid in the arena is below it.
        /// </summary>
        const float ProbeHeight = 0.62f;

        /// <summary>Start just outside the core wall's outer face, which stands at 8.35 m.</summary>
        const float SweepFrom = TurfArena.CoreRadius + 1.2f;
        /// <summary>Well past the barrier, so the crowd stands and anything behind them are seen too.</summary>
        const float SweepTo = 55f;

        /// <summary>
        /// Where the wall really is. TurfArena.BarrierRadius is 45.5, but DuckTurfBuilder stands the
        /// wall's box colliders at BarrierRadius + 0.4 — this is the number a mower actually meets.
        /// </summary>
        const float WallRadius = TurfArena.BarrierRadius + 0.4f;

        /// <summary>
        /// The three rings of dressing that carry colliders but are not in TurfArena's radius table.
        ///
        /// Measured off the built scene rather than derived, because DuckTurfBuilder places them from
        /// its own local numbers: twelve planters just inside the plaza kerb, and the lamp posts in
        /// two rings, one either side of the hedge. They are modelled and rendered, so a driver meets
        /// them with plenty of warning — they are obstacles, not boundary, and the report has to know
        /// the difference or it reports twenty phantoms and stops being worth running.
        /// </summary>
        const float PlanterRing = 17.7f;
        const float LampRingInner = 28.2f;
        const float LampRingOuter = 34.8f;

        /// <summary>
        /// How far out the mower's CENTRE can actually get, which is not the touchline.
        ///
        /// The touchline used to sit at 42 m with three and a half metres of run-off behind it, so
        /// "did the centre reach the touchline" was a fair question. It is not any more: ArenaRadius
        /// is now BarrierRadius, the paintable disc runs into the wall, and a mower is a box 1.45 m
        /// long. Drive it straight at the barrier and its nose stops on the wall's inner face with
        /// its centre still three quarters of a metre short — for ever, on every bearing, in a
        /// perfectly healthy arena. Asking for the touchline would fail all 36 bearings and call a
        /// working game broken, which is the one thing a regression check must never do.
        ///
        /// So the target is whichever comes first: the touchline, or as close to the wall as a body
        /// with length can physically put its middle. When the touchline is inboard of the wall the
        /// first term wins and this reads exactly as it did before.
        /// </summary>
        const float WallFace = WallRadius - 0.3f;          // half the barrier's 0.6 m thickness
        const float ChassisHalfLength = 0.725f;            // Mower.prefab BoxCollider m_Size.z 1.45
        static float ReachTarget =>
            Mathf.Min(TurfArena.ArenaRadius, WallFace - ChassisHalfLength);

        /// <summary>Beyond the hedge ring and its gate pillars: the first radius where a blocker is news.</summary>
        const float OuterFrom = TurfArena.HedgeOuter + 1.5f;
        /// <summary>A first blocker in the outfield closer in than this is the fault being hunted.</summary>
        const float OuterExpected = WallRadius - 2f;

        // ------------------------------------------------------------------ probe tuning

        const int ReachAzimuths = 36;
        /// <summary>
        /// Where each run starts: on the loop, one metre outside the hedge.
        ///
        /// NOT the middle of the arena, and the reason matters. A run started in the hub and pointed
        /// outward meets the hedge on seven bearings out of eight, and a hedge is an intentional
        /// wall — every one of those runs would report a failure at 30 m and bury the outfield
        /// result this exists to measure. Starting on the loop puts nothing between the machine and
        /// the touchline except whatever is wrongly there.
        /// </summary>
        const float ReachStart = TurfArena.HedgeOuter + 1f;
        /// <summary>Ride height the arena spawns machines at. See TurfArena.Slot.SpawnPosition.</summary>
        const float SpawnHeight = 0.42f;
        /// <summary>Hard step budget per bearing, so a wedged machine cannot hang the editor.</summary>
        const int ReachMaxSteps = 200;
        /// <summary>Fixed steps without gaining ground before a run is called done.</summary>
        const int StallSteps = 24;
        /// <summary>The match is run fast while the probe drives, and put back afterwards.</summary>
        const float ProbeTimeScale = 6f;

        // ==================================================================== mode A

        [MenuItem("Duck/QA · Bloom Rush · Collider sweep", priority = 70)]
        public static void SweepMenu()
        {
            if (!SceneIsBloomRush()) return;

            var rows = Sweep(SweepAzimuths);
            var sb = new StringBuilder();
            SweepText(sb, rows);
            Debug.Log(sb.ToString());
        }

        // ==================================================================== mode B

        [MenuItem("Duck/QA · Bloom Rush · Reachability probe", priority = 71)]
        public static void ReachMenu()
        {
            if (!SceneIsBloomRush()) return;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Bloom QA] the reachability probe drives a real mower, so it needs " +
                                 "PLAY MODE. Press play on BloomRush and run this again. The collider " +
                                 "sweep works without it.");
                return;
            }

            var player = FindPlayer(out string how);
            if (player == null || player.mower == null)
            {
                Debug.LogError("[Bloom QA] no player mower found. Looked for TurfDirector.Player, then " +
                               "a TurfCompetitor with isPlayer set, then one whose brain is null — and " +
                               "none of the three found a competitor with a mower wired to it.");
                return;
            }

            var samples = new List<ReachSample>(ReachAzimuths);
            player.StartCoroutine(ProbeRoutine(player, how, ReachAzimuths, samples, null));
        }

        // ==================================================================== mode C

        [MenuItem("Duck/QA · Bloom Rush · Boundary report", priority = 72)]
        public static void ReportMenu()
        {
            if (!SceneIsBloomRush()) return;

            var rows = Sweep(SweepAzimuths);

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Bloom QA] edit mode: the report has the collider sweep in it but no " +
                                 "reachability probe, so two of the three verdicts cannot be decided. " +
                                 "Press play and run this again for the whole thing.");
                Finish(rows, null, "not run — edit mode");
                return;
            }

            var player = FindPlayer(out string how);
            if (player == null || player.mower == null)
            {
                Debug.LogError("[Bloom QA] no player mower found; writing the sweep half only.");
                Finish(rows, null, "not run — no player mower");
                return;
            }

            var samples = new List<ReachSample>(ReachAzimuths);
            player.StartCoroutine(ProbeRoutine(player, how, ReachAzimuths, samples,
                                               () => Finish(rows, samples, how)));
        }

        // ==================================================================== the sweep

        /// <summary>One run of blocked ground along one bearing.</summary>
        struct Span
        {
            public float from, to;
            public string collider;
            public string path;
        }

        class AzimuthSweep
        {
            public float azimuth;
            /// <summary>First radius outside the core at which anything solid stands. -1 for none.</summary>
            public float firstBlock;
            public string firstName;
            public string firstPath;
            /// <summary>First blocker beyond the hedge ring — the outfield number. -1 for none.</summary>
            public float outerBlock;
            public string outerName;
            public string outerPath;
            public readonly List<Span> spans = new List<Span>(6);
        }

        static List<AzimuthSweep> Sweep(int azimuths)
        {
            // Edit-mode transforms are not necessarily in the physics scene yet. One call, once.
            Physics.SyncTransforms();

            var rows = new List<AzimuthSweep>(azimuths);

            for (int i = 0; i < azimuths; i++)
            {
                float deg = i * 360f / azimuths;
                Vector3 dir = TurfArena.Bearing(deg);

                var row = new AzimuthSweep { azimuth = deg, firstBlock = -1f, outerBlock = -1f };
                bool inSpan = false;
                Span cur = default;

                // Stepped by integer count rather than by adding a float to itself, so the last
                // sample lands where it is supposed to rather than a few centimetres adrift.
                int steps = Mathf.FloorToInt((SweepTo - SweepFrom) / SweepStep) + 1;
                for (int s = 0; s < steps; s++)
                {
                    float r = SweepFrom + s * SweepStep;
                    Vector3 p = dir * r;
                    p.y = ProbeHeight;

                    Collider hit = Blocking(p);

                    if (hit != null)
                    {
                        string name = hit.name;
                        string path = PathOf(hit.transform);

                        // A new span whenever a DIFFERENT collider takes over, not only when open
                        // air intervenes. Otherwise a stray object standing shoulder to shoulder
                        // with the hedge is filed under the hedge's radius and never reported.
                        if (inSpan && cur.path != path)
                        {
                            row.spans.Add(cur);
                            inSpan = false;
                        }

                        if (!inSpan)
                        {
                            inSpan = true;
                            cur = new Span { from = r, to = r, collider = name, path = path };
                        }
                        else cur.to = r;

                        if (row.firstBlock < 0f)
                        {
                            row.firstBlock = r;
                            row.firstName = name;
                            row.firstPath = path;
                        }
                        if (row.outerBlock < 0f && r >= OuterFrom)
                        {
                            row.outerBlock = r;
                            row.outerName = name;
                            row.outerPath = path;
                        }
                    }
                    else if (inSpan)
                    {
                        inSpan = false;
                        row.spans.Add(cur);
                    }
                }

                if (inSpan) row.spans.Add(cur);
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// What is standing at this point, or null for open air.
        ///
        /// CheckSphere first because it allocates nothing and answers no in one call, which is what
        /// nearly sixteen thousand of these are. Only a positive pays for the overlap query that
        /// names the thing.
        /// </summary>
        static Collider Blocking(Vector3 p)
        {
            if (!Physics.CheckSphere(p, ProbeRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                return null;

            var found = Physics.OverlapSphere(p, ProbeRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            // The NEAREST of whatever is in there, not whatever the query happened to list first.
            // Overlap order is unspecified, and two touching boxes reported in a different order on
            // consecutive steps would chop one wall into a dozen little spans.
            Collider best = null;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < found.Length; i++)
            {
                var c = found[i];
                if (c == null) continue;
                if (IsGround(c)) continue;
                if (IsMower(c)) continue;

                float d = Vector3.Distance(p, c.ClosestPoint(p));
                if (d >= bestDist) continue;
                bestDist = d;
                best = c;
            }
            return best;
        }

        /// <summary>
        /// The floor. Excluded by NAME and TYPE: DuckTurfBuilder gives the arena one MeshCollider on
        /// a GameObject called "Ground" and there is no other mesh collider in the scene called that.
        /// Belt and braces with <see cref="ProbeHeight"/> — see the note there for why one is not
        /// enough.
        /// </summary>
        static bool IsGround(Collider c) => c is MeshCollider && c.gameObject.name == "Ground";

        /// <summary>
        /// A machine, not the arena. All four are parked out on the loop at 37.5 m in the saved
        /// scene, three of them on bearings the sweep samples exactly, so without this every spoke
        /// would report a phantom blocker sitting in the middle of the outfield.
        /// </summary>
        static bool IsMower(Collider c) => c.GetComponentInParent<MowerController>() != null;

        static string PathOf(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// <summary>
        /// Is a blocker at this radius one of the arena's own intentional walls?
        ///
        /// The bands are the built geometry plus the probe's own radius, because a 0.5 m sphere
        /// registers a wall half a metre before its face. Anything outside all of them and inside
        /// the outfield is, by definition, the thing this tool was written to find.
        /// </summary>
        static bool Known(float r)
        {
            if (r <= TurfArena.CoreRadius + 1.2f) return true;                       // core wall
            if (Mathf.Abs(r - TurfArena.PlazaRadius) <= 1.2f) return true;           // plaza kerb
            if (r >= TurfArena.HedgeInner - 1.5f && r <= TurfArena.HedgeOuter + 1.5f)
                return true;                                                          // hedge + gate pillars
            if (r >= WallRadius - 1.5f) return true;                                  // barrier wall and beyond

            // The three prop rings. These are dressing rather than boundary, so they are not in
            // TurfArena's radius table — but they are modelled, rendered and collided, and a driver
            // sees every one of them coming. Measured off the built scene: twelve planters standing
            // just inside the kerb, and the lamp posts in two rings either side of the hedge. Left
            // out of this list they read as twenty phantom blockers and the report cries wolf, which
            // is worse than not running it.
            if (Mathf.Abs(r - PlanterRing) <= 1.2f) return true;                     // plaza planters
            if (Mathf.Abs(r - LampRingInner) <= 1.0f) return true;                   // lamp posts, inside the hedge
            if (Mathf.Abs(r - LampRingOuter) <= 1.0f) return true;                   // lamp posts, out on the loop
            return false;
        }

        static void SweepText(StringBuilder sb, List<AzimuthSweep> rows)
        {
            sb.AppendLine("[Bloom QA] COLLIDER SWEEP");
            sb.AppendLine($"  a {ProbeRadius:0.00} m sphere at y={ProbeHeight:0.00}, marched from " +
                          $"{SweepFrom:0.0} m to {SweepTo:0.0} m in {SweepStep:0.00} m steps, on " +
                          $"{rows.Count} bearings.");
            sb.AppendLine("  the ground mesh and all four mowers are excluded; triggers are ignored.");
            sb.AppendLine($"  known walls: core {TurfArena.CoreRadius:0.#}, kerb {TurfArena.PlazaRadius:0.#}, " +
                          $"hedge {TurfArena.HedgeInner:0.#}-{TurfArena.HedgeOuter:0.#}, " +
                          $"barrier {WallRadius:0.0}. Touchline {TurfArena.ArenaRadius:0.#}.");
            sb.AppendLine();
            sb.AppendLine("   deg |  first | what                      | outfield | what");
            sb.AppendLine("  -----+--------+---------------------------+----------+---------------------------");

            float minFirst = float.PositiveInfinity, maxFirst = float.NegativeInfinity, sumFirst = 0f;
            int countFirst = 0;
            float minOuter = float.PositiveInfinity, maxOuter = float.NegativeInfinity, sumOuter = 0f;
            int countOuter = 0;

            foreach (var row in rows)
            {
                string first = row.firstBlock < 0f ? "  none" : $"{row.firstBlock,6:0.00}";
                string outer = row.outerBlock < 0f ? "    none" : $"{row.outerBlock,8:0.00}";
                sb.AppendLine($"  {row.azimuth,4:0} | {first} | {Fit(row.firstName, 25)} | {outer} | {Fit(row.outerName, 25)}");

                if (row.firstBlock >= 0f)
                {
                    minFirst = Mathf.Min(minFirst, row.firstBlock);
                    maxFirst = Mathf.Max(maxFirst, row.firstBlock);
                    sumFirst += row.firstBlock;
                    countFirst++;
                }
                if (row.outerBlock >= 0f)
                {
                    minOuter = Mathf.Min(minOuter, row.outerBlock);
                    maxOuter = Mathf.Max(maxOuter, row.outerBlock);
                    sumOuter += row.outerBlock;
                    countOuter++;
                }
            }

            sb.AppendLine();
            sb.AppendLine("  SUMMARY");
            sb.AppendLine(countFirst == 0
                ? "    first blocker: NOTHING SOLID ON ANY BEARING — the arena has no walls at all."
                : $"    first blocker (any radius): min {minFirst:0.00}  max {maxFirst:0.00}  " +
                  $"mean {sumFirst / countFirst:0.00}  ({countFirst}/{rows.Count} bearings blocked)");
            sb.AppendLine(countOuter == 0
                ? $"    outfield blocker (r >= {OuterFrom:0.0}): none on any bearing — NOTHING is holding " +
                  "mowers in the arena."
                : $"    outfield blocker (r >= {OuterFrom:0.0}): min {minOuter:0.00}  max {maxOuter:0.00}  " +
                  $"mean {sumOuter / countOuter:0.00}  ({countOuter}/{rows.Count} bearings blocked)");

            // The list that matters: an outfield blocker standing well inside the wall.
            var early = new List<AzimuthSweep>();
            foreach (var row in rows)
                if (row.outerBlock >= 0f && row.outerBlock < OuterExpected) early.Add(row);

            sb.AppendLine();
            if (early.Count == 0)
                sb.AppendLine($"    no bearing is blocked more than 2 m inside the wall — the outfield is " +
                              $"clear from {OuterFrom:0.0} m to the barrier all the way round.");
            else
            {
                sb.AppendLine($"    {early.Count} BEARING(S) BLOCKED MORE THAN 2 m INSIDE THE WALL " +
                              $"(expected clear until {OuterExpected:0.0} m):");
                foreach (var row in early)
                    sb.AppendLine($"      {row.azimuth,4:0} deg  blocked at {row.outerBlock:0.00} m by " +
                                  $"'{row.outerName}'  ({row.outerPath})");
            }

            // And every solid thing inside the outfield that is not one of the arena's own walls.
            var strays = Strays(rows);
            sb.AppendLine();
            if (strays.Count == 0)
                sb.AppendLine("    no unexplained geometry inside 44 m: every blocker is the core wall, " +
                              "the plaza kerb, the hedge ring or the barrier.");
            else
            {
                sb.AppendLine($"    {strays.Count} UNEXPLAINED BLOCKER(S) INSIDE 44 m:");
                foreach (var s in strays) sb.AppendLine("      " + s);
            }
        }

        /// <summary>Every blocked run inside 44 m that is not one of the arena's known walls.</summary>
        static List<string> Strays(List<AzimuthSweep> rows)
        {
            var strays = new List<string>();
            foreach (var row in rows)
                foreach (var span in row.spans)
                {
                    if (span.from >= 44f) continue;
                    if (Known(span.from)) continue;
                    strays.Add($"{row.azimuth,4:0} deg  {span.from:0.00} m to {span.to:0.00} m  " +
                               $"'{span.collider}'  ({span.path})");
                }
            return strays;
        }

        static string Fit(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) return new string(' ', width);
            if (s.Length > width) return s.Substring(0, width);
            return s.PadRight(width);
        }

        // ==================================================================== the probe

        class ReachSample
        {
            public float azimuth;
            public float maxRadius;
            public float endRadius;
            public int steps;
            public string note = "";
        }

        /// <summary>Full throttle, dead ahead, nothing else. The most a driver could ask of the machine.</summary>
        sealed class FullThrottle : IMowerInput
        {
            public float Steer => 0f;
            public float Throttle => 1f;
            public bool Handbrake => false;
            public bool Boost => false;
        }

        static readonly WaitForFixedUpdate _fixedStep = new WaitForFixedUpdate();

        static float Radius(Vector3 p) => Mathf.Sqrt(p.x * p.x + p.z * p.z);

        /// <summary>
        /// The player's machine, found three ways because there is more than one truth about it.
        ///
        /// TurfDirector.Player first: the director resolves it on Begin and it is the same object
        /// every other system means. Then the isPlayer flag, which is what the builder stamped into
        /// the scene. The "brain is null" test is LAST on purpose — it is the real distinction in
        /// TurfCompetitor, but DuckTurfShots' capture autopilot bolts a TurfBrain onto the player's
        /// competitor, so in a capture session it quietly identifies an opponent instead.
        /// </summary>
        static TurfCompetitor FindPlayer(out string how)
        {
            var director = Object.FindFirstObjectByType<TurfDirector>();
            if (director != null && director.Player != null && director.Player.mower != null)
            {
                how = "TurfDirector.Player";
                return director.Player;
            }

            var all = Object.FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);
            if (all != null)
            {
                foreach (var c in all)
                    if (c != null && c.isPlayer && c.mower != null) { how = "isPlayer flag"; return c; }
                foreach (var c in all)
                    if (c != null && c.brain == null && c.mower != null) { how = "brain is null"; return c; }
            }

            how = "not found";
            return null;
        }

        /// <summary>
        /// Drive the player's mower straight at the boundary on every bearing in turn.
        ///
        /// Everything it touches is saved first and put back in a finally, including the things that
        /// are easy to forget: the match director is switched OFF for the duration (so the clock
        /// cannot run out mid-probe and park every machine for the reveal), the roller is lifted on
        /// all four competitors (so a survey does not repaint the match), rival machines are made
        /// non-colliding (so a bot wandering into the lane cannot fake a failure), and the input
        /// wiring, time scale, physics mode and the mower's own pose all go back.
        ///
        /// Run on the player COMPETITOR rather than the director, because a coroutine stops dead
        /// when the behaviour hosting it is disabled and the director is about to be.
        /// </summary>
        static IEnumerator ProbeRoutine(TurfCompetitor player, string how, int azimuths,
                                        List<ReachSample> samples, System.Action done)
        {
            var mower = player.mower;
            var rb = mower.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError("[Bloom QA] the player's mower has no Rigidbody; nothing to drive.");
                done?.Invoke();
                yield break;
            }

            var director = Object.FindFirstObjectByType<TurfDirector>();
            var competitors = Object.FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);

            // ---- everything that gets put back ----
            Vector3 homePos = mower.transform.position;
            Quaternion homeRot = mower.transform.rotation;
            Vector3 homeVel = rb.linearVelocity;
            Vector3 homeAngVel = rb.angularVelocity;
            IMowerInput prevInput = mower.inputSource;
            float prevTimeScale = Time.timeScale;
            bool prevScripted = SimClock.Scripted;
            SimulationMode prevSim = Physics.simulationMode;
            bool prevDirector = director != null && director.enabled;
            bool wasLive = director != null && director.State == TurfDirector.Phase.Live;

            var ignoredA = new List<Collider>(16);
            var ignoredB = new List<Collider>(16);

            if (director != null &&
                (director.State == TurfDirector.Phase.Lock || director.State == TurfDirector.Phase.Reveal))
                Debug.LogWarning("[Bloom QA] the match is already in its closing sequence. The probe " +
                                 "still runs, but the machines were being parked for the verdict — " +
                                 "restart the match for a cleaner reading.");

            try
            {
                // A previous scripted capture may have left the clock and the physics under manual
                // control, in which case nothing steps and every bearing reads as "went nowhere".
                if (prevScripted || prevSim != SimulationMode.FixedUpdate)
                    Debug.LogWarning("[Bloom QA] the scripted sim clock was still on; taking it off for " +
                                     "the probe and putting it back afterwards.");
                SimClock.Scripted = false;
                Physics.simulationMode = SimulationMode.FixedUpdate;

                if (director != null) director.enabled = false;
                if (competitors != null)
                    foreach (var c in competitors) if (c != null) c.StopPainting();

                // Rivals cannot be allowed to decide the answer.
                var mine = mower.GetComponentsInChildren<Collider>(true);
                if (competitors != null && mine != null)
                {
                    foreach (var c in competitors)
                    {
                        if (c == null || c == player || c.mower == null) continue;
                        var theirs = c.mower.GetComponentsInChildren<Collider>(true);
                        if (theirs == null) continue;
                        foreach (var a in mine)
                        {
                            if (a == null || !a.enabled || !a.gameObject.activeInHierarchy) continue;
                            foreach (var b in theirs)
                            {
                                if (b == null || !b.enabled || !b.gameObject.activeInHierarchy) continue;
                                Physics.IgnoreCollision(a, b, true);
                                ignoredA.Add(a);
                                ignoredB.Add(b);
                            }
                        }
                    }
                }

                mower.inputSource = new FullThrottle();
                Time.timeScale = ProbeTimeScale;

                Debug.Log($"[Bloom QA] reachability probe: {azimuths} bearings, player found via {how}, " +
                          $"starting each run on the loop at {ReachStart:0.0} m at full throttle. " +
                          $"Time scale {ProbeTimeScale:0} for the duration.");

                for (int i = 0; i < azimuths; i++)
                {
                    float deg = i * 360f / azimuths;
                    Vector3 outward = TurfArena.Bearing(deg);
                    var sample = new ReachSample { azimuth = deg };

                    Vector3 start = outward * ReachStart;
                    start.y = SpawnHeight;
                    // ParkAt rather than ResetTo: it places the rigidbody properly and stops it dead
                    // without wiping the competitor's running totals for the match.
                    mower.ParkAt(start, Quaternion.LookRotation(outward, Vector3.up));

                    yield return _fixedStep;

                    // Handed the top speed it would have arrived with, so a soft push-back is being
                    // asked to stop a machine at full pelt rather than one still pulling away.
                    rb.linearVelocity = outward * mower.maxSpeed;

                    float best = Radius(rb.position);
                    int stalled = 0;
                    int step = 0;

                    for (; step < ReachMaxSteps; step++)
                    {
                        yield return _fixedStep;

                        float r = Radius(rb.position);
                        if (r > best + 0.01f) { best = r; stalled = 0; }
                        else stalled++;

                        if (r > WallRadius + 2f) { sample.note = "LEFT THE ARENA"; break; }
                        if (stalled >= StallSteps) { sample.note = "stopped advancing"; break; }
                    }

                    sample.steps = step;
                    sample.maxRadius = best;
                    sample.endRadius = Radius(rb.position);
                    if (sample.note.Length == 0) sample.note = "step budget spent";
                    samples.Add(sample);
                }
            }
            finally
            {
                Time.timeScale = prevTimeScale;
                mower.inputSource = prevInput;

                for (int i = 0; i < ignoredA.Count; i++)
                {
                    var a = ignoredA[i];
                    var b = ignoredB[i];
                    if (a == null || b == null) continue;
                    if (!a.enabled || !b.enabled) continue;
                    if (!a.gameObject.activeInHierarchy || !b.gameObject.activeInHierarchy) continue;
                    Physics.IgnoreCollision(a, b, false);
                }

                mower.ParkAt(homePos, homeRot);
                rb.linearVelocity = homeVel;
                rb.angularVelocity = homeAngVel;

                if (director != null) director.enabled = prevDirector;
                if (wasLive && competitors != null)
                    foreach (var c in competitors) if (c != null) c.BeginPainting();

                SimClock.Scripted = prevScripted;
                Physics.simulationMode = prevSim;
            }

            var sb = new StringBuilder();
            ReachText(sb, samples);
            Debug.Log(sb.ToString());

            done?.Invoke();
        }

        static void ReachText(StringBuilder sb, List<ReachSample> samples)
        {
            sb.AppendLine("[Bloom QA] REACHABILITY PROBE");
            sb.AppendLine($"  the player's mower, full throttle outward from {ReachStart:0.0} m, " +
                          $"{samples.Count} bearings, at most {ReachMaxSteps} fixed steps each " +
                          $"({ReachMaxSteps * Time.fixedDeltaTime:0.0} s).");
            sb.AppendLine($"  reach target {ReachTarget:0.00} m — anything short of it is a FAILURE. " +
                          $"wall {WallRadius:0.0} m — anything past it is also a FAILURE.");
            sb.AppendLine();
            sb.AppendLine("   deg |    max |    end | steps | outcome");
            sb.AppendLine("  -----+--------+--------+-------+--------------------------------");

            float min = float.PositiveInfinity, max = float.NegativeInfinity, sum = 0f;
            foreach (var s in samples)
            {
                min = Mathf.Min(min, s.maxRadius);
                max = Mathf.Max(max, s.maxRadius);
                sum += s.maxRadius;
                string flag = s.maxRadius < ReachTarget ? "  << SHORT" :
                              s.maxRadius > WallRadius ? "  << ESCAPED" : "";
                sb.AppendLine($"  {s.azimuth,4:0} | {s.maxRadius,6:0.00} | {s.endRadius,6:0.00} | " +
                              $"{s.steps,5} | {s.note}{flag}");
            }

            sb.AppendLine();
            if (samples.Count == 0)
            {
                sb.AppendLine("  SUMMARY: no bearings sampled.");
                return;
            }

            sb.AppendLine($"  SUMMARY: min {min:0.00}  max {max:0.00}  mean {sum / samples.Count:0.00}  " +
                          $"over {samples.Count} bearings.");

            var stoppedShort = new List<ReachSample>();
            var over = new List<ReachSample>();
            foreach (var s in samples)
            {
                if (s.maxRadius < ReachTarget) stoppedShort.Add(s);
                if (s.maxRadius > WallRadius) over.Add(s);
            }

            if (stoppedShort.Count == 0)
                sb.AppendLine($"    every bearing reached {ReachTarget:0.00} m — the wall, or the touchline if it is nearer.");
            else
            {
                sb.AppendLine($"    {stoppedShort.Count} BEARING(S) NEVER REACHED THE TOUCHLINE:");
                foreach (var s in stoppedShort)
                    sb.AppendLine($"      {s.azimuth,4:0} deg  stopped at {s.maxRadius:0.00} m  " +
                                  $"({ReachTarget - s.maxRadius:0.00} m short)  [{s.note}]");
            }

            if (over.Count == 0)
                sb.AppendLine($"    nothing got past the {WallRadius:0.0} m wall.");
            else
            {
                sb.AppendLine($"    {over.Count} BEARING(S) LEFT THE ARENA:");
                foreach (var s in over)
                    sb.AppendLine($"      {s.azimuth,4:0} deg  reached {s.maxRadius:0.00} m  [{s.note}]");
            }
        }

        // ==================================================================== the report

        static void Finish(List<AzimuthSweep> rows, List<ReachSample> samples, string how)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BLOOM RUSH — BOUNDARY REPORT");
            sb.AppendLine($"  {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}   scene " +
                          $"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}   " +
                          $"play mode {(Application.isPlaying ? "yes" : "no")}   player: {how}");
            sb.AppendLine("  arena: core 8, plaza 19, hedge 30-33, loop mid 37.5, touchline 42, " +
                          $"barrier {TurfArena.BarrierRadius:0.0}, wall colliders {WallRadius:0.0}.");
            sb.AppendLine();

            SweepText(sb, rows);
            sb.AppendLine();

            if (samples != null && samples.Count > 0)
            {
                ReachText(sb, samples);
                sb.AppendLine();
            }

            // ---- the three criteria ----
            bool haveReach = samples != null && samples.Count > 0;

            int shortCount = 0, escapeCount = 0;
            if (haveReach)
                foreach (var s in samples)
                {
                    if (s.maxRadius < ReachTarget) shortCount++;
                    if (s.maxRadius > WallRadius) escapeCount++;
                }

            var strays = Strays(rows);

            string v1 = !haveReach ? "NOT RUN"
                      : shortCount == 0 ? "PASS" : "FAIL";
            string v2 = !haveReach ? "NOT RUN"
                      : escapeCount == 0 ? "PASS" : "FAIL";
            string v3 = strays.Count == 0 ? "PASS" : "FAIL";

            var verdict = new StringBuilder();
            verdict.AppendLine("VERDICT");
            verdict.AppendLine($"  [{v1}] every bearing reaches the {ReachTarget:0.00} m reach target" +
                               (haveReach ? $"  ({samples.Count - shortCount}/{samples.Count})"
                                          : "  (needs play mode)"));
            verdict.AppendLine($"  [{v2}] no bearing gets past the {WallRadius:0.0} m wall" +
                               (haveReach ? $"  ({escapeCount} escape(s))" : "  (needs play mode)"));
            verdict.AppendLine($"  [{v3}] no unexplained collider inside 44 m outside the hedge ring at " +
                               $"30-33  ({strays.Count} found)");

            string verdictText = verdict.ToString();
            sb.Append(verdictText);

            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, $"boundary_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());

            bool anyFail = v1 == "FAIL" || v2 == "FAIL" || v3 == "FAIL";
            string echo = $"[Bloom QA] boundary report written to {path}\n" + verdictText;
            if (anyFail) Debug.LogError(echo);
            else Debug.Log(echo);
        }

        // ==================================================================== plumbing

        static bool SceneIsBloomRush()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.path == DuckTurfBuilder.ScenePath) return true;

            string what = string.IsNullOrEmpty(active.path) ? active.name : active.path;
            Debug.LogWarning($"[Bloom QA] the open scene is '{what}', not {DuckTurfBuilder.ScenePath}. " +
                             "Open Bloom Rush (File > Open Scene, or rebuild it with " +
                             "Duck/4 · Build bloom rush scene) and run this again.");
            return false;
        }
    }
}
