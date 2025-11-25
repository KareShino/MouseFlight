using UnityEngine;
using UnityEngine.InputSystem;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class Plane : MonoBehaviour
    {
        [Header("Input Actions")]
        public InputActionReference pitchAction;
        public InputActionReference yawAction;
        public InputActionReference rollAction;
        public InputActionReference throttleAction;

        [Header("Physics")]
        public float thrust = 100f;
        public Vector3 turnTorque = new Vector3(90f, 25f, 45f);
        public float forceMult = 1000f;
        public float drag = 0.01f;

        [Header("Debug")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(-1f, 1f)] public float throttle;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            pitchAction.action.Enable();
            yawAction.action.Enable();
            rollAction.action.Enable();
            throttleAction.action.Enable();
        }

        private void OnDisable()
        {
            pitchAction.action.Disable();
            yawAction.action.Disable();
            rollAction.action.Disable();
            throttleAction.action.Disable();
        }

        private void Update()
        {
            pitch = Mathf.Clamp(pitchAction.action.ReadValue<Vector2>().y, -1f, 1f);
            yaw = Mathf.Clamp(yawAction.action.ReadValue<Vector2>().x, -1f, 1f);
            roll = Mathf.Clamp(rollAction.action.ReadValue<Vector2>().x, -1f, 1f);

            float t = throttleAction.action.ReadValue<Vector2>().y;
            throttle = Mathf.Clamp(throttle + t * Time.deltaTime, 0f, 1f);
        }

        private void FixedUpdate()
        {
            rb.AddRelativeForce(Vector3.forward * thrust * throttle * forceMult);

            rb.AddRelativeTorque(
                new Vector3(
                    turnTorque.x * pitch,
                    turnTorque.y * yaw,
                    -turnTorque.z * roll
                ) * forceMult,
                ForceMode.Force
            );

            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);
        }
    }
}
