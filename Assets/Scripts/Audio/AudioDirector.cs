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

        [Header("Music")]
        public AudioClip musicRound, musicRoundUrgent, musicReveal, musicJudgingBed;
        public AudioClip fanfareGood, fanfareBad;

        [Header("Mix")]
        [Range(0f, 1f)] public float masterEngine = 0.55f;
        [Range(0f, 1f)] public float masterBlade = 0.45f;
        [Range(0f, 1f)] public float masterAmbience = 0.35f;
        [Range(0f, 1f)] public float masterMusic = 0.34f;
        [Range(0f, 1f)] public float masterSfx = 0.7f;

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
        AudioSource _music, _musicUrgent;
        AudioSource _oneShot, _oneShotB;

        float _rpm = IdleRpm;
        bool _engineRunning = true;
        float _urgentMix;
        GameState _lastState = GameState.Boot;
        System.Random _rng;

        void Awake()
        {
            _rng = new System.Random(12345);

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

            _oneShot = MakeOneShot("OneShot");
            _oneShotB = MakeOneShot("OneShotB");
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

            FadeIn(_ambBirds, masterAmbience * 0.5f);
            FadeIn(_ambWind, masterAmbience * 0.7f);
            FadeIn(_ambCrowd, masterAmbience * 0.6f);
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
            UpdateEngine(dt);
            UpdateBlade(dt);
            UpdateMower(dt);
            UpdateMusic(dt);
            UpdateCrowd(dt);
        }

        /// <summary>
        /// The crowd bed rises under a reaction and settles again. Without this a cheer is a sound
        /// effect played at a crowd; with it, the crowd itself gets louder for a moment.
        /// </summary>
        void UpdateCrowd(float dt)
        {
            if (_ambCrowd == null) return;
            _crowdSwell = Mathf.MoveTowards(_crowdSwell, 0f, dt * 0.55f);
            float baseline = masterAmbience * 0.6f;
            _ambCrowd.volume = Mathf.Lerp(_ambCrowd.volume,
                                          baseline * (1f + _crowdSwell * 1.6f), dt * 4f);
        }

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
            float bladeVol = engaged ? masterBlade : 0f;
            // The blade's chop order is 4x the mid layer, so it tracks the same RPM.
            float bladePitch = Mathf.Clamp(_rpm / MidRpm, 0.6f, 1.6f);

            _bladeA.volume = Mathf.MoveTowards(_bladeA.volume, bladeVol, dt * 3f);
            _bladeA.pitch = bladePitch;

            // The shredding layer is the audio reward for mowing grass that is actually there.
            float cutVol = engaged ? masterBlade * Mathf.Clamp01(mower.SmoothedCutLoad * 1.4f) : 0f;
            _bladeB.volume = Mathf.MoveTowards(_bladeB.volume, cutVol, dt * 5f);
            _bladeB.pitch = bladePitch * 0.98f;

            // Occasional stone off the deck while cutting hard.
            if (engaged && mower.SmoothedCutLoad > 0.35f && _rng.NextDouble() < dt * 0.55f)
                PlayRandom(debrisPings, 0.25f);
        }

        void UpdateMower(float dt)
        {
            if (mower == null) return;

            float driftVol = mower.IsDrifting ? masterSfx * 0.5f : 0f;
            _drift.volume = Mathf.MoveTowards(_drift.volume, driftVol, dt * 4f);
            _drift.pitch = 0.9f + mower.SpeedFraction * 0.35f;

            float boostVol = mower.IsBoosting ? masterSfx * 0.55f : 0f;
            if (mower.IsBoosting && _boost.volume < 0.01f) PlayOne(boostStart, 0.6f);
            if (!mower.IsBoosting && _boost.volume > 0.3f) PlayOne(boostEnd, 0.45f);
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

                case GameState.Reveal:
                    PlayOne(musicReveal, masterMusic * 1.15f);
                    _ambCrowd.volume = masterAmbience * 0.9f;
                    break;

                case GameState.Judging:
                    SwapMusic(musicJudgingBed, masterMusic * 0.8f);
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

        void SwapMusic(AudioClip clip, float volume)
        {
            if (_music == null) return;
            if (_music.clip != clip)
            {
                _music.clip = clip;
                if (clip != null) _music.Play(); else _music.Stop();
            }
            _music.volume = volume;
            if (clip != musicRound) _musicUrgent.volume = 0f;
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

        public void PlayOne(AudioClip clip, float volume)
        {
            if (clip == null || _oneShot == null) return;
            _oneShot.PlayOneShot(clip, Mathf.Clamp01(volume * masterSfx / 0.7f));
        }

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
    }
}
