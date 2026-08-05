using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Binds a rival plot's own cut mask to its own patch of grass.
    ///
    /// The lawn shader reads <c>_CutMask</c>, <c>_FieldSize</c>, <c>_FieldHalf</c> and
    /// <c>_FieldOrigin</c>. For the player's lawn those come from shader globals, which is why the
    /// original single-field code needed no material at all. Each rival lawn overrides them with a
    /// <see cref="MaterialPropertyBlock"/> pointing at its own 256² mask and its own centre, and
    /// the player's field carries on reading the globals, untouched.
    ///
    /// A material instance does NOT work here, whatever the obvious reading of "a value set on a
    /// material beats the global" suggests — those four are declared outside UnityPerMaterial in
    /// GrassCommon.hlsl, so under the SRP Batcher they are globals and per-material writes to them
    /// are dropped. See Awake.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class RivalLawn : MonoBehaviour
    {
        public Vector2 plotCentre;
        public float plotSize = 48f;

        static readonly int IdFieldOrigin = Shader.PropertyToID("_FieldOrigin");

        MeshRenderer _renderer;
        Material _instance;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _instance = new Material(_renderer.sharedMaterial) { name = $"{name}_Lawn" };
            _renderer.sharedMaterial = _instance;

            // The field parameters go through a property block, NOT the material.
            //
            // The class comment used to claim that "a value set on a material instance beats the
            // global". That is only true for properties declared inside UnityPerMaterial, and
            // _FieldSize, _FieldHalf and _FieldOrigin are declared in GrassCommon.hlsl OUTSIDE any
            // CBUFFER. With the SRP Batcher on, everything outside that buffer is a global, so the
            // per-material writes were silently discarded and every rival lawn sampled its own cut
            // mask through the PLAYER's origin and extent.
            //
            // The symptom was not a static offset, because the player's globals change as their
            // own field is set up: a rival's mown lines appeared, slid and vanished again as the
            // camera toured the venue. A property block is bound per draw and applies to plain
            // uniforms as well as batched ones, which is exactly what this needs. It costs these
            // three renderers their SRP batching — three draw calls, against artwork that was
            // previously unreadable.
            _mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(Field.IdFieldSize, plotSize);
            _mpb.SetFloat(Field.IdFieldHalf, plotSize * 0.5f);
            _mpb.SetVector(IdFieldOrigin, new Vector4(plotCentre.x, plotCentre.y, 0f, 0f));
            // Until the round starts there is no mask; an unset texture would sample as white and
            // the plot would appear fully mown before anybody had touched it.
            _mpb.SetTexture(Field.IdCutMask, Texture2D.blackTexture);
            _renderer.SetPropertyBlock(_mpb);

            // A rival plot has no blade layer, and the ground shader's base colours are the colours
            // of the soil between blades — right when a metre of geometry is standing on top of it,
            // far too dark when it is the whole lawn. Every camera that ever sees these plots looks
            // down on them, and looking down at grass you see tips. Biasing the bases toward the tip
            // colours makes a rival lawn read as the same grass as the player's from the air, which
            // matters because the tour asks you to compare the two.
            // The gap between these two is the artwork. Lift the cut side hard and the uncut side
            // only a little: the lawn stops reading as a dark hole from the air, and the mown
            // picture stays the brightest thing on it.
            LiftTowardTips("_UncutBase", "_UncutTip", 0.20f);
            LiftTowardTips("_CutBase", "_CutTip", 0.70f);
            // Slightly stronger mottling as well, so the flat plane still has some life in it.
            _instance.SetFloat("_MottleAmount", Mathf.Min(1f, _instance.GetFloat("_MottleAmount") * 1.35f));
        }

        void LiftTowardTips(string baseProp, string tipProp, float amount)
        {
            if (!_instance.HasProperty(baseProp) || !_instance.HasProperty(tipProp)) return;
            _instance.SetColor(baseProp, Color.Lerp(_instance.GetColor(baseProp),
                                                    _instance.GetColor(tipProp), amount));
        }

        void OnDestroy() { if (_instance != null) Destroy(_instance); }

        public void Bind(RenderTexture mask)
        {
            if (_renderer == null || _mpb == null || mask == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(Field.IdCutMask, mask);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
