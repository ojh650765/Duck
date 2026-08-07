using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Everything the arena throws in the air, in one place.
    ///
    /// A thin front on <see cref="DefenceImpactFX"/> rather than a second particle system. That class
    /// already solved the hard part — pooled opaque boxes on shared meshes, no allocation, no
    /// transparency, and a clock that freezes with the hit stop so a burst hangs in the air through
    /// the freeze and lets go as the world releases. Rewriting that for four competitors would have
    /// been a way to get all three of those decisions wrong again.
    ///
    /// NOTHING HERE IS DRAWN ON THE GROUND, and that is a deliberate reversal.
    ///
    /// The first version leant on ground decals — skid stripes down the line a goose left on, a wedge
    /// under a bird about to strike, a shock ring at the point of a perfect parry. On paper that
    /// solves trajectory readability. On screen it did the opposite: flat opaque shapes lying on the
    /// lawn have no depth, no motion and no direction, so an independent review read the single most
    /// prominent one as "a banana peel or a paint spill" and could not tell which way it pointed.
    /// They also never went away fast enough — a mark meant to say "this just happened" was still
    /// sitting there sixty seconds later saying it, so it stopped meaning anything at all and became
    /// scenery competing with the birds for attention.
    ///
    /// So the impact reads in the AIR — feathers, grit, dust — where it has parallax, a lifetime and a
    /// direction the eye can actually follow, and the job the decals were doing badly is given to the
    /// HUD and the world-space markers instead, which are built to do it.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    public class RallyFX : MonoBehaviour
    {
        public static RallyFX Instance { get; private set; }

        [Header("Wiring")]
        [Tooltip("The pooled debris. Made here if left empty.")]
        public DefenceImpactFX debris;

        [Header("Strength")]
        [Tooltip("Feathers thrown by a knockout, as a multiple of a normal parry.")]
        public float knockoutScale = 2.4f;

        [Header("Audio")]
        [Range(0f, 1f)] public float impactVolume = 0.8f;
        [Range(0f, 1f)] public float honkVolume = 0.5f;
        [Tooltip("Wing beats, as a fraction of honkVolume. Low on purpose and NOT a taste " +
                 "setting: up to nine birds fire this seven times a second each, and AUDIO_SPEC §4 " +
                 "puts the clip at an AudioSource volume of 0.093 against a honk's 0.25. At the 0.5 " +
                 "this used to be, the flock arrived about 9 dB hot and stopped being countable.")]
        [Range(0f, 1f)] public float wingBeatVolume = 0.19f;

        System.Random _rng;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(2024);
            if (debris == null)
            {
                var go = new GameObject("~ Rally debris");
                go.transform.SetParent(transform, false);
                debris = go.AddComponent<DefenceImpactFX>();
            }
            debris.Setup(_rng);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            // Scaled deltaTime, deliberately. A hit stop drops the timescale to two percent, and the
            // debris has to hang there with everything else — see the class note on DefenceImpactFX.
            debris?.Tick(Time.deltaTime);
        }

        public void Clear() => debris?.Clear();

        // ------------------------------------------------------------------ the parry

        /// <summary>
        /// A connection. Everything that fires here fires at once and leaves along the same vector,
        /// which is what makes one impact read as one event rather than as four effects that happened
        /// to coincide.
        /// </summary>
        public void Parry(Vector3 contact, Vector3 launchDir, RallyStrike.Tier tier, Color livery)
        {
            float strength = tier switch
            {
                RallyStrike.Tier.Perfect => 1f,
                RallyStrike.Tier.Good => 0.68f,
                _ => 0.42f
            };

            Vector3 flat = new Vector3(launchDir.x, 0f, launchDir.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            // Feathers down the launch line, lifted a little so they arc rather than skate.
            debris?.Impact(contact, launchDir + Vector3.up * 0.35f, strength);
            // Grit off the deck. Thrown UP and outward from the contact point rather than laid on
            // the floor — the same information, with somewhere to fall.
            debris?.Dirt(contact, strength * 0.9f);

            if (tier != RallyStrike.Tier.Normal)
                Gust(contact, flat, tier == RallyStrike.Tier.Perfect ? 1f : 0.55f);

            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayOne(Pick(a.bonks), impactVolume * (0.55f + strength * 0.45f));
            a.PlayOne(Pick(a.debrisPings), impactVolume * 0.4f * strength);
            if (tier == RallyStrike.Tier.Perfect) a.PlayOne(a.stamp, impactVolume * 0.55f);
            a.PlayAt(Pick(a.gooseHonks) ?? a.heronHigh, honkVolume * (0.6f + strength * 0.4f), contact);
        }

        /// <summary>
        /// What the MACHINE throws on a strike: dirt kicked up from under the back wheels as the
        /// recoil digs it in.
        ///
        /// The tyre marks this used to lay on the ground are gone with the rest of the decals. What
        /// is left is airborne and short-lived, which is the correct lifetime for "that just
        /// happened" — a mark that outlives the moment stops describing it.
        /// </summary>
        public void MowerRecoil(Vector3 position, Vector3 heading, Vector3 right, float strength)
        {
            Vector3 flat = new Vector3(heading.x, 0f, heading.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            Vector3 side = new Vector3(right.x, 0f, right.z).normalized;

            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 wheel = position + side * (s * 0.44f) - flat * 0.5f;
                wheel.y = position.y - 0.2f;
                debris?.Dirt(wheel, 0.4f + strength * 0.6f);
            }
        }

        /// <summary>The elimination. Loud, wide, and over in a second — it has to feel like a result.</summary>
        public void Knockout(Vector3 contact, Vector3 dir, Color livery)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            // Fired as a spray rather than one burst, so the feathers do not all leave along the same
            // vector. A knockout that throws a tidy cone looks like a nozzle.
            for (int i = 0; i < 5; i++)
            {
                Vector3 fan = Quaternion.Euler(0f, (i - 2f) * 38f, 0f) * flat;
                debris?.Impact(contact + Vector3.up * (i * 0.1f),
                               fan + Vector3.up * (0.5f + i * 0.12f),
                               knockoutScale * 0.42f);
            }
            debris?.Dirt(contact, 1f);
            Gust(contact, flat, 1.3f);

            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayOne(Pick(a.bonks), impactVolume);
            a.PlayOne(a.stamp, impactVolume * 0.7f);
            // The undignified noise a goose makes when a lawnmower reaches it: cracked, falling,
            // comic rather than distressing. The heron croak that used to be here was a stand-in
            // borrowed from the judges — the wrong bird making the wrong shape of sound at the one
            // moment the player is meant to feel they have won something.
            a.PlayAt(a.gooseSquawk ?? a.heronLow, honkVolume * 1.1f, contact);
            a.PlayOne(a.laugh, 0.4f);
        }

        /// <summary>The bird got through. Splinters, soil and a groan.</summary>
        public void Crash(Vector3 impact, Vector3 velocity, Color livery)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            debris?.Impact(impact + Vector3.up * 0.3f, flat + Vector3.up * 0.7f, 0.9f);
            debris?.Dirt(impact, 1f);
            Gust(impact, flat, 0.8f);

            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayOne(Pick(a.bonks), impactVolume * 0.9f);
            a.PlayOne(Pick(a.debrisPings), impactVolume * 0.7f);
            a.PlayOne(a.heronLow, honkVolume);
            a.CrowdGroan(0.6f);
        }

        // ------------------------------------------------------------------ the ground

        /// <summary>
        /// A bounce. Dust kicked up where the bird hit, and nothing left behind.
        ///
        /// This used to lay a directional stripe on the lawn. It is airborne now for the reason in
        /// the class note: a puff at the bounce point tells the player where it landed, and the bird
        /// itself — which is moving, lit and has a shadow — tells them where it is going. The stripe
        /// only ever added a third, flatter, longer-lived answer to a question already answered.
        /// </summary>
        public void GroundHit(Vector3 pos, Vector3 velocity, float strength)
        {
            debris?.Dirt(pos, strength * 0.8f);
            AudioDirector.Instance?.PlayOne(Pick(AudioDirector.Instance.suspensionBumps),
                                            impactVolume * 0.35f * strength);
        }

        /// <summary>Dust under a running bird. Cheap, frequent, and what stops it looking like it is on rails.</summary>
        public void Scuff(Vector3 pos, Vector3 velocity, float strength)
        {
            debris?.TrailPuff(pos);
            if (strength > 0.8f) debris?.Dirt(pos, 0.25f);
        }

        /// <summary>A puff behind a goose in flight. The trajectory, drawn while it is still happening.</summary>
        public void Trail(Vector3 pos, Vector3 velocity, float menace)
            => debris?.TrailPuff(pos);

        /// <summary>
        /// The tell, a moment before a bird commits.
        ///
        /// Audio and a puff off the bird itself. The ground wedge that used to be the whole of this is
        /// gone: it stated the rule in the one place the player is not looking — under their own
        /// wheels — and it stayed up long after the moment it described.
        /// </summary>
        public void Anticipate(Vector3 pos, float menace)
        {
            debris?.TrailPuff(pos);
            Hiss(pos, menace);
        }

        /// <summary>A goose has been put in. Announce it, so nobody is surprised by a bird they never heard.</summary>
        public void ServeCall(Vector3 from, int targetSlot)
        {
            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayAt(Pick(a.gooseHonks) ?? a.heronLow, honkVolume * 0.85f, from);
            a.CrowdCheer(0.3f);
        }

        /// <summary>One wing stroke. Called off the animation, so the beat and the sound are the same beat.</summary>
        public void WingBeat(Vector3 pos, float strength)
        {
            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayAt(Pick(a.gooseWingBeats), honkVolume * wingBeatVolume * strength, pos);
        }

        /// <summary>A call from a bird already in play.</summary>
        public void Honk(Vector3 pos, float menace)
        {
            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayAt(Pick(a.gooseHonks) ?? a.heronHigh, honkVolume * (0.45f + menace * 0.45f), pos);
        }

        /// <summary>The bird has seen the machine coming and does not intend to move.</summary>
        public void Hiss(Vector3 pos, float menace)
        {
            var a = AudioDirector.Instance;
            if (a == null) return;
            a.PlayAt(a.gooseHiss ?? a.heronHigh, honkVolume * (0.5f + menace * 0.4f), pos);
        }

        /// <summary>
        /// Push grass and leaves away from an impact — as debris thrown outward, low and fast.
        ///
        /// Emitted as grit rather than as a decal or a shader effect on the lawn: the lawn material is
        /// shared with the mowing scene and the rally must not be able to break it, and a flat ring
        /// painted on the floor was the thing this pass exists to remove.
        /// </summary>
        void Gust(Vector3 centre, Vector3 dir, float strength)
        {
            for (int i = 0; i < 3; i++)
            {
                float spread = (i - 1) * 40f;
                Vector3 d = Quaternion.Euler(0f, spread, 0f) * dir;
                debris?.Dirt(centre + d * (0.8f + i * 0.5f) + Vector3.up * 0.15f, strength * 0.45f);
            }
        }

        AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0) return null;
            return set[_rng.Next(set.Length)];
        }
    }
}
