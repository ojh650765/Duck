using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckMow
{
    /// <summary>
    /// Takes the round out to Bloom Rush and brings it back.
    ///
    /// A copy of <see cref="RallyStage"/>'s shape, deliberately and not by accident of history. The
    /// load is ADDITIVE and Main is put to sleep behind it rather than swapped out, for exactly the
    /// reason set out there: Main is holding the cut mask, which is the picture the player has just
    /// spent seventy-five seconds mowing, and there is no way to reload that scene and have the
    /// reveal show what they actually cut. Main sleeps, the arena runs in front of it, the arena
    /// unloads, Main wakes exactly as it was.
    ///
    /// Written as its own class rather than as a parameter on the rally's for one reason worth
    /// stating: the two stages have different lifetimes and different failure modes, and a shared
    /// loader is a single place where a change made for Bloom Rush silently alters how the goose
    /// rally sleeps Main. The duplication here is nine lines of scene juggling. The coupling it
    /// avoids is every completed mode in the game.
    /// </summary>
    public class TurfStage
    {
        public const string SceneName = "BloomRush";

        public enum Step { Idle, Loading, Playing, Leaving, Done, Failed }

        public Step State { get; private set; } = Step.Idle;
        public bool Finished => State == Step.Done || State == Step.Failed;
        /// <summary>True when the arena never opened, so the round can carry on without it.</summary>
        public bool Failed => State == Step.Failed;

        /// <summary>
        /// Main's roots that stay awake behind the arena: the input pipeline the player is driving
        /// with, the audio bus, and the touch controls a phone still needs.
        /// </summary>
        static readonly string[] KeepAwake = { "~ Systems", "~ Audio", "~ Touch Controls" };

        readonly List<GameObject> _slept = new(16);
        Scene _home;
        Scene _arena;
        TurfBootstrap _boot;

        public IEnumerator Run(int roundNumber)
        {
            State = Step.Loading;
            TurfHandoff.Send(roundNumber);

            _home = SceneManager.GetActiveScene();

            // The OUTRO, then the wipe. Blossom for this one — the stage is about painting a garden
            // in flowers, and the curtain says so before the arena is on screen. See StageSeam.
            yield return StageSeam.Begin(MatchState.Seam.RoundToBloom,
                                         "BLOOM RUSH", StageSeam.RoundKicker(roundNumber));

            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                // Almost always the scene missing from the build settings, which is a wiring fault
                // rather than a gameplay one — so say which, and let the round continue without the
                // stage rather than stranding it one beat short of the reveal.
                Debug.LogError($"[Bloom] '{SceneName}' could not be loaded. Is it in the build " +
                               "settings? Run 'Duck/4 · Build bloom rush scene'.");
                TurfHandoff.Clear();
                yield return StageSeam.End();
                State = Step.Failed;
                yield break;
            }
            yield return load;

            _arena = SceneManager.GetSceneByName(SceneName);
            if (!_arena.IsValid() || !_arena.isLoaded)
            {
                Debug.LogError($"[Bloom] '{SceneName}' loaded but is not valid.");
                TurfHandoff.Clear();
                yield return StageSeam.End();
                State = Step.Failed;
                yield break;
            }

            Sleep();
            // The active scene decides lighting and skybox, so the arena has to own it or it is lit
            // by the lawn's sun from underneath the floor.
            SceneManager.SetActiveScene(_arena);

            _boot = Object.FindFirstObjectByType<TurfBootstrap>();
            if (_boot == null)
            {
                Debug.LogError("[Bloom] the arena scene has no TurfBootstrap.");
                yield return Leave();
                State = Step.Failed;
                yield break;
            }

            State = Step.Playing;

            // Up onto the arena's own establishing shot, once it has actually drawn one — see
            // StageSeam.End on why the lift is gated on frames rendered rather than on the load
            // handle reporting itself finished.
            yield return StageSeam.End();

            while (_boot != null && !_boot.Finished) yield return null;

            yield return Leave();
            State = Step.Done;
        }

        /// <summary>
        /// Wake Main back up this instant, without waiting for the unload. See RallyStage.WakeNow —
        /// the caller's next line is usually the start of a fresh round, which touches a rigidbody,
        /// a cut mask and a camera that all have to be awake for it to take.
        /// </summary>
        public void WakeNow() => Wake();

        /// <summary>Wind the arena up early — a retry, or Escape pressed mid-match.</summary>
        public IEnumerator AbortNow()
        {
            if (State == Step.Idle || State == Step.Done || State == Step.Failed) yield break;
            _boot?.Abort();
            yield return Leave();
            State = Step.Done;
        }

        IEnumerator Leave()
        {
            State = Step.Leaving;

            // The way back is a seam too. Skipped on an abort, where the curtain is already down and
            // whoever slammed it owns raising it — see TurfStage.AbortNow and GameDirector.
            if (!StageSeam.InProgress)
            {
                string headline = "THE PICTURE", kicker = "BACK TO THE LAWN";
                GameDirector.Instance?.NextSeamLabel(out headline, out kicker);
                yield return StageSeam.Begin(MatchState.Seam.StageToRound, headline, kicker);
            }

            // Order matters. The arena is unloaded FIRST and Main woken afterwards, so there is
            // never a frame with two suns, two audio listeners and two cameras claiming to be main —
            // which renders as a single black frame and reads as a crash.
            if (_arena.IsValid() && _arena.isLoaded)
            {
                if (_home.IsValid()) SceneManager.SetActiveScene(_home);
                var unload = SceneManager.UnloadSceneAsync(_arena);
                if (unload != null) yield return unload;
            }

            Wake();
            _boot = null;

            // Left DOWN on purpose — the round's next act re-frames the camera for the reveal, and
            // raising here would show one frame of the lawn through the arena's lens first.
            // GameDirector raises it once the reveal has taken. See its Reveal case.
        }

        void Sleep()
        {
            _slept.Clear();
            foreach (var root in _home.GetRootGameObjects())
            {
                if (!root.activeSelf) continue;           // already off; not ours to turn back on
                if (System.Array.IndexOf(KeepAwake, root.name) >= 0) continue;
                root.SetActive(false);
                _slept.Add(root);
            }
        }

        void Wake()
        {
            foreach (var go in _slept)
                if (go != null) go.SetActive(true);
            _slept.Clear();
        }
    }
}
