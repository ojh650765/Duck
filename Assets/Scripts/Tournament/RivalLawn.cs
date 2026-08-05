using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Binds a rival plot's own cut mask to its own patch of grass.
    ///
    /// The lawn shader reads <c>_CutMask</c>, <c>_FieldSize</c>, <c>_FieldHalf</c> and
    /// <c>_FieldOrigin</c>. For the player's lawn those come from shader globals, which is why the
    /// original single-field code needed no material at all. A value set on a material instance
    /// beats the global, so each rival lawn gets one instance pointing at its own 256² mask and
    /// its own centre — and the player's field carries on reading the globals, untouched.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class RivalLawn : MonoBehaviour
    {
        public Vector2 plotCentre;
        public float plotSize = 48f;

        static readonly int IdFieldOrigin = Shader.PropertyToID("_FieldOrigin");

        MeshRenderer _renderer;
        Material _instance;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _instance = new Material(_renderer.sharedMaterial) { name = $"{name}_Lawn" };
            _renderer.sharedMaterial = _instance;

            _instance.SetFloat(Field.IdFieldSize, plotSize);
            _instance.SetFloat(Field.IdFieldHalf, plotSize * 0.5f);
            _instance.SetVector(IdFieldOrigin, new Vector4(plotCentre.x, plotCentre.y, 0f, 0f));
            // Until the round starts there is no mask; an unset texture would sample as white and
            // the plot would appear fully mown before anybody had touched it.
            _instance.SetTexture(Field.IdCutMask, Texture2D.blackTexture);

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
            if (_instance == null || mask == null) return;
            _instance.SetTexture(Field.IdCutMask, mask);
        }
    }
}
