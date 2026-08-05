using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The chalk outline on a rival's lawn.
    ///
    /// Until this existed the picture was drawn on the ground of exactly one plot — the player's —
    /// and three neighbours mowed apparently from nothing. That is a fairness problem before it is
    /// an art problem: the round is built on everybody losing the same guide at the same moment,
    /// and the overhead study beat is the shot that has to establish it. A player looking down at
    /// four lawns and seeing chalk on only their own reasonably concludes the rivals are being
    /// given something they are not.
    ///
    /// It deliberately shares the director's chalk MATERIAL rather than instancing one. The guide's
    /// line alpha, dissolve and anchors are animated on that single material every frame, so a
    /// shared material is what makes all four plots fade together on the same schedule — get this
    /// wrong and the rivals keep their outline after the player has lost theirs, which is the exact
    /// impression the whole thing exists to avoid.
    ///
    /// What IS per-plot goes through a <see cref="MaterialPropertyBlock"/>: this plot's centre, its
    /// extent, its shape radius and its own cut mask. A material instance cannot carry these —
    /// <c>_FieldOrigin</c>, <c>_FieldSize</c>, <c>_FieldHalf</c> and <c>_ShapeRadius</c> are
    /// declared outside UnityPerMaterial in GrassCommon.hlsl, so under the SRP Batcher they are
    /// globals and per-material writes to them are dropped. See RivalLawn, which learned this the
    /// hard way.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class RivalChalk : MonoBehaviour
    {
        public Vector2 plotCentre;
        public float plotSize = 48f;
        [Tooltip("World radius that shape space [-1,1] maps onto for this plot.")]
        public float shapeRadius = 19.2f;

        static readonly int IdFieldOrigin = Shader.PropertyToID("_FieldOrigin");
        static readonly int IdShapeRadius = Shader.PropertyToID("_ShapeRadius");

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);

            _mpb.SetFloat(Field.IdFieldSize, plotSize);
            _mpb.SetFloat(Field.IdFieldHalf, plotSize * 0.5f);
            _mpb.SetVector(IdFieldOrigin, new Vector4(plotCentre.x, plotCentre.y, 0f, 0f));
            // The distance field is baked over the player's 64 m field and stores distances scaled
            // by the player's shape radius. Reading it through this plot's own radius is what
            // converts the stored value back into metres HERE, so the chalk line comes out the
            // same width on a 48 m rival lawn as on the player's 64 m one.
            _mpb.SetFloat(IdShapeRadius, shapeRadius);
            // No mask yet. Left unset it would sample white and the shader would scuff the whole
            // outline away as though the plot had already been mown flat.
            _mpb.SetTexture(Field.IdCutMask, Texture2D.blackTexture);

            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Point the scuffing at this plot's own cut mask, once the round has one.</summary>
        public void Bind(RenderTexture mask)
        {
            if (_renderer == null || _mpb == null || mask == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(Field.IdCutMask, mask);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
