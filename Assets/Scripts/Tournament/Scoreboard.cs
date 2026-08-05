using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    [System.Serializable]
    public class ScoreboardRow
    {
        public Transform root;
        public MeshRenderer plate;
        public TMPro.TextMeshPro place;
        public TMPro.TextMeshPro name;
        public TMPro.TextMeshPro total;
        public TMPro.TextMeshPro grade;

        [HideInInspector] public Vector3 restPosition;
        [HideInInspector] public Material plateInstance;
    }

    /// <summary>
    /// The championship board at the plaza.
    ///
    /// The rows do not simply appear in final order. They arrive in the order the tour visited the
    /// plots, then re-sort themselves in front of you — the whole point of ending here is watching
    /// your name move, so the board animates the change rather than reporting it.
    /// </summary>
    public class Scoreboard : MonoBehaviour
    {
        public TMPro.TextMeshPro title;
        public TMPro.TextMeshPro winnerLine;
        public ScoreboardRow[] rows = new ScoreboardRow[0];

        [Header("Timing")]
        public float rowInterval = 0.55f;
        public float sortDelay = 0.9f;
        public float sortDuration = 1.35f;
        public float winnerDelay = 0.7f;

        public bool Finished { get; private set; }

        static readonly Color PlayerPlate = new Color(1f, 0.86f, 0.52f);
        static readonly Color RivalPlate = new Color(0.86f, 0.79f, 0.64f);

        readonly List<Standing> _order = new List<Standing>(4);
        List<Standing> _final;
        float _clock;
        int _revealed;
        bool _running;
        bool _sorting;
        float _sortT;
        Vector3[] _from, _to;
        int[] _dest;

        void Awake() => ResetBoard();

        public void ResetBoard()
        {
            Finished = false;
            _running = false;
            _sorting = false;
            _clock = 0f;
            _revealed = 0;
            _order.Clear();

            foreach (var r in rows)
            {
                if (r?.root == null) continue;
                r.restPosition = r.root.localPosition;
                SetRowAlpha(r, 0f);
                if (r.plate != null)
                {
                    if (r.plateInstance == null)
                    {
                        r.plateInstance = new Material(r.plate.sharedMaterial);
                        r.plate.sharedMaterial = r.plateInstance;
                    }
                    r.plate.enabled = false;
                }
            }
            if (winnerLine != null) { winnerLine.text = ""; winnerLine.alpha = 0f; }
        }

        /// <summary>
        /// Post one contestant's result. Called by the tour as it reaches each plot, so the board
        /// fills in as the camera travels rather than all at once at the end.
        /// </summary>
        public void Post(Standing s)
        {
            if (_order.Count >= rows.Length) return;
            _order.Add(s);
            _running = true;
        }

        /// <summary>All results are in — sort into rank order and announce the winner.</summary>
        public void Settle(IReadOnlyList<Standing> finalOrder)
        {
            _final = new List<Standing>(finalOrder);
            _sorting = true;
            _sortT = -sortDelay;
            _from = new Vector3[rows.Length];
            _to = new Vector3[rows.Length];

            // Where each posted row ends up. This has to be a genuine permutation — if two rows
            // are ever assigned the same slot they stack on top of each other and leave a hole,
            // which is exactly what a mismatched name or a contestant who never got posted would
            // do. So the mapping is built by name, recorded, and any row that failed to match is
            // parked out of the way rather than being left to collide with a real one.
            _dest = new int[rows.Length];
            var taken = new bool[rows.Length];

            for (int i = 0; i < rows.Length; i++)
            {
                _from[i] = rows[i]?.root != null ? rows[i].root.localPosition : Vector3.zero;
                _dest[i] = -1;

                if (i >= _order.Count) continue;
                int k = _final.FindIndex(s => s.name == _order[i].name);
                if (k < 0 || k >= rows.Length || taken[k])
                {
                    Debug.LogWarning($"[Duck] Scoreboard could not place '{_order[i].name}' " +
                                     $"(match={k}); leaving its row where it is.");
                    continue;
                }
                _dest[i] = k;
                taken[k] = true;
            }

            for (int i = 0; i < rows.Length; i++)
                _to[i] = _dest[i] >= 0 ? rows[_dest[i]].restPosition : _from[i];
        }

        public void Tick(float dt)
        {
            if (_running && _revealed < _order.Count)
            {
                _clock += dt;
                if (_clock >= rowInterval * _revealed)
                {
                    Fill(rows[_revealed], _order[_revealed]);
                    _revealed++;
                }
            }

            // Rows fade up individually so a result lands with the camera rather than before it.
            for (int i = 0; i < _revealed && i < rows.Length; i++)
            {
                var r = rows[i];
                if (r?.place == null) continue;
                float a = Mathf.MoveTowards(r.place.alpha, 1f, dt * 3.2f);
                SetRowAlpha(r, a);
            }

            if (!_sorting) return;

            _sortT += dt;
            if (_sortT < 0f) return;

            float t = Mathf.Clamp01(_sortT / Mathf.Max(sortDuration, 0.01f));
            // Overshoot slightly on the way in: the rows arrive with a shunt, not a glide.
            float e = 1f - Mathf.Pow(1f - t, 3f);
            e += Mathf.Sin(t * Mathf.PI) * 0.06f;

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i]?.root == null) continue;
                rows[i].root.localPosition = Vector3.LerpUnclamped(_from[i], _to[i], e);
            }

            if (t < 1f) return;

            // Renumber into final order once everything has landed.
            if (!Finished && _final != null)
            {
                // Numbered from the same mapping that moved them, so the number on a row and the
                // slot it landed in can never disagree.
                for (int i = 0; i < rows.Length; i++)
                {
                    if (_dest == null || _dest[i] < 0 || rows[i]?.place == null) continue;
                    rows[i].place.text = Place(_dest[i] + 1);
                }

                if (winnerLine != null && _final.Count > 0)
                {
                    var w = _final[0];
                    winnerLine.text = w.isPlayer
                        ? $"CHAMPION — {w.name}!"
                        : $"CHAMPION — {w.name} THE {w.species.ToUpperInvariant()}";
                    winnerLine.color = w.livery;
                }
                Finished = true;
            }

            if (winnerLine != null)
                winnerLine.alpha = Mathf.MoveTowards(winnerLine.alpha, 1f, dt * 1.6f);
        }

        void Fill(ScoreboardRow r, Standing s)
        {
            if (r == null) return;
            // No place number yet. Rows arrive in the order the tour visited the plots, and until
            // the last contestant is in, that order says nothing about who is winning — numbering
            // them as they land meant the board showed tour order in the numbers and rank order in
            // the positions, which read as a scrambled list.
            if (r.place != null) r.place.text = "";
            if (r.name != null) r.name.text = s.isPlayer ? $"{s.name}  (YOU)" : s.name;
            if (r.total != null) r.total.text = $"{s.total:0} / 30";
            if (r.grade != null) { r.grade.text = s.rank; r.grade.color = s.livery; }
            if (r.plate != null)
            {
                r.plate.enabled = true;
                r.plateInstance?.SetColor("_BaseColor", s.isPlayer ? PlayerPlate : RivalPlate);
            }
        }

        static void SetRowAlpha(ScoreboardRow r, float a)
        {
            if (r.place != null) r.place.alpha = a;
            if (r.name != null) r.name.alpha = a;
            if (r.total != null) r.total.alpha = a;
            if (r.grade != null) r.grade.alpha = a;
        }

        static string Place(int n) => n switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{n}th"
        };
    }
}
