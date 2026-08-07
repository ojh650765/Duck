using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The whole game's audio in one place: a layered engine that follows the mower's revs, a
    /// blade that only shreds when it is actually cutting, ambience beds, crowd reactions and
    /// music that swaps with the round state.
    ///
    /// The engine is three seamless loops at measured fundamentals (28 / 52 / 95 Hz) crossfaded
    /// and pitched by RPM, because a single loop pitched over the whole range either chugs or
    /// chipmunks. The crossovers sit at the geometric means of neighbouring layers so the pitch
    /// shift is never worse than about a third either way.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class AudioDirector : MonoBehaviour
    {
        [Header("Sources")]
        public MowerController mower;
        public GameDirector director;
        public JudgePanel judges;

        [Header("Engine layers (base RPM from the audio spec)")]
        public AudioClip engineIdle;   // f0 28.02 Hz -> 1680 rpm
        public AudioClip engineMid;    // f0 52.00 Hz -> 3120 rpm
        public AudioClip engineHigh;   // f0 95.04 Hz -> 5700 rpm
        public AudioClip engineStart;
        public AudioClip engineStop;

        [Header("Blade")]
        public AudioClip bladeLoop;
        public AudioClip bladeCutGrassLoop;
        public AudioClip bladeEngage, bladeDisengage;
        public AudioClip[] debrisPings;

        [Header("Mower")]
        public AudioClip driftLoop;
        public AudioClip boostStart, boostLoop, boostEnd;
        public AudioClip[] bonks;
        public AudioClip[] suspensionBumps;
        public AudioClip horn;

        [Header("Ambience")]
        public AudioClip birds, windGrass, crowdAmbient;

        [Header("Crowd")]
        public AudioClip cheerSmall, cheerBig, gasp, aww, laugh, applause;

        [Header("UI")]
        public AudioClip countdownBeep, countdownGo, klaxon, scoreTick, cardRaise, stamp;

        [Header("Duck")]
        public AudioClip quackHappy, quackAnnoyed, quackPanic, quackProud;

        [Header("Judges")]
        public AudioClip goatLow, goatHigh, badgerLow, badgerHigh, heronLow, heronHigh;

        [Header("Geese")]
        [Tooltip("Stage two. Null is fine everywhere here — the rally falls back to the heron calls " +
                 "it used before, which is the wrong bird but never silence.")]
        public AudioClip[] gooseHonks;
        [Tooltip("One per downstroke. Fired from the wing beat itself, so the rate is the animation's.")]
        public AudioClip[] gooseWingBeats;
        public AudioClip gooseHiss;
        public AudioClip gooseSquawk;

        [Header("Music")]
        public AudioClip musicRound, musicRoundUrgent, musicReveal, musicJudgingBed;
        [Tooltip("Stage two: the goose rally. Same key/tempo family as the round loop (still the " +
                 "same engine carve — the mower keeps running under it), a step up in drive because " +
                 "the rally is the middle stage. See AUDIO_SPEC.md §3/§4 for how it was built.")]
        public AudioClip musicRally;
        [Tooltip("Stage three: Bloom Rush, at night. Same 128 BPM and the same tonic as the other " +
                 "two stages so it reads as the same game, but D Mixolydian and two chords a bar, " +
                 "and it is the only cue with a mower under it that lets the glockenspiel play. " +
                 "Same engine carve as the round and the rally — four machines run under this one. " +
                 "See AUDIO_SPEC.md §3/§4.")]
        public AudioClip musicBloom;
        [Tooltip("The bed under the opening story page. 12 bars in G at 80 BPM, no percussion — " +
                 "see AUDIO_SPEC §3. Null is fine; the story then plays over silence, as it did.")]
        public AudioClip musicCutscene;
        public AudioClip fanfareGood, fanfareBad;

        [Header("Mix")]
        [Range(0f, 1f)] public float masterEngine = 0.55f;
        [Range(0f, 1f)] public float masterBlade = 0.45f;
        [Range(0f, 1f)] public float masterAmbience = 0.35f;
        [Range(0f, 1f)] public float masterMusic = 0.34f;
        [Range(0f, 1f)] public float masterSfx = 0.7f;

        [Header("Opening story")]
        [Tooltip("How loud the storybook bed sits, as a fraction of the music bus. Well under the " +
                 "round loop on purpose: there is a narrator on top of it.")]
        [Range(0f, 1f)] public float cutsceneMusicMix = 0.55f;
        [Tooltip("Seconds the world takes to fade away under the story page, and to come back " +
                 "afterwards. Long enough to be a fade rather than a mute, short enough that the " +
                 "engine is gone before the first panel lands.")]
        [Range(0.05f, 3f)] public float cutsceneFade = 0.6f;

        // Base RPM of each engine layer, measured by the synthesis pass.
        const float IdleRpm = 1680f, MidRpm = 3120f, HighRpm = 5700f;
        const float IdleMax = 1900f, RedlineRpm = 6200f;

        AudioSource _engA, _engB, _engC;
        AudioSource _bladeA, _bladeB;
        AudioSource _drift, _boost;
        AudioSource _ambBirds, _ambWind, _ambCrowd;

        static AudioDirector _instance;
        /// <summary>Resolved lazily, like the other systems: an editor domain reload clears statics
        /// without re-running Awake, and a silent crowd is a hard bug to notice.</summary>
        public static AudioDirector Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<AudioDirector>();
                return _instance;
            }
        }
        AudioSource _music, _musicUrgent, _musicStory, _musicOut;

        /// <summary>Where the incoming bed is heading, and how fast. See SwapMusic.</summary>
        float _musicTarget, _musicRate = 2f, _musicOutRate = 2f;
        AudioSource _oneShot, _oneShotB;

        float _rpm = IdleRpm;
        bool _engineRunning = true;
        float _urgentMix;
        GameState _lastState = GameState.Boot;
        System.Random _rng;

        /// <summary>
        /// How far into the opening story we are, 0 (a lawn) to 1 (a page of a picture book).
        ///
        /// One scalar drives the whole gate — the engine, the blade, the drift and boost loops, all
        /// three ambience beds and the storybook music — for the sake of the *un*-gating: everything
        /// is recomputed from this number every frame, so nothing can be left switched off when the
        /// story ends. An earlier draft muted the sources on entering Intro and unmuted them on
        /// leaving it, which is one missed state transition away from a silent game.
        /// </summary>
        float _storyMix;

        /// <summary>Everything that belongs to the fairground rather than to the story page.</summary>
        float WorldMix => 1f - _storyMix;

        /// <summary>
        /// How much of the venue is audible: 1 normally, 0 while the opening story page is up.
        ///
        /// Public because this director does not own every source in the venue. The three rival
        /// mowers on the neighbouring plots have engine loops of their own (see NeighbourAmbience),
        /// and "no engine runs under the story" has to be true of every engine at the fair, not
        /// just the player's — otherwise the bug is fixed on the one machine and left on three.
        /// </summary>
        public float WorldAudio => WorldMix;

        /// <summary>
        /// True while the storybook page is on screen. Asked of the director every frame rather than
        /// latched off OnStateChanged, because the Intro transition is one this component cannot
        /// hear: GameDirector runs at execution order -10 and sets Intro inside its own Start, which
        /// is *before* this component's Start has subscribed. The event is still handled for the
        /// music swap, and <see cref="Start"/> syncs against whatever state it finds — but the gate
        /// itself does not depend on either of those having happened.
        /// </summary>
        bool Storybook => director != null && director.State == GameState.Intro;

        // The ambience beds' share of the ambience bus. Named because the gate has to be able to
        // restore them, and "the value Start happened to pass" is not something it can restore to.
        const float BirdsMix = 0.5f, WindMix = 0.7f;

        /// <summary>
        /// True once <see cref="Awake"/> has actually run on this instance.
        ///
        /// Worth a field because the alternative was a NullReferenceException every frame out of the
        /// crowd murmur. A second AudioDirector arriving with an additively loaded scene can be
        /// ticked before Unity has run its Awake — the tick comes from whoever resolved
        /// <see cref="Instance"/>, and FindFirstObjectByType is perfectly happy to hand back an
        /// object that has been created but not yet initialised.
        /// </summary>
        bool _ready;

        void Awake()
        {
            _rng = new System.Random(12345);
            _ready = true;

            _engA = MakeLoop("Engine_Idle", engineIdle, 0f);
            _engB = MakeLoop("Engine_Mid", engineMid, 0f);
            _engC = MakeLoop("Engine_High", engineHigh, 0f);
            _bladeA = MakeLoop("Blade", bladeLoop, 0f);
            _bladeB = MakeLoop("Blade_Cut", bladeCutGrassLoop, 0f);
            _drift = MakeLoop("Drift", driftLoop, 0f);
            _boost = MakeLoop("Boost", boostLoop, 0f);

            _ambBirds = MakeLoop("Amb_Birds", birds, 0f);
            _ambWind = MakeLoop("Amb_Wind", windGrass, 0f);
            _ambCrowd = MakeLoop("Amb_Crowd", crowdAmbient, 0f);

            _music = MakeLoop("Music", null, 0f);
            _musicUrgent = MakeLoop("Music_Urgent", musicRoundUrgent, 0f);
            // The bed that is on its way OUT. See SwapMusic — a stage boundary used to change the
            // clip on one source, which is a hard cut heard as the music being switched off and a
            // different tune being switched on, in the same frame the picture is also changing.
            _musicOut = MakeLoop("Music_Out", null, 0f);

            // Stopped, unlike every other loop here, which all run silently for the whole session.
            // This one is 36 s of stereo Vorbis that is heard once, in the first minute, and a
            // WebGL build decoding it from the title screen to the ceremony is pure cost. Stopping
            // it also means the bed always starts at the top of the tune when the story begins.
            _musicStory = MakeLoop("Music_Story", musicCutscene, 0f);
            _musicStory.Stop();

            _oneShot = MakeOneShot("OneShot");
            _oneShotB = MakeOneShot("OneShotB");

            _placed = new AudioSource[PlacedVoices];
            for (int i = 0; i < _placed.Length; i++) _placed[i] = MakePlaced($"Placed{i}");
        }

        void Start()
        {
            if (director != null)
            {
                director.OnStateChanged += HandleState;
                director.OnCountdownTick += HandleCountdown;
                director.OnImpactFelt += HandleImpact;
                director.OnLowTimeStarted += () => PlayOne(gasp, 0.5f);
            }
            if (judges != null)
            {
                judges.OnJudgeScored += HandleJudgeScored;
                judges.OnVerdict += HandleVerdict;
            }
            if (mower != null) mower.OnImpact += (s, p) => PlayRandom(bonks, 0.5f + s * 0.5f);

            Gnome.OnKnocked += (pos, strength) => PlayOne(laugh, 0.35f + strength * 0.4f);

            // Snapped, not faded. The director has already set Intro by the time this runs (see
            // Storybook), and a fade from 0 here would put a second and a half of dawn chorus and
            // idling engine over the first panel — which is the bug this gate exists to fix.
            _storyMix = Storybook ? 1f : 0f;
            if (Storybook)
            {
                if (musicCutscene == null)
                    Debug.LogWarning("[Duck] the opening story has no music bed wired " +
                                     "(AudioDirector.musicCutscene); rebuild the scene to pick up " +
                                     "Music/music_cutscene_loop.");
                // The state change we were too late to hear, handled by hand.
                HandleState(GameState.Intro);
            }

            FadeIn(_ambBirds, masterAmbience * BirdsMix * WorldMix);
            FadeIn(_ambWind, masterAmbience * WindMix * WorldMix);
            FadeIn(_ambCrowd, masterAmbience * crowdMurmur * WorldMix);
        }

        void OnDestroy()
        {
            if (director != null)
            {
                director.OnStateChanged -= HandleState;
                director.OnCountdownTick -= HandleCountdown;
                director.OnImpactFelt -= HandleImpact;
            }
            if (judges != null)
            {
                judges.OnJudgeScored -= HandleJudgeScored;
                judges.OnVerdict -= HandleVerdict;
            }
        }

        AudioSource MakeLoop(string name, AudioClip clip, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = volume;
            src.spatialBlend = 0f;      // the camera moves a lot; 2D keeps the mix stable
            src.dopplerLevel = 0f;
            if (clip != null) src.Play();
            return src;
        }

        /// <summary>
        /// A voice that plays where the thing happened.
        ///
        /// The arena has up to nine geese honking and beating their wings at once, and every one of
        /// them was going through the flat 2D bus — so a bird behind the player's shoulder and a
        /// bird sixty metres away across the pitch arrived at exactly the same volume, from exactly
        /// the middle of the stereo field. That is not a mix; it is a pile. Distance and direction
        /// are the two things that make a busy soundscape legible, and both were being thrown away
        /// at the last step.
        ///
        /// A small ring of real 3D sources rather than PlayClipAtPoint, which allocates a
        /// GameObject and an AudioSource per call — at seven wingbeats a second per bird that is a
        /// garbage collection the browser cannot afford. The ring is bounded, so the cost is fixed
        /// however loud the arena gets, and the oldest voice is simply stolen.
        /// </summary>
        AudioSource MakePlaced(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 6f;
            src.maxDistance = 70f;
            return src;
        }

        AudioSource MakeOneShot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            return src;
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            // Nothing before Awake has run. See _ready — a second director arriving with an
            // additively loaded scene can be found and ticked in the window before Unity initialises
            // it, and the crowd murmur then throws on a null generator every frame for the rest of
            // the session.
            if (!_ready) return;
            _clock += dt;
            // Before anything else: everything below reads WorldMix.
            _storyMix = Mathf.MoveTowards(_storyMix, Storybook ? 1f : 0f,
                                          dt / Mathf.Max(cutsceneFade, 0.05f));
            UpdateEngine(dt);
            UpdateBlade(dt);
            UpdateMower(dt);
            UpdateMusic(dt);
            UpdateMusicFade(dt);
            UpdateStoryMusic(dt);
            UpdateAmbience(dt);
            UpdateCrowd(dt);
        }

        /// <summary>
        /// The two beds nobody was driving.
        ///
        /// Birds and wind used to be set once in <see cref="Start"/> and never touched again, which
        /// is exactly why a dawn chorus played under the opening story: there was no code anywhere
        /// that could turn them down. They are now computed every frame from the same two constants
        /// Start uses, times the story gate — so outside the story they sit at precisely the level
        /// they always did, and inside it they are silent without anything having been "muted".
        /// </summary>
        void UpdateAmbience(float dt)
        {
            float rate = dt * 3f;
            if (_ambBirds != null)
                _ambBirds.volume = Mathf.MoveTowards(_ambBirds.volume,
                                                     masterAmbience * BirdsMix * WorldMix, rate);
            if (_ambWind != null)
                _ambWind.volume = Mathf.MoveTowards(_ambWind.volume,
                                                    masterAmbience * WindMix * WorldMix, rate);
        }

        /// <summary>
        /// The storybook bed. Faded up when the page is on screen and down when it is not, from the
        /// same scalar that fades the fairground out, so the two are one crossfade rather than two
        /// cues that happen to overlap.
        ///
        /// It has a source of its own instead of going through <see cref="SwapMusic"/> for two
        /// reasons: SwapMusic is an instant swap, and the round music has to be able to come up
        /// under this while it is still fading — a hard cut from a wistful G major into a 128 BPM
        /// reel is the transition this was meant to soften.
        /// </summary>
        void UpdateStoryMusic(float dt)
        {
            if (_musicStory == null || _musicStory.clip == null) return;

            float want = _storyMix * masterMusic * cutsceneMusicMix;
            _musicStory.volume = want;

            bool audible = want > 0.001f;
            if (audible && !_musicStory.isPlaying) _musicStory.Play();
            else if (!audible && _musicStory.isPlaying) _musicStory.Stop();
        }

        /// <summary>
        /// The crowd bed rises under a reaction and settles again. Without this a cheer is a sound
        /// effect played at a crowd; with it, the crowd itself gets louder for a moment.
        /// </summary>
        void UpdateCrowd(float dt)
        {
            if (_ambCrowd == null) return;

            _crowdSwell = Mathf.MoveTowards(_crowdSwell, 0f, dt * 0.55f);

            // The murmur is not a constant. A crowd this size is never at one level: it rises and
            // falls on its own, in slow waves, and a flat loop underneath the game is exactly what
            // makes a stand full of animals read as wallpaper. Two decorrelated waves keep it from
            // ever settling into an audible period.
            float murmur = Mathf.Sin(_clock * 0.21f) * 0.5f + Mathf.Sin(_clock * 0.077f + 1.7f) * 0.5f;
            murmur = 1f + murmur * 0.28f;

            // Occasional spontaneous ripples of interest — somebody in the stand reacting to the
            // mowing without anything specific having happened.
            _murmurTimer -= dt;
            if (_murmurTimer <= 0f)
            {
                _murmurTimer = 7f + (float)_rng.NextDouble() * 11f;
                if (director != null && director.State == GameState.Mowing)
                {
                    PlayOne(cheerSmall, 0.16f + (float)_rng.NextDouble() * 0.10f);
                    _crowdSwell = Mathf.Max(_crowdSwell, 0.22f);
                }
            }

            float baseline = masterAmbience * crowdMurmur * WorldMix;
            _ambCrowd.volume = Mathf.Lerp(_ambCrowd.volume,
                                          baseline * murmur * (1f + _crowdSwell * 1.6f), dt * 4f);
        }

        float _murmurTimer = 4f;

        // ------------------------------------------------------------------ engine

        void UpdateEngine(float dt)
        {
            if (mower == null) return;

            // Revs follow load with a little lag, so the engine sounds like it is working rather
            // than tracking the speedometer exactly.
            float targetRpm = _engineRunning
                ? Mathf.Lerp(IdleRpm, RedlineRpm, mower.EngineRpm01)
                : 0f;
            _rpm = Mathf.Lerp(_rpm, targetRpm, 1f - Mathf.Exp(-3.5f * dt));

            float vol = _engineRunning ? masterEngine : 0f;
            if (_rpm < 50f) vol = 0f;
            vol *= WorldMix;    // the story page has no mower in it

            // Crossfade the three layers around the geometric-mean crossovers.
            float wIdle = Weight(_rpm, 0f, IdleRpm, 2289f);
            float wMid = Weight(_rpm, 1680f, MidRpm, 4217f);
            float wHigh = Weight(_rpm, 2289f, HighRpm, 9000f);
            float sum = Mathf.Max(wIdle + wMid + wHigh, 1e-4f);

            SetLayer(_engA, wIdle / sum * vol, _rpm / IdleRpm);
            SetLayer(_engB, wMid / sum * vol * 0.915f, _rpm / MidRpm);   // -0.78 dB trim
            SetLayer(_engC, wHigh / sum * vol * 0.931f, _rpm / HighRpm); // -0.62 dB trim
        }

        /// <summary>Triangular weight peaking at this layer's base RPM.</summary>
        static float Weight(float rpm, float lo, float peak, float hi)
        {
            if (rpm <= lo || rpm >= hi) return 0f;
            return rpm < peak
                ? Mathf.InverseLerp(lo, peak, rpm)
                : 1f - Mathf.InverseLerp(peak, hi, rpm);
        }

        static void SetLayer(AudioSource src, float volume, float pitch)
        {
            if (src == null || src.clip == null) return;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            if (!src.isPlaying && src.volume > 0.001f) src.Play();
        }

        // ------------------------------------------------------------------ blade

        void UpdateBlade(float dt)
        {
            if (mower == null) return;

            bool engaged = mower.BladeEngaged && _engineRunning;
            float bladeVol = engaged ? masterBlade * WorldMix : 0f;
            // The blade's chop order is 4x the mid layer, so it tracks the same RPM.
            float bladePitch = Mathf.Clamp(_rpm / MidRpm, 0.6f, 1.6f);

            _bladeA.volume = Mathf.MoveTowards(_bladeA.volume, bladeVol, dt * 3f);
            _bladeA.pitch = bladePitch;

            // The shredding layer is the audio reward for mowing grass that is actually there.
            float cutVol = engaged
                ? masterBlade * Mathf.Clamp01(mower.SmoothedCutLoad * 1.4f) * WorldMix
                : 0f;
            _bladeB.volume = Mathf.MoveTowards(_bladeB.volume, cutVol, dt * 5f);
            _bladeB.pitch = bladePitch * 0.98f;

            // Occasional stone off the deck while cutting hard.
            if (engaged && mower.SmoothedCutLoad > 0.35f && _storyMix < 0.5f &&
                _rng.NextDouble() < dt * 0.55f)
                PlayRandom(debrisPings, 0.25f);
        }

        void UpdateMower(float dt)
        {
            if (mower == null) return;

            float driftVol = mower.IsDrifting ? masterSfx * 0.5f * WorldMix : 0f;
            _drift.volume = Mathf.MoveTowards(_drift.volume, driftVol, dt * 4f);
            _drift.pitch = 0.9f + mower.SpeedFraction * 0.35f;

            float boostVol = mower.IsBoosting ? masterSfx * 0.55f * WorldMix : 0f;
            // The boost stings are gated on the gate rather than on the loop's volume, which is what
            // they used to read: with the loop held at zero by the story, every frame of a boost
            // would otherwise satisfy "boosting and silent" and fire the whoosh again.
            if (_storyMix < 0.5f)
            {
                if (mower.IsBoosting && _boost.volume < 0.01f) PlayOne(boostStart, 0.6f);
                if (!mower.IsBoosting && _boost.volume > 0.3f) PlayOne(boostEnd, 0.45f);
            }
            _boost.volume = Mathf.MoveTowards(_boost.volume, boostVol, dt * 6f);
        }

        // ------------------------------------------------------------------ music & states

        void UpdateMusic(float dt)
        {
            if (director == null) return;

            // The urgent layer rides on top of the round loop for the last stretch. Both were
            // rendered the same length at the same tempo so they stay sample-aligned.
            float wantUrgent = director.IsLowTime ? masterMusic * 0.85f : 0f;
            _urgentMix = Mathf.MoveTowards(_urgentMix, wantUrgent, dt * 0.5f);
            _musicUrgent.volume = _urgentMix;
        }

        void HandleState(GameState s)
        {
            switch (s)
            {
                case GameState.Intro:
                    // The story page. The bed, the engine and the ambience are all handled
                    // continuously off _storyMix — see Storybook for why this case cannot be
                    // trusted to run at all — so all there is to do here is make sure no round
                    // music is left playing under it, which matters when the story is entered
                    // from a state that had some.
                    _engineRunning = false;
                    SwapMusic(null, 0f);
                    break;

                case GameState.Briefing:
                    _engineRunning = false;
                    SwapMusic(musicRound, masterMusic * 0.6f);
                    break;

                case GameState.Countdown:
                    _engineRunning = true;
                    PlayOne(engineStart, 0.7f);
                    SwapMusic(musicRound, masterMusic);
                    break;

                case GameState.Mowing:
                    _engineRunning = true;
                    if (_lastState == GameState.Countdown) PlayOne(bladeEngage, 0.5f);
                    break;

                case GameState.Klaxon:
                    PlayOne(klaxon, 0.8f);
                    PlayOne(bladeDisengage, 0.4f);
                    _engineRunning = false;
                    PlayOneB(engineStop, 0.6f);
                    SwapMusic(null, 0f);
                    break;

                case GameState.Rally:
                    // Stage two. The mower is still out there (GooseDefence/RallyDirector drive it
                    // on the same chase camera), so this still needs the engine carve — musicRally
                    // was rendered with carve=True for exactly that reason. Re-engaged here rather
                    // than left running from Countdown because Klaxon already silenced the round
                    // loop above, and the rally deserves its own cue rather than dead air.
                    _engineRunning = true;
                    SwapMusic(musicRally, masterMusic);
                    break;

                case GameState.Bloom:
                    // Stage three, and the same contract the rally makes above: the arena is an
                    // additively loaded scene, "~ Audio" is one of the three Main roots TurfStage
                    // deliberately leaves awake, so this director is the one scoring it and the
                    // mowers running in it are the ones this director's engine layers follow.
                    // Hence the same carve, and hence re-engaging the engine here — Klaxon killed
                    // it, and a stage played on four lawnmowers cannot open in silence.
                    _engineRunning = true;
                    SwapMusic(musicBloom, masterMusic);
                    break;

                case GameState.Reveal:
                    // Stop whatever the run took to get here — the round loop went silent at
                    // Klaxon, but a round that passed through the rally left musicRally (and its
                    // engine) running, and nothing else was ever going to turn either off before
                    // the fanfare.
                    _engineRunning = false;
                    SwapMusic(null, 0f);
                    PlayOne(musicReveal, masterMusic * 1.15f);
                    _ambCrowd.volume = masterAmbience * crowdMurmur * 1.5f;
                    break;

                case GameState.Judging:
                    SwapMusic(musicJudgingBed, masterMusic * 0.8f);
                    break;

                case GameState.Ceremony:
                    // Drop the judging bed. It is still looping at this point — nothing has cleared
                    // it since the panel deliberated — and a title announced over the music that
                    // accompanies a scorecard being filled in sounds like a formality rather than a
                    // result. The crowd and the ceremony's own fanfare are the sound of this beat.
                    SwapMusic(null, 0f);
                    _ambCrowd.volume = masterAmbience * crowdMurmur * 1.8f;
                    break;
            }
            _lastState = s;
        }

        void HandleCountdown(int n)
        {
            if (n > 0) PlayOne(countdownBeep, 0.7f);
            else { PlayOne(countdownGo, 0.85f); PlayOne(quackHappy, 0.5f); }
        }

        void HandleImpact(float strength)
        {
            PlayRandom(suspensionBumps, 0.3f + strength * 0.3f);
            if (strength > 0.5f) PlayOneB(quackPanic, 0.5f);
        }

        void HandleJudgeScored(int index, float score, string quip)
        {
            PlayOne(cardRaise, 0.55f);

            bool good = score >= 6f;
            AudioClip voice = index switch
            {
                0 => good ? goatHigh : goatLow,
                1 => good ? badgerHigh : badgerLow,
                _ => good ? heronHigh : heronLow
            };
            PlayOneB(voice, 0.7f);

            if (score >= 5f) CrowdCheer(Mathf.InverseLerp(5f, 10f, score));
            else CrowdGroan(0.5f);
        }

        void HandleVerdict(float total, string rank)
        {
            PlayOne(stamp, 0.8f);
            // The applause the whole round has been building toward.
            CrowdCheer(Mathf.InverseLerp(8f, 28f, total), applaud: true);
            PlayOneB(total >= 20f ? fanfareGood : fanfareBad, masterMusic * 1.1f);
            if (total >= 24f) PlayOne(quackProud, 0.6f);
            else if (total < 12f) PlayOne(quackAnnoyed, 0.5f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Start a stage's music without a round having asked for it.
        ///
        /// Every other cue in this class is chosen by a <see cref="GameState"/> transition, which is
        /// right while a round is running and useless when one is not. A stage played in its own
        /// scene can be opened and pressed play on its own — that standalone run is the entire
        /// review loop for it — and in that run there is no GameDirector, no state change, and so
        /// no music at all. The stage was silent in exactly the mode it is developed in.
        ///
        /// Additive and unused by the round: nothing that already worked changes.
        /// </summary>
        public void PlayStageMusic(AudioClip clip, bool engineRunning = true)
        {
            _engineRunning = engineRunning;
            SwapMusic(clip, masterMusic);
        }

        /// <summary>
        /// Put stage two's bed on, from the arena itself.
        ///
        /// The state machine already does this on GameState.Rally, and that covers the real route
        /// in — but only that route. Open GooseRally on its own, or reach it by any path that does
        /// not run GameDirector, and the state change never happens, so the whole stage plays in
        /// silence. The match is the thing that knows it has started; asking here means the bed
        /// follows the match rather than following one particular way of arriving at it.
        ///
        /// Idempotent: SwapMusic compares clips, so the round's own call and this one cannot fight.
        /// </summary>
        public void PlayRallyMusic()
        {
            _engineRunning = true;
            SwapMusic(musicRally, masterMusic);
        }

        /// <summary>
        /// Change the bed under the game, without anybody hearing it happen.
        ///
        /// This used to assign a new clip to one source and call Play, which is a hard cut: the
        /// round's tune stops mid-bar and the arena's starts from its first sample, in the same
        /// frame the picture is also changing scene. Two abrupt things at once do not cancel out;
        /// they compound into the game visibly and audibly stopping to load something.
        ///
        /// So the outgoing bed is handed to a second source AT ITS CURRENT PLAYHEAD and faded down
        /// while the new one comes up. Handing over the playhead rather than restarting matters for
        /// the return trip in particular — a stage that ends and gives the round its music back
        /// should sound like the round's music was playing quietly in another room, not like
        /// somebody put the record on again.
        ///
        /// <paramref name="fade"/> is the crossfade in seconds. The default is a beat and a half at
        /// the game's tempo, which is long enough to be inaudible as an edit and short enough that
        /// a stage entrance still lands on its downbeat.
        /// </summary>
        void SwapMusic(AudioClip clip, float volume, float fade = 0.85f)
        {
            if (_music == null) return;

            if (_music.clip == clip)
            {
                // Same bed, different level. A ramp rather than a jump, for the same reason.
                _musicTarget = volume;
                _musicRate = Mathf.Max(volume, _music.volume) / Mathf.Max(fade, 0.05f);
                if (clip != musicRound) _musicUrgent.volume = 0f;
                return;
            }

            if (_musicOut != null && _music.clip != null && _music.isPlaying && _music.volume > 0.001f)
            {
                _musicOut.Stop();
                _musicOut.clip = _music.clip;
                _musicOut.loop = _music.loop;
                _musicOut.pitch = _music.pitch;
                _musicOut.volume = _music.volume;
                // Clamped off the end: a source told to start at exactly clip.length never plays.
                _musicOut.time = Mathf.Clamp(_music.time, 0f, Mathf.Max(_music.clip.length - 0.05f, 0f));
                _musicOut.Play();
                _musicOutRate = _musicOut.volume / Mathf.Max(fade, 0.05f);
            }

            _music.clip = clip;
            _music.volume = 0f;
            if (clip != null) _music.Play(); else _music.Stop();

            _musicTarget = volume;
            _musicRate = Mathf.Max(volume, 0.001f) / Mathf.Max(fade, 0.05f);
            if (clip != musicRound) _musicUrgent.volume = 0f;
        }

        /// <summary>Step both beds towards where SwapMusic last pointed them.</summary>
        void UpdateMusicFade(float dt)
        {
            if (_music != null)
            {
                _music.volume = Mathf.MoveTowards(_music.volume, _musicTarget, _musicRate * dt);
                // A source left running at zero after a swap to silence is a decoder still working
                // for nothing, which on WebGL is a real cost.
                if (_musicTarget <= 0.0001f && _music.volume <= 0.0001f && _music.isPlaying)
                    _music.Stop();
            }

            if (_musicOut != null && _musicOut.isPlaying)
            {
                _musicOut.volume = Mathf.MoveTowards(_musicOut.volume, 0f, _musicOutRate * dt);
                if (_musicOut.volume <= 0.0001f) { _musicOut.Stop(); _musicOut.clip = null; }
            }
        }

        /// <summary>
        /// The crowd reacts. One entry point so every cheer in the game comes from the same place
        /// and cannot stack into a wall of noise: a burst is refused if the last one was too
        /// recent, and the ambient bed swells behind it so the reaction sounds like it came out of
        /// a crowd that was already there.
        /// </summary>
        public void CrowdCheer(float intensity, bool applaud = false)
        {
            if (Time.unscaledTime - _lastCheer < 0.45f) return;
            _lastCheer = Time.unscaledTime;

            intensity = Mathf.Clamp01(intensity);
            var clip = applaud ? applause : (intensity >= 0.62f ? cheerBig : cheerSmall);
            PlayOne(clip, Mathf.Lerp(0.35f, 0.85f, intensity));
            _crowdSwell = Mathf.Max(_crowdSwell, Mathf.Lerp(0.25f, 0.7f, intensity));
        }

        /// <summary>The crowd is disappointed. Used when a contestant makes a mess of it.</summary>
        public void CrowdGroan(float intensity = 0.5f)
        {
            if (Time.unscaledTime - _lastCheer < 0.45f) return;
            _lastCheer = Time.unscaledTime;
            PlayOne(Random.value < 0.5f ? aww : gasp, Mathf.Lerp(0.3f, 0.7f, Mathf.Clamp01(intensity)));
            _crowdSwell = Mathf.Max(_crowdSwell, 0.3f);
        }

        float _lastCheer;
        float _crowdSwell;
        float _clock;

        [Tooltip("How loud the crowd murmurs under everything. This is the bed the cheers rise out of.")]
        [Range(0f, 2f)] public float crowdMurmur = 1.15f;

        public void PlayOne(AudioClip clip, float volume)
        {
            if (clip == null || _oneShot == null) return;
            _oneShot.PlayOneShot(clip, Mathf.Clamp01(volume * masterSfx / 0.7f));
        }

        /// <summary>
        /// Play a one-shot AT a world position, with real distance falloff and stereo placement.
        ///
        /// Falls back to the flat bus if the ring was never built, so a caller can always reach for
        /// this without checking.
        /// </summary>
        public void PlayAt(AudioClip clip, float volume, Vector3 world)
        {
            if (clip == null) return;
            if (_placed == null || _placed.Length == 0) { PlayOne(clip, volume); return; }

            var src = _placed[_nextPlaced];
            _nextPlaced = (_nextPlaced + 1) % _placed.Length;
            if (src == null) { PlayOne(clip, volume); return; }

            src.transform.position = world;
            src.PlayOneShot(clip, Mathf.Clamp01(volume * masterSfx / 0.7f));
        }

        const int PlacedVoices = 10;
        AudioSource[] _placed;
        int _nextPlaced;

        void PlayOneB(AudioClip clip, float volume)
        {
            if (clip == null || _oneShotB == null) return;
            _oneShotB.PlayOneShot(clip, Mathf.Clamp01(volume * masterSfx / 0.7f));
        }

        void PlayRandom(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            PlayOne(clips[_rng.Next(clips.Length)], volume);
        }

        void FadeIn(AudioSource src, float volume)
        {
            if (src == null || src.clip == null) return;
            src.volume = volume;
            if (!src.isPlaying) src.Play();
        }

        public void PlayHorn() => PlayOne(horn, 0.7f);

        /// <summary>
        /// The championship result. Louder than the per-round fanfare in <see cref="HandleVerdict"/>
        /// and routed through the second one-shot source, so it can ring under the applause the
        /// ceremony fires at the same instant instead of cutting it off.
        /// </summary>
        public void PlayFanfare(bool triumphant)
        {
            PlayOneB(triumphant ? fanfareGood : fanfareBad, masterMusic * 1.4f);
            if (triumphant) PlayOne(quackProud, 0.8f);
        }
    }
}
