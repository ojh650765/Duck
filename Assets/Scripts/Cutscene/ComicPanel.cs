using System;
using UnityEngine;

namespace DuckMow
{
    /// <summary>How a panel arrives on the page.</summary>
    public enum PanelTransition
    {
        /// <summary>Lands instantly. For beats that should hit rather than arrive.</summary>
        Cut,
        /// <summary>Rises onto the page while the previous panel settles back into its slot.</summary>
        CrossFade,
        /// <summary>A cut with a white punch over it. Reserved for the two shocks.</summary>
        Flash
    }

    /// <summary>
    /// One frame of the opening story, described entirely as data.
    ///
    /// The whole sequence is authored as an array of these by
    /// <c>DuckMow.EditorTools.DuckCutsceneBuilder</c> rather than as a timeline asset. A timeline
    /// would have been the obvious Unity answer and it was the wrong one here: the panel art does
    /// not exist yet, so the thing that had to be reviewable first was the *timing* — how long the
    /// page holds on the price tag versus the prize, whether the shock lands. Numbers in a builder
    /// can be edited and re-run in seconds; a timeline has to be re-authored by hand in the
    /// Inspector, which is exactly the workflow this project has decided it does not want.
    /// </summary>
    [Serializable]
    public class ComicPanel
    {
        [Tooltip("Diagnostic name. Appears in the log when the art for this panel is missing.")]
        public string id = "panel";

        [Tooltip("The illustration. Null is legal — the sequence draws an obvious placeholder card " +
                 "instead so the flow can still be reviewed before the renders land.")]
        public Texture2D art;

        [Tooltip("Seconds the panel is the hero of the page, excluding its own arrival.")]
        public float duration = 4f;

        // Ken Burns is expressed as the sub-rectangle of the texture that is visible, in normalised
        // UV, and driven straight into RawImage.uvRect. Doing it that way rather than by sliding an
        // oversized child inside a mask means there is no mask, no extra draw call, no stencil, and
        // no chance of the image escaping the frame on an aspect ratio nobody tested. A width of 1
        // is the whole picture; 0.55 is a hard push in.
        [Tooltip("Visible sub-rectangle of the art at the start of the panel, in normalised UV.")]
        public Rect moveFrom = new Rect(0f, 0f, 1f, 1f);
        [Tooltip("Visible sub-rectangle at the end. Equal to moveFrom for a still.")]
        public Rect moveTo = new Rect(0f, 0f, 1f, 1f);

        // The narration band is the only text in the sequence.
        //
        // There was a second layer: big gold title-card lettering on a plate under the hero card,
        // "7 DAYS" on panel 2 and "$10,000" on panels 5 and 6, and the pair of prices in one fixed
        // position was how the coincidence at the heart of the story was made without stating it.
        // It was cut on the player's own verdict — "필요없어", not wanted — so the narration now
        // carries the deadline and both figures itself. That is why panel 2's line ends on the
        // number of days and why "$10,000" appears verbatim in two consecutive panels: the echo has
        // moved into this band, and it still depends on the reader seeing the same string twice in
        // the same place, so those two lines are not free to be paraphrased.
        //
        // It is narration and not dialogue — the duck never speaks — and no line describes what the
        // picture already shows. That was the first pass's mistake: a spoken line under every panel
        // read as a subtitle track for a film nobody was watching, the images doing the work and the
        // words restating them. Each line here carries what the picture cannot: what the duck knew,
        // what he could not afford, what he told himself.
        //
        // An array rather than a single string so the long panels can turn a line over mid-hold.
        // ComicSequence dips the band between them, so a line is never seen being replaced.
        [Tooltip("Storybook narration, one line at a time. Empty for a panel that narrates nothing.")]
        public string[] narration = Array.Empty<string>();

        // The lines used to divide the panel's duration evenly between them, and that was right for
        // as long as the band was silent: nothing about a written sentence has a length, so equal
        // shares were the only defensible split. A *spoken* line does have a length, and an even
        // split is wrong the moment one exists — a 5.2 s reading in a 4 s share is cut off mid-word,
        // and a 1.8 s reading in the same share leaves three seconds of a lit band saying nothing.
        //
        // So each line now carries its own hold. The numbers are still authored rather than read off
        // the clip at runtime, for the reason in this class's summary: the panel's duration is what
        // the Ken Burns move, the emphasis ramp and GameDirector's intro watchdog are all measured
        // against, and none of those can be allowed to depend on which audio files happen to have
        // been rendered. DuckCutsceneBuilder measures the imported clips once and writes the holds
        // here, so the timing is exact *and* still a column of numbers somebody can argue with.
        //
        // Empty is the important case and it is the state the project is in until the voice is
        // rendered: an empty array means "split the duration evenly", i.e. exactly what the silent
        // page did.
        [Tooltip("Seconds each narration line holds, including its clearances at the panel's edges. " +
                 "Same length as narration, or empty to split the panel's duration evenly as the " +
                 "silent page did.")]
        public float[] narrationHold = Array.Empty<float>();

        [Tooltip("The spoken line, one per narration entry. Null entries are legal and expected — " +
                 "that line is read and not heard, and the page plays on.")]
        public AudioClip[] narrationVoice = Array.Empty<AudioClip>();

        public PanelTransition transition = PanelTransition.CrossFade;

        [Tooltip("Seconds the panel takes to reach the middle of the page. Ignored for Cut.")]
        public float transitionDuration = 0.55f;

        // "Letterbox emphasis" in a storybook frame is not bars over a photograph — the panel is
        // already a rectangle sitting on a page, so bars across the whole screen would just be a
        // second, competing frame. What emphasis does here is clear the page: the bars close in
        // from top and bottom, the settled panels around the hero dim down, and the beat is left
        // alone in the middle. Same intent, correct idiom.
        [Range(0f, 1f)]
        [Tooltip("0 leaves the page open. 1 closes the bars in and dims the settled panels around " +
                 "the hero, for the close-ups.")]
        public float emphasis;

        [Tooltip("Key into ComicSequence.sfx. Empty for silence.")]
        public string sfxId = "";

        [Tooltip("Colour of the placeholder card drawn when art is null. Taken from the game's own " +
                 "palette so a placeholder run still reads as this game.")]
        public Color placeholderTint = new Color(0.55f, 0.55f, 0.55f);

        [Tooltip("What this panel is meant to show. Only ever drawn on the placeholder card — the " +
                 "point of a placeholder pass is being able to follow the story without the art.")]
        public string placeholderLabel = "";
    }
}
