using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Play-mode controls and the capture rig.
    ///
    /// Reviewing a game by taking screenshots at whatever moment a tool call happens to land is
    /// useless — every shot is a different second of a different round. These put the round on
    /// rails: force a picture, let the autopilot mow it, then capture the same set of framings
    /// every time so two builds can actually be compared.
    /// </summary>
    public static class DuckPlayTools
    {
        const string CaptureDir = "Captures";

        static GameDirector Director => Object.FindFirstObjectByType<GameDirector>();
        static Autopilot Pilot => Object.FindFirstObjectByType<Autopilot>();

        [MenuItem("Duck/Play · Autopilot ON", priority = 20)]
        public static void AutopilotOn()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Not in play mode."); return; }
            d.autopilotEnabled = true;
            if (d.State == GameState.Mowing) Pilot?.Begin();
            Debug.Log("[Duck] Autopilot ON");
        }

        [MenuItem("Duck/Play · Autopilot OFF", priority = 21)]
        public static void AutopilotOff()
        {
            var d = Director;
            if (d == null) return;
            d.autopilotEnabled = false;
            Pilot?.Stop();
            Debug.Log("[Duck] Autopilot OFF");
        }

        [MenuItem("Duck/Play · Restart round", priority = 22)]
        public static void Restart()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Not in play mode."); return; }
            d.RetrySameShape();
        }

        [MenuItem("Duck/Play · Skip to reveal", priority = 23)]
        public static void SkipToReveal()
        {
            var d = Director;
            if (d == null) return;
            d.DebugSetTimeRemaining(0.01f);
        }

        [MenuItem("Duck/Play · Victory ceremony (cheat)", priority = 27)]
        public static void JumpToVictory()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            d.DebugJumpToCeremony(true);
            Debug.Log("[Duck] Jumped to the ceremony as champion.");
        }

        [MenuItem("Duck/Play · Defeat ceremony (cheat)", priority = 28)]
        public static void JumpToDefeat()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            d.DebugJumpToCeremony(false);
            Debug.Log("[Duck] Jumped to the ceremony as runner-up.");
        }

        [MenuItem("Duck/Play · Time scale 6x", priority = 24)]
        public static void Fast() { Time.timeScale = 6f; Debug.Log("[Duck] timeScale 6"); }

        [MenuItem("Duck/Play · Time scale 1x", priority = 25)]
        public static void Normal() { Time.timeScale = 1f; Debug.Log("[Duck] timeScale 1"); }

        [MenuItem("Duck/Play · Time scale 0 (pause sim)", priority = 26)]
        public static void Pause() { Time.timeScale = 0f; Debug.Log("[Duck] timeScale 0"); }

        // ------------------------------------------------------------------ capture rig

        /// <summary>
        /// Runs a whole round on fast-forward with the autopilot driving, then captures the
        /// standard frame sheet: mid-mow chase, overhead reveal, judging, verdict.
        /// </summary>
        [MenuItem("Duck/Capture · Full round frame sheet", priority = 30)]
        public static void CaptureRound()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            d.StartCoroutine(CaptureRoutine(d));
        }

        static IEnumerator CaptureRoutine(GameDirector d)
        {
            Directory.CreateDirectory(CaptureDir);
            string stamp = System.DateTime.Now.ToString("HHmmss");

            d.autopilotEnabled = true;
            d.DebugSetShape(ShapeId.Duckling);
            Time.timeScale = 1f;

            // Briefing establishing shot.
            yield return WaitRealtime(1.4f);
            Shot($"{stamp}_01_briefing");

            // Countdown.
            while (d.State == GameState.Briefing) yield return null;
            yield return WaitRealtime(1.2f);
            Shot($"{stamp}_02_countdown");

            while (d.State == GameState.Countdown) yield return null;

            // Early mowing — first cut, chase camera.
            yield return WaitRealtime(3.5f);
            Shot($"{stamp}_03_mow_early");

            Time.timeScale = 5f;
            yield return WaitRealtime(3.0f);
            Time.timeScale = 1f;
            yield return WaitRealtime(0.6f);
            Shot($"{stamp}_04_mow_mid");

            Time.timeScale = 5f;
            // Let the autopilot finish the picture, then end the round.
            float guard = 0f;
            while (d.State == GameState.Mowing && guard < 40f)
            {
                guard += Time.unscaledDeltaTime;
                var p = Pilot;
                if (p != null && p.WaypointCount > 0 && p.WaypointIndex >= p.WaypointCount) break;
                yield return null;
            }
            Time.timeScale = 1f;
            yield return WaitRealtime(0.4f);
            Shot($"{stamp}_05_mow_done");

            d.DebugSetTimeRemaining(0.01f);
            while (d.State == GameState.Mowing) yield return null;

            yield return WaitRealtime(1.0f);
            Shot($"{stamp}_06_klaxon");

            while (d.State == GameState.Klaxon) yield return null;

            yield return WaitRealtime(1.4f);
            Shot($"{stamp}_07_reveal_picture");
            yield return WaitRealtime(1.6f);
            Shot($"{stamp}_08_reveal_ghost");
            yield return WaitRealtime(1.6f);
            Shot($"{stamp}_09_reveal_analysis");

            while (d.State == GameState.Reveal) yield return null;

            yield return WaitRealtime(1.2f);
            Shot($"{stamp}_10_judges_lean");
            yield return WaitRealtime(2.6f);
            Shot($"{stamp}_11_judges_card1");
            yield return WaitRealtime(3.0f);
            Shot($"{stamp}_12_judges_card2");
            yield return WaitRealtime(3.0f);
            Shot($"{stamp}_13_judges_card3");

            while (d.State == GameState.Judging) yield return null;
            yield return WaitRealtime(0.8f);
            Shot($"{stamp}_14_verdict");

            Debug.Log($"[Duck] Frame sheet {stamp} captured to {CaptureDir}/ — score " +
                      $"coverage={d.LastScore.coverage:0.00} spill={d.LastScore.spill:0.00} " +
                      $"accuracy={d.LastScore.accuracy:0.00} edge={d.LastScore.edgeQuality:0.00}");
        }

        static IEnumerator WaitRealtime(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// <summary>
        /// Renders the main camera to a texture and writes the PNG directly.
        /// ScreenCapture.CaptureScreenshot needs a focused, rendering Game View; this does not,
        /// which is the difference between a capture rig that works unattended and one that
        /// silently produces nothing.
        /// </summary>
        static void Shot(string name, int width = 1600, int height = 900)
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Duck] No main camera to capture."); return; }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { antiAliasing = 1 };
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;

            // The restore is in a finally, and the reason is worse than a leaked texture.
            //
            // This borrows the MAIN camera's target. If anything between here and the restore throws
            // — Render, ReadPixels, EncodeToPNG, a full disk — then cam.targetTexture is left pointing
            // at a RenderTexture that is about to be destroyed, and the game view renders to a dead
            // target from then on: the screen goes black and stays black for the rest of the session,
            // with the original exception as the only clue. A capture tool that can permanently break
            // the thing it is meant to observe is worse than one that occasionally fails.
            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);

                Directory.CreateDirectory(CaptureDir);
                File.WriteAllBytes(Path.Combine(CaptureDir, name + ".png"), tex.EncodeToPNG());
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

        /// <summary>Free-standing capture of the current frame, for one-off checks.</summary>
        [MenuItem("Duck/Capture · Single frame", priority = 31)]
        public static void CaptureSingle()
        {
            Directory.CreateDirectory(CaptureDir);
            string name = "single_" + System.DateTime.Now.ToString("HHmmss");
            Shot(name);
            Debug.Log($"[Duck] captured {name}.png");
        }
    }

}
