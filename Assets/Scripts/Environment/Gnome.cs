using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// A garden gnome. Stands its ground until a mower hits it, at which point it should leave
    /// the scene with as much dignity as physics allows, and slowly right itself afterwards so
    /// the lawn does not fill up with fallen gnomes over a long session.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Gnome : MonoBehaviour
    {
        public float launchBoost = 3.2f;
        public float spinBoost = 9f;
        public float recoverAfterSeconds = 6f;
        public float recoverSpeed = 1.6f;

        public static event System.Action<Vector3, float> OnKnocked;

        Rigidbody _rb;
        Vector3 _homePosition;
        Quaternion _homeRotation;
        float _knockedAt = -999f;
        bool _knocked;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.maxAngularVelocity = 22f;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            Sleep();
        }

        void Sleep()
        {
            _rb.isKinematic = true;
            _knocked = false;
        }

        public void ResetGnome()
        {
            _rb.isKinematic = true;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _knocked = false;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_knocked) return;
            var mower = c.collider.GetComponentInParent<MowerController>();
            if (mower == null) return;

            float speed = Mathf.Abs(mower.ForwardSpeed);
            if (speed < 1.5f) return;

            _knocked = true;
            _knockedAt = Time.time;
            _rb.isKinematic = false;

            Vector3 away = (transform.position - c.transform.position);
            away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = mower.transform.forward;
            away.Normalize();

            _rb.linearVelocity = away * (speed * 0.65f + launchBoost) + Vector3.up * (2.2f + speed * 0.25f);
            _rb.angularVelocity = Random.onUnitSphere * spinBoost;

            OnKnocked?.Invoke(transform.position, Mathf.Clamp01(speed / 10f));
        }

        void FixedUpdate()
        {
            if (!_knocked) return;
            if (Time.time - _knockedAt < recoverAfterSeconds) return;

            // Creep back to the plinth rather than teleporting; a gnome sliding home unnoticed
            // is funnier than one blinking out of existence.
            transform.position = Vector3.MoveTowards(transform.position, _homePosition, recoverSpeed * Time.fixedDeltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _homeRotation, 90f * Time.fixedDeltaTime);

            if ((transform.position - _homePosition).sqrMagnitude < 0.0025f &&
                Quaternion.Angle(transform.rotation, _homeRotation) < 1.5f)
            {
                ResetGnome();
            }
            else
            {
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, Vector3.zero, 0.2f);
            }
        }
    }
}
