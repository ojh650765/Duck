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
    /// the bench says WHO TOOK IT; the board says where it leaves everybody.
    ///
    /// ---- the cards carry a face, not a percentage ----
    ///
    /// They used to hold up each contestant's share of the arena in whole percent, in finishing
    /// order. That was three numbers the player had just finished reading off the overhead, held up
    /// again by three animals — and it made the bench a second display rather than a verdict.
    ///
    /// <see cref="RallyVerdict"/> settled this argument for the other arena and the argument is
    /// identical here: a mark answers "how well did you do", and this stage is not marked out of ten
    /// by anybody — it is WON, on ground held. A photograph answers "who took it" in a way three
    /// digits never could, particularly to the three losing gardeners sitting in the plaza watching
    /// somebody else's portrait go up. So the same mechanism is reused rather than reinvented:
    /// <see cref="ContestantPortraits"/> renders the faces and <see cref="JudgeCharacter.ShowPortrait"/>
    /// hangs them on the cards, exactly as the rally does.
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
        [Tooltip("Portraits, rendered from the real models. Without it the cards come up blank and " +
                 "the beat says nothing.")]
        public ContestantPortraits portraits;

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
        /// <summary>Whose face the cards are showing. Empty when nobody could be named.</summary>
        public string Winner { get; private set; } = "";

        float _time;
        int _raised;
        bool _running;
        Color _livery = Color.white;

        /// <summary>
        /// Who the bench is about to name, and why it is this and not the championship's table.
        ///
        /// <see cref="TurfDirector.Winner"/> is the slot at the head of the arena's own standings,
        /// which are sorted on CELLS HELD at the horn. That is what winning means in this stage, and
        /// it is the same order every other thing the player has just looked at was drawn from: the
        /// overhead's percentages, the placings on the stage card, and <see cref="TurfHandoff"/>'s
        /// <c>place</c>, which is what the championship banks. So the face going up cannot disagree
        /// with the standings the player is reading beside it.
        ///
        /// It is deliberately NOT read off <see cref="Tournament.Standings"/>. Those are not closed
        /// for this stage until the arena has finished and handed back — the director does it on the
        /// way out — so asking here would either return the PREVIOUS round's table or force this
        /// beat to close the round early, which is the one thing an arena must not do while it is
        /// still on screen.
        ///
        /// The one seam where the two rules could part is a rounding tie: marks come off the share
        /// through <see cref="Tournament.BloomMarks"/>, which is monotonic in share but rounds to a
        /// whole number, so two gardeners a fraction of a percent apart can bank the same mark and be
        /// separated alphabetically on the table while the ground still separates them here. Ground
        /// is the right answer for this bench — it is the thing the stage measured — and the
        /// director already raises <see cref="TurfDirector.DeadHeat"/> for that case.
        /// </summary>
        string NameWinner(out Color livery)
        {
            livery = Color.white;
            int slot = director != null ? director.Winner : -1;
            if (slot < 0) return "";
            var s = TurfArena.Get(slot);
            livery = s.livery;
            return s.contestant;
        }

        /// <summary>Begin. Called by the director the moment the standings are settled.</summary>
        public void Begin()
        {
            // Named before the bench is checked, so the stage still knows who won even in a scene
            // with no judges in it to say so.
            Winner = NameWinner(out _livery);

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
        /// Put the next card up, carrying the winner's face.
        ///
        /// All three carry the SAME face, which is the point of it: this is one verdict delivered by
        /// three people, not three separate opinions. The rally does exactly this and the reasoning
        /// carries over whole — see the class note.
        ///
        /// The card is tinted to the winner's livery rather than to whoever is holding it, so the
        /// result is legible from across the plaza to somebody who cannot make out a face at that
        /// distance. That is the same job the per-slot tint used to do for the per-slot numbers.
        /// </summary>
        void RaiseNext()
        {
            var c = panel.judges[_raised]?.character;
            int at = _raised;
            _raised++;
            if (c == null) return;

            c.CardUp = 1f;

            // Instance is the fallback and not the route: the arena builds portraits of its own, the
            // same way the rally's does, so the faces are there in a scene opened on its own. But a
            // BloomRush.unity saved before that wiring existed still has a bench, and in a
            // championship the venue's own portraits are alive behind the arena — Main is asleep,
            // not unloaded, and the static outlives a deactivated root. Reaching for that is the
            // difference between a face and a blank on a scene nobody has rebuilt yet.
            var source = portraits != null ? portraits : ContestantPortraits.Instance;
            var tex = source != null && !string.IsNullOrEmpty(Winner) ? source.Get(Winner) : null;
            if (tex != null) c.ShowPortrait(tex);
            else c.SetCardNumber(1);          // a bench with no portrait still has to say SOMETHING

            // Said out loud, exactly as the rally says it, because a blank card is the one failure
            // this beat cannot survive and it has four causes that look identical on screen: no
            // portraits anywhere, no winner to name, no subject rendered for that contestant, or a
            // card rig with nothing to hang the picture on.
            Debug.Log($"[Bloom] card {_raised} for '{Winner}': portraits=" +
                      $"{(portraits != null ? "wired" : source != null ? "via Instance" : "NONE")} " +
                      $"texture={(tex != null ? tex.width + "px" : "NULL")} " +
                      $"cardNumber={(c.cardNumber != null ? "yes" : "NULL")} " +
                      $"card={(c.card != null ? c.card.name : "NULL")}");

            if (c.cardRenderer != null)
                c.cardRenderer.material.color = Color.Lerp(Color.white, _livery, 0.55f);

            // Every card is the same good news now, so every card is applauded and the room builds
            // rather than the second and third being read out flat. That flatness was correct while
            // the cards were three different contestants' numbers and is simply wrong for three
            // copies of one result.
            c.Punch(0.9f);
            AudioDirector.Instance?.CrowdCheer(0.4f + at * 0.2f, applaud: _raised >= 3);
        }
    }
}
