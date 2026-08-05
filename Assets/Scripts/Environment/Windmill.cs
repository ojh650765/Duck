using UnityEngine;

namespace DuckMow
{
    /// <summary>Turns the windmill sails. Small, but a moving landmark is what stops a skyline
    /// reading as a painted backdrop.</summary>
    public class Windmill : MonoBehaviour
    {
        public Transform blades;
        public float degreesPerSecond = 22f;
        [Tooltip("How much the speed wanders, as a fraction of the base rate.")]
        [Range(0f, 1f)] public float gustiness = 0.35f;

        float _angle;
        float _seed;
        float _clock;

        void Awake() => _seed = Random.value * 100f;

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (blades == null) return;
            _clock += dt;
            float gust = 1f + (Mathf.PerlinNoise(_clock * 0.12f, _seed) - 0.5f) * 2f * gustiness;
            _angle += degreesPerSecond * gust * dt;
            blades.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }
    }
}
