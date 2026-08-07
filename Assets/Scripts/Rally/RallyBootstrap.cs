using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Runs GooseRally.unity. What makes it a level you can open and play rather than a set the
    /// round teleports into.
    ///
    /// Two ways in, and it does not care which:
    ///   * From a round — GameDirector has loaded this scene alongside Main and is waiting on
    ///     <see cref="Finished"/>. The results go back through <see cref="RallyHandoff"/>.
    ///   * On its own — pressed play with the scene open. Nothing is waiting and nothing is loaded
    ///     afterwards; it just runs, which is the entire review loop for the mode. Every feel
    ///     adjustment in the rally is checkable in about four seconds because of this.
    ///
    /// It creates an <see cref="InputReader"/> only if there isn't one, which is the standalone case.
    /// Entering from a round, Main's is still alive and still the singleton — so the player drives
    /// the rally with the identical input pipeline they drove the round with, smoothing and all.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class RallyBootstrap : MonoBehaviour
    {
        [Tooltip("The match. Found in this scene if left empty.")]
        public RallyDirector director;
        [Tooltip("Extra seconds held on the finished arena before the round is told it can move on.")]
        public float exitHold = 1.6f;
        [Tooltip("The bench that names the winner. Null is fine — the match simply ends without one.")]
        public RallyVerdict verdict;

        public bool Finished { get; private set; }
        /// <summary>True when a round is waiting on this rather than it being a standalone review run.</summary>
        public bool FromRound { get; private set; }

        float _hold;

        [Tooltip("This scene's own audio bus. Shut down when a round is already running one, so the " +
                 "arena never plays a second crowd over the top of the venue's.")]
        public AudioDirector localAudio;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<RallyDirector>();

            // Standalone only. Entering from a round, Main's reader is alive and this finds it.
            if (InputReader.Instance == null)
            {
                var go = new GameObject("~ InputReader (standalone)");
                go.transform.SetParent(transform, false);
                go.AddComponent<InputReader>();
            }

            // One audio bus, always.
            //
            // The arena carries its own so it can be opened and played on its own — without it every
            // honk, thud and cheer the match fires does nothing at all, silently. But entering from a
            // round, Main's bus is still alive behind us: two directors means two ambience beds, two
            // crowd loops and two engines mixed on top of each other, and it is also how one of them
            // ends up being ticked through a singleton before Unity has run its Awake. So the arena's
            // own stands down whenever it is the second one in the room.
            if (localAudio == null) localAudio = GetComponentInChildren<AudioDirector>(true);
            if (localAudio == null)
                foreach (var a in FindObjectsByType<AudioDirector>(FindObjectsSortMode.None))
                    if (a.gameObject.scene == gameObject.scene) { localAudio = a; break; }

            if (localAudio == null) return;
            foreach (var other in FindObjectsByType<AudioDirector>(FindObjectsSortMode.None))
            {
                if (other == localAudio) continue;
                localAudio.gameObject.SetActive(false);
                Debug.Log("[Rally] a round is already running the audio bus; the arena's own is off.");
                break;
            }
        }

        void Start()
        {
            if (director == null)
            {
                Debug.LogError("[Rally] GooseRally has no RallyDirector; nothing to run. Rebuild with " +
                               "'Duck/3 · Build goose rally scene'.");
                Finished = true;
                return;
            }

            FromRound = RallyHandoff.Active;
            Debug.Log(FromRound
                ? $"[Rally] round {RallyHandoff.RoundNumber}, picture {RallyHandoff.PictureScore:0.0}."
                : "[Rally] standalone review run — no round is waiting on this.");

            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = true;
            director.Begin();
        }

        void Update()
        {
            if (director == null || Finished) return;
            if (!director.Finished) return;

            // The bench, once the match is over and the results are banked.
            //
            // Started here rather than by the director because it is not part of the match — the
            // match is decided the moment the horn goes, and this is the ceremony that follows.
            // Keeping the two apart means a match aborted mid-flight never has to unwind a
            // half-delivered verdict.
            if (verdict != null && verdict.State == RallyVerdict.Step.Idle)
            {
                var winner = TopContestant(out Color livery);
                verdict.Begin(winner, livery);
                if (director.cameraDirector != null)
                {
                    // Cut to the bench, the same way the round's own judging beat does — see
                    // GameDirector's Judging case on why this is a cut and not a blend.
                    director.cameraDirector.SetMode(CameraMode.Judges, 0f);
                    director.cameraDirector.SnapToCurrent();
                }
            }
            if (verdict != null && !verdict.Finished) return;

            // The director posts the results the moment its settle beat ends, so by the time this is
            // reached they are already banked. All that is left is to hold on the finished arena
            // long enough for the last feathers to come down.
            _hold += Time.unscaledDeltaTime;
            if (_hold < exitHold) return;
            Finished = true;

            if (!FromRound)
                Debug.Log("[Rally] standalone run complete — staying put so the arena can be inspected.");
        }

        /// <summary>Wind the match up early, for a retry or a trip back to the menu mid-rally.</summary>
        public void Abort()
        {
            director?.Abort();
            verdict?.Abort();
            Finished = true;
        }

        /// <summary>
        /// Whoever ends with the most garden. Ties break on knockouts, then on parries — the
        /// contestant who did the most about it, rather than the one nobody happened to attack.
        /// </summary>
        string TopContestant(out Color livery)
        {
            RallyCompetitor best = null;
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = director.CompetitorAt(i);
                if (c == null) continue;
                if (best == null ||
                    c.Integrity > best.Integrity ||
                    (Mathf.Approximately(c.Integrity, best.Integrity) &&
                     (c.Knockouts > best.Knockouts ||
                      (c.Knockouts == best.Knockouts && c.Parries > best.Parries))))
                    best = c;
            }
            livery = best != null ? best.Livery : Color.white;
            return best != null ? best.Name : Venue.Player.contestant;
        }
    }
}
