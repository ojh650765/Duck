using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Runs the game deterministically from the editor, stepping every system by hand instead of
    /// waiting for Unity's player loop.
    ///
    /// An unfocused Unity editor advances play mode about once every five seconds, which makes
    /// every timing-dependent check — the round clock, the autopilot, the reveal choreography —
    /// impossible to test unattended. Stepping the loop ourselves fixes that and, as a bonus,
    /// simulates a whole 90-second round in about a second, so a full review pass is cheap.
    ///
    /// Step order mirrors Unity's own: input, fixed physics, gameplay, then visuals and camera.
    /// </summary>
    public static class DuckSimulator
    {
        const string CaptureDir = "Captures";
        const float FixedStep = 1f / 60f;

        class Rig
        {
            public GameDirector director;
            public InputReader input;
            public Autopilot autopilot;
            public MowerController mower;
            public MowerVisuals visuals;
            public CutMask cutMask;
            public GrassField grass;
            public CameraDirector camera;
            public JudgePanel judges;
            public SpectatorCrowd crowd;
            public Windmill[] windmills;
            public HUD hud;
            public MowerVFX vfx;
            public AudioDirector audio;
            public Tournament tournament;
            public NeighbourAmbience ambience;
            public Camera cam;
        }

        static Rig Collect()
        {
            var r = new Rig
            {
                director = Object.FindFirstObjectByType<GameDirector>(),
                input = Object.FindFirstObjectByType<InputReader>(),
                autopilot = Object.FindFirstObjectByType<Autopilot>(),
                mower = Object.FindFirstObjectByType<MowerController>(),
                visuals = Object.FindFirstObjectByType<MowerVisuals>(),
                cutMask = Object.FindFirstObjectByType<CutMask>(),
                grass = Object.FindFirstObjectByType<GrassField>(),
                camera = Object.FindFirstObjectByType<CameraDirector>(),
                judges = Object.FindFirstObjectByType<JudgePanel>(),
                crowd = Object.FindFirstObjectByType<SpectatorCrowd>(),
                windmills = Object.FindObjectsByType<Windmill>(FindObjectsSortMode.None),
                hud = Object.FindFirstObjectByType<HUD>(),
                vfx = Object.FindFirstObjectByType<MowerVFX>(),
                audio = Object.FindFirstObjectByType<AudioDirector>(),
                tournament = Object.FindFirstObjectByType<Tournament>(),
                ambience = Object.FindFirstObjectByType<NeighbourAmbience>(),
                cam = Camera.main
            };
            return r;
        }

        static void BeginScripted()
        {
            SimClock.Scripted = true;
            SimClock.ElapsedScripted = 0f;
            Physics.simulationMode = SimulationMode.Script;
        }

        static void EndScripted()
        {
            SimClock.Scripted = false;
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        /// <summary>Advance the game by one fixed step.</summary>
        static void Step(Rig r, float dt)
        {
            r.input?.Tick(dt);
            r.autopilot?.TickPhysics(dt);
            r.mower?.TickPhysics(dt);

            Physics.Simulate(dt);

            r.director?.Tick(dt);
            r.judges?.Tick(dt);
            r.visuals?.Tick(dt);
            r.vfx?.Tick(dt);
            r.crowd?.Tick(dt);
            if (r.windmills != null) foreach (var w in r.windmills) w?.Tick(dt);

            // The mask blit is the expensive part of a step, and a scripted round runs every step
            // of ninety seconds inside a single editor frame — thousands of blits with no present
            // in between, which is over the driver's watchdog and takes the device down with it.
            // Under the scripted clock the mask only has to be correct when something looks at it,
            // so batch the uploads. The CPU grid that scoring reads is untouched by this.
            if ((_stepsSinceFlush & 3) == 0) r.cutMask?.Flush();
            r.grass?.TickLod(dt);
            r.camera?.Tick(dt);
            r.hud?.Tick(dt);
            r.audio?.Tick(dt);

            SimClock.ElapsedScripted += dt;

            // Every step stamps into the cut mask, and the whole scripted round happens inside a
            // single editor frame — so without this the driver is handed one command list holding
            // thousands of blits and no present, and the GPU watchdog resets the device mid-run.
            // That is a hard editor crash, and it looks exactly like a bug in the game. Flushing
            // periodically keeps each submission small enough to complete.
            if (++_stepsSinceFlush >= 32) { _stepsSinceFlush = 0; GL.Flush(); }
        }

        static int _stepsSinceFlush;

        static void Advance(Rig r, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / FixedStep));
            for (int i = 0; i < steps; i++) Step(r, FixedStep);
        }

        /// <summary>Advance until a predicate holds, with a hard step budget so it cannot hang.</summary>
        static bool AdvanceUntil(Rig r, System.Func<bool> done, float maxSeconds = 30f)
        {
            int budget = Mathf.RoundToInt(maxSeconds / FixedStep);
            for (int i = 0; i < budget; i++)
            {
                if (done()) return true;
                Step(r, FixedStep);
            }
            return done();
        }

        // ------------------------------------------------------------------ capture

        static RenderTexture _shotRt;
        static Texture2D _shotTex;

        /// <summary>
        /// The capture target is kept alive between shots. Allocating and releasing a render
        /// texture fourteen times inside one editor frame, each followed by a full readback, is
        /// what pushed the device over the edge; reusing one costs nothing and asks far less of
        /// the driver.
        /// </summary>
        static void EnsureShotTargets(int w, int h)
        {
            if (_shotRt != null && (_shotRt.width != w || _shotRt.height != h))
            {
                _shotRt.Release(); Object.DestroyImmediate(_shotRt); _shotRt = null;
                Object.DestroyImmediate(_shotTex); _shotTex = null;
            }
            if (_shotRt == null)
                _shotRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            if (_shotTex == null)
                _shotTex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
        }

        public static void Shot(Camera cam, string name, int w = 1280, int h = 720)
        {
            if (cam == null) return;
            // Steps batch their mask uploads; a frame that is going to be looked at must not.
            Object.FindFirstObjectByType<CutMask>()?.Flush();
            Directory.CreateDirectory(CaptureDir);

            EnsureShotTargets(w, h);
            var rt = _shotRt;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            bool prevEnabled = cam.enabled;
            cam.enabled = true;

            var tex = _shotTex;

            // Render, then check the result actually contains an image. Occasionally the first
            // Render into a fresh target comes back empty, which silently produced pure black
            // frames in the middle of a capture set - worse than no frame, because a reviewer
            // reads it as a rendering bug in the game itself.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0.55f, 0.71f, 0.81f, 1f));
                RenderTexture.active = null;

                cam.targetTexture = rt;
                cam.Render();
                // Drain before the readback. A readback that waits on a fence behind a very deep
                // command list is what the device-removal crash landed in.
                GL.Flush();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = null;

                if (!LooksBlank(tex)) break;
            }

            cam.targetTexture = prevTarget;
            cam.enabled = prevEnabled;
            RenderTexture.active = prevActive;

            File.WriteAllBytes(Path.Combine(CaptureDir, name + ".png"), tex.EncodeToPNG());
        }

        /// <summary>Cheap check for a frame that contains essentially one colour.</summary>
        static bool LooksBlank(Texture2D tex)
        {
            var px = tex.GetPixels32();
            if (px.Length == 0) return true;
            var first = px[0];
            int differing = 0;
            for (int i = 0; i < px.Length; i += 313)
            {
                var c = px[i];
                if (Mathf.Abs(c.r - first.r) + Mathf.Abs(c.g - first.g) + Mathf.Abs(c.b - first.b) > 12)
                    if (++differing > 24) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ menu entries

        [MenuItem("Duck/Sim · Run round + frame sheet", priority = 10)]
        public static void RunRoundAndCapture() => RunRound(ShapeId.Duckling, true);

        [MenuItem("Duck/Sim · Run round (no capture)", priority = 11)]
        public static void RunRoundQuiet() => RunRound(ShapeId.Duckling, false);

        public static void RunRound(ShapeId shape, bool capture, string prefix = null)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Duck] Enter play mode first (the systems need their Awake to have run).");
                return;
            }

            var r = Collect();
            if (r.director == null) { Debug.LogError("[Duck] No GameDirector in scene."); return; }

            string stamp = prefix ?? System.DateTime.Now.ToString("HHmmss");
            var log = new System.Text.StringBuilder();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            bool wasAutopilot = r.director.autopilotEnabled;
            BeginScripted();
            try
            {
                r.director.autopilotEnabled = true;
                r.director.DebugSetShape(shape);
                r.camera?.SnapToCurrent();

                // --- briefing ---
                Advance(r, 1.2f);
                if (capture) Shot(r.cam, $"{stamp}_01_briefing");
                AdvanceUntil(r, () => r.director.State != GameState.Briefing, 12f);

                // --- countdown ---
                Advance(r, 1.0f);
                if (capture) Shot(r.cam, $"{stamp}_02_countdown");
                AdvanceUntil(r, () => r.director.State != GameState.Countdown, 12f);

                // --- mowing ---
                Advance(r, 4.0f);
                if (capture) Shot(r.cam, $"{stamp}_03_mow_early");
                log.AppendLine($"  after 4s mowing: cells={r.cutMask?.CutCellCount} " +
                               $"dist={r.mower?.DistanceTravelled:0.0} m speed={r.mower?.ForwardSpeed:0.0} " +
                               $"waypoint={r.autopilot?.WaypointIndex}/{r.autopilot?.WaypointCount}");

                Advance(r, 16f);
                if (capture) Shot(r.cam, $"{stamp}_04_mow_mid");

                // Let the autopilot finish its pattern, or the clock run out.
                AdvanceUntil(r, () =>
                    r.director.State != GameState.Mowing ||
                    (r.autopilot != null && r.autopilot.WaypointCount > 0 &&
                     r.autopilot.WaypointIndex >= r.autopilot.WaypointCount), 140f);

                if (capture) Shot(r.cam, $"{stamp}_05_mow_done");
                log.AppendLine($"  mowing done: cells={r.cutMask?.CutCellCount} dist={r.mower?.DistanceTravelled:0.0} m " +
                               $"waypoint={r.autopilot?.WaypointIndex}/{r.autopilot?.WaypointCount} " +
                               $"timeLeft={r.director.TimeRemaining:0.0}");

                // --- klaxon ---
                r.director.DebugSetTimeRemaining(0.01f);
                AdvanceUntil(r, () => r.director.State == GameState.Klaxon, 5f);
                Advance(r, 1.0f);
                if (capture) Shot(r.cam, $"{stamp}_06_klaxon");

                // --- reveal, three beats ---
                AdvanceUntil(r, () => r.director.State == GameState.Reveal, 6f);
                // Wait out the 2.4 s camera lift so the hero frame is the settled overhead shot.
                Advance(r, 2.7f);
                if (capture) Shot(r.cam, $"{stamp}_07_reveal_picture");
                Advance(r, 1.7f);
                if (capture) Shot(r.cam, $"{stamp}_08_reveal_ghost");
                Advance(r, 1.9f);
                if (capture) Shot(r.cam, $"{stamp}_09_reveal_analysis");

                // --- judging ---
                AdvanceUntil(r, () => r.director.State == GameState.Judging, 10f);
                Advance(r, 1.0f);
                if (capture) Shot(r.cam, $"{stamp}_10_judges_lean");
                Advance(r, 2.6f);
                if (capture) Shot(r.cam, $"{stamp}_11_judges_card1");
                Advance(r, 3.0f);
                if (capture) Shot(r.cam, $"{stamp}_12_judges_card2");
                Advance(r, 3.4f);
                if (capture) Shot(r.cam, $"{stamp}_13_judges_card3");

                AdvanceUntil(r, () => r.director.State == GameState.Verdict, 20f);
                Advance(r, 0.8f);
                if (capture) Shot(r.cam, $"{stamp}_14_verdict");

                var s = r.director.LastScore;
                log.Insert(0, $"[Duck] SIM ROUND {shape} ({watch.ElapsedMilliseconds} ms, " +
                              $"{SimClock.ElapsedScripted:0.0} s simulated)\n");
                log.AppendLine($"  score: coverage={s.coverage:0.000} spill={s.spill:0.000} " +
                               $"accuracy={s.accuracy:0.000} edge={s.edgeQuality:0.000} style={s.style:0.000} " +
                               $"mown={s.mownArea:0} m2 of {r.director.target.TargetAreaSqm:0} m2 target");
                if (r.judges != null)
                    log.AppendLine($"  judges: {r.judges.Scores[0]} / {r.judges.Scores[1]} / {r.judges.Scores[2]} " +
                                   $"= {r.judges.Total} rank {r.judges.Rank}");
                log.AppendLine($"  final state = {r.director.State}");
                Debug.Log(log.ToString());
            }
            finally
            {
                // Hand the game back exactly as it was found. Leaving the autopilot on meant the
                // next round the player started drove itself.
                r.director.autopilotEnabled = wasAutopilot;
                if (!wasAutopilot) r.autopilot?.Stop();
                EndScripted();
            }
        }

        /// <summary>
        /// Skips the round and photographs only the judging and verdict beats.
        ///
        /// The full sheet simulates ninety seconds of mowing to reach the part of the show that
        /// most edits are actually about — the desk cards, the close-ups, the results panel. This
        /// mows for eight seconds so there is something to score, then cuts straight to the bench.
        /// </summary>
        [MenuItem("Duck/Sim · Capture judging only", priority = 12)]
        public static void CaptureJudging()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Duck] Enter play mode first.");
                return;
            }

            var r = Collect();
            if (r.director == null) { Debug.LogError("[Duck] No GameDirector in scene."); return; }

            string stamp = "judge";
            bool wasAutopilot = r.director.autopilotEnabled;
            BeginScripted();
            try
            {
                r.director.autopilotEnabled = true;
                r.director.DebugSetShape(ShapeId.Duckling);
                r.camera?.SnapToCurrent();

                AdvanceUntil(r, () => r.director.State == GameState.Mowing, 20f);
                Advance(r, 8f);

                r.director.DebugSetTimeRemaining(0.01f);
                AdvanceUntil(r, () => r.director.State == GameState.Klaxon, 5f);
                Advance(r, 1.0f);
                Shot(r.cam, $"{stamp}_01_klaxon");

                AdvanceUntil(r, () => r.director.State == GameState.Reveal, 6f);
                Advance(r, 2.7f);
                Shot(r.cam, $"{stamp}_02_reveal");

                AdvanceUntil(r, () => r.director.State == GameState.Judging, 12f);
                Advance(r, 0.5f);
                Shot(r.cam, $"{stamp}_03_bench");

                // Shoot each judge on the frame their card finishes standing, not on a stopwatch.
                // Fixed offsets drifted a whole beat out of step, so the close-ups kept landing on
                // the next judge while the previous judge's number was the one on screen.
                for (int i = 0; i < 3; i++)
                {
                    int which = i;
                    AdvanceUntil(r, () =>
                    {
                        var c = r.judges != null && which < r.judges.judges.Length
                              ? r.judges.judges[which]?.character : null;
                        return c != null && c.CardUp >= 0.999f;
                    }, 14f);
                    Advance(r, 0.35f);
                    Shot(r.cam, $"{stamp}_0{4 + i}_card{i + 1}");

                    var jc = r.judges.judges[which]?.character;
                    Debug.Log($"[Duck] card{which} panelIndex={r.judges.CurrentJudge} " +
                              $"subject={jc?.name} at x={jc?.transform.position.x:0.00} " +
                              $"cam={r.cam.transform.position} aim={r.cam.transform.forward} " +
                              $"cardUp=[{Up(r,0):0.00},{Up(r,1):0.00},{Up(r,2):0.00}]");
                }

                AdvanceUntil(r, () => r.director.State == GameState.Verdict, 20f);
                Advance(r, 1.2f);
                Shot(r.cam, $"{stamp}_07_verdict");

                Debug.Log($"[Duck] Judging sheet written. state={r.director.State} " +
                          $"judges={r.judges?.Scores[0]}/{r.judges?.Scores[1]}/{r.judges?.Scores[2]}");
            }
            finally
            {
                // Hand the game back exactly as it was found. Leaving the autopilot on meant the
                // next round the player started drove itself.
                r.director.autopilotEnabled = wasAutopilot;
                if (!wasAutopilot) r.autopilot?.Stop();
                EndScripted();
            }
        }

        static float Up(Rig r, int i)
        {
            var c = r.judges != null && i < r.judges.judges.Length ? r.judges.judges[i]?.character : null;
            return c != null ? c.CardUp : -1f;
        }

        /// <summary>
        /// The championship endgame: mow a while, close the round, then follow the camera round the
        /// venue and onto the scoreboard. This is the sequence the whole expansion exists for, so it
        /// gets its own capture rather than being the tail of the full sheet.
        /// </summary>
        [MenuItem("Duck/Sim · Capture venue finale", priority = 15)]
        public static void CaptureFinale()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Duck] Enter play mode first."); return; }

            var r = Collect();
            if (r.director == null) { Debug.LogError("[Duck] No GameDirector in scene."); return; }

            bool wasAutopilot = r.director.autopilotEnabled;
            BeginScripted();
            try
            {
                r.director.autopilotEnabled = true;
                r.director.DebugSetShape(ShapeId.Heart);
                r.camera?.SnapToCurrent();

                AdvanceUntil(r, () => r.director.State == GameState.Mowing, 20f);
                // Long enough that every contestant has laid down a recognisable picture.
                Advance(r, 30f);
                Shot(r.cam, "finale_01_mowing");

                r.director.DebugSetTimeRemaining(0.01f);
                AdvanceUntil(r, () => r.director.State == GameState.Reveal, 8f);
                Advance(r, 2.8f);
                Shot(r.cam, "finale_02_player_reveal");

                AdvanceUntil(r, () => r.director.State == GameState.Verdict, 40f);
                Advance(r, 1.5f);
                Shot(r.cam, "finale_03_player_verdict");

                AdvanceUntil(r, () => r.director.State == GameState.VenueTour, 20f);
                for (int i = 0; i < 4; i++)
                {
                    // Wait for the camera to actually settle over the plot before photographing it.
                    AdvanceUntil(r, () => r.camera != null && r.camera.TourArrived, 14f);
                    Advance(r, 0.9f);
                    Shot(r.cam, $"finale_0{4 + i}_tour_{i}");
                    Advance(r, 2.4f);
                }

                AdvanceUntil(r, () => r.director.State == GameState.Scoreboard, 25f);
                AdvanceUntil(r, () => r.camera != null && r.camera.ScoreboardArrived, 14f);
                Advance(r, 1.0f);
                Shot(r.cam, "finale_08_board_arrive");
                Advance(r, 3.0f);
                Shot(r.cam, "finale_09_board_sorted");
                Advance(r, 2.5f);
                Shot(r.cam, "finale_10_board_winner");

                var log = new System.Text.StringBuilder("[Duck] VENUE FINALE\n");
                if (r.tournament != null)
                {
                    foreach (var st in r.tournament.Standings)
                        log.AppendLine($"  {st.name,-9} {st.total,5:0} / 30  rank {st.rank}{(st.isPlayer ? "   <- player" : "")}");
                    log.AppendLine($"  player placed {r.tournament.PlayerPlace}");
                    foreach (var rv in r.tournament.rivals)
                        if (rv != null)
                            log.AppendLine($"  {rv.displayName}: shape={rv.Shape} coverage={rv.Score.coverage:0.00} " +
                                           $"spill={rv.Score.spill:0.00} edge={rv.Score.edgeQuality:0.00} marks=" +
                                           $"{rv.Marks[0]:0}/{rv.Marks[1]:0}/{rv.Marks[2]:0}");
                }
                log.AppendLine($"  final state = {r.director.State}");
                Debug.Log(log.ToString());
            }
            finally
            {
                r.director.autopilotEnabled = wasAutopilot;
                if (!wasAutopilot) r.autopilot?.Stop();
                EndScripted();
            }
        }

        /// <summary>
        /// One frame of the countdown for every picture, so the start pose can be judged by eye
        /// rather than only by the numbers the audit prints.
        /// </summary>
        [MenuItem("Duck/Sim · Capture every start pose", priority = 16)]
        public static void CaptureStarts()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Duck] Enter play mode first."); return; }

            var r = Collect();
            if (r.director == null) { Debug.LogError("[Duck] No GameDirector in scene."); return; }

            bool wasAutopilot = r.director.autopilotEnabled;
            BeginScripted();
            try
            {
                r.director.autopilotEnabled = false;
                foreach (var shape in TargetShapes.All)
                {
                    r.director.DebugSetShape(shape);
                    // Straight past the briefing to the countdown, where the mower is parked on its
                    // start pose and has not moved yet.
                    AdvanceUntil(r, () => r.director.State == GameState.Countdown, 12f);
                    Advance(r, 0.6f);
                    r.camera?.SnapToCurrent();
                    Advance(r, 0.2f);
                    Shot(r.cam, $"start_{shape}");

                    // And straight down, because the chase camera cannot answer the question that
                    // matters: is the mower inside the outline, or is the outline simply ahead of
                    // it? From above, both are in the same frame and there is nothing to argue about.
                    var cam = r.cam;
                    var prevPos = cam.transform.position;
                    var prevRot = cam.transform.rotation;
                    float prevFov = cam.fieldOfView;
                    Vector3 m = r.mower != null ? r.mower.transform.position : Vector3.zero;

                    cam.transform.SetPositionAndRotation(new Vector3(0f, 78f, 0f),
                        Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    cam.fieldOfView = 52f;
                    Shot(cam, $"start_{shape}_overhead");

                    cam.transform.SetPositionAndRotation(prevPos, prevRot);
                    cam.fieldOfView = prevFov;
                    Debug.Log($"[Duck] {shape} start at ({m.x:0.0}, {m.z:0.0})");
                }
                Debug.Log("[Duck] Start pose frames written to Captures/.");
            }
            finally
            {
                r.director.autopilotEnabled = wasAutopilot;
                if (!wasAutopilot) r.autopilot?.Stop();
                EndScripted();
            }
        }

        [MenuItem("Duck/Sim · Mow 20s and capture", priority = 12)]
        public static void QuickMow()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            var r = Collect();
            if (r.director == null) return;

            bool wasAutopilot = r.director.autopilotEnabled;
            BeginScripted();
            try
            {
                r.director.autopilotEnabled = true;
                r.director.DebugSetShape(ShapeId.Heart);
                r.camera?.SnapToCurrent();
                AdvanceUntil(r, () => r.director.State == GameState.Mowing, 15f);
                Advance(r, 20f);
                string stamp = System.DateTime.Now.ToString("HHmmss");
                Shot(r.cam, $"{stamp}_quickmow_chase");

                // Overhead too, so the cut pattern can be judged directly.
                var cam = r.cam;
                var prevPos = cam.transform.position;
                var prevRot = cam.transform.rotation;
                float prevFov = cam.fieldOfView;
                cam.transform.position = new Vector3(0f, 92f, -9f);
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -92f, 9f).normalized, Vector3.forward);
                cam.fieldOfView = 42f;
                Shot(cam, $"{stamp}_quickmow_overhead");
                cam.transform.SetPositionAndRotation(prevPos, prevRot);
                cam.fieldOfView = prevFov;

                Debug.Log($"[Duck] quick mow: cells cut={r.cutMask?.CutCellCount} " +
                          $"({100f * (r.cutMask?.CutCellCount ?? 0) / (Field.GridRes * Field.GridRes):0.0}%), " +
                          $"distance={r.mower?.DistanceTravelled:0.0} m, " +
                          $"waypoints {r.autopilot?.WaypointIndex}/{r.autopilot?.WaypointCount}");
            }
            finally { EndScripted(); }
        }
    }
}
