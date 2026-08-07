using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Publishes the arena's shape and the four liveries to the shaders.
    ///
    /// Split out of <see cref="TurfMask"/> because of what it costs to leave it there. The mask is a
    /// runtime object — it allocates render textures and a quarter-million-cell grid in Awake — so
    /// its constants only exist once play has been pressed. But the ground shader carves the hedge
    /// footprint out of the floor from those same constants, and with them all zero it decides that
    /// no point in the world is playable and paints the entire arena as bare soil.
    ///
    /// Which means the scene the builder saves to disk cannot be looked at. That is not a cosmetic
    /// problem: this project's whole method is bake a scene, open it, judge it, adjust the numbers,
    /// bake again, and an arena that only renders correctly in play mode has left that loop. The
    /// first survey of Bloom Rush came back as a black floor for exactly this reason and nothing was
    /// wrong with it.
    ///
    /// So the constants live here, they are pushed by a component that runs in edit mode as well as
    /// in play mode, and the mask pushes the identical set on top when it wakes and adds the one
    /// thing only it has: the ownership texture itself.
    /// </summary>
    public static class TurfPalette
    {
        public static readonly int IdMask = Shader.PropertyToID("_TurfMask");
        public static readonly int IdSize = Shader.PropertyToID("_TurfSize");
        public static readonly int IdTexel = Shader.PropertyToID("_TurfTexel");
        public static readonly int IdFlipV = Shader.PropertyToID("_TurfFlipV");
        public static readonly int IdLivery = Shader.PropertyToID("_TurfLivery");
        public static readonly int IdAccent = Shader.PropertyToID("_TurfAccent");
        public static readonly int IdGeometry = Shader.PropertyToID("_TurfGeometry");
        public static readonly int IdGaps = Shader.PropertyToID("_TurfGaps");
        public static readonly int IdPulse = Shader.PropertyToID("_TurfPulse");

        static readonly Vector4[] _livery = new Vector4[TurfArena.Count];
        static readonly Vector4[] _accent = new Vector4[TurfArena.Count];

        /// <summary>
        /// The colour a competitor's flowers bloom in: their livery, lifted and shifted.
        ///
        /// Not the livery itself. A bed painted in the same colour as the turf it is growing out of
        /// is a bed nobody can see, and the blooms are most of what stops claimed ground reading as
        /// paint. A sixth of a hue step over, desaturated and brightened, keeps it obviously the same
        /// gardener's while separating it from their turf at every light level.
        /// </summary>
        public static Color Accent(int slot)
        {
            Color.RGBToHSV(TurfArena.Livery(slot), out float h, out float s, out float v);
            // Lifted a little and shifted a little, and NOT much of either.
            //
            // The first pass took saturation down by nearly a third and pushed value to 1.45 plus a
            // fat offset, which for every livery in the game clipped straight to white. The blooms
            // then covered most of a claimed patch in near-white, and DUCK's deep red territory read
            // on screen as salmon while BRAMBLE's purple read as lavender — four pastels that are
            // hard to tell apart and impossible to read as "whose is that" at forty metres.
            //
            // A bloom has to be visible AGAINST its own turf without ceasing to be that gardener's
            // colour. This keeps most of the saturation and lifts value by a sixth.
            return Color.HSVToRGB(Mathf.Repeat(h + 0.04f, 1f),
                                  Mathf.Clamp01(s * 0.88f),
                                  Mathf.Clamp01(v * 1.18f + 0.10f));
        }

        /// <summary>Push everything except the mask texture. Safe to call in edit mode.</summary>
        public static void Push()
        {
            Shader.SetGlobalFloat(IdSize, TurfMask.Size);
            Shader.SetGlobalFloat(IdTexel, TurfMask.Size / TurfMask.MaskRes);
            Shader.SetGlobalFloat(IdFlipV, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);

            for (int i = 0; i < TurfArena.Count; i++)
            {
                Color c = TurfArena.Livery(i);
                // The pattern bearing rides in alpha: each competitor's turf is mown along their own
                // spoke, which is both a free way to tell them apart and a quiet reminder of where
                // each of them started.
                _livery[i] = new Vector4(c.r, c.g, c.b, TurfArena.SpokeAngle(i) * Mathf.Deg2Rad);
                Color a = Accent(i);
                _accent[i] = new Vector4(a.r, a.g, a.b, 1f);
            }
            Shader.SetGlobalVectorArray(IdLivery, _livery);
            Shader.SetGlobalVectorArray(IdAccent, _accent);

            Shader.SetGlobalVector(IdGeometry, new Vector4(
                TurfArena.ArenaRadius, TurfArena.PlazaRadius,
                TurfArena.HedgeInner, TurfArena.HedgeOuter));
            Shader.SetGlobalVector(IdGaps, new Vector4(
                TurfArena.SpokeHalfWidth, TurfArena.ChokeHalfWidth, TurfArena.RampHeight,
                TurfArena.CoreRadius));
        }

        /// <summary>
        /// Everything above, plus an empty mask and no crown.
        ///
        /// The edit-mode form. Binding black explicitly matters: an unbound texture sampler does not
        /// read as zero on every platform, and a mask that happens to read white decodes to owner
        /// seven — which indexes four elements off the end of the livery array and produces whatever
        /// happened to be in the constant buffer, differently on every machine.
        /// </summary>
        public static void PushEmpty()
        {
            Push();
            Shader.SetGlobalTexture(IdMask, Texture2D.blackTexture);
            Shader.SetGlobalVector(IdPulse, new Vector4(0f, -1f, 0f, 0f));
        }
    }

    /// <summary>
    /// Keeps the turf shaders configured while the scene is being looked at rather than played.
    ///
    /// Placed in BloomRush.unity by its builder. In play mode <see cref="TurfMask"/> takes over on
    /// the very next frame and this stops mattering; out of play mode it is the only thing making
    /// the saved scene render as the arena it is.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-60)]
    public class TurfPaletteBinder : MonoBehaviour
    {
        void OnEnable()
        {
            // Only ever the empty form. In play mode the mask's own push follows immediately and
            // replaces the black placeholder with the live texture; pushing the placeholder here
            // first is harmless and covers the frame before the mask has woken.
            TurfPalette.PushEmpty();
        }

        void Update()
        {
            // Edit mode only, and only because shader globals do not survive a domain reload, an
            // assembly recompile or a scene reopen — all of which happen constantly while the arena
            // is being tuned, and each of which would otherwise turn the floor black until somebody
            // worked out that it had.
            if (!Application.isPlaying) TurfPalette.PushEmpty();
        }
    }
}
