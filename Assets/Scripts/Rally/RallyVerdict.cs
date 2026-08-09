using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The end of the rally: the bench names a winner.
    ///
    /// This is the mowing round's judging beat, reused rather than reinvented — the same three
    /// characters, the same lean-in, the same card coming up off the desk and slamming to a stop,
    /// the same camera pushing in on whoever is currently holding one up. A player who has watched
    /// the panel mark three rounds already knows exactly how to read it, and that recognition is
    /// worth more than any bespoke sequence this mode could have had.
    ///
    /// One thing changes, and it is the right one. The rally is not scored out of ten, it is WON, so
    /// the cards carry the winner's face instead of a number. A mark answers "how well did you do";
    /// this beat answers "who took it", and a photograph answers that in a way three digits never
    /// could — particularly to the three losing contestants, who are sitting in the arena watching
    /// somebody else's portrait go up.
    ///
    /// Driven straight off <see cref="JudgeCharacter"/> rather than through <see cref="JudgePanel"/>:
    /// the panel's job is deliberation and arithmetic over a RoundScore, none of which applies here,
    /// while the presentation — attention, the raise, the slam, the settle — all lives on the
    /// characters. Borrowing the half that is about performance and leaving the half that is about
    /// marking is what makes this a reuse rather than a contortion.
    /// </summary>
    public class RallyVerdict : MonoBehaviour
    {
        public enum Step { Idle, Settle, LeanIn, Raise, Hold, Board, Done }

        [Header("Cast")]
        [Tooltip("The bench. Its three characters are driven directly.")]
        public JudgePanel panel;
        public CameraDirector cameraDirector;
        [Tooltip("Portraits, rendered from the real models. Without it the cards come up blank and " +
                 "the beat says nothing.")]
        public ContestantPortraits portraits;
        [Tooltip("The championship board behind the bench. The cards say WHO; the board says what " +
                 "it was worth — this round's marks out of thirty, which is the number that goes " +
                 "into the tournament. Null is tolerated and the beat ends at the cards.")]
        public Scoreboard scoreboard;

        [Header("Timing")]
        [Tooltip("Seconds after the horn before the bench takes over. Long enough for the last " +
                 "feathers to land, short enough that the arena does not go quiet.")]
        public float settleSeconds = 1.4f;
        [Tooltip("Seconds the judges lean in before anything is raised. The pause is the beat.")]
        public float leanSeconds = 1.0f;
        [Tooltip("Seconds between one card going up and the next.")]
        public float betweenCards = 0.45f;
        [Tooltip("Seconds a card takes to come up off the desk.")]
        public float raiseSeconds = 0.32f;
        [Tooltip("Seconds the three cards are held before the camera leaves for the board.")]
        public float holdSeconds = 3.2f;
        [Tooltip("Seconds the board is held after its last row lands.")]
        public float boardHoldSeconds = 2.6f;

        [Header("Debug")]
        [Tooltip("Skip the wait and deliver the verdict now, for reviewing the beat without playing " +
                 "a seventy-eight second match first. Editor tooling only.")]
        public bool debugJumpToCards;

        public Step State { get; private set; } = Step.Idle;
        public bool Finished => State == Step.Done;
        /// <summary>Who the cards are showing. Read by the HUD for its own card.</summary>
        public string Winner { get; private set; } = "";

        float _timer;
        int _raised;
        Color _livery = Color.white;

        /// <summary>Start the beat. <paramref name="winner"/> is whose face goes up.</summary>
        public void Begin(string winner, Color livery)
        {
            Winner = winner;
            _livery = livery;
            _timer = 0f;
            _raised = 0;
            State = Step.Settle;

            if (panel == null) return;
            foreach (var j in panel.judges)
            {
                var c = j?.character;
                if (c == null) continue;
                c.CardUp = 0f;
                c.Attention = 0f;
                c.ClearPortrait();
            }
        }

        public void Abort()
        {
            State = Step.Idle;
            if (panel == null) return;
            foreach (var j in panel.judges)
            {
                var c = j?.character;
                if (c == null) continue;
                c.CardUp = 0f;
                c.ClearPortrait();
            }
        }

        void Update()
        {
            if (State == Step.Idle || State == Step.Done) return;
            // Unscaled: the match's hit stop has no business slowing the ceremony down, and by this
            // point nothing is left in the arena for it to be freezing anyway.
            _timer += Time.unscaledDeltaTime;

            switch (State)
            {
                case Step.Settle:
                    if (_timer < settleSeconds && !debugJumpToCards) break;
                    Advance(Step.LeanIn);
                    Attention(1f);
                    AudioDirector.Instance?.CrowdCheer(0.35f);
                    break;

                case Step.LeanIn:
                    if (_timer < leanSeconds) break;
                    Advance(Step.Raise);
                    break;

                case Step.Raise:
                {
                    // One card at a time, on a stagger. Three going up together is a scoreboard;
                    // three going up one after another is a verdict being delivered.
                    int want = Mathf.Min(panel != null ? panel.judges.Length : 0,
                                         1 + Mathf.FloorToInt(_timer / Mathf.Max(betweenCards, 0.01f)));
                    while (_raised < want) RaiseNext();

                    for (int i = 0; i < _raised; i++)
                    {
                        var c = CharacterAt(i);
                        if (c == null) continue;
                        float since = _timer - i * betweenCards;
                        c.CardUp = Mathf.Clamp01(since / Mathf.Max(raiseSeconds, 0.01f));
                    }

                    if (panel != null && _raised >= panel.judges.Length &&
                        _timer > (panel.judges.Length - 1) * betweenCards + raiseSeconds)
                        Advance(Step.Hold);
                    break;
                }

                case Step.Hold:
                    if (_timer < holdSeconds) break;
                    if (!BeginBoard()) { State = Step.Done; break; }
                    Advance(Step.Board);
                    break;

                case Step.Board:
                    scoreboard.Tick(Time.unscaledDeltaTime);
                    if (!scoreboard.Finished) { _timer = 0f; break; }
                    if (_timer >= boardHoldSeconds) State = Step.Done;
                    break;
            }
        }

        void Advance(Step next) { State = next; _timer = 0f; }

        /// <summary>
        /// A standings table built from the rally alone, for when there is no championship.
        ///
        /// The only table this beat builds now — see <see cref="BeginBoard"/>, which stands aside
        /// entirely when there IS a championship, because the venue's board is coming and it has the
        /// running total on it. The marks are computed the way the championship would have banked
        /// them, so a standalone rally prints the same figure the same match would have scored
        /// inside a real evening.
        ///
        /// (An orphaned doc block describing BeginBoard's camera move used to sit above this one,
        /// left behind by an earlier edit. It has gone with the board it was describing.)
        /// </summary>
        System.Collections.Generic.List<Standing> StandingsFromRally()
        {
            var ranked = RallyHandoff.Ranked();
            if (ranked == null || ranked.Length == 0) return null;

            var list = new System.Collections.Generic.List<Standing>(ranked.Length);
            for (int i = 0; i < ranked.Length; i++)
            {
                var r = ranked[i];
                // The round's marks, out of thirty, off the same static the championship uses — so a
                // standalone rally and one played as round two print the same number for the same
                // garden. This used to carry the bare award in defenceAward with a zero total, which
                // was right while the rally was a bonus hung off a picture and is wrong now that it
                // is a round: the board would have shown "7" where every other round shows "21 / 30".
                float marks = Tournament.RallyMarks(r, i + 1, ranked.Length);
                list.Add(new Standing
                {
                    name = r.contestant,
                    species = RallyArena.Get(r.slot).species,
                    isPlayer = r.isPlayer,
                    livery = RallyArena.Get(r.slot).livery,
                    total = marks,
                    defenceAward = 0,
                    rallyMarked = false,
                    rank = Scoring.Rank(marks),
                });
            }
            return list;
        }

        bool BeginBoard()
        {
            if (scoreboard == null) return false;

            // ---- IN A CHAMPIONSHIP THIS ARENA DOES NOT RAISE A BOARD AT ALL ----
            //
            // It used to, and the player got two boards in a row for one round: this one, filled
            // with the match's own marks out of thirty, and then the venue's the moment the arena
            // unloaded and Main woke up behind it — filled, until now, from exactly the same
            // standings. The same four rows, twice, with a curtain in between. That is the "stage
            // two shows stage one's scoreboard" the owner is reading: it is not stage one's data,
            // it is stage one's BOARD, the per-round marks table a lawn round ends on, and then it
            // is shown again.
            //
            // The one that goes is this one, and the choice is forced rather than preferred:
            // GameDirector's Scoreboard state is not a graphic, it is the gate. It banks the round
            // (see Tournament.BankRound on why the board and not the verdict), it is where SPACE
            // goes to the next stage, and it is where a completed championship becomes the ending.
            // It cannot be dropped, so a single board has to be that one — and it now carries the
            // running total across all three rounds, which is the number this beat could never have
            // shown honestly anyway, because nothing has been banked while the arena is still up.
            //
            // The CloseMatchRound call that used to sit here has gone with the board it was for.
            // Its stated reason was that this board reads the standings before the director closes
            // them; with no board there is nothing in this scene that reads Tournament.Standings at
            // all, and the director closes the round on the way out as it always did.
            if (Tournament.Instance != null) return false;

            // ---- STANDALONE ONLY, and then this board is the only word there is ----
            //
            // An arena opened on its own — the review loop, and the front page's stage select — has
            // no championship behind it and no venue to go back to. The beat used to end silently
            // the moment Tournament.Instance was null, and the symptom was the one thing nobody can
            // debug from: the judges deliver a verdict and then nothing happens, with no error. A
            // ceremony that has a result to show should always show it.
            var order = StandingsFromRally();
            if (order == null || order.Count == 0)
            {
                Debug.LogWarning("[Rally] the board has nothing to show — no championship and no " +
                                 "rally results. The verdict ends at the cards.");
                return false;
            }

            cameraDirector?.SetMode(CameraMode.Scoreboard, 1.6f);
            scoreboard.ResetBoard();
            scoreboard.Settle(order, string.IsNullOrEmpty(Winner) ? "WINNER" : Winner.ToUpperInvariant());
            return true;
        }

        void Attention(float amount)
        {
            if (panel == null) return;
            foreach (var j in panel.judges)
                if (j?.character != null) j.character.Attention = amount;
        }

        JudgeCharacter CharacterAt(int i)
        {
            if (panel == null || i < 0 || i >= panel.judges.Length) return null;
            return panel.judges[i]?.character;
        }

        void RaiseNext()
        {
            var c = CharacterAt(_raised);
            _raised++;
            if (c == null) return;

            var tex = portraits != null ? portraits.Get(Winner) : null;
            if (tex != null) c.ShowPortrait(tex);
            else c.SetCardNumber(1);          // a bench with no portrait still has to say SOMETHING

            // Said out loud, because a blank card is the one failure this beat cannot survive and it
            // has three separate causes that look identical on screen: no portraits component, no
            // subject for that contestant, or a card rig with nothing to hang the picture on.
            Debug.Log($"[Rally] card {_raised} for {Winner}: portraits={(portraits != null ? "yes" : "NULL")} " +
                      $"texture={(tex != null ? tex.width + "px" : "NULL")} " +
                      $"cardNumber={(c.cardNumber != null ? "yes" : "NULL")} " +
                      $"card={(c.card != null ? c.card.name : "NULL")}");

            c.Punch(0.9f);
            AudioDirector.Instance?.PlayOne(AudioDirector.Instance.cardRaise, 0.7f);
            AudioDirector.Instance?.CrowdCheer(0.5f + _raised * 0.15f, applaud: _raised >= 3);
            // The winner's colour thrown up behind the card, so the beat is legible from anywhere in
            // the arena rather than only to whoever is close enough to recognise a face.
            if (c.card != null)
                RallyWorldFX.Instance?.Burst(c.card.position + Vector3.up * 0.5f, _livery, 0.8f, 5);
        }
    }
}
