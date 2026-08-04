using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>One line of the final standings.</summary>
    public struct Standing
    {
        public string name;
        public string species;
        public float total;        // out of 30
        public string rank;        // S..D
        public bool isPlayer;
        public Vector2 plotCentre;
        public Color livery;
    }

    /// <summary>
    /// The championship: the player and every rival, running the same round at the same time on
    /// their own plots, judged by the same rules, ranked on one board at the end.
    ///
    /// It owns nothing about how a contestant mows — the player has a physical mower and the
    /// rivals have routes — only that they all start together, stop together, and are compared
    /// on the same numbers.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class Tournament : MonoBehaviour
    {
        public static Tournament Instance { get; private set; }

        public RivalContestant[] rivals = Array.Empty<RivalContestant>();

        [Header("Player entry")]
        public string playerName = "DUCK";
        public string playerSpecies = "duck";
        public Color playerLivery = new Color(0.78f, 0.24f, 0.20f);

        [Header("Variety")]
        [Tooltip("Rivals mow different pictures from the player, so the venue does not look like a photocopier.")]
        public bool rivalsGetOwnShapes = true;

        /// <summary>Fired for anything a neighbour does that the player might notice.</summary>
        public event Action<RivalEvent> OnRivalEvent;

        public IReadOnlyList<Standing> Standings => _standings;
        readonly List<Standing> _standings = new List<Standing>(4);

        public Standing Winner => _standings.Count > 0 ? _standings[0] : default;
        public int PlayerPlace { get; private set; } = 1;

        System.Random _rng;
        bool _running;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(Environment.TickCount ^ 0x5EED);
            foreach (var r in rivals)
            {
                if (r == null) continue;
                var captured = r;
                captured.OnEvent += e => OnRivalEvent?.Invoke(e);
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Everyone starts at the klaxon. The player's shape comes in; rivals draw their own.</summary>
        public void BeginRound(ShapeId playerShape)
        {
            _standings.Clear();
            _running = true;

            var all = TargetShapes.All;
            foreach (var r in rivals)
            {
                if (r == null) continue;
                ShapeId s = playerShape;
                if (rivalsGetOwnShapes)
                {
                    int guard = 0;
                    do { s = all[_rng.Next(all.Length)]; } while (s == playerShape && ++guard < 12);
                }
                r.Begin(s);
            }
        }

        /// <summary>
        /// Step every rival. <paramref name="progress01"/> is the round's own progress, so the
        /// whole venue finishes together however long the player's picture happened to grant.
        /// </summary>
        public void Tick(float dt, float progress01)
        {
            if (!_running) return;
            foreach (var r in rivals)
            {
                if (r == null) continue;
                r.Tick(dt, progress01);
            }
        }

        /// <summary>Flush every rival's mask. Called before anything is going to look at a lawn.</summary>
        public void FlushMasks()
        {
            foreach (var r in rivals) r?.FlushMask();
        }

        /// <summary>
        /// Klaxon. Everyone downs tools, every plot is marked, and the board is built.
        /// The player's score arrives already computed by their own bench, so the two paths meet
        /// here and nowhere else.
        /// </summary>
        public void CloseRound(float playerTotal, string playerRank, Vector2 playerPlot)
        {
            _running = false;

            foreach (var r in rivals) r?.Finish();

            _standings.Clear();
            _standings.Add(new Standing
            {
                name = playerName,
                species = playerSpecies,
                total = playerTotal,
                rank = playerRank,
                isPlayer = true,
                plotCentre = playerPlot,
                livery = playerLivery
            });

            foreach (var r in rivals)
            {
                if (r == null) continue;
                _standings.Add(new Standing
                {
                    name = r.displayName,
                    species = r.species,
                    total = r.Total,
                    rank = r.Rank,
                    isPlayer = false,
                    plotCentre = r.plotCentre,
                    livery = r.liveryColour
                });
            }

            // Highest total wins; ties break toward the tidier outline, then alphabetically, so the
            // order is stable and never depends on which contestant happened to be listed first.
            _standings.Sort((a, b) =>
            {
                int byTotal = b.total.CompareTo(a.total);
                if (byTotal != 0) return byTotal;
                return string.CompareOrdinal(a.name, b.name);
            });

            PlayerPlace = 1;
            for (int i = 0; i < _standings.Count; i++)
                if (_standings[i].isPlayer) { PlayerPlace = i + 1; break; }
        }

        /// <summary>Find the rival working a given plot, for the tour.</summary>
        public RivalContestant RivalAt(Vector2 plotCentre)
        {
            foreach (var r in rivals)
                if (r != null && (r.plotCentre - plotCentre).sqrMagnitude < 1f) return r;
            return null;
        }
    }
}
