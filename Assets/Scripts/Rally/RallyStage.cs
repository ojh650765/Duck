using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckMow
{
    /// <summary>
    /// Takes the round out to the rally arena and brings it back.
    ///
    /// The load is ADDITIVE and Main is put to sleep behind it, rather than a straight scene swap.
    /// That is not a shortcut around building a real level — GooseRally.unity is a separate scene
    /// with its own ground, lighting, cast and camera, and it is opened and played on its own during
    /// development. It is a decision about what the round is allowed to lose.
    ///
    /// A single-scene load destroys Main, and Main is holding the thing the whole game is about: the
    /// cut mask, which is the picture the player has just spent seventy-five seconds mowing. There is
    /// no way to reload that scene and have the reveal show what they actually cut, so a swap would
    /// mean either re-mowing the round on the way back or reducing the player's artwork to a number.
    /// It would also cost the instant retry, which the round is built around — see GameDirector.
    ///
    /// So: Main sleeps, the arena runs in front of it, the arena unloads, Main wakes exactly as it
    /// was and the reveal shows the real picture with the rally's damage carried in beside it. The
    /// three roots kept awake are the ones the arena genuinely shares — the input pipeline the player
    /// is driving with, the audio bus, and the touch controls.
    /// </summary>
    public class RallyStage
    {
        public const string SceneName = "GooseRally";

        public enum Step { Idle, Loading, Playing, Leaving, Done, Failed }

        public Step State { get; private set; } = Step.Idle;
        public bool Finished => State == Step.Done || State == Step.Failed;
        /// <summary>True when the arena never opened, so the round can carry on without it.</summary>
        public bool Failed => State == Step.Failed;

        /// <summary>
        /// Main's roots that stay awake behind the arena.
        ///
        /// Systems carries GameDirector and InputReader: the first is what is waiting for the arena
        /// to finish, and the second is what the player steers the rally mower with — so the machine
        /// in the arena is driven by the identical input path, smoothing and all, as the machine on
        /// the lawn. Audio is the bus. Touch is the controls, which a phone still needs.
        /// </summary>
        static readonly string[] KeepAwake = { "~ Systems", "~ Audio", "~ Touch Controls" };

        readonly List<GameObject> _slept = new(16);
        Scene _home;
        Scene _arena;
        RallyBootstrap _boot;

        public IEnumerator Run(int roundNumber, float pictureScore, ShapeId shape)
        {
            State = Step.Loading;
            RallyHandoff.Send(roundNumber, pictureScore, shape);

            _home = SceneManager.GetActiveScene();

            // The OUTRO. The round's last frame is pushed through rather than cut away from: the
            // camera dollies in, the picture the player just mowed is what the leaves blow across,
            // and the pose is recorded so the arena's establishing shot knows which way the move was
            // going. See StageSeam — everything in this game that changes scene goes through it.
            yield return StageSeam.Begin(MatchState.Seam.RoundToRally,
                                         "GOOSE RALLY", StageSeam.RoundKicker(roundNumber));

            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                // Almost always the scene missing from the build settings, which is a wiring fault
                // rather than a gameplay one — so say which, and let the round continue without the
                // rally rather than stranding it one beat short of the reveal.
                Debug.LogError($"[Rally] '{SceneName}' could not be loaded. Is it in the build " +
                               "settings? Run 'Duck/3 · Build goose rally scene'.");
                RallyHandoff.Clear();
                yield return StageSeam.End();
                State = Step.Failed;
                yield break;
            }
            yield return load;

            _arena = SceneManager.GetSceneByName(SceneName);
            if (!_arena.IsValid() || !_arena.isLoaded)
            {
                Debug.LogError($"[Rally] '{SceneName}' loaded but is not valid.");
                RallyHandoff.Clear();
                yield return StageSeam.End();
                State = Step.Failed;
                yield break;
            }

            Sleep();
            // Active scene decides lighting and skybox, so the arena has to own it or it is lit by
            // the lawn's sun from underneath the floor.
            SceneManager.SetActiveScene(_arena);

            _boot = Object.FindFirstObjectByType<RallyBootstrap>();
            if (_boot == null)
            {
                Debug.LogError("[Rally] the arena scene has no RallyBootstrap.");
                yield return Leave();
                State = Step.Failed;
                yield break;
            }

            State = Step.Playing;

            // The curtain comes up ONTO the arena's establishing shot, not before it and not after
            // it. StageSeam waits for the arena to have actually composed a frame first, so the
            // reveal is a garden appearing rather than a flash of whatever the camera was on when
            // it was switched.
            yield return StageSeam.End();

            while (_boot != null && !_boot.Finished) yield return null;

            yield return Leave();
            State = Step.Done;
        }

        /// <summary>
        /// Wake Main back up this instant, without waiting for the unload.
        ///
        /// Split out of <see cref="AbortNow"/> because the caller's next line is usually the start of
        /// a fresh round, and that touches a rigidbody, a cut mask and a camera which all have to be
        /// awake for it to take. UnloadSceneAsync is a yield away at best; the round cannot be asked
        /// to wait a frame for it, and it does not need to — an arena still loaded for one more frame
        /// behind a woken Main is invisible, whereas a round set up against sleeping objects is a
        /// mower that starts the countdown in the wrong place.
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

            // The way back is a seam too, and it used to be the worst one in the game: the arena
            // simply vanished and the reveal cut in from ninety metres up. Now the arena's last beat
            // is swept away by the same weather that brought it, and the picture is underneath when
            // the frame clears.
            //
            // Skipped entirely on an abort, where the curtain is already down and somebody else owns
            // raising it — see AbortNow.
            if (!StageSeam.InProgress)
            {
                string headline = "THE PICTURE", kicker = "BACK TO THE LAWN";
                GameDirector.Instance?.NextSeamLabel(out headline, out kicker);
                yield return StageSeam.Begin(MatchState.Seam.StageToRound, headline, kicker);
            }

            // Order matters. The arena is unloaded FIRST and Main woken afterwards, so there is never
            // a frame with two suns, two audio listeners and two cameras claiming to be main — which
            // renders as a single black frame and reads as a crash.
            if (_arena.IsValid() && _arena.isLoaded)
            {
                if (_home.IsValid()) SceneManager.SetActiveScene(_home);
                var unload = SceneManager.UnloadSceneAsync(_arena);
                if (unload != null) yield return unload;
            }

            Wake();
            _boot = null;

            // Left DOWN on purpose. The round's next act is the reveal, which re-frames the camera
            // ninety metres up and re-evaluates the picture; raising the curtain here would show a
            // frame of the lawn from the arena's lens first. GameDirector raises it once the reveal
            // has taken. See GameDirector's Reveal case.
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
