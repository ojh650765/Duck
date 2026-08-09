using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The opening story, played as a scrapbook page rather than as a film.
    ///
    /// The reference is Overcooked's between-level story pages, and the reason it beats a
    /// full-bleed cinematic here is the art: the panels are renders of this game's own duck and
    /// mower — flat, saturated, chunky, no fine detail. Blown edge to edge that reads as a low
    /// resolution screenshot. Sitting on a paper page inside a painted frame, slightly askew, it
    /// reads as a picture somebody made, which is also what the game is about.
    ///
    /// The page accumulates. A panel rises out of its own slot in the margin, holds the middle
    /// while its Ken Burns move runs, then shrinks back into that slot and *stays there* while the
    /// next one comes up. By panel ten the page carries the whole story at once, which is a far
    /// stronger close than fading out on a single frame — and it costs nothing, because the slots
    /// are fixed and every card already exists.
    ///
    /// Everything is stepped through <see cref="Tick"/> for the same reason as the rest of the
    /// game: an unfocused editor advances play mode about once every five seconds, so anything
    /// timed on Update alone cannot be reviewed unattended.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class ComicSequence : MonoBehaviour
    {
        /// <summary>The widgets one panel needs. Built and wired by DuckCutsceneBuilder.</summary>
        [Serializable]
        public class PanelView
        {
            public RectTransform card;
            public CanvasGroup group;
            public RawImage art;
            [Tooltip("Only ever shown when this panel's art is missing.")]
            public TextMeshProUGUI stamp;
            [Tooltip("Where this panel lives on the page once it has had its turn, in canvas units.")]
            public Vector2 restPosition;
            public float restRotation;
            [Tooltip("The hand-pinned tilt while it is the hero. Small — a page, not a mess.")]
            public float heroRotation;
        }

        /// <summary>
        /// A named clip a panel can ask for. Playback goes through <see cref="AudioDirector"/>'s
        /// existing one-shot bus rather than a second AudioSource of our own, so the cutscene
        /// cannot end up mixed against the game instead of inside it.
        /// </summary>
        [Serializable]
        public class PanelSfx
        {
            public string id;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.6f;
        }

        [Header("Content")]
        public ComicPanel[] panels = Array.Empty<ComicPanel>();
        public PanelView[] views = Array.Empty<PanelView>();
        public PanelSfx[] sfx = Array.Empty<PanelSfx>();

        [Header("Page")]
        [Tooltip("Everything visible. Switched off while the cutscene is not running, so the page " +
                 "costs nothing once it is over.")]
        public GameObject page;
        [Tooltip("Sits between the settled panels and the hero, and darkens the page on emphasis.")]
        public Image dimmer;
        public RectTransform barTop, barBottom;
        [Tooltip("The narration band in the bottom letterbox bar. Null on a page built before the " +
                 "narration layer existed; the sequence then plays silently, as it did.")]
        public CanvasGroup narrationGroup;
        public TextMeshProUGUI narrationText;
        public CanvasGroup skipGroup;
        public Image flash;
        public Image fade;

        [Header("Timing")]
        public float fadeInDuration = 0.9f;
        public float fadeOutDuration = 1.1f;
        [Tooltip("A skip is short and deliberate: long enough not to be a jump cut, short enough " +
                 "that nobody who has seen this before feels made to sit through the exit as well.")]
        public float skipFadeDuration = 0.35f;
        [Tooltip("Seconds the finished page is held after the last panel has settled onto it. " +
                 "Without this the tenth panel shrinks into place and is immediately faded out, " +
                 "which throws away the one shot where the whole story is on screen at once.")]
        public float pageHold = 2.2f;
        [Tooltip("Seconds before the story can be skipped at all, counted from the first frame of " +
                 "the fade-in. The hint is shown off the same clock and not a moment earlier.")]
        public float skipArmDelay = 8f;

        [Header("Page layout")]
        [Tooltip("Scale of a panel once it has settled back into its slot in the margin.")]
        public float restScale = 0.19f;
        [Tooltip("Where the hero panel sits. Slightly above centre, clear of the narration band.")]
        public Vector2 heroPosition = new Vector2(0f, 56f);
        [Tooltip("How present the already-played panels stay. Not 1: they are memory, not the beat.")]
        [Range(0f, 1f)] public float restAlpha = 0.82f;
        [Tooltip("Height the emphasis bars close to, in canvas units.")]
        public float barHeight = 130f;
        [Range(0f, 1f)] public float dimStrength = 0.55f;

        [Header("Narration voice")]
        [Tooltip("How loud the spoken narration is. Higher than the panel stings on purpose — the " +
                 "band is the only thing telling the story and the stings are punctuation.")]
        [Range(0f, 1f)] public float narrationVolume = 0.85f;

        /// <summary>
        /// Seconds the first line of a panel waits before it appears, and seconds the last line is
        /// gone before the panel ends. Public because DuckCutsceneBuilder has to budget for both when
        /// it sizes a line's hold around its clip: if the two ever disagreed, the tail of every last
        /// line would be spoken over the next card landing.
        /// </summary>
        public const float NarrationLeadIn = 0.34f;
        public const float NarrationLeadOut = 0.30f;

        /// <summary>True once the page is done, however it got there — played out, skipped, or failed.</summary>
        public bool Finished { get; private set; }

        /// <summary>
        /// Whether the player may skip yet. The hint's visibility and the input read are both this
        /// one thing, and they have to stay one thing.
        ///
        /// They were not, and the first version was the worst of both: the input was live from frame
        /// one while the prompt waited 1.5 s, so the opening could be — and was — thrown away by a
        /// keypress that arrived before there was anything on screen saying a keypress would do that.
        /// A skip nobody asked for looks like the game failing to play its own cutscene, and there is
        /// nothing on screen afterwards to explain it. Hidden-but-live is strictly worse than
        /// visible-and-live; the only honest arrangement is armed exactly when it says it is armed.
        /// </summary>
        public bool SkipArmed => _runTime >= skipArmDelay;
        /// <summary>Fired exactly once when the sequence is over and the game may start.</summary>
        public event Action OnFinished;

        /// <summary>Longest the sequence can legitimately run. The director uses it as a watchdog.</summary>
        public float TotalSeconds
        {
            get
            {
                float t = fadeInDuration + pageHold + fadeOutDuration;
                if (panels != null)
                    foreach (var p in panels)
                        if (p != null) t += p.duration + Mathf.Max(p.transitionDuration, 0f);
                return t;
            }
        }

        enum Phase { Idle, FadeIn, Playing, Compose, FadeOut, Done }
        Phase _phase = Phase.Idle;

        int _index = -1;
        float _phaseTime;
        float _panelTime;
        float _runTime;
        float _liftTime = 0.5f;
        float _fadeAmount = 1f;
        float _fadeOutTime = 1f;
        float _flashAmount;
        float _emphasis, _emphasisTarget;
        float _skipAlpha;
        float _narrationAlpha;
        string _narrationShown = "";

        // The current panel's line holds, resolved once when the panel starts rather than validated
        // every frame: the fallback to an even split depends on the authored array being the right
        // length and wholly positive, and that is a question with one answer per panel.
        float[] _holds = Array.Empty<float>();
        int _holdCount;

        AudioSource _voice;

        float[] _lift = Array.Empty<float>();
        bool[] _seen = Array.Empty<bool>();
        Texture2D[] _placeholders = Array.Empty<Texture2D>();

        void Awake()
        {
            // Hidden until asked for. The director decides whether the story runs at all, and a
            // full-screen opaque page left switched on would otherwise cover the game on any scene
            // where it is not wanted.
            if (page != null) page.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var t in _placeholders)
                if (t != null) Destroy(t);
        }

        /// <summary>
        /// One frame of the story, on the UNSCALED clock.
        ///
        /// ---- this used to read Time.deltaTime, and it froze the game ----
        ///
        /// The symptom was the opening story stopping dead on its fade-in frame and never advancing,
        /// with no way out at all. The mechanism is worth writing down in full, because every part of
        /// it was individually correct:
        ///
        ///   * PopupStack borrows Time.timeScale and holds it at ZERO for as long as any popup in it
        ///     wants time stopped. That is its whole job and it is right.
        ///   * ControlsPrimer pushed itself over the story, because it inferred "a stage is about to
        ///     start" from the wheel being held — and the wheel is held for the whole of the story.
        ///   * So the scaled clock stopped, and this Update, reading the scaled clock, stopped with
        ///     it. The page held whatever frame it had reached.
        ///   * And the failsafe went with it: GameDirector's Intro budget was accumulating scaled
        ///     time too, so the deadline that exists to rescue exactly this could never arrive.
        ///
        /// The primer's trigger has been fixed and no longer fires here. That is not enough on its
        /// own, and this line is the reason: any popup that stops time — one that exists today, or
        /// the next one somebody adds — would reproduce it, and it reproduces as a HANG rather than
        /// as a glitch. A sequence whose own skip and own timeout are both driven by a clock that
        /// something else is entitled to stop has no way to survive being interrupted.
        ///
        /// UNSCALED is the right clock for this page on its own merits, not merely as a repair. The
        /// story is not part of the world: nothing in it is simulated, nothing in it collides, and it
        /// plays over a scene that is deliberately doing nothing. Every other screen in this project
        /// that sits IN FRONT of the game already runs unscaled and says so — the popup stack ticks
        /// its popups unscaled, the curtain animates unscaled, PopupView reads nothing else. This
        /// page belongs to that family and was the one member of it still on the world's clock.
        ///
        /// SimClock.Scripted is still honoured. Under the capture harness the game is stepped by
        /// hand through <see cref="Tick"/> so that frame sheets are reproducible, and a page that
        /// also advanced itself on a wall clock would put a different panel in every capture.
        /// </summary>
        void Update()
        {
            if (SimClock.Scripted) return;
            // Clamped for the reason the curtain and the popup stack both clamp: a browser tab
            // regaining focus hands over a quarter-second delta, and a fade stepped through one of
            // those arrives already finished.
            Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        }

        // ------------------------------------------------------------------ control

        /// <summary>Start the story. Safe to call on a sequence with no art and no panels.</summary>
        public void Begin()
        {
            if (_phase != Phase.Idle) return;

            if (panels == null || panels.Length == 0 || views == null || views.Length < panels.Length)
            {
                Debug.LogWarning("[Duck] cutscene has no panels to play; skipping straight to the round.");
                Conclude(false);
                return;
            }

            _lift = new float[panels.Length];
            _seen = new bool[panels.Length];
            _index = -1;
            _phaseTime = 0f;
            _panelTime = 0f;
            _runTime = 0f;
            _emphasis = _emphasisTarget = 0f;
            _flashAmount = 0f;
            _skipAlpha = 0f;
            _narrationAlpha = 0f;
            _narrationShown = "";
            _holdCount = 0;
            if (narrationText != null) narrationText.text = "";
            if (_voice != null) _voice.Stop();
            _fadeAmount = 1f;
            Finished = false;

            EnsurePlaceholders();
            WarnIfNarrationUnwired();

            if (page != null) page.SetActive(true);
            foreach (var v in views)
            {
                if (v == null) continue;
                if (v.group != null) v.group.alpha = 0f;
                if (v.card != null) v.card.anchoredPosition = v.restPosition;
            }

            _phase = Phase.FadeIn;
        }

        /// <summary>Cut to the end. Any key, click or pad button lands here.</summary>
        public void Skip()
        {
            if (_phase == Phase.Idle || _phase == Phase.Done || _phase == Phase.FadeOut) return;
            _phase = Phase.FadeOut;
            _phaseTime = 0f;
            _fadeOutTime = Mathf.Max(skipFadeDuration, 0.05f);
        }

        /// <summary>
        /// Take the page down without announcing it. The director calls this when it has decided
        /// on its own that the story is over — firing OnFinished from inside the director's own
        /// tick, in response to the director, would be a loop waiting to happen.
        /// </summary>
        public void Hide()
        {
            _phase = Phase.Done;
            Finished = true;
            if (_voice != null) _voice.Stop();
            if (page != null) page.SetActive(false);
        }

        void Conclude(bool announce)
        {
            _phase = Phase.Done;
            Finished = true;
            // The picture going away does not stop a sound: an AudioSource on a child of this canvas
            // keeps playing with the page switched off, and the one thing that must never happen is
            // the narrator finishing a sentence about the pond over the starting countdown.
            if (_voice != null) _voice.Stop();
            if (page != null) page.SetActive(false);
            if (announce) OnFinished?.Invoke();
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            if (_phase == Phase.Idle || _phase == Phase.Done) return;

            // The story is a nicety; the mowing loop is the game. If anything in here throws, the
            // player must still end up on a lawn — so the failure is reported loudly and the page
            // is taken down, rather than left mid-panel waiting for a Finished that never comes.
            try
            {
                Step(dt);
            }
            catch (Exception e)
            {
                Debug.LogError("[Duck] the opening sequence failed; starting the round without it.");
                Debug.LogException(e, this);
                Conclude(true);
            }
        }

        void Step(float dt)
        {
            _phaseTime += dt;
            _runTime += dt;

            // SkipPressed is not even asked while the skip is unarmed, which is deliberate: it reads
            // wasPressedThisFrame, so a key mashed during the first seconds is consumed by the frame
            // it happened on and cannot be waiting in a buffer to fire the instant the gate opens.
            // The player presses again, after the prompt has told them it will work.
            if (_phase != Phase.FadeOut && SkipArmed && SkipPressed()) Skip();

            switch (_phase)
            {
                case Phase.FadeIn:
                    _fadeAmount = 1f - Mathf.Clamp01(_phaseTime / Mathf.Max(fadeInDuration, 0.01f));
                    if (_phaseTime >= fadeInDuration) ShowPanel(0);
                    break;

                case Phase.Playing:
                {
                    _panelTime += dt;
                    var p = panels[_index];
                    if (_panelTime >= p.duration)
                    {
                        if (_index + 1 < panels.Length) ShowPanel(_index + 1);
                        else
                        {
                            // Last panel done. Everything settles — _index of -1 means no card is
                            // the hero, so they all go home — and the finished page gets a beat to
                            // itself before the fade. That page is the whole story in one frame and
                            // it is the last thing seen before the lawn; assembling it and cutting
                            // straight out throws away the only shot that pays off the accumulation.
                            _phase = Phase.Compose;
                            _phaseTime = 0f;
                            _emphasisTarget = 0f;
                            _index = -1;
                        }
                    }
                    break;
                }

                case Phase.Compose:
                    if (_phaseTime >= pageHold)
                    {
                        _phase = Phase.FadeOut;
                        _phaseTime = 0f;
                        _fadeOutTime = Mathf.Max(fadeOutDuration, 0.05f);
                    }
                    break;

                case Phase.FadeOut:
                    _fadeAmount = Mathf.Clamp01(_phaseTime / _fadeOutTime);
                    if (_phaseTime >= _fadeOutTime) { Conclude(true); return; }
                    break;
            }

            AnimateCards(dt);
            AnimateOverlays(dt);
        }

        void ShowPanel(int i)
        {
            _phase = Phase.Playing;
            _index = i;
            _panelTime = 0f;

            var p = panels[i];
            _seen[i] = true;

            // A cut is a cut: the incoming panel is simply there, and the outgoing one is simply
            // back in its slot. Anything longer than a couple of frames turns a shock into a
            // transition, which is the opposite of the impact it is meant to have.
            //
            // A FLASH is not a cut, though, and grouping the two lost the arrival entirely. At a
            // 40 ms lift the card reached its mark while the screen was still white, so the two
            // biggest beats in the story — the shock and the greed — were the only ones where the
            // panel did not visibly fly onto the page. The punch landed and then a picture was
            // simply, quietly, already there.
            //
            // The flash decays fast (alpha is the square of an amount falling at 2.8/s, so it is
            // effectively gone by 0.3 s). Giving the lift a little longer than that means the card
            // slams into place as the light clears, which is what the flash was punctuating in the
            // first place.
            // One rhythm, not two.
            //
            // These started at 40 ms so that a cut would feel instant. It did — but this page is
            // built on cards visibly flying onto it, and at 40 ms the card was simply already
            // there. Raising them to 0.19 / 0.38 made the arrival exist without fixing the real
            // problem, which is that the cross-fades run 0.7–1.1 s and everything else ran at a
            // third of that. Four panels moving at one speed and six at another does not read as
            // punctuation, it reads as the sequence stuttering.
            //
            // So the hard transitions now sit at the bottom of the same band rather than in a band
            // of their own. They are still clearly faster than a dissolve — that is what makes a
            // cut a cut — but they belong to the same piece of film.
            //
            // The flash still works: its white decays inside about 0.3 s, so the screen has cleared
            // by the time the card lands and the punch reads as the thing that threw it in.
            const float CutLift = 0.45f;
            const float FlashLift = 0.62f;

            _liftTime = p.transition switch
            {
                PanelTransition.Cut => CutLift,
                PanelTransition.Flash => FlashLift,
                _ => Mathf.Max(p.transitionDuration, 0.05f)
            };

            // The white flash is retired.
            //
            // It was meant to punctuate the two shock beats, but on a page whose whole idiom is
            // cards settling onto paper it did not read as a punch — it read as the screen
            // glitching, and it took the incoming card's arrival with it. Together with the
            // emphasis dimmer slamming to full on the same panels, the sequence appeared to blink
            // twice. Both have been dialled out; the shock beats now carry on framing and hold
            // length alone, which is what the rest of the page already uses.
            //
            // The transition enum and the overlay are left in place rather than ripped out: the
            // flash is one line from returning if it is ever wanted, and deleting the plumbing
            // would make that a rewrite instead.
            // if (p.transition == PanelTransition.Flash) _flashAmount = 1f;

            _emphasisTarget = p.emphasis;

            // The band itself is still driven from the panel clock in AnimateOverlays — a panel's
            // lines are not one string shown for the whole hold. What has to happen here is deciding
            // how the hold is divided, because that answer is per panel and not per frame.
            PrepareNarration(p);

            // Stacking, and it only ever happens on a panel change. The dimmer has to sit above
            // every settled card and below the hero, and the hero has to be above the dimmer — so
            // both are pushed to the top of the pile in that order.
            if (dimmer != null) dimmer.rectTransform.SetAsLastSibling();
            var v = views[i];
            if (v != null && v.card != null) v.card.SetAsLastSibling();

            PlaySfx(p.sfxId);
        }

        /// <summary>
        /// Lift the hero out of the page and settle everything else back onto it.
        ///
        /// One shared lift rate rather than a timer per card: the outgoing panel is leaving because
        /// the incoming one arrived, so they are one movement and should share its duration. Giving
        /// them separate rates made a cross-fade look like two unrelated animations that happened
        /// to overlap.
        /// </summary>
        void AnimateCards(float dt)
        {
            float rate = dt / Mathf.Max(_liftTime, 0.02f);
            float settledAlpha = restAlpha * (1f - _emphasis * 0.85f);

            for (int i = 0; i < views.Length && i < _lift.Length; i++)
            {
                var v = views[i];
                if (v == null || v.card == null) continue;

                float want = i == _index ? 1f : 0f;
                _lift[i] = Mathf.MoveTowards(_lift[i], want, rate);
                float e = Mathf.SmoothStep(0f, 1f, _lift[i]);

                v.card.anchoredPosition = Vector2.LerpUnclamped(v.restPosition, heroPosition, e);
                float s = Mathf.LerpUnclamped(restScale, 1f, e);
                v.card.localScale = new Vector3(s, s, 1f);
                v.card.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.LerpUnclamped(v.restRotation, v.heroRotation, e));

                float a = _seen[i] ? Mathf.LerpUnclamped(settledAlpha, 1f, e) : 0f;
                SetGroup(v.group, a);
            }

            // Ken Burns, on the hero only. The settled panels keep whatever crop they finished on,
            // which is the right one — it is the last thing that panel showed.
            if (_index >= 0 && _index < panels.Length)
            {
                var p = panels[_index];
                var hero = views[_index];
                if (hero != null && hero.art != null)
                {
                    float k = p.duration > 0.001f ? Mathf.Clamp01(_panelTime / p.duration) : 1f;
                    k = k * k * (3f - 2f * k);   // eased both ends: a push that starts or stops
                                                 // abruptly reads as a camera bump, not a move
                    hero.art.uvRect = new Rect(
                        Mathf.Lerp(p.moveFrom.x, p.moveTo.x, k),
                        Mathf.Lerp(p.moveFrom.y, p.moveTo.y, k),
                        Mathf.Lerp(p.moveFrom.width, p.moveTo.width, k),
                        Mathf.Lerp(p.moveFrom.height, p.moveTo.height, k));
                }
            }
        }

        void AnimateOverlays(float dt)
        {
            _emphasis = Mathf.MoveTowards(_emphasis, _emphasisTarget, dt * 2.2f);

            // The bars are a fixed frame, not an effect.
            //
            // They used to grow and shrink with the panel's emphasis, which meant the shape of the
            // screen changed four times during the sequence — and a frame that resizes reads as
            // the picture wobbling, not as emphasis. The whole point of a letterbox is that it is
            // the one thing on screen you stop noticing.
            //
            // Emphasis is still expressed, entirely through the dimmer: settled panels sink back
            // and the hero holds the light. That was always doing most of the work anyway.
            if (barTop != null) barTop.sizeDelta = new Vector2(0f, barHeight);
            if (barBottom != null) barBottom.sizeDelta = new Vector2(0f, barHeight);
            SetAlpha(dimmer, _emphasis * dimStrength);

            // Squared, so the punch is over almost immediately and the tail is a suggestion.
            _flashAmount = Mathf.MoveTowards(_flashAmount, 0f, dt * 2.8f);
            SetAlpha(flash, _flashAmount * _flashAmount);

            SetAlpha(fade, _fadeAmount);

            // The string is committed only once the band has gone dark, so a line is never seen
            // being replaced. On the panels that turn a line over mid-hold, writing the new text
            // straight into a lit band would pop one sentence into another in a single frame — which
            // on a page that has already had its flashes taken out is exactly the artefact to avoid.
            int narSlot = NarrationSlot(out string narWant);
            bool narChanging = narWant != _narrationShown;
            _narrationAlpha = Mathf.MoveTowards(_narrationAlpha,
                narChanging || narWant.Length == 0 ? 0f : 1f, dt * 4.5f);
            if (narChanging && _narrationAlpha <= 0.001f)
            {
                _narrationShown = narWant;
                if (narrationText != null) narrationText.text = narWant;
                // The voice starts on the frame the text is committed, not on the frame the line
                // becomes due. Those are the same frame while the band is already dark, which it
                // always is here — the dip between lines is what makes them one event rather than
                // a subtitle that arrives late to its own reading.
                if (narSlot >= 0) Speak(narSlot);
            }
            SetGroup(narrationGroup, _narrationAlpha);

            // The voice rides the page's own fade, which is what makes a skip clean: the player hits
            // a key mid-sentence and the reading goes down with the picture over the same 0.35 s
            // instead of being guillotined. Conclude still hard-stops, for the case where the fade
            // never ran at all.
            if (_voice != null && _voice.isPlaying)
                _voice.volume = Mathf.Clamp01(narrationVolume) * (1f - _fadeAmount);

            bool hintWanted = _phase != Phase.FadeOut && SkipArmed;
            _skipAlpha = Mathf.MoveTowards(_skipAlpha, hintWanted ? 1f : 0f, dt * 1.6f);
            // A slow breathe rather than a blink. It has to be findable without competing with the
            // panel for attention for the whole forty seconds.
            //
            // Phased from the moment it arms rather than from the start of the run, so the first
            // breath always begins on the way up. Off the run clock the hint could fade in onto the
            // bottom of a trough and read as though it were already fading out again.
            float pulse = 0.62f + 0.38f * Mathf.Sin(Mathf.Max(_runTime - skipArmDelay, 0f) * 2.1f);
            SetGroup(skipGroup, _skipAlpha * pulse);
        }

        // ------------------------------------------------------------------ narration

        /// <summary>
        /// Work out how this panel's hold is divided between its lines.
        ///
        /// Authored holds win when there is a complete, positive set of them, because they were
        /// measured off the rendered audio and an even split cannot be. A missing, short or partly
        /// zeroed array falls all the way back to the even split rather than filling the gaps — a
        /// half-authored set of windows would not add up to the panel's duration, and lines would
        /// start landing on top of each other in a way that looks like a timing bug rather than like
        /// missing data.
        /// </summary>
        void PrepareNarration(ComicPanel p)
        {
            var lines = p?.narration;
            _holdCount = lines == null ? 0 : lines.Length;
            if (_holdCount == 0) return;
            if (_holds.Length < _holdCount) _holds = new float[_holdCount];

            var authored = p.narrationHold;
            bool useAuthored = authored != null && authored.Length == _holdCount;
            if (useAuthored)
                for (int k = 0; k < _holdCount; k++)
                    if (!(authored[k] > 0.01f)) { useAuthored = false; break; }

            float even = Mathf.Max(p.duration, 0.01f) / _holdCount;
            for (int k = 0; k < _holdCount; k++) _holds[k] = useAuthored ? authored[k] : even;
        }

        /// <summary>
        /// Which line the band should be showing right now, and its text. -1 and "" for none.
        ///
        /// The band is held clear of the panel's own two edges: a line that arrives on the incoming
        /// cross-fade reads as part of the transition rather than as a voice, and one still up when
        /// the next card lands gets read a second time against the wrong picture.
        ///
        /// Only the edges, though. Between two lines *inside* one panel the band already dips to
        /// black and back, which is separation enough — charging every line the full lead as well
        /// left a fifty-character sentence about two seconds to be read in, and reading rate is the
        /// one constraint here that is not a matter of taste.
        ///
        /// The last line absorbs any overrun, which is what keeps the walk equivalent to the integer
        /// division it replaced: with equal holds a panel clock past the end lands on the last line,
        /// exactly as the old clamp did.
        /// </summary>
        int NarrationSlot(out string line)
        {
            line = "";
            if (_phase != Phase.Playing || _index < 0 || _index >= panels.Length) return -1;
            if (_holdCount == 0) return -1;

            var p = panels[_index];
            float t = _panelTime;
            int k = 0;
            while (k < _holdCount - 1 && t >= _holds[k]) { t -= _holds[k]; k++; }

            float lead = k == 0 ? NarrationLeadIn : 0f;
            float tail = k == _holdCount - 1 ? NarrationLeadOut : 0f;
            if (t < lead || t > _holds[k] - tail) return -1;

            line = (k < p.narration.Length ? p.narration[k] : null) ?? "";
            return k;
        }

        /// <summary>
        /// Read a line out loud, if it was ever rendered.
        ///
        /// This is the one sound in the game that does not go through AudioDirector's one-shot bus,
        /// and the reason is that PlayOneShot cannot be un-fired. The narration has to stop on the
        /// exact frame the player skips, and it has to stop without silencing whatever else is on
        /// that bus. A source of its own is also the honest shape of it: the audio spec already puts
        /// voice on its own group, and this is the only voice in the sequence.
        ///
        /// Assigning the clip before Play means a new line always replaces the one before it, so a
        /// panel can never end up with two narrators. By the holds the builder writes that cannot
        /// happen anyway; this is what makes it true even if the numbers are edited by hand.
        /// </summary>
        void Speak(int line)
        {
            var clips = panels[_index]?.narrationVoice;
            if (clips == null || line >= clips.Length) return;
            var clip = clips[line];
            if (clip == null) return;      // this line is read and not heard; the page plays on

            var src = VoiceSource();
            if (src == null) return;
            src.clip = clip;
            src.volume = Mathf.Clamp01(narrationVolume);
            src.Play();
        }

        /// <summary>
        /// Made on demand rather than by the builder, so a page saved before the voice existed grows
        /// one the first time a line is actually spoken. Parented to this canvas and not to
        /// <see cref="page"/>: the page is switched off the moment the sequence ends, and a source
        /// under it would be silenced by the object being deactivated rather than by anyone deciding
        /// to silence it — which is the same bug as the skip, just harder to see.
        /// </summary>
        AudioSource VoiceSource()
        {
            if (_voice != null) return _voice;
            var go = new GameObject("NarrationVoice");
            go.transform.SetParent(transform, false);
            _voice = go.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.loop = false;
            _voice.spatialBlend = 0f;       // the page has no geometry; 2D or it is nowhere
            _voice.dopplerLevel = 0f;
            _voice.bypassReverbZones = true;
            return _voice;
        }

        /// <summary>
        /// The narration text lives on the page, and the page is built by an editor script — so a
        /// scene saved before the band existed has panels full of narration and nowhere to put it,
        /// and it fails by playing silently, which is indistinguishable from a design decision. One
        /// warning at Begin names the fix.
        ///
        /// The second check is the same failure one layer down: holds that no longer fit the clips
        /// they were measured from. That happens when the voice is re-baked at a different tempo or
        /// with a different voice and the page is not rebuilt, and it presents as lines truncated
        /// mid-word — which reads as a broken audio file rather than as stale data.
        /// </summary>
        void WarnIfNarrationUnwired()
        {
            bool bandMissing = narrationText == null;
            bool anyNarration = false;
            int overrun = 0;

            foreach (var p in panels)
            {
                if (p == null || p.narration == null || p.narration.Length == 0) continue;
                anyNarration = true;

                var clips = p.narrationVoice;
                var holds = p.narrationHold;
                if (clips == null || holds == null || holds.Length != p.narration.Length) continue;
                for (int k = 0; k < clips.Length && k < holds.Length; k++)
                {
                    if (clips[k] == null) continue;
                    float lead = k == 0 ? NarrationLeadIn : 0f;
                    float tail = k == p.narration.Length - 1 ? NarrationLeadOut : 0f;
                    if (clips[k].length > holds[k] - lead - tail + 0.01f) overrun++;
                }
            }

            if (bandMissing && anyNarration)
                Debug.LogWarning("[Duck] the cutscene has narration but no band to show it in; " +
                                 "re-run Duck/3 to rebuild the page.");
            if (overrun > 0)
                Debug.LogWarning($"[Duck] {overrun} narration line(s) are longer than the hold they " +
                                 "were given and will be cut off; re-run Duck/3 to re-measure them.");
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Any key, any click, any pad button, any touch.
        ///
        /// Deliberately not routed through InputReader: that is a driving layer with a fixed set of
        /// named actions, and "any key" is not one of them. A cutscene you cannot skip is a cutscene
        /// players resent on the second run, and this one plays in front of a fresh session — so the
        /// skip has to answer to whatever the player happens to hit, not to the two keys we chose.
        /// </summary>
        static bool SkipPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                  mouse.rightButton.wasPressedThisFrame)) return true;

            // Enumerated by hand rather than by walking allControls, which allocates every frame.
            var pad = Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame ||
                                pad.buttonEast.wasPressedThisFrame ||
                                pad.buttonWest.wasPressedThisFrame ||
                                pad.buttonNorth.wasPressedThisFrame ||
                                pad.startButton.wasPressedThisFrame ||
                                pad.selectButton.wasPressedThisFrame ||
                                pad.leftShoulder.wasPressedThisFrame ||
                                pad.rightShoulder.wasPressedThisFrame)) return true;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            return false;
        }

        // ------------------------------------------------------------------ audio

        void PlaySfx(string id)
        {
            if (string.IsNullOrEmpty(id) || sfx == null) return;
            var audio = AudioDirector.Instance;
            if (audio == null) return;    // no audio in the scene is not a reason to stop the story

            for (int i = 0; i < sfx.Length; i++)
            {
                if (sfx[i] == null || sfx[i].clip == null || sfx[i].id != id) continue;
                audio.PlayOne(sfx[i].clip, sfx[i].volume);
                return;
            }
        }

        // ------------------------------------------------------------------ placeholders

        /// <summary>
        /// Stand in for art that has not been rendered yet.
        ///
        /// Generated at runtime rather than checked in as ten placeholder PNGs, because a
        /// placeholder file sitting in the same folder as the real art is a placeholder that ships:
        /// it imports, it loads, it looks deliberate in the Inspector, and nobody notices until it
        /// is in the build. A hazard-striped texture built in code cannot survive the art landing —
        /// the moment panel_03.png exists, this stops running for panel 3.
        /// </summary>
        void EnsurePlaceholders()
        {
            if (_placeholders.Length != panels.Length)
                _placeholders = new Texture2D[panels.Length];

            for (int i = 0; i < panels.Length; i++)
            {
                var p = panels[i];
                var v = i < views.Length ? views[i] : null;
                if (p == null || v == null || v.art == null) continue;

                bool missing = p.art == null;
                if (!missing)
                {
                    v.art.texture = p.art;
                    v.art.color = Color.white;
                    if (v.stamp != null) v.stamp.gameObject.SetActive(false);
                    continue;
                }

                if (_placeholders[i] == null) _placeholders[i] = BuildPlaceholder(p.placeholderTint);
                v.art.texture = _placeholders[i];
                v.art.color = Color.white;
                if (v.stamp != null)
                {
                    v.stamp.gameObject.SetActive(true);
                    v.stamp.text = string.IsNullOrEmpty(p.placeholderLabel)
                        ? $"{i + 1:00}  ART MISSING"
                        : $"{i + 1:00}  {p.placeholderLabel}";
                }
            }
        }

        static Texture2D BuildPlaceholder(Color tint)
        {
            const int W = 96, H = 54;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false)
            {
                name = "ComicPlaceholder",
                filterMode = FilterMode.Point,      // blocky on purpose: never mistakable for art
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[W * H];
            Color dark = tint * 0.62f; dark.a = 1f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Diagonal hazard stripes, plus a border, which together say "unfinished" in a
                    // way a flat colour swatch does not.
                    bool stripe = (((x + y) / 6) & 1) == 0;
                    bool border = x < 2 || y < 2 || x >= W - 2 || y >= H - 2;
                    px[y * W + x] = border ? (Color32)Color.black
                                           : (Color32)(stripe ? tint : dark);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// CanvasGroup.alpha is a native property with no equality check of its own, and writing it
        /// dirties the whole subtree — so with eleven groups on this page, writing every one every
        /// frame is a canvas rebuild per frame for nothing.
        /// </summary>
        static void SetGroup(CanvasGroup g, float a)
        {
            if (g == null) return;
            if (Mathf.Abs(g.alpha - a) < 0.002f) return;
            g.alpha = a;
        }

        /// <summary>
        /// Graphic's own colour setter already ignores an equal colour, so only the enable matters
        /// here: a fully transparent full-screen image is still a full-screen quad of overdraw, and
        /// there are four of them stacked over this page.
        /// </summary>
        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = a;
            g.color = c;
            bool on = a > 0.003f;
            if (g.enabled != on) g.enabled = on;
        }
    }
}
