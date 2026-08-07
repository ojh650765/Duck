using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Contact sheets for Bloom Rush, in both modes.
    ///
    /// The edit-mode survey answers "is this arena composed and readable" — fixed viewpoints, no play
    /// mode, so a layout change can be judged in seconds. The play-mode sheet answers the question
    /// this mode actually lives on, "can you tell whose ground that is", and it is EVENT-DRIVEN: it
    /// subscribes to the match's own steal and crown events and captures on the frame after each.
    /// Capturing on a stopwatch produces sheets full of empty turf captioned as a steal.
    ///
    /// Both render through a RenderTexture rather than ScreenCapture, which needs a focused Game
    /// View — the difference between a rig that works unattended and one that silently produces
    /// nothing. Same rig as <see cref="DuckRallyShots"/>, deliberately, so the two modes' sheets are
    /// comparable frame for frame.
    /// </summary>
    public static class DuckTurfShots
    {
        const string Dir = "Captures/Bloom";
        const int W = 1600, H = 900;

        // ------------------------------------------------------------------ edit mode

        [MenuItem("Duck/Bloom · Arena survey (no play mode)", priority = 43)]
        public static void Survey()
        {
            if (!EnsureSceneOpen()) return;
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");

            var me = TurfArena.Get(0);

            Pose("01_overhead", new Vector3(0f, 104f, -26f), Vector3.zero, 46f, stamp);
            Pose("02_three_quarter", new Vector3(-58f, 40f, -62f), Vector3.zero, 50f, stamp);

            // The shot that decides whether the layout works: a driver's eye height out on the loop,
            // looking in. Everything the mode asks the player to choose between has to be legible
            // from here, because this is where they spend the match.
            Pose("03_loop_eyeline", me.outward * TurfArena.LoopMid + Vector3.up * 2.6f,
                 Vector3.up * 2.5f, 55f, stamp);

            // A wide spoke, entered from the loop. The main road.
            Vector3 spokeMouth = TurfArena.Bearing(TurfArena.SpokeAngle(0)) * (TurfArena.HedgeOuter + 6f);
            Pose("04_spoke_approach", spokeMouth + Vector3.up * 2.4f, Vector3.up * 1.5f, 52f, stamp);

            // A climbing choke, from the loop side. The narrow contested route, and the one thing in
            // the arena with a silhouette above the hedge line.
            Vector3 chokeOut = TurfArena.Bearing(TurfArena.ChokeAngle(0)) * (TurfArena.HedgeOuter + 9f);
            Pose("05_choke_approach", chokeOut + Vector3.up * 2.2f,
                 TurfArena.Bearing(TurfArena.ChokeAngle(0)) * TurfArena.HedgeMid
                 + Vector3.up * (TurfArena.RampHeight + 1f), 50f, stamp);

            // The crest of the same climb, looking down into the hub — what a driver committing to
            // the short route actually gets to see.
            Vector3 crest = TurfArena.Bearing(TurfArena.ChokeAngle(0)) * TurfArena.HedgeMid;
            crest.y = TurfArena.GroundHeight(crest.x, crest.z) + 1.9f;
            Pose("06_choke_crest", crest, Vector3.up * 1.5f, 58f, stamp);

            Pose("07_hub", new Vector3(0f, 3.0f, -(TurfArena.PlazaRadius + 8f)),
                 Vector3.up * 2.6f, 52f, stamp);
            Pose("08_hub_overhead", new Vector3(0f, 34f, -20f), Vector3.zero, 48f, stamp);

            Pose("09_barrier_low", TurfArena.Bearing(200f) * (TurfArena.ArenaRadius + 1.5f)
                 + Vector3.up * 1.6f, Vector3.up * 3f, 55f, stamp);

            // The cast, close enough to count the drivers.
            //
            // Mower.prefab ships with the PLAYER's duck already sitting on it, so a rival built from
            // that prefab has two animals in one seat unless the duck is taken off first — a hare
            // and a duck, intersecting, at exactly the height the chase camera lives at. It went
            // unnoticed for a whole day because every shot in this survey was framed on the
            // architecture. A frame that looks at who is actually driving is worth one line here.
            for (int i = 1; i < TurfArena.Count; i++)
            {
                var s = TurfArena.Get(i);
                Vector3 seat = s.SpawnPosition + Vector3.up * 0.35f;
                Pose($"10_rival_{s.contestant}", seat + s.right * 3.4f + Vector3.up * 1.1f,
                     seat, 34f, stamp);
            }

            Debug.Log($"[Bloom] arena survey {stamp} written to {Dir}/.");
            AssetDatabase.Refresh();
        }

        static bool EnsureSceneOpen()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.path == DuckTurfBuilder.ScenePath) return true;
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Bloom] not the Bloom Rush scene, and play mode is running.");
                return false;
            }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DuckTurfBuilder.ScenePath);
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
            cam.farClipPlane = 420f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up));

            Render(cam, $"{stamp}_{name}");
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ play mode

        /// <summary>
        /// Put a bot on the player's machine for the next play session.
        ///
        /// Without it a review run is ninety seconds of a parked mower while three opponents divide
        /// the arena, which exercises exactly none of the mode's rules. The flag is static and lives
        /// on the component, so it cannot be saved into the scene by accident — see
        /// <see cref="TurfDirector.DebugAutopilotPlayer"/>.
        /// </summary>
        [MenuItem("Duck/Bloom · Autopilot the player (next play)", priority = 45)]
        public static void AutopilotPlayer()
        {
            TurfDirector.DebugAutopilotPlayer = !TurfDirector.DebugAutopilotPlayer;
            Debug.Log($"[Bloom] player autopilot {(TurfDirector.DebugAutopilotPlayer ? "ON" : "OFF")} " +
                      "for the next play session.");
        }

        /// <summary>
        /// Seed a plausible result and end the match, so the closing sequence can be watched alone.
        ///
        /// The evaluation is five beats and every one is a timing decision that wants looking at
        /// dozens of times; reaching it by driving costs seventy-five seconds a go. The territory is
        /// seeded first because a skipped match finishes at four zeroes, and four zeroes exercise
        /// the sequence without testing the one thing it is for — whether the result READS.
        /// </summary>
        [MenuItem("Duck/Bloom · Skip to the result (play mode)", priority = 46)]
        public static void SkipToResult()
        {
            var director = Object.FindFirstObjectByType<TurfDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Bloom] enter play mode on BloomRush first.");
                return;
            }
            director.DebugSeedTerritory();
            director.DebugFinishNow();
            Debug.Log("[Bloom] skipped to the result — horn, freeze, lift, percentages, board.");
        }

        /// <summary>End the match where it stands, without touching the ground anybody claimed.</summary>
        [MenuItem("Duck/Bloom · End the match now (play mode)", priority = 47)]
        public static void EndNow()
        {
            var director = Object.FindFirstObjectByType<TurfDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Bloom] enter play mode on BloomRush first.");
                return;
            }
            director.DebugFinishNow();
        }

        [MenuItem("Duck/Bloom · Match contact sheet (play mode)", priority = 44)]
        public static void MatchSheet()
        {
            var director = Object.FindFirstObjectByType<TurfDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Bloom] enter play mode on BloomRush first.");
                return;
            }
            director.StartCoroutine(SheetRoutine(director));
        }

        static IEnumerator SheetRoutine(TurfDirector d)
        {
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");
            int shot = 0;
            var pending = new List<string>();

            System.Action<TurfCompetitor, TurfCompetitor, float, Vector3> onSteal =
                (thief, victim, m2, where) => pending.Add(
                    $"steal_{(thief != null ? thief.Name : "?")}_off_" +
                    $"{(victim != null ? victim.Name : "?")}_{m2:0}m2");
            System.Action<int, int> onCrown =
                (now, before) => pending.Add(
                    $"crown_{(now >= 0 ? TurfArena.Get(now).contestant : "open")}");

            d.OnBigSteal += onSteal;
            d.OnCrownChange += onCrown;

            // Establishing frames regardless of what happens, so a match in which nobody takes
            // anything still produces a sheet that says so.
            yield return Wait(1.0f);
            Render(Camera.main, $"{stamp}_{++shot:00}_countdown");
            yield return Wait(3.0f);
            Render(Camera.main, $"{stamp}_{++shot:00}_first_claims");

            float guard = 0f;
            int lastPeriodic = -1;
            while (!d.Finished && guard < 200f)
            {
                guard += Time.unscaledDeltaTime;

                if (pending.Count > 0)
                {
                    // One frame later, deliberately: the steal event fires on the frame the ground
                    // changes hands, before the burst has been drawn and while the mask still holds
                    // last frame's picture. The frame worth keeping is the one after.
                    yield return null;
                    foreach (var label in pending)
                        Render(Camera.main, $"{stamp}_{++shot:00}_{label}");
                    pending.Clear();
                }

                // A periodic frame so the sheet has the quiet stretches too, captioned with the
                // board — which is how a sheet shows a mode drifting toward one colour.
                int bucket = Mathf.FloorToInt(d.Elapsed / 10f);
                var mask = TurfMask.Instance;
                if (bucket != lastPeriodic && mask != null && d.State == TurfDirector.Phase.Live)
                {
                    lastPeriodic = bucket;
                    Render(Camera.main, $"{stamp}_{++shot:00}_t{d.Elapsed:00}" +
                                        $"_{mask.Share(0) * 100f:00}-{mask.Share(1) * 100f:00}" +
                                        $"-{mask.Share(2) * 100f:00}-{mask.Share(3) * 100f:00}");
                }

                yield return null;
            }

            d.OnBigSteal -= onSteal;
            d.OnCrownChange -= onCrown;

            // The reveal, caught three times as it resolves: the lift, the standings landing, and
            // the winner called. This is the payoff beat and it is the one most worth judging.
            Render(Camera.main, $"{stamp}_{++shot:00}_reveal_lift");
            yield return Wait(2.2f);
            Render(Camera.main, $"{stamp}_{++shot:00}_reveal_standings");
            yield return Wait(2.4f);
            Render(Camera.main, $"{stamp}_{++shot:00}_reveal_winner");

            Debug.Log($"[Bloom] match sheet {stamp}: {shot} frames to {Dir}/.");
        }

        /// <summary>
        /// The ENDING, frame by frame — the one sheet that has to show the UI as well as the world.
        ///
        /// Every other sheet here renders a camera straight to a RenderTexture, which is what makes
        /// them work unattended, and which also means they cannot see a screen-space overlay canvas:
        /// an overlay is composited by the graphics device after the camera is finished, so a
        /// camera-only render of the result beat is a picture of a mower with no card next to it.
        /// The HUD is therefore lent to the camera for the duration — ScreenSpaceCamera, planes set
        /// so it draws in front of everything — and handed back in a finally. That is a real change
        /// to a live object, so it is undone even if a frame throws.
        /// </summary>
        [MenuItem("Duck/Bloom · Ending contact sheet (play mode)", priority = 48)]
        public static void EndingSheet()
        {
            var director = Object.FindFirstObjectByType<TurfDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Bloom] enter play mode on BloomRush first.");
                return;
            }
            director.StartCoroutine(EndingRoutine(director));
        }

        static IEnumerator EndingRoutine(TurfDirector d)
        {
            Directory.CreateDirectory(Dir);
            var cam = Camera.main;

            Canvas hud = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.name == "Bloom HUD") { hud = c; break; }

            RenderMode prevMode = RenderMode.ScreenSpaceOverlay;
            Camera prevCam = null;
            float prevPlane = 100f;
            if (hud != null)
            {
                prevMode = hud.renderMode;
                prevCam = hud.worldCamera;
                prevPlane = hud.planeDistance;
                hud.renderMode = RenderMode.ScreenSpaceCamera;
                hud.worldCamera = cam;
                hud.planeDistance = cam != null ? cam.nearClipPlane + 0.05f : 0.35f;
            }

            d.DebugSeedTerritory();
            d.DebugFinishNow();

            // Sampled against the beats rather than a stopwatch, because the bench decides when the
            // card is allowed to arrive and a fixed schedule photographs whatever happens to be up.
            float t = 0f;
            int shot = 0;
            bool sawCard = false, sawBoard = false;
            float cardAt = -1f, boardAt = -1f;

            while (t < 30f && !d.Finished)
            {
                t += Time.unscaledDeltaTime;

                if (shot == 0 && t >= 1.2f) { Render(cam, $"end_{++shot:00}_lift"); }
                else if (shot == 1 && t >= 4.5f) { Render(cam, $"end_{++shot:00}_overhead"); }
                else if (shot == 2 && d.State == TurfDirector.Phase.Reveal && t >= 7.5f)
                    { Render(cam, $"end_{++shot:00}_bench"); }

                if (!sawCard && d.OnCard)
                {
                    sawCard = true; cardAt = t;
                    yield return Wait(1.4f); t += 1.4f;      // let the blend land
                    Render(cam, $"end_{++shot:00}_card");
                }
                else if (sawCard && !sawBoard && cardAt > 0f && t >= cardAt + 4.2f && shot < 5)
                    { Render(cam, $"end_{++shot:00}_card_hold"); }

                if (!sawBoard && d.OnBoard)
                {
                    sawBoard = true; boardAt = t;
                    yield return Wait(1.8f); t += 1.8f;
                    Render(cam, $"end_{++shot:00}_board");
                }
                else if (sawBoard && boardAt > 0f && t >= boardAt + 4.5f && shot < 7)
                    { Render(cam, $"end_{++shot:00}_board_hold"); }

                yield return null;
            }

            if (hud != null)
            {
                hud.renderMode = prevMode;
                hud.worldCamera = prevCam;
                hud.planeDistance = prevPlane;
            }

            Debug.Log($"[Bloom] ending sheet: {shot} frames. card at {cardAt:0.0}s, board at {boardAt:0.0}s, " +
                      $"finished={d.Finished}.");
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
        /// RenderTexture about to be destroyed — after which the game view renders black for the
        /// rest of the session with the original exception as the only clue.
        /// </summary>
        static void Render(Camera cam, string name)
        {
            if (cam == null) { Debug.LogWarning("[Bloom] no camera to capture."); return; }

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
