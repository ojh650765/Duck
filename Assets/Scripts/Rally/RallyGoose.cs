using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// A goose, from the moment it is served to the moment it is carried off.
    ///
    /// Explicitly not a ball with a goose on it. A ball has one state and a velocity; this thing
    /// flies in, lands badly, picks a garden, runs at it, braces when it sees the mower coming, gets
    /// hit, deforms, tumbles across the grass, shakes itself off, and gets up angrier. The states
    /// below are the whole point — take them out and the mode becomes tennis, which is a game the
    /// arena is much too big for.
    ///
    /// Moved by script rather than by a rigidbody. Four of these plus four mowers plus a hit stop is
    /// already a lot for a WebGL frame, and the thing physics would buy — believable tumbling —
    /// is exactly the thing that has to be authored anyway so a bird always ends up somewhere
    /// playable. Ground contact is a swept raycast, so nothing tunnels no matter how hard it is hit.
    ///
    /// Each goose counts its own parries and can be struck exactly twice: the second one kills it.
    /// That counter lives here rather than in the director because the pool recycles these, and a
    /// count kept anywhere else is a count that survives into the next bird.
    /// </summary>
    public class RallyGoose : MonoBehaviour
    {
        public enum State { Idle, Serve, Land, Charge, Brace, Struck, Recover, Crash, Leaving, Down }

        [Header("Visual rig")]
        [Tooltip("The whole bird, squashed and stretched along its travel. Usually the model root.")]
        public Transform body;
        [Tooltip("Neck root. Whipped by acceleration — the single biggest tell that it was HIT.")]
        public Transform neck;
        public Transform wingLeft;
        public Transform wingRight;
        public Animator animator;

        [Header("Flight")]
        public float serveSpeed = 21f;
        public float serveHeight = 17f;
        [Tooltip("Height it is dropped from as it crosses into the arena.")]
        public float serveEntryRadius = 62f;

        [Header("Charging")]
        [Tooltip("Ground speed at zero parries. This is the goose the player learns the rule on.")]
        public float chargeSpeed = 7.6f;
        [Tooltip("Extra ground speed after one successful parry. Angrier, and it shows.")]
        public float chargeSpeedPerParry = 4.4f;
        [Tooltip("How sharply it can turn while running, degrees per second.")]
        public float chargeTurnRate = 150f;
        [Tooltip("Height of the little hops it takes while running. A goose does not glide along the floor.")]
        public float hopHeight = 0.42f;
        [Tooltip("Metres of ground covered per full stride — one left step and one right step.\n\n" +
                 "Measured in DISTANCE, not in seconds, which is the whole reason the walk reads. Tie " +
                 "a gait to a clock and the feet skate: the bird speeds up and takes the same number " +
                 "of steps over more ground. Tie it to distance and every footfall lands where a " +
                 "footfall should, at any speed, with no blend tree and no root motion.")]
        public float strideLength = 1.35f;
        [Tooltip("Degrees the run wanders off the direct line, and how fast it wanders.\n\n" +
                 "A goose does not run down a surveyor's line. This is also defensive cover for the " +
                 "bird: a perfectly straight approach can be intercepted by parking on it once, and " +
                 "a weaving one has to be read. Falls away as it closes, so the last few metres are " +
                 "a committed charge rather than a drunkard's walk.")]
        public float weaveDegrees = 13f;
        public float weaveRate = 1.15f;

        [Header("Launch")]
        [Tooltip("Ground speed a Normal strike sends it away at.")]
        public float launchSpeedNormal = 15f;
        public float launchSpeedGood = 19f;
        public float launchSpeedPerfect = 25f;
        [Tooltip("Upward part of a launch. Enough to arc and bounce, not enough to leave the arena.")]
        public float launchLift = 5.2f;
        public float gravity = 21f;
        [Tooltip("How much speed a ground bounce keeps.")]
        [Range(0.1f, 0.9f)] public float bounceRestitution = 0.52f;
        [Tooltip("Below this vertical speed a bounce stops bouncing and the bird starts recovering.")]
        public float bounceSettle = 2.4f;

        [Header("Timing")]
        public float landSeconds = 0.85f;
        public float braceSeconds = 0.3f;
        public float recoverSeconds = 0.75f;
        public float crashSeconds = 1.5f;
        public float koSeconds = 1.15f;
        [Tooltip("Nobody may strike it again for this long. The whole of the double-parry guard: one " +
                 "contact per launch, whoever is standing there.")]
        public float strikeImmunity = 0.55f;
        [Tooltip("Seconds of no progress toward the target before it is written off and recycled. " +
                 "A goose wedged in a corner is worse than no goose at all.")]
        public float stuckSeconds = 6f;

        [Header("Rig")]
        [Tooltip("Metres the model's origin sits above the deck when the bird is standing on it. " +
                 "Not a guess and not a taste call: the ground clips are AUTHORED with the contact " +
                 "surface at 0.43 m below the origin, and every one of them keys the root height per " +
                 "frame so the webs stay planted through a stride. Put the origin anywhere else and " +
                 "the goose walks through the lawn or hovers over it, in a way no amount of animation " +
                 "tuning can fix. Airborne states are free — nothing is touching anything.")]
        public float deckOffset = 0.43f;

        [Header("Feel")]
        [Tooltip("How far it squashes on contact, and how far it stretches down its travel after.")]
        [Range(0f, 1f)] public float squashAmount = 0.55f;
        public float squashRecovery = 5.5f;
        [Tooltip("Degrees the neck whips on a hit.")]
        public float neckWhip = 78f;
        public float neckStiffness = 46f;
        public float neckDamping = 7.5f;

        // ------------------------------------------------------------------ live state

        public State Phase { get; private set; } = State.Idle;
        /// <summary>Successful parries taken. Two is dead.</summary>
        public int Parried { get; private set; }
        /// <summary>Who it is currently going for. -1 while it belongs to nobody.</summary>
        public int TargetSlot { get; private set; } = -1;
        /// <summary>Who hit it last, so an own-goal and a kill can be told apart. -1 for a fresh serve.</summary>
        public int LastStriker { get; private set; } = -1;
        public Vector3 Velocity => _vel;
        /// <summary>
        /// Where it was at the top of this frame. The other end of the segment every strike test is
        /// run against — see <see cref="RallyCompetitor.Evaluate"/>. Without this the mode silently
        /// eats parries whenever the frame rate dips or the bird is going fast, which is exactly when
        /// the player most believes they connected.
        /// </summary>
        public Vector3 PreviousPosition => _lastPos;
        public bool Active => Phase != State.Idle;
        /// <summary>True while it can be struck. False through the immunity window and after it is dead.</summary>
        public bool Strikeable => (Phase == State.Charge || Phase == State.Brace ||
                                   Phase == State.Struck || Phase == State.Recover) &&
                                  _immunity <= 0f;
        /// <summary>True when it is closing on a garden rather than picking itself up. Drives the HUD.</summary>
        public bool Threatening => Phase == State.Charge || Phase == State.Brace ||
                                   (Phase == State.Struck && _vel.y < 2f);
        /// <summary>How dangerous it looks right now, 0..1. Drives alarm, colour and audio.</summary>
        public float Menace => Mathf.Clamp01(0.32f + Parried * 0.34f);

        public System.Action<RallyGoose> OnRetired;

        RallyDirector _director;
        Vector3 _vel;
        float _timer;
        float _immunity;
        /// <summary>Stride phase in radians, 0..2pi over one left-and-right step. Advanced by DISTANCE.</summary>
        float _gait;
        /// <summary>How much of the walk is showing, 0..1. Faded so a hit does not snap the pose off.</summary>
        float _gaitWeight;
        float _weavePhase;
        float _callTimer;
        float _squash;
        float _neckAngle, _neckVel;
        float _groundY;
        float _stuckTimer;
        float _bestDistance;
        Vector3 _lastPos;
        float _dustTimer;
        int _bounces;
        float _wingPhase;
        /// <summary>
        /// The spot in the garden this bird is actually going for.
        ///
        /// Not the garden's centre, and that is a gameplay fix rather than a decoration. Aiming at
        /// the centre meant every goose ran down the exact diagonal the defender was already parked
        /// on, so the encounter was decided before it started — the defender either happened to be
        /// standing on the line or was not, and nothing they did in between mattered. Spreading the
        /// attack across the frontage is what turns defending into DRIVING: you read where it is
        /// going and you get there.
        /// </summary>
        Vector3 _attackAim;
        /// <summary>Where a served bird is flying to. Middle of the arena, never anybody's garden.</summary>
        Vector3 _serveMark;

        static readonly int HashSpeed = Animator.StringToHash("Speed");

        Quaternion _bodyRest = Quaternion.identity, _neckRest = Quaternion.identity;
        Quaternion _wingLRest = Quaternion.identity, _wingRRest = Quaternion.identity;
        bool _restTaken;

        void TakeRest()
        {
            if (_restTaken) return;
            _restTaken = true;
            if (body != null) _bodyRest = body.localRotation;
            if (neck != null) _neckRest = neck.localRotation;
            if (wingLeft != null) _wingLRest = wingLeft.localRotation;
            if (wingRight != null) _wingRRest = wingRight.localRotation;
        }

        void Awake() => TakeRest();

        public void Init(RallyDirector director)
        {
            _director = director;
            Retire(false);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Send a fresh goose at a garden. Parry count starts at zero and so does everything else.</summary>
        public void Serve(int targetSlot)
        {
            gameObject.SetActive(true);
            Parried = 0;
            LastStriker = -1;
            TargetSlot = targetSlot;
            _immunity = 0f;
            _bounces = 0;
            _squash = 0f;
            _neckAngle = _neckVel = 0f;
            _stuckTimer = 0f;

            var t = RallyArena.Get(targetSlot);
            AimSomewhereInGarden(t);
            // Offset across the frontage as well, so the run-up is already on a diagonal and the
            // defender has to move before the bird has even landed.
            Vector3 touchdown = RallyArena.Touchdown(t)
                              + t.right * Random.Range(-7f, 7f);
            touchdown.y = _groundY;
            _serveMark = touchdown;

            // Comes in over the shoulder of the garden it is going for, so the player sees it arrive
            // from the direction it will attack from rather than materialising in the middle.
            Vector3 fromDir = Quaternion.Euler(0f, Random.Range(-38f, 38f), 0f) * t.outward;
            Vector3 start = touchdown + fromDir * serveEntryRadius + Vector3.up * serveHeight;

            transform.position = start;
            Vector3 to = touchdown - start;
            _vel = to.normalized * serveSpeed;
            transform.rotation = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z).normalized, Vector3.up);
            _lastPos = start;
            _bestDistance = float.MaxValue;

            Enter(State.Serve);
            Play("Fly", 0.1f);
            RallyFX.Instance?.ServeCall(start, targetSlot);
        }

        /// <summary>
        /// Somebody connected. Angrier, faster, and pointed at whoever they aimed at — unless that
        /// was its second, in which case it is over.
        /// </summary>
        public void Struck(RallyCompetitor by, RallyStrike.Tier tier, Vector3 contact,
                           int newTarget, Vector3 launchDir)
        {
            Parried++;
            LastStriker = by.slot;
            _immunity = strikeImmunity;
            _bounces = 0;
            _stuckTimer = 0f;
            _bestDistance = float.MaxValue;

            float speed = tier switch
            {
                RallyStrike.Tier.Perfect => launchSpeedPerfect,
                RallyStrike.Tier.Good => launchSpeedGood,
                _ => launchSpeedNormal
            };

            // Squash on the frame of contact and whip the neck the other way. Both are impulses into
            // springs rather than animations, so a Perfect visibly deforms the bird further than a
            // Normal without anybody authoring three versions of the reaction.
            float bite = tier == RallyStrike.Tier.Perfect ? 1f : tier == RallyStrike.Tier.Good ? 0.72f : 0.5f;
            _squash = bite;
            _neckVel = -neckWhip * bite * 14f;

            if (Parried >= 2)
            {
                TargetSlot = -1;
                _vel = launchDir * (speed * 0.6f) + Vector3.up * (launchLift * 1.5f);
                Enter(State.Down);
                Play("KO", 0.02f);
                RallyFX.Instance?.Knockout(contact, launchDir, by.Livery);
                return;
            }

            TargetSlot = newTarget;
            AimSomewhereInGarden(RallyArena.Get(newTarget));
            _vel = launchDir * speed + Vector3.up * (launchLift * (tier == RallyStrike.Tier.Perfect ? 1.15f : 1f));
            Enter(State.Struck);
            Play("Struck", 0.02f);
            RallyFX.Instance?.Parry(contact, launchDir, tier, by.Livery);
        }

        /// <summary>
        /// Called on the horn. The bird leaves under its own power rather than being deleted, and
        /// stops being a threat to anybody on the way out.
        /// </summary>
        public void Dismiss()
        {
            if (!Active || Phase == State.Down) return;
            TargetSlot = -1;
            Enter(State.Leaving);
            Play("Fly", 0.15f);
        }

        /// <summary>
        /// Driven back by the horn. Not damage and not a parry — ground lost.
        ///
        /// The bird is thrown backwards, loses its footing, and has to pick its line up again, which
        /// costs it about a second of approach. Its parry count is untouched: the horn buys time, it
        /// does not win the point, or it would be a better weapon than the mower.
        /// </summary>
        public void Startle(Vector3 away, float strength)
        {
            if (Phase != State.Charge && Phase != State.Brace && Phase != State.Recover) return;

            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = -transform.forward;
            away.Normalize();

            strength = Mathf.Clamp01(strength);
            _vel = away * (5f + strength * 5f) + Vector3.up * (2.2f + strength * 2f);
            _squash = Mathf.Max(_squash, 0.45f);
            _neckVel = neckWhip * 9f;
            _stuckTimer = 0f;
            _bestDistance = float.MaxValue;
            if (TargetSlot >= 0) AimSomewhereInGarden(RallyArena.Get(TargetSlot));

            Enter(State.Recover);
            Play("Recover", 0.05f);
            RallyFX.Instance?.Hiss(transform.position, Menace);
        }

        /// <summary>Take it out of play and hand the slot back to the director.</summary>
        public void Retire(bool notify = true)
        {
            Phase = State.Idle;
            TargetSlot = -1;
            _vel = Vector3.zero;
            gameObject.SetActive(false);
            if (notify) OnRetired?.Invoke(this);
        }

        void Enter(State s)
        {
            Phase = s;
            _timer = 0f;
        }

        /// <summary>
        /// Cross-fade to a pose, if the rig has one by that name.
        ///
        /// Asks the animator whether the STATE exists, not whether a clip does. Those are different
        /// questions and getting them confused is what filled the console with
        /// "Animator.GotoState: State could not be found" — the FBX carried a Charge clip, so the
        /// clip test passed, while the controller of the day had it buried inside a blend tree with
        /// no state of its own to fade to. Missing a pose is not an error: the bird keeps whatever it
        /// was playing and its procedural half still deforms it correctly.
        /// </summary>
        void Play(string clip, float fade)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!_states.TryGetValue(clip, out int hash))
            {
                int h = Animator.StringToHash(clip);
                hash = animator.HasState(0, h) ? h : 0;   // 0 is the cached "this rig has no such pose"
                _states[clip] = hash;
            }
            if (hash == 0) return;
            animator.CrossFadeInFixedTime(hash, fade, 0);
        }

        readonly System.Collections.Generic.Dictionary<string, int> _states = new(8);
        /// <summary>Whether the controller declares Speed. Writing a parameter it does not have is a
        /// console error every frame, and this bird ticks sixty times a second.</summary>
        bool _hasSpeedParam;
        bool _paramsChecked;

        // ------------------------------------------------------------------ the tick

        /// <summary>
        /// Stepped by the director rather than by Update, so a hit stop and the match clock are the
        /// same clock and nothing can carry on moving through a freeze.
        /// </summary>
        public void Tick(float dt)
        {
            if (Phase == State.Idle || dt <= 0f) return;

            // Captured once, here, rather than inside Step. Charging sets the position directly and
            // never calls Step, so a bird running at a garden had no previous position to sweep
            // against and was the one state that could still slip past a defender unhit.
            _lastPos = transform.position;

            // A hard ceiling on how far anything moves in one tick.
            //
            // A backgrounded browser tab hands back a delta of a second or more on the frame it wakes
            // up. At that step a goose crosses the whole arena, arrives inside somebody's flowers and
            // the match records a breach nobody was given a chance to defend. Clamping is the honest
            // answer: the bird moves a bit slower through a stall rather than teleporting through it.
            if (dt > 0.1f) dt = 0.1f;

            _timer += dt;
            if (_immunity > 0f) _immunity -= dt;

            switch (Phase)
            {
                case State.Serve: TickServe(dt); break;
                case State.Land: TickLand(dt); break;
                case State.Charge:
                case State.Brace: TickCharge(dt); break;
                case State.Struck: TickStruck(dt); break;
                case State.Recover: TickRecover(dt); break;
                case State.Crash: TickCrash(dt); break;
                case State.Leaving: TickLeaving(dt); break;
                case State.Down: TickDown(dt); break;
            }

            TickBounds();
        }

        /// <summary>
        /// The deformation, applied AFTER the Animator has had its say.
        ///
        /// This has to be LateUpdate and it took a frame-by-frame capture to see why: the Animator
        /// writes the bones during its own update phase, which runs after Update — so a neck whipped
        /// from the director's tick was overwritten before it was ever drawn, and the bird took a
        /// perfect parry without moving a muscle. Squash, whip and flap all sit on top of whatever
        /// clip is playing, which is also what lets a hit landing mid-stride deform the bird
        /// correctly instead of snapping it to a reaction pose.
        /// </summary>
        void LateUpdate()
        {
            if (Phase == State.Idle) return;
            TickVisual(Time.deltaTime);
        }

        /// <summary>
        /// The approach: flown to a mark, not dropped.
        ///
        /// It was ballistic, and ballistic was catastrophically wrong — not ugly, WRONG. Launched
        /// from 62 m out at 21 m/s with gravity on it, the bird hit the ground about thirty metres
        /// short of where it was aimed, which is outside the target's own garden. So it landed BEHIND
        /// the garden it was attacking and strolled forward into the flowers, never once crossing the
        /// strip the defender is stood on. Every goose scored, nobody could parry anything, and the
        /// logs showed only a tidy list of breaches.
        ///
        /// Steering to the mark instead guarantees the one thing the whole mode rests on: a goose
        /// touches down in the middle of the arena, and the only way to its target is through
        /// somebody. Authored rather than simulated, for the same reason the hops are.
        /// </summary>
        void TickServe(float dt)
        {
            Vector3 to = _serveMark - transform.position; to.y = 0f;
            float d = to.magnitude;
            Vector3 dir = d > 0.05f ? to / d : transform.forward;

            float eta = d / Mathf.Max(serveSpeed, 0.1f);
            float drop = (_groundY + deckOffset) - transform.position.y;
            // Arrives at the deck exactly as it arrives at the mark, with a floor on the descent so
            // the last metre is a stall and a flare rather than a vertical plummet.
            float vy = eta > 0.08f ? Mathf.Max(drop / eta, -serveSpeed * 0.8f) : -3.5f;
            _vel = dir * serveSpeed + Vector3.up * vy;

            Step(dt);
            FaceTravel(dt, 9f);

            if (transform.position.y <= GroundAt(transform.position) + deckOffset + 0.12f || d < 0.6f)
            {
                transform.position = new Vector3(transform.position.x,
                                                 GroundAt(transform.position) + deckOffset,
                                                 transform.position.z);
                // A bad landing. Keeps most of its speed and skids, which is what makes the arrival
                // read as an animal crashing in rather than as a spawn point going off.
                _vel = new Vector3(_vel.x, 3.1f, _vel.z) * 0.62f;
                Enter(State.Land);
                Play("Land", 0.04f);
                RallyFX.Instance?.GroundHit(transform.position, _vel, 0.6f);
            }
        }

        void TickLand(float dt)
        {
            _vel.y -= gravity * dt;
            _vel.x *= 1f - 2.4f * dt;
            _vel.z *= 1f - 2.4f * dt;
            Step(dt);
            Ground();
            FaceTravel(dt, 6f);

            if (_timer < landSeconds) return;
            Enter(State.Charge);
            Play("Charge", 0.12f);
        }

        void TickCharge(float dt)
        {
            if (TargetSlot < 0) { Enter(State.Leaving); return; }
            var t = RallyArena.Get(TargetSlot);

            Vector3 to = _attackAim - transform.position; to.y = 0f;
            float dist = to.magnitude;

            // Brace when the defender is close enough to swing. A quarter of a second of visible
            // anticipation is what turns a collision into a moment the player was ready for.
            var defender = _director != null ? _director.CompetitorAt(TargetSlot) : null;
            bool close = defender != null &&
                         (defender.Position - transform.position).sqrMagnitude < 26f;
            if (Phase == State.Charge && close)
            {
                Enter(State.Brace);
                Play("Brace", 0.06f);
                RallyFX.Instance?.Anticipate(transform.position, Menace);
            }
            else if (Phase == State.Brace && !close && _timer > braceSeconds)
            {
                Enter(State.Charge);
                Play("Charge", 0.1f);
            }

            float cruise = chargeSpeed + chargeSpeedPerParry * Parried;
            if (Phase == State.Brace) cruise *= 0.72f;

            // ------------------------------------------------------------------ the walk
            //
            // A goose does not travel at a constant speed in a straight line, and the version that
            // did was the single thing making this bird read as a ball with a goose on it. Three
            // things fix it, and none of them is a clip:
            //
            //   the stride is measured in METRES, so footfalls land where footfalls belong at any
            //   speed and nothing ever skates;
            //
            //   the speed SURGES within each stride, because a goose covers ground in shoves — it
            //   pushes off, coasts, pushes off — and a constant rate is the one thing a walking
            //   animal never does;
            //
            //   the line WEAVES, wide when it is far out and committing as it closes.
            //
            // Everything the eye actually reads as "goose" — the roll onto the planted foot, the
            // head thrust, the half-open wings — hangs off the phase computed here. See TickVisual.
            _gait += (cruise * dt / Mathf.Max(strideLength, 0.2f)) * Mathf.PI * 2f;
            if (_gait > Mathf.PI * 2f) _gait -= Mathf.PI * 2f;

            float surge = 0.76f + 0.48f * Mathf.Abs(Mathf.Sin(_gait));
            float speed = cruise * surge;

            Vector3 want = dist > 1e-3f ? to / dist : transform.forward;

            _weavePhase += dt * weaveRate * (1f + Parried * 0.35f);
            float weave = Mathf.Sin(_weavePhase) * weaveDegrees * Mathf.Clamp01((dist - 4f) / 12f);
            want = Quaternion.AngleAxis(weave, Vector3.up) * want;

            Vector3 fwd = Vector3.RotateTowards(transform.forward, want,
                                                chargeTurnRate * Mathf.Deg2Rad * dt, 0f);
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            // The vertical is authored, not simulated: a running bird's gait is a shape, and a
            // spring would only ever approximate it. Two rises per stride, one per foot.
            float y = GroundAt(transform.position) + deckOffset + Mathf.Abs(Mathf.Sin(_gait)) * hopHeight;
            Vector3 p = transform.position + transform.forward * (speed * dt);
            p.y = y;
            transform.position = p;
            _vel = transform.forward * speed;

            _callTimer -= dt;
            if (_callTimer <= 0f)
            {
                _callTimer = Random.Range(2.2f, 4.6f) / (1f + Parried * 0.5f);
                RallyFX.Instance?.Honk(transform.position, Menace);
            }

            Dust(dt, 0.16f, 0.5f + Parried * 0.25f);
            RaiseAlarm(t, dist);
            Progress(dist, dt);

            if (RallyArena.CrossedFence(t, _lastPos, transform.position)) { BeginCrash(t); return; }

            // A charging bird that goes WIDE of the pickets leaves, rather than milling about.
            //
            // This check existed only for a bird in the air off a parry, and on the ground is where
            // it actually matters: the fence is nineteen metres of a thirty-metre frontage, so a
            // goose running in on a shallow angle can pass the fence LINE outside the fence itself,
            // at which point breaching is arithmetically impossible for it — CrossedFence requires
            // the lateral offset to be inside the pickets. It cannot score and it will not turn
            // round, so it wanders behind the garden until the six-second stuck timer notices.
            // A bird that has run out of runway should just go.
            if (RallyArena.MissedWide(t, _lastPos, transform.position))
            {
                TargetSlot = -1;
                Enter(State.Leaving);
                Play("Fly", 0.12f);
            }
        }

        /// <summary>
        /// Pick a spot in a garden to run at. Somewhere across the frontage, never dead centre.
        ///
        /// Public knowledge on purpose: the defender's own prediction reads the bird's velocity, not
        /// this, so choosing a corner is information the goose has and nobody else does — which is
        /// what makes reading its line a skill rather than a lookup.
        /// </summary>
        void AimSomewhereInGarden(in RallyArena.Slot t)
        {
            _attackAim = t.gardenCentre
                       + t.right * Random.Range(-RallyArena.GardenHalfWidth * 0.85f,
                                                 RallyArena.GardenHalfWidth * 0.85f)
                       + t.outward * Random.Range(-1.5f, 2.5f);
            _attackAim.y = _groundY;
        }

        void RaiseAlarm(in RallyArena.Slot t, float dist)
        {
            var g = _director != null ? _director.GardenAt(t.index) : null;
            if (g == null) return;
            g.Alarm(Mathf.Clamp01(1f - dist / 26f) * (0.5f + Menace * 0.5f));
        }

        void TickStruck(float dt)
        {
            _vel.y -= gravity * dt;
            Step(dt);
            FaceTravel(dt, 14f);

            float g = GroundAt(transform.position);
            if (transform.position.y > g + deckOffset)
            {
                Trail(dt);
                CheckArrival();
                return;
            }

            // Ground bounce. Directional, and it leaves a mark: this is most of how the trajectory
            // stays readable once the bird is travelling faster than the camera can follow.
            transform.position = new Vector3(transform.position.x, g + deckOffset, transform.position.z);
            _bounces++;
            RallyFX.Instance?.GroundHit(transform.position, _vel, 0.5f + Menace * 0.5f);
            _squash = Mathf.Max(_squash, 0.4f);

            if (Mathf.Abs(_vel.y) < bounceSettle || _bounces > 3)
            {
                _vel.y = 0f;
                _vel.x *= 0.5f; _vel.z *= 0.5f;
                Enter(State.Recover);
                Play("Recover", 0.05f);
                return;
            }

            _vel.y = -_vel.y * bounceRestitution;
            _vel.x *= 0.86f; _vel.z *= 0.86f;
            CheckArrival();
        }

        /// <summary>
        /// A struck goose that reaches a garden while still in the air breaks in from above, which
        /// is the funniest and the fairest version: the defender it was aimed at had the whole flight
        /// to get across, and missing means watching it come down in the flowers.
        /// </summary>
        void CheckArrival()
        {
            if (TargetSlot < 0) return;
            var t = RallyArena.Get(TargetSlot);
            var g = _director != null ? _director.GardenAt(TargetSlot) : null;
            g?.Alarm(0.8f);
            if (RallyArena.CrossedFence(t, _lastPos, transform.position)) { BeginCrash(t); return; }

            // Sent past the garden but wide of the pickets: the shot missed, and a miss is over.
            // The bird carries on out rather than landing behind the defence and walking back in.
            if (RallyArena.MissedWide(t, _lastPos, transform.position))
            {
                TargetSlot = -1;
                Enter(State.Leaving);
                Play("Fly", 0.1f);
            }
        }

        void TickRecover(float dt)
        {
            _vel.x *= 1f - 4f * dt;
            _vel.z *= 1f - 4f * dt;
            _vel.y -= gravity * dt;
            Step(dt);
            Ground();

            if (_timer < recoverSeconds) return;
            if (TargetSlot < 0) { Enter(State.Leaving); return; }
            Enter(State.Charge);
            Play("Charge", 0.12f);
        }

        void BeginCrash(in RallyArena.Slot t)
        {
            Vector3 from = _lastPos;
            // Carry on into the beds rather than stopping at the fence, so the damage lands where a
            // tumbling bird would actually end up.
            Vector3 into = transform.position + new Vector3(_vel.x, 0f, _vel.z).normalized * 3.4f;
            into = Vector3.ClampMagnitude(into - t.gardenCentre, RallyArena.GardenHalfWidth * 0.8f) + t.gardenCentre;
            into.y = GroundAt(into) + deckOffset;

            _director?.ReportBreach(this, t.index, from, into);
            transform.position = into;
            _vel = new Vector3(_vel.x, 2.6f, _vel.z) * 0.4f;
            Enter(State.Crash);
            Play("Recover", 0.05f);
        }

        void TickCrash(float dt)
        {
            _vel.y -= gravity * dt;
            _vel.x *= 1f - 3f * dt;
            _vel.z *= 1f - 3f * dt;
            Step(dt);
            Ground();
            Dust(dt, 0.1f, 0.9f);
            if (_timer >= crashSeconds) { Enter(State.Leaving); Play("Fly", 0.15f); }
        }

        void TickLeaving(float dt)
        {
            // Flies off rather than blinking out. A bird that vanishes is an object being deleted;
            // one that beats its way over the barrier is a bird that has had enough.
            Vector3 out_ = transform.position; out_.y = 0f;
            Vector3 dir = out_.sqrMagnitude > 1f ? out_.normalized : Vector3.forward;
            _vel = Vector3.Lerp(_vel, dir * 13f + Vector3.up * 7f, 3f * dt);
            Step(dt);
            FaceTravel(dt, 5f);
            if (_timer > 2.2f || transform.position.y > 20f) Retire();
        }

        void TickDown(float dt)
        {
            _vel.y -= gravity * 0.75f * dt;
            _vel.x *= 1f - 1.6f * dt;
            _vel.z *= 1f - 1.6f * dt;
            Step(dt);

            float g = GroundAt(transform.position);
            if (transform.position.y < g + deckOffset)
            {
                transform.position = new Vector3(transform.position.x, g + deckOffset, transform.position.z);
                _vel.y = Mathf.Abs(_vel.y) * 0.3f;
                _vel.x *= 0.5f; _vel.z *= 0.5f;
            }

            // Spins out as it goes. A knocked-out goose has to look knocked out from any angle,
            // including the three-quarters of the arena that are not looking at the burst.
            transform.Rotate(Vector3.up, 620f * dt, Space.World);
            transform.Rotate(Vector3.right, 210f * dt, Space.Self);

            if (_timer >= koSeconds) Retire();
        }

        // ------------------------------------------------------------------ helpers

        void Step(float dt)
        {
            Vector3 next = transform.position + _vel * dt;

            // Swept against the ground, so nothing tunnels through the floor however hard it is hit.
            float gNext = GroundAt(next);
            if (next.y < gNext + 0.02f && _vel.y < 0f) next.y = gNext + 0.02f;
            transform.position = next;
        }

        void Ground()
        {
            float g = GroundAt(transform.position);
            if (transform.position.y >= g + deckOffset) return;
            transform.position = new Vector3(transform.position.x, g + deckOffset, transform.position.z);
            if (_vel.y < 0f) _vel.y = 0f;
        }

        float GroundAt(Vector3 p)
        {
            // The arena is flat, and saying so is cheaper and steadier than a raycast per goose per
            // frame. The field is here as one number so a raised bed or a ramp later has one place
            // to change.
            return _groundY;
        }

        public void SetGroundHeight(float y) => _groundY = y;

        /// <summary>
        /// Point the bird down its travel — but never nose-first into the ground.
        ///
        /// A goose coming down off a bounce has a steeply negative vertical velocity, and aiming the
        /// whole body along that vector drives the head and neck straight through the deck: from the
        /// chase camera the bird arrives beak-buried with its tail in the air.
        ///
        /// The clamp is ASYMMETRIC, and that asymmetry is the whole correction. It used to squeeze
        /// both directions down to six degrees near the deck, which fixed the burying and broke
        /// something worse: a bird flung upward off a parry ALSO had its nose pinned level, so it
        /// rose out of the arena flat like a thrown plank and never once lifted its head. Nose-down
        /// is the direction with a floor in it; nose-up has nothing to collide with, and a goose
        /// climbing steeply should be looking where it is going. So the ground clamps the dive and
        /// leaves the climb alone — the head goes up, and stays out of the lawn.
        /// </summary>
        void FaceTravel(float dt, float rate)
        {
            Vector3 v = _vel;
            if (v.sqrMagnitude < 0.25f) return;

            Vector3 dir = v.normalized;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f) return;         // straight up or down: keep the heading
            flat.Normalize();

            // How much room is left underneath, 0 at the deck and 1 a couple of metres up.
            float clearance = Mathf.Clamp01((transform.position.y - (_groundY + deckOffset)) / 2f);
            float maxDive = Mathf.Lerp(4f, 40f, clearance);
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg,
                                      -maxDive, 62f);

            Quaternion want = Quaternion.LookRotation(flat, Vector3.up)
                            * Quaternion.Euler(-pitch, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-rate * dt));
        }

        void Progress(float dist, float dt)
        {
            if (dist < _bestDistance - 0.25f) { _bestDistance = dist; _stuckTimer = 0f; return; }
            _stuckTimer += dt;
            if (_stuckTimer > stuckSeconds)
            {
                Debug.Log($"[Rally] goose gave up on garden {TargetSlot} after {stuckSeconds:0.0}s of no progress.");
                Enter(State.Leaving);
                Play("Fly", 0.15f);
            }
        }

        void TickBounds()
        {
            Vector3 p = transform.position;
            float r2 = p.x * p.x + p.z * p.z;
            float limit = RallyArena.ArenaRadius + 12f;
            if (r2 > limit * limit && Phase != State.Serve && Phase != State.Leaving)
            {
                Enter(State.Leaving);
                Play("Fly", 0.15f);
            }
            if (p.y < _groundY - 6f) Retire();
        }

        void Dust(float dt, float interval, float strength)
        {
            _dustTimer -= dt;
            if (_dustTimer > 0f) return;
            _dustTimer = interval;
            RallyFX.Instance?.Scuff(transform.position, _vel, strength);
        }

        void Trail(float dt)
        {
            _dustTimer -= dt;
            if (_dustTimer > 0f) return;
            _dustTimer = 0.045f;
            RallyFX.Instance?.Trail(transform.position, _vel, Menace);
        }

        /// <summary>
        /// Squash, stretch, neck whip and wings — all of it driven off what the bird is doing rather
        /// than played as clips, so a hit that arrives mid-stride still deforms it correctly.
        /// </summary>
        void TickVisual(float dt)
        {
            _squash = Mathf.MoveTowards(_squash, 0f, squashRecovery * dt);

            bool walking = Phase == State.Charge || Phase == State.Brace;
            _gaitWeight = Mathf.MoveTowards(_gaitWeight, walking ? 1f : 0f, dt * 5f);

            // One stride, read three ways. The bob is already in the position — see TickCharge —
            // and these are the parts that make it a BIRD rather than a bouncing object:
            //
            //   roll   the body tips onto whichever foot is planted. One cycle per stride, so it
            //          leans left, then right. This is the waddle; without it a goose reads as a
            //          hopping ball no matter how good the hop is.
            //   sway   the tail end swings the other way, because that is where the mass goes.
            //   lunge  the chest dips forward on the push-off and comes back on the coast, which is
            //          what turns a walk into a bird that means it.
            float roll = Mathf.Sin(_gait) * 14f * _gaitWeight;
            float sway = -Mathf.Sin(_gait) * 6f * _gaitWeight;
            float lunge = (Mathf.Abs(Mathf.Sin(_gait)) * 7f + Menace * 9f) * _gaitWeight;

            // Airborne, the whole bird lurches with the beat. A goose in flapping flight is not a
            // stable object being carried along; it is fighting for every metre.
            if (Phase == State.Serve || Phase == State.Leaving || Phase == State.Land)
            {
                roll += Mathf.Sin(_wingPhase * 0.5f) * 19f;
                lunge += Mathf.Sin(_wingPhase) * 11f;
                sway += Mathf.Sin(_wingPhase * 0.33f) * 9f;
            }

            if (body != null)
            {
                float s = _squash * squashAmount;
                // Stretched down travel and flattened across it: the classic read, and it works
                // because the bird is already rotated to face where it is going.
                body.localScale = new Vector3(1f - s * 0.6f, 1f - s * 0.75f, 1f + s);
                // Composed in the ROOT's frame, then whatever rest rotation the node was authored
                // with. The mesh's own correction lives a level below and stays out of this.
                body.localRotation = Quaternion.Euler(lunge, sway, roll) * _bodyRest;
            }

            if (neck != null)
            {
                // A real spring, so the neck overshoots, comes back, overshoots less. A curve would
                // have to be re-authored for every strength; this one just takes a bigger shove.
                float accel = -neckStiffness * _neckAngle - neckDamping * _neckVel;
                _neckVel += accel * dt;
                _neckAngle += _neckVel * dt;
                _neckAngle = Mathf.Clamp(_neckAngle, -neckWhip * 1.6f, neckWhip * 1.6f);

                // The head bob, and the head is UP.
                //
                // A walking bird thrusts its head forward on the step and holds it still in space
                // while the body catches up — offset off the stride so the nod and the footfall do
                // not land together, which is what makes it look mechanical when they do. On top of
                // that the neck is carried RAISED: a charging goose runs with its head high and its
                // beak level, and it only levels off — never drops — as it gets angry, which is the
                // difference between a bird hunting you down and a bird looking for worms.
                float nod = Mathf.Sin(_gait - 0.9f) * 13f * _gaitWeight;
                float carry = -Mathf.Lerp(15f, 6f, Menace) * _gaitWeight;
                neck.localRotation = Quaternion.Euler(_neckAngle + nod + carry,
                                                      _neckAngle * 0.25f + sway * 0.5f, 0f)
                                   * _neckRest;
            }

            float rate, amp;
            switch (Phase)
            {
                case State.Serve: rate = 44f; amp = 108f; break;
                case State.Land: rate = 40f; amp = 100f; break;
                case State.Leaving: rate = 42f; amp = 104f; break;
                case State.Down: rate = 38f; amp = 88f; break;
                case State.Struck: rate = 30f; amp = 74f; break;
                case State.Recover: rate = 11f; amp = 28f; break;
                default: rate = 9f; amp = 17f; break;
            }
            float wingBefore = _wingPhase;
            _wingPhase += dt * rate;

            // One sound per downstroke, fired off the animation rather than off a timer, so the
            // rate the ear hears is exactly the rate the eye sees however hard the bird is working.
            if (rate > 20f && Mathf.Floor(wingBefore / Mathf.PI) != Mathf.Floor(_wingPhase / Mathf.PI))
                RallyFX.Instance?.WingBeat(transform.position, Mathf.Clamp01(amp / 110f));

            float sw = Mathf.Sin(_wingPhase);
            sw = Mathf.Sign(sw) * Mathf.Pow(Mathf.Abs(sw), 0.55f);
            float beat = sw * amp;
            // Half-open while it runs, and further open the angrier it is. A goose bearing down on
            // something does not run with its wings folded — the flare is most of the silhouette,
            // and it is the part that reads from across the arena where the head does not.
            float flare = _gaitWeight * (20f + Menace * 26f);
            if (wingLeft != null) wingLeft.localRotation = Quaternion.Euler(0f, 0f, beat + flare) * _wingLRest;
            if (wingRight != null) wingRight.localRotation = Quaternion.Euler(0f, 0f, -(beat + flare)) * _wingRRest;

            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!_paramsChecked)
            {
                _paramsChecked = true;
                foreach (var p in animator.parameters)
                    if (p.nameHash == HashSpeed) { _hasSpeedParam = true; break; }
            }
            if (_hasSpeedParam)
                animator.SetFloat(HashSpeed, new Vector3(_vel.x, 0f, _vel.z).magnitude);
        }
    }
}
