using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The transition's own sound, held in an asset rather than on a component in a scene.
    ///
    /// It has to live somewhere that outlives a scene load, because the whole point of these clips is
    /// that they are playing ACROSS one — the sweep starts in the scene being left and the downbeat
    /// lands in the scene being entered, and there is a stretch in the middle where neither scene's
    /// AudioDirector exists. A ScriptableObject under Resources is loaded once by
    /// <see cref="Curtain"/> and referenced by an object marked DontDestroyOnLoad, so the clips are
    /// resident for the session and no boundary can drop them.
    ///
    /// Built and filled by Duck/9 · Build transition audio set.
    /// </summary>
    [CreateAssetMenu(menuName = "Duck/Curtain Audio Set", fileName = "CurtainAudio")]
    public class CurtainAudioSet : ScriptableObject
    {
        [Header("Sweeps — played as the curtain closes")]
        [Tooltip("Dry rustle for the foliage wipes.")]
        public AudioClip leafSweep;
        [Tooltip("Heavier cloth pass for the banner wipe.")]
        public AudioClip bannerWhoosh;
        [Tooltip("Rising bed timed to END on the cut.")]
        public AudioClip riser;

        [Header("Downbeats — played on the frame the scene changes")]
        [Tooltip("The soft wooden landing under every transition.")]
        public AudioClip impact;
        [Tooltip("Playful two-note sting for the early, quiet transitions.")]
        public AudioClip stampSmall;
        [Tooltip("Brass flourish for the final stage entrance and the run into judging.")]
        public AudioClip fanfareBig;

        [Header("World")]
        [Tooltip("Crowd rising and settling, layered in as the championship escalates.")]
        public AudioClip crowdSwell;
        [Tooltip("The arena gate. Used when the transition is a drive-through.")]
        public AudioClip gateCreak;

        /// <summary>The sweep that belongs to a curtain style.</summary>
        public AudioClip SweepFor(CurtainStyle style) => style switch
        {
            CurtainStyle.BannerWipe => bannerWhoosh != null ? bannerWhoosh : leafSweep,
            CurtainStyle.Fanfare    => bannerWhoosh != null ? bannerWhoosh : leafSweep,
            _                       => leafSweep
        };

        /// <summary>
        /// The downbeat that belongs to an escalation level. Loud transitions get the brass; quiet
        /// ones get the little flourish, and both land on the impact so the cut always has a floor.
        /// </summary>
        public AudioClip StingFor(float intensity)
        {
            if (intensity >= 0.66f && fanfareBig != null) return fanfareBig;
            return stampSmall != null ? stampSmall : fanfareBig;
        }
    }
}
