using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Contact sheets for the rally, in both modes.
    ///
    /// The edit-mode survey answers "is this arena composed" — eight fixed viewpoints, no play mode,
    /// so a layout change can be judged in seconds. The play-mode sheet answers the harder question,
    /// "does a parry read", and it is EVENT-DRIVEN rather than timed: it subscribes to the match's
    /// own strike, knockout and breach events and captures on the frame after each one. Capturing on
    /// a stopwatch is what produces sheets full of empty grass with a caption claiming a knockout.
    ///
    /// Both render through a RenderTexture rather than ScreenCapture, which needs a focused Game View
    /// — the difference between a rig that works unattended and one that silently produces nothing.
    /// </summary>
    public static class DuckRallyShots
    {
        const string Dir = "Captures/Rally";
        const int W = 1600, H = 900;

        // ------------------------------------------------------------------ edit mode

        [MenuItem("Duck/Rally · Arena survey (no play mode)", priority = 33)]
        public static void Survey()
        {
            if (!EnsureSceneOpen()) return;
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");

            float r = RallyArena.Reach;
            Pose("01_overhead", new Vector3(0f, 96f, -34f), Vector3.zero, 46f, stamp);
            Pose("02_three_quarter", new Vector3(-52f, 34f, -62f), Vector3.zero, 48f, stamp);

            // The shot that actually matters: the player's own eye line, from their dirt, looking
            // across at the three opponents. If the arena does not read from here it does not read.
            var me = RallyArena.Get(0);
            Pose("03_player_eyeline", me.bandCentre - me.inward * 7f + Vector3.up * 3.4f,
                 Vector3.zero + Vector3.up * 1.2f, 54f, stamp);
            Pose("04_player_garden", me.gardenCentre - me.outward * 17f + Vector3.up * 6f,
                 me.gardenCentre + Vector3.up * 0.6f, 46f, stamp);
            Pose("05_fence_close", me.fenceCentre - me.outward * 5.5f + Vector3.up * 1.5f,
                 me.fenceCentre + Vector3.up * 0.5f, 40f, stamp);

            var across = RallyArena.Get(2);
            Pose("06_across", me.bandCentre + Vector3.up * 2.6f,
                 across.gardenCentre + Vector3.up * 1f, 50f, stamp);

            var left = RallyArena.Get(3);
            Pose("07_neighbour", left.bandCentre - left.inward * 9f + Vector3.up * 4f,
                 left.gardenCentre + Vector3.up * 1f, 50f, stamp);

            Pose("08_barrier_low", new Vector3(0f, 1.6f, -(RallyArena.ArenaRadius + 1f)),
                 new Vector3(0f, 3f, r), 55f, stamp);

            Debug.Log($"[Rally] arena survey {stamp} written to {Dir}/.");
            AssetDatabase.Refresh();
        }

        static bool EnsureSceneOpen()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.path == DuckRallyBuilder.ScenePath) return true;
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Rally] not the rally scene, and play mode is running.");
                return false;
            }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DuckRallyBuilder.ScenePath);
            return true;
        }

        static void Pose(string name, Vector3 pos, Vector3 lookAt, float fov, string stamp)
        {
            var go = new GameObject("~ shot cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up));

            Render(cam, $"{stamp}_{name}");
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ play mode

        /// <summary>
        /// Put a bot on the player's machine for the next play session.
        ///
        /// Without it a review run is four minutes of the player's undefended garden absorbing every
        /// redirect in the arena, which exercises exactly one of the mode's rules. The flag is static
        /// and lives on the component, so it cannot be saved into the scene — see
        /// RallyDirector.DebugAutopilotPlayer.
        /// </summary>
        [MenuItem("Duck/Rally · Autopilot the player (next play)", priority = 35)]
        public static void AutopilotPlayer()
        {
            RallyDirector.DebugAutopilotPlayer = !RallyDirector.DebugAutopilotPlayer;
            Debug.Log($"[Rally] player autopilot {(RallyDirector.DebugAutopilotPlayer ? "ON" : "OFF")} " +
                      "for the next play session.");
        }

        /// <summary>
        /// From a round in progress, go to the arena now.
        ///
        /// The rally is the LAST beat of the LAST round of a championship, which means reaching it
        /// honestly costs three full rounds — and every pass over the handover, the sleeping scene,
        /// the results crossing back and the reveal that follows has to start by getting there. That
        /// is not a review process.
        /// </summary>
        [MenuItem("Duck/Rally · Jump to the rally (from a round)", priority = 36)]
        public static void JumpToRally()
        {
            var d = Object.FindFirstObjectByType<GameDirector>();
            if (d == null) { Debug.LogWarning("[Rally] enter play mode on Main first."); return; }
            d.rallyEnabled = true;
            d.rallyOnRound = 0;          // any round, for review

            d.DebugSetTimeRemaining(0.01f);
            d.DebugForceState(GameState.Klaxon);
            Debug.Log("[Rally] klaxon forced; the arena is next.");
        }

        /// <summary>
        /// One frame INCLUDING the HUD.
        ///
        /// Every other capture here renders a camera into a RenderTexture, and that path cannot see a
        /// Screen Space - Overlay canvas — overlay UI is composited after all cameras have drawn. So
        /// the whole review loop was reporting a game with no HUD on it, and I spent a round of
        /// debugging looking for a HUD that had never been missing. ScreenCapture composites the real
        /// back buffer, which is the only way to photograph what the player is actually looking at.
        /// </summary>
        [MenuItem("Duck/Rally · Screenshot with UI (play mode)", priority = 37)]
        public static void ShotWithUI()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }
            Directory.CreateDirectory(Dir);
            string name = $"ui_{System.DateTime.Now:HHmmss}.png";
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name), 2);
            Debug.Log($"[Rally] {name} queued — it lands at the end of this frame.");
        }

        /// <summary>
        /// Eight frames through one parry, from a camera parked on the contact point.
        ///
        /// The match sheet answers "did a parry happen". This answers the question the mode actually
        /// lives or dies on — "is a parry WORTH watching" — and it has to be its own tool because
        /// the whole event is over in about a second, most of it inside a hit stop. Frames are taken
        /// on UNSCALED time and at a fixed cadence, so the freeze is photographed rather than
        /// skipped: a sheet shot on scaled time takes almost every frame from the two hundred
        /// milliseconds either side of the stop and none from inside it, which is exactly the part
        /// being tuned.
        /// </summary>
        [MenuItem("Duck/Rally · Parry close-up sequence (play mode)", priority = 41)]
        public static void ParrySequence()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }
            var d = Object.FindFirstObjectByType<RallyDirector>();
            if (d == null) { Debug.LogWarning("[Rally] no director."); return; }
            d.StartCoroutine(ParryRoutine(d));
            Debug.Log("[Rally] armed: the next Good or Perfect parry will be photographed.");
        }

        static IEnumerator ParryRoutine(RallyDirector d)
        {
            Directory.CreateDirectory(Dir);

            RallyCompetitor hit = null;
            RallyStrike.Tier hitTier = RallyStrike.Tier.Miss;
            System.Action<RallyCompetitor, RallyStrike.Tier, int> onStrike = (by, tier, target) =>
            {
                if (hit != null) return;
                if (tier != RallyStrike.Tier.Good && tier != RallyStrike.Tier.Perfect) return;
                hit = by; hitTier = tier;
            };
            d.OnStrike += onStrike;

            float armed = 0f;
            while (hit == null && armed < 90f) { armed += Time.unscaledDeltaTime; yield return null; }
            d.OnStrike -= onStrike;
            if (hit == null) { Debug.LogWarning("[Rally] no Good or Perfect parry inside 90 s."); yield break; }

            Vector3 at = hit.SweetSpot;
            Vector3 side = Vector3.Cross(Vector3.up, hit.Heading).normalized;
            var cam = new GameObject("~ParryCam").AddComponent<Camera>();
            // Back far enough to hold the whole event. At 4.2 m and 40 degrees the goose alone
            // filled three screens and had left frame entirely by the second exposure, so the sheet
            // showed neither the bird nor where it went — which is the one thing it is for.
            cam.fieldOfView = 52f;
            cam.transform.position = at + side * 8.5f - hit.Heading * 3f + Vector3.up * 3.4f;
            cam.transform.LookAt(at);

            string stamp = $"{hitTier}_{System.DateTime.Now:HHmmss}";
            for (int i = 1; i <= 8; i++)
            {
                Render(cam, $"parry_{stamp}_{i}");
                float t = 0f;
                while (t < 0.11f) { t += Time.unscaledDeltaTime; yield return null; }
            }
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log($"[Rally] parry sequence written: parry_{stamp}_1..8.");
        }

        /// <summary>
        /// The elimination, photographed. Ten frames on unscaled time through a second parry.
        ///
        /// Its own tool rather than a flag on the parry sheet, because a knockout is the one beat
        /// that has to answer a different question. A parry asks "did that read as a hit"; a KO asks
        /// "did that read as a RESULT" — and the failure mode it is guarding against is the bird
        /// simply ceasing to exist, which looks identical to a hit in any single frame and only
        /// shows up across the second after.
        /// </summary>
        /// <summary>
        /// The same fixed viewpoints as the arena survey, but taken in PLAY mode.
        ///
        /// The edit-mode survey cannot see half of what this arena is made of. The grass blades are
        /// baked on Awake, the geese are pooled at runtime, and the crowd is an instanced draw from
        /// SpectatorCrowd.Tick — so an empty stand in the edit survey is not evidence of an empty
        /// stand, and I have twice reached for that survey to check something it structurally
        /// cannot show. Same poses, so the two sheets are comparable; play mode, so what is on them
        /// is what the player sees.
        /// </summary>
        [MenuItem("Duck/Rally · Survey (play mode)", priority = 44)]
        public static void SurveyLive()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");

            var me = RallyArena.Get(0);
            var across = RallyArena.Get(2);

            Pose("L1_overhead", new Vector3(0f, 96f, -34f), Vector3.zero, 46f, stamp);
            Pose("L2_three_quarter", new Vector3(-52f, 34f, -62f), Vector3.zero, 48f, stamp);
            // Straight at a bank of stands from inside the arena: the shot that says whether two
            // hundred spectators are looking at the pitch or at the trees behind them.
            Pose("L3_stand", me.outward * (RallyArena.ArenaRadius - 14f) + Vector3.up * 4.5f,
                 me.outward * (RallyArena.ArenaRadius + 7f) + Vector3.up * 2.2f, 38f, stamp);
            Pose("L4_stand_across", across.outward * (RallyArena.ArenaRadius - 14f) + Vector3.up * 4.5f,
                 across.outward * (RallyArena.ArenaRadius + 7f) + Vector3.up * 2.2f, 38f, stamp);
            Pose("L5_player_eyeline", me.bandCentre - me.inward * 7f + Vector3.up * 3.4f,
                 Vector3.up * 1.2f, 54f, stamp);

            Debug.Log($"[Rally] live survey {stamp} written to {Dir}/.");
        }

        [MenuItem("Duck/Rally · Knockout close-up (play mode)", priority = 43)]
        public static void KnockoutSequence()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }
            var d = Object.FindFirstObjectByType<RallyDirector>();
            if (d == null) { Debug.LogWarning("[Rally] no director."); return; }
            d.StartCoroutine(KnockoutRoutine(d));
            Debug.Log("[Rally] armed: the next knockout will be photographed.");
        }

        static IEnumerator KnockoutRoutine(RallyDirector d)
        {
            Directory.CreateDirectory(Dir);

            RallyCompetitor by = null;
            System.Action<RallyCompetitor, RallyCompetitor> onKo = (striker, victim) =>
            {
                if (by == null) by = striker;
            };
            d.OnKnockout += onKo;

            float armed = 0f;
            while (by == null && armed < 120f) { armed += Time.unscaledDeltaTime; yield return null; }
            d.OnKnockout -= onKo;
            if (by == null) { Debug.LogWarning("[Rally] no knockout inside 120 s."); yield break; }

            Vector3 at = by.SweetSpot;
            Vector3 side = Vector3.Cross(Vector3.up, by.Heading).normalized;
            var cam = new GameObject("~KoCam").AddComponent<Camera>();
            cam.fieldOfView = 54f;
            cam.transform.position = at + side * 9f - by.Heading * 3.5f + Vector3.up * 4f;
            cam.transform.LookAt(at);

            string stamp = $"{System.DateTime.Now:HHmmss}";
            for (int i = 1; i <= 10; i++)
            {
                Render(cam, $"ko_{stamp}_{i:00}");
                float t = 0f;
                while (t < 0.13f) { t += Time.unscaledDeltaTime; yield return null; }
            }
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log($"[Rally] knockout sequence written: ko_{stamp}_01..10 (by {by.Name}).");
        }

        [MenuItem("Duck/Rally · End the match now (play mode)", priority = 40)]
        public static void EndMatchNow()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }
            var d = Object.FindFirstObjectByType<RallyDirector>();
            if (d == null) { Debug.LogWarning("[Rally] no director."); return; }
            Debug.Log($"[Rally] {d.EndNow()}.");
        }

        [MenuItem("Duck/Rally · Goose close-up (play mode)", priority = 39)]
        public static void GooseCloseUp()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Rally] enter play mode first."); return; }

            RallyGoose pick = null;
            foreach (var g in Object.FindObjectsByType<RallyGoose>(FindObjectsSortMode.None))
            {
                if (!g.Active) continue;
                if (pick == null) pick = g;
                if (g.Phase == RallyGoose.State.Charge || g.Phase == RallyGoose.State.Brace) { pick = g; break; }
            }
            if (pick == null) { Debug.LogWarning("[Rally] no goose in play."); return; }

            Directory.CreateDirectory(Dir);
            var cam = new GameObject("~GooseCam").AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.clearFlags = CameraClearFlags.Skybox;

            Vector3 side = Vector3.Cross(Vector3.up, pick.transform.forward).normalized;
            Vector3 at = pick.transform.position + Vector3.up * 0.35f;
            cam.transform.position = at + pick.transform.forward * 6.4f + side * 4.0f + Vector3.up * 1.6f;
            cam.transform.LookAt(at);

            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string file = Path.Combine(Dir, $"goose_{System.DateTime.Now:HHmmss}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log($"[Rally] {file} — {pick.Phase}, parried {pick.Parried}.");
        }

        /// <summary>
        /// Build the player with the ARENA at index zero, so the browser opens straight into it.
        ///
        /// The rally is the last beat of the second round, which means proving it works in a real
        /// browser otherwise costs two full rounds of play per attempt — and nobody checks a build
        /// that costs four minutes to look at. This is the same argument as RallyBootstrap running
        /// the arena standalone: the mode has to be cheap to open, or it stops being reviewed.
        ///
        /// The scene order is restored afterwards, always, so a review build cannot leave the
        /// shipping build opening on the wrong scene.
        /// </summary>
        [MenuItem("Duck/Rally · Build WebGL opening on the arena", priority = 38)]
        public static void BuildWebGLRallyFirst() => BuildRallyFirst(development: true);

        /// <summary>
        /// The same arena-first player, built to SHIP.
        ///
        /// A development player says nothing about performance: different IL2CPP settings, debug
        /// symbols, no code stripping and no compression — the one this reviewed at 157 MB comes
        /// out at 69 MB when built properly. Frame rate measured on the development build is not a
        /// frame rate, so the arena needs to be reachable in a release player too, and deleting the
        /// development one to get there is not an answer either.
        /// </summary>
        [MenuItem("Duck/Rally · Build WebGL on the arena (release)", priority = 42)]
        public static void BuildWebGLRallyFirstRelease() => BuildRallyFirst(development: false);

        static void BuildRallyFirst(bool development)
        {
            var saved = EditorBuildSettings.scenes;
            try
            {
                var list = new List<EditorBuildSettingsScene>
                {
                    new EditorBuildSettingsScene(DuckRallyBuilder.ScenePath, true)
                };
                foreach (var s in saved)
                    if (s.path != DuckRallyBuilder.ScenePath) list.Add(s);
                EditorBuildSettings.scenes = list.ToArray();

                var options = new BuildPlayerOptions
                {
                    scenes = System.Array.ConvertAll(list.ToArray(), s => s.path),
                    // Separate folders, so a release review build can never overwrite the
                    // development one that carries the console — and neither has to be deleted to
                    // look at the other.
                    locationPathName = development ? "C:/Duck/Web_Dev" : "C:/Duck/Web_Arena",
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    // DEVELOPMENT, and that is the point of this build rather than an oversight.
                    //
                    // A release WebGL player does not forward Debug.Log to the browser console, so
                    // the first time the arena misbehaved in a browser the console had two lines in
                    // it, both about an IndexedDB cache, and nothing whatsoever about the match. A
                    // review build that cannot be read is not a review build.
                    // CleanBuildCache on the release path, and it is not paranoia.
                    //
                    // Two arena-first release builds in a row reported "Succeeded, 0 errors" — one in
                    // thirty-seven seconds and one in THREE, the second after the output folder had
                    // been deleted — and both produced a player that opens on the menu instead of the
                    // arena, byte-for-byte the same size. A WebGL release build does not take three
                    // seconds. The incremental cache was handing back the previous player, and the
                    // only thing that had changed between them was the order of the scene list, which
                    // it evidently does not treat as an input.
                    //
                    // A review build that silently returns a DIFFERENT build is worse than no review
                    // build: it reports success and shows you the wrong game. Correctness beats the
                    // few minutes this costs, and this path is only ever run by hand.
                    options = development
                        ? BuildOptions.Development | BuildOptions.AllowDebugging
                        : BuildOptions.CleanBuildCache
                };
                // Printed BEFORE the build, because two theories about why the resulting player
                // opens on the menu have now been wrong, and both were guesses about what happens
                // AFTER this point. What the player is actually asked to contain, in order, is the
                // one fact neither theory checked.
                var report = BuildPipeline.BuildPlayer(options);
                // The scene list is reported AFTER the build, with the result.
                //
                // It was logged before, and it vanished: the console clears when a build starts, so
                // the one fact this was added to establish was wiped by the very action it was
                // measuring. Anything a build diagnostic needs to say has to be said on the far side
                // of the build.
                Debug.Log($"[Rally] arena-first {(development ? "development" : "RELEASE")} WebGL build: " +
                          $"scenes [{string.Join(" | ", options.scenes)}], " +
                          $"{report.summary.result}, " +
                          $"{report.summary.totalErrors} errors, {report.summary.totalTime}.");
            }
            finally
            {
                EditorBuildSettings.scenes = saved;
            }
        }

        [MenuItem("Duck/Rally · Match contact sheet (play mode)", priority = 34)]
        public static void MatchSheet()
        {
            var director = Object.FindFirstObjectByType<RallyDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Rally] enter play mode on GooseRally first.");
                return;
            }
            director.StartCoroutine(SheetRoutine(director));
        }

        static IEnumerator SheetRoutine(RallyDirector d)
        {
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");
            int shot = 0;
            var pending = new List<string>();

            void Queue(string label) => pending.Add(label);

            System.Action<RallyCompetitor, RallyStrike.Tier, int> onStrike =
                (by, tier, target) => Queue($"strike_{tier}_{by.Name}");
            System.Action<RallyCompetitor, RallyCompetitor> onKo =
                (by, victim) => Queue($"KO_{by.Name}");
            System.Action<RallyCompetitor, int> onBreach =
                (def, lost) => Queue($"breach_{(def != null ? def.Name : "?")}_{lost}beds");

            d.OnStrike += onStrike;
            d.OnKnockout += onKo;
            d.OnBreach += onBreach;

            // A few establishing frames regardless of what happens, so a match in which nothing
            // connects still produces a sheet that says so.
            yield return Wait(1.2f);
            Render(Camera.main, $"{stamp}_{++shot:00}_open");

            float guard = 0f;
            while (!d.Finished && guard < 180f)
            {
                guard += Time.unscaledDeltaTime;

                if (pending.Count > 0)
                {
                    // One frame later, deliberately: the strike event fires on the frame of contact,
                    // when the goose has not yet moved and the debris has not been emitted. The
                    // interesting frame is the one after, with the launch under way and the hit stop
                    // still holding it there to be seen.
                    yield return null;
                    foreach (var label in pending)
                        Render(Camera.main, $"{stamp}_{++shot:00}_{label}");
                    pending.Clear();
                }

                // A periodic frame so the sheet has the quiet moments too — a serve arriving, three
                // geese loose, a defender lining up.
                if (Mathf.FloorToInt(d.Elapsed) % 12 == 0 && Time.frameCount % 60 == 0)
                    Render(Camera.main, $"{stamp}_{++shot:00}_t{d.Elapsed:00}_g{d.ActiveGeese}");

                yield return null;
            }

            d.OnStrike -= onStrike;
            d.OnKnockout -= onKo;
            d.OnBreach -= onBreach;

            Render(Camera.main, $"{stamp}_{++shot:00}_final");
            Debug.Log($"[Rally] match sheet {stamp}: {shot} frames to {Dir}/.");
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        // ------------------------------------------------------------------ the render

        /// <summary>
        /// Render one camera to a PNG.
        ///
        /// The restore is in a finally, and the reason is worse than a leaked texture: this borrows
        /// the camera's target, and anything that throws in between leaves it pointing at a
        /// RenderTexture about to be destroyed — after which the game view renders black for the rest
        /// of the session with the original exception as the only clue.
        /// </summary>
        static void Render(Camera cam, string name)
        {
            if (cam == null) { Debug.LogWarning("[Rally] no camera to capture."); return; }

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { antiAliasing = 1 };
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;

            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply(false);
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (tex != null) Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
