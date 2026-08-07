using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The end of Bloom Rush: the bench reads the ground back.
    ///
    /// This exists because the stage was ending on a results screen — four lines of text in the
    /// middle of the frame — and a results screen is not this game's language. Every other verdict
    /// in it is delivered by three animals at a desk: they lean in, a card comes up off the table
    /// and slams flat, and the camera pushes in on whoever is holding one. A player who has watched
    /// that panel mark three rounds can read it without being told, and that recognition is worth
    /// more than any bespoke card this mode could have drawn for itself.
    ///
    /// It is also the honest presentation of what this stage actually is. Bloom Rush does not decide
    /// anything — see <see cref="TurfDirector"/>: it measures ground and hands the number to the
    /// competition. Ending on the panel is that fact made visible. The overhead says WHAT happened;
    /// the bench says what it was worth; the board says where it leaves everybody.
    ///
    /// Driven straight off <see cref="JudgeCharacter"/> rather than through <see cref="JudgePanel"/>,
    /// the same choice <see cref="RallyVerdict"/> makes and for the same reason: the panel's job is
    /// deliberation over a RoundScore, which is not what is being shown here, while the presentation
    /// — attention, the raise, the slam, the settle — all lives on the characters. Borrowing the
    /// half that is about performance and leaving the half that is about arithmetic is what makes
    /// this a reuse rather than a contortion.
    /// </summary>
    [DefaultExecutionOrder(-18)]
    public class TurfVerdict : MonoBehaviour
    {
        public enum Step { Idle, Settle, LeanIn, Raise, Hold, Done }

        [Header("Wiring")]
        public TurfDirector director;
        public JudgePanel panel;
        public CameraDirector cameraDirector;

        [Header("Timing")]
        [Tooltip("Seconds held on the overhead before the camera comes down to the bench.")]
        public float settleSeconds = 2.6f;
        [Tooltip("Seconds the three of them take to look up and pay attention.")]
        public float leanSeconds = 1.1f;
        [Tooltip("Seconds between one card going up and the next.")]
        public float raiseInterval = 0.85f;
        [Tooltip("Seconds the three cards are held together before the board.")]
        public float holdSeconds = 1.8f;

        public Step State { get; private set; } = Step.Idle;
        public bool Finished => State == Step.Done;

        float _time;
        int _raised;
        bool _running;

        /// <summary>Begin. Called by the director the moment the standings are settled.</summary>
        public void Begin()
        {
            if (panel == null || panel.judges == null || panel.judges.Length == 0)
            {
                // No bench in this scene is a wiring fault, not a reason to strand the stage one
                // beat short of its ending. The director's own reveal carries on without it.
                State = Step.Done;
                return;
            }

            _running = true;
            _raised = 0;
            _time = 0f;
            State = Step.Settle;
            Attention(0f);
            foreach (var j in panel.judges)
                if (j?.character != null) { j.character.CardUp = 0f; j.character.ClearPortrait(); }
        }

        void Update()
        {
            if (!_running || SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Stepped rather than run as a coroutine, for the same reason the judging panel is: the
        /// capture harness advances the game on a scripted clock, and a sequence that only moves on
        /// real frames cannot be photographed.
        /// </summary>
        public void Tick(float dt)
        {
            if (!_running) return;
            _time += dt;

            switch (State)
            {
                case Step.Settle:
                    // The overhead is still up and still doing its job — the percentages are
                    // counting on it. The bench is not brought in until they have landed, or two
                    // things are asking to be read at once.
                    if (_time < settleSeconds) break;
                    Advance(Step.LeanIn);
                    cameraDirector?.SetMode(CameraMode.Judges, 1.7f);
                    break;

                case Step.LeanIn:
                    Attention(Mathf.Clamp01(_time / Mathf.Max(leanSeconds, 0.01f)));
                    if (_time < leanSeconds) break;
                    Advance(Step.Raise);
                    break;

                case Step.Raise:
                    // One at a time, in seating order. Three cards arriving together is a graphic;
                    // three arriving in turn is three people each making up their own mind.
                    while (_raised < panel.judges.Length && _time >= _raised * raiseInterval)
                        RaiseNext();
                    if (_raised < panel.judges.Length) break;
                    Advance(Step.Hold);
                    break;

                case Step.Hold:
                    if (_time < holdSeconds) break;
                    Advance(Step.Done);
                    _running = false;
                    break;
            }
        }

        void Advance(Step next) { State = next; _time = 0f; }

        void Attention(float amount)
        {
            foreach (var j in panel.judges)
                if (j?.character != null) j.character.Attention = amount;
        }

        /// <summary>
        /// Put the next card up, carrying a percentage.
        ///
        /// The cards read the SHARE OF THE ARENA, in whole percent, in the order the standings
        /// finished — so the first card up is the winner's number. Not a mark out of ten: this
        /// stage does not award marks, it measures ground, and printing a ten here would claim an
        /// authority it deliberately does not have. The number on the card is the same number on
        /// the board and the same number the panel will fold into the round.
        /// </summary>
        void RaiseNext()
        {
            var standings = director != null ? director.Standings : null;
            var c = panel.judges[_raised]?.character;
            int at = _raised;
            _raised++;
            if (c == null) return;

            c.CardUp = 1f;

            var mask = TurfMask.Instance;
            if (mask != null && standings != null && at < standings.Count)
            {
                int slot = standings[at];
                c.SetCardNumber(Mathf.RoundToInt(mask.Share(slot) * 100f));
                // Tinted to the gardener whose number it is, so three cards in a row are three
                // contestants rather than three digits.
                if (c.cardRenderer != null)
                    c.cardRenderer.material.color = Color.Lerp(Color.white, TurfArena.Livery(slot), 0.55f);
            }

            // The reaction is the sign of the result from THIS bench's point of view: the winner's
            // card is applauded, the rest are simply read out.
            c.Punch(at == 0 ? 0.9f : -0.25f);
            AudioDirector.Instance?.CrowdCheer(at == 0 ? 0.7f : 0.2f);
        }
    }
}
