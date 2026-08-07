using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// What the player hears and glimpses from the other plots while they are working.
    ///
    /// The brief for this is unusually tight: the venue has to feel occupied without ever pulling
    /// the eye off the outline the player is trying to follow. So everything here is positional and
    /// quiet — a rival's engine is a 3D source that fades with distance and never rises above the
    /// player's own mower, a crowd reaction is a short burst from the direction of that plot's
    /// stand, and a mistake throws a puff of clippings up over the hedge rather than anything that
    /// moves in the player's peripheral vision. Nothing is ever drawn between the player and their
    /// own picture.
    /// </summary>
    public class NeighbourAmbience : MonoBehaviour
    {
        public Tournament tournament;
        public AudioDirector audioDirector;

        [Header("Engines")]
        [Tooltip("Loudest a neighbour's engine can be, relative to the player's.")]
        [Range(0f, 1f)] public float engineVolume = 0.16f;
        public float engineMinDistance = 12f;
        public float engineMaxDistance = 190f;

        [Header("Reactions")]
        [Range(0f, 1f)] public float reactionVolume = 0.30f;
        [Tooltip("Shortest gap between any two neighbour reactions, so the venue never chatters.")]
        public float reactionCooldown = 2.6f;

        [Header("Clippings")]
        public ParticleSystem clippingPuffPrefab;
        [Tooltip("Height above the plot the puff appears — just clearing the hedge line.")]
        public float puffHeight = 2.4f;

        AudioSource[] _engines;
        AudioSource _reaction;
        ParticleSystem[] _puffs;
        float _reactionTimer;

        void Awake()
        {
            if (tournament == null) tournament = Tournament.Instance;
            if (audioDirector == null) audioDirector = FindFirstObjectByType<AudioDirector>();
            if (tournament == null) return;

            int n = tournament.rivals.Length;
            _engines = new AudioSource[n];
            _puffs = new ParticleSystem[n];

            for (int i = 0; i < n; i++)
            {
                var r = tournament.rivals[i];
                if (r == null) continue;

                var go = new GameObject($"NeighbourEngine_{r.displayName}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(r.plotCentre.x, 0.6f, r.plotCentre.y);

                var src = go.AddComponent<AudioSource>();
                src.clip = audioDirector != null ? audioDirector.engineMid : null;
                src.loop = true;
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = engineMinDistance;
                src.maxDistance = engineMaxDistance;
                src.volume = 0f;
                // Detune per contestant so three neighbours are a work site, not one engine tripled.
                src.pitch = 0.82f + i * 0.07f;
                if (src.clip != null) src.Play();
                _engines[i] = src;

                if (clippingPuffPrefab != null)
                {
                    var puff = Instantiate(clippingPuffPrefab, transform);
                    puff.name = $"NeighbourClippings_{r.displayName}";
                    puff.gameObject.SetActive(true);
                    _puffs[i] = puff;
                }
            }

            var rgo = new GameObject("NeighbourReaction");
            rgo.transform.SetParent(transform, false);
            _reaction = rgo.AddComponent<AudioSource>();
            _reaction.spatialBlend = 1f;
            _reaction.rolloffMode = AudioRolloffMode.Linear;
            _reaction.minDistance = 20f;
            _reaction.maxDistance = 240f;
            _reaction.playOnAwake = false;

            tournament.OnRivalEvent += HandleEvent;
        }

        void OnDestroy()
        {
            if (tournament != null) tournament.OnRivalEvent -= HandleEvent;
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _reactionTimer -= dt;
            if (tournament == null || _engines == null) return;

            for (int i = 0; i < _engines.Length; i++)
            {
                var src = _engines[i];
                var r = i < tournament.rivals.Length ? tournament.rivals[i] : null;
                if (src == null || r == null) continue;

                // Follow the mower rather than sitting at the plot centre, so a rival working the
                // near edge of their lawn is audibly closer than one on the far side.
                src.transform.position = new Vector3(r.MowerPosition.x, 0.6f, r.MowerPosition.z);

                // Scaled by the venue gate, which is zero while the opening story page is on
                // screen. A neighbour's engine is still an engine: three of them idling under a
                // storybook about a duck is the same bug as the player's own mower doing it, and
                // AudioDirector cannot fix it from over there because these sources are ours.
                float want = r.Finished ? 0f : engineVolume;
                if (audioDirector != null) want *= audioDirector.WorldAudio;
                src.volume = Mathf.MoveTowards(src.volume, want, dt * 0.8f);
            }
        }

        void HandleEvent(RivalEvent e)
        {
            if (e.who == null) return;

            switch (e.kind)
            {
                case RivalEventKind.Mistake:
                    Puff(e, 1.6f);
                    React(e.who, audioDirector != null ? audioDirector.gasp : null, 0.9f);
                    audioDirector?.CrowdGroan(0.35f);
                    break;

                case RivalEventKind.Boost:
                    Puff(e, 1.0f);
                    React(e.who, audioDirector != null ? audioDirector.cheerSmall : null, 0.7f);
                    audioDirector?.CrowdCheer(0.3f);
                    break;

                case RivalEventKind.CrowdCheer:
                    React(e.who, audioDirector != null ? audioDirector.cheerSmall : null, 0.55f);
                    audioDirector?.CrowdCheer(0.25f);
                    break;

                case RivalEventKind.Finished:
                    React(e.who, audioDirector != null ? audioDirector.applause : null, 1f);
                    audioDirector?.CrowdCheer(0.5f, applaud: true);
                    break;
            }
        }

        /// <summary>
        /// A reaction from the direction of that plot's spectator bank, not from the mower — the
        /// crowd is who reacts, and hearing it come from the stand is what tells the player which
        /// neighbour just did something.
        /// </summary>
        void React(RivalContestant who, AudioClip clip, float scale)
        {
            if (clip == null || _reaction == null || _reactionTimer > 0f) return;
            _reactionTimer = reactionCooldown;

            Vector3 stand = new Vector3(who.plotCentre.x - who.plotSize * 0.5f - 6.5f, 1.4f, who.plotCentre.y);
            _reaction.transform.position = stand;
            _reaction.pitch = Random.Range(0.94f, 1.07f);
            _reaction.PlayOneShot(clip, reactionVolume * scale);
        }

        void Puff(RivalEvent e, float scale)
        {
            if (_puffs == null) return;
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (i >= tournament.rivals.Length || tournament.rivals[i] != e.who) continue;
                var p = _puffs[i];
                if (p == null) return;
                p.transform.position = new Vector3(e.position.x, puffHeight, e.position.z);
                p.Emit(Mathf.RoundToInt(14f * scale));
                return;
            }
        }
    }
}
