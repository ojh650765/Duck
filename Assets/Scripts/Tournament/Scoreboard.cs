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
        [HideInInspector] public bool restCaptured;
        [HideInInspector] public Material plateInstance;
    }

    /// <summary>
    /// The championship board at the plaza.
    ///
    /// Rows are written straight into rank order and never move. They used to arrive in the order
    /// the tour visited the plots and then animate into their final places, which was a better
    /// moment but carried a bug that got worse every round:
    ///
    ///     ResetBoard() captured each row's "rest position" from its CURRENT localPosition, and the
    ///     sort had already moved it there. So the second round's rest positions were the first
    ///     round's sorted positions, the third round's were the second's, and the mapping drifted
    ///     further from the authored layout every time. Two rows would eventually be sent to the
    ///     same slot, stack, and leave a hole — a board that looked shuffled rather than sorted,
    ///     and only ever on a replay, which is why it read as "the sort is broken".
    ///
    /// The fix is not to animate positions at all. The drama now comes from the ORDER OF REVEAL:
    /// the board fills from last place upward, so the top line — and therefore who won — is the
    /// final thing to land. That reads better than the shuffle did and it cannot desynchronise,
    /// because nothing is ever a permutation of anything.
    /// </summary>
    public class Scoreboard : MonoBehaviour
    {
        public TMPro.TextMeshPro title;
        public TMPro.TextMeshPro winnerLine;
        public ScoreboardRow[] rows = new ScoreboardRow[0];

        [Header("Timing")]
        [Tooltip("Seconds between rows landing, from last place upward.")]
        public float rowInterval = 0.55f;
        [Tooltip("Seconds before the first row lands, so the camera arrives to a blank board.")]
        public float revealDelay = 0.5f;
        public float winnerDelay = 0.7f;

        public bool Finished { get; private set; }

        static readonly Color PlayerPlate = new Color(1f, 0.86f, 0.52f);
        static readonly Color RivalPlate = new Color(0.86f, 0.79f, 0.64f);

        List<Standing> _final;
        string _winnerLabel = "WINNER";
        float _clock;
        int _landed;
        bool _running;
        float _winnerTimer;

        void Awake() => ResetBoard();

        public void ResetBoard()
        {
            Finished = false;
            _running = false;
            _clock = 0f;
            _landed = 0;
            _winnerTimer = 0f;
            _final = null;

            foreach (var r in rows)
            {
                if (r?.root == null) continue;

                // Captured ONCE, from the layout the builder authored. Re-reading this every round
                // is what corrupted the board — see the class comment. Restoring from it every
                // round is belt and braces: nothing moves rows any more, but if anything ever does,
                // the next round still starts from the real layout.
                if (!r.restCaptured)
                {
                    r.restPosition = r.root.localPosition;
                    r.restCaptured = true;
                }
                r.root.localPosition = r.restPosition;

                if (r.place != null) r.place.text = "";
                if (r.name != null) r.name.text = "";
                if (r.total != null) r.total.text = "";
                if (r.grade != null) r.grade.text = "";
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
        /// Called by the tour as it reaches each plot.
        ///
        /// Kept because the tour's pacing is built around it, but it no longer touches the board:
        /// posting results in tour order was the thing that required a permutation later. The board
        /// is at the plaza and the tour camera is out over the lawns, so nothing was being watched
        /// here anyway.
        /// </summary>
        public void Post(Standing s) { }

        /// <summary>
        /// All results are in. Write them in rank order and reveal from the bottom up.
        ///
        /// <paramref name="winnerLabel"/> is what the bottom line calls the contestant on top. It has
        /// to be told rather than assumed, because this board shows one picture out of several: the top
        /// of it is the winner of THIS one, and calling them the champion — which it used to, every
        /// single time — announced a title three times over and got it wrong twice.
        ///
        /// The board's own <see cref="title"/> is deliberately left alone. It carries the venue's name
        /// as the builder painted it, and this method briefly overwrote it with the round number,
        /// which is presentation the game no longer does anywhere.
        /// </summary>
        public void Settle(IReadOnlyList<Standing> finalOrder, string winnerLabel = "WINNER")
        {
            if (finalOrder == null) return;

            _winnerLabel = string.IsNullOrEmpty(winnerLabel) ? "WINNER" : winnerLabel;

            _final = new List<Standing>(finalOrder);
            _clock = 0f;
            _landed = 0;
            _winnerTimer = 0f;
            Finished = false;

            // Row 0 is the top line. The standings arrive already sorted by the tournament, so this
            // is a straight copy — no matching by name, no slot allocation, nothing that can fail.
            // Places come off the shared rule so ties read 1st / 2nd / 2nd / 4th here and in the
            // outro card, which are on screen at the same time.
            var places = Scoring.CompetitionPlaces(_final);

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null) continue;
                if (i < _final.Count) Fill(rows[i], _final[i], places[i]);
                SetRowAlpha(rows[i], 0f);
                if (rows[i].plate != null) rows[i].plate.enabled = false;
            }

            _running = true;
        }

        public void Tick(float dt)
        {
            if (!_running || _final == null) return;

            _clock += dt;

            // Land one row at a time from last place upward, so the winner's line is last.
            int filled = Mathf.Min(_final.Count, rows.Length);
            if (_landed < filled && _clock >= revealDelay + rowInterval * _landed)
            {
                int row = filled - 1 - _landed;
                if (rows[row]?.plate != null) rows[row].plate.enabled = true;
                _landed++;
            }

            // Fade in whatever has landed. Indices at or below (filled - _landed) are still to come.
            for (int i = 0; i < filled; i++)
            {
                var r = rows[i];
                if (r?.place == null) continue;
                bool landed = i >= filled - _landed;
                float a = Mathf.MoveTowards(r.place.alpha, landed ? 1f : 0f, dt * 3.4f);
                SetRowAlpha(r, a);
            }

            if (_landed < filled) return;

            _winnerTimer += dt;
            if (_winnerTimer < winnerDelay) return;

            if (!Finished && _final.Count > 0)
            {
                var w = _final[0];
                if (winnerLine != null)
                {
                    winnerLine.text = w.isPlayer
                        ? $"{_winnerLabel} — {w.name}!"
                        : $"{_winnerLabel} — {w.name} THE {w.species.ToUpperInvariant()}";
                    winnerLine.color = w.livery;
                }
                Finished = true;
            }

            if (winnerLine != null)
                winnerLine.alpha = Mathf.MoveTowards(winnerLine.alpha, 1f, dt * 1.6f);
        }

        void Fill(ScoreboardRow r, Standing s, int place)
        {
            if (r == null) return;
            if (r.place != null) r.place.text = Place(place);
            if (r.name != null) r.name.text = s.isPlayer ? $"{s.name}  (YOU)" : s.name;
            if (r.total != null) r.total.text = $"{s.total:0} / 30";
            if (r.grade != null) { r.grade.text = s.rank; r.grade.color = s.livery; }
            if (r.plate != null)
                r.plateInstance?.SetColor("_BaseColor", s.isPlayer ? PlayerPlate : RivalPlate);
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
