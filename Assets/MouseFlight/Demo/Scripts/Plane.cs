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

        [Header("Pitch-based Forward/Back Accel")]
        public float pitchAccel = 30f;      // ピッチ角による前後加速の強さ
        public float pitchDeadZone = 0.05f; // ほぼ水平のときに無視する閾値


        [Header("Debug")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(0f, 1f)] public float throttle;

        private Rigidbody rb;

        // === ここから追加パラメータ ===

        [Header("Tilt Drag (機首上げで後ろに引っ張る)")]
        public float tiltBackForce = 50f;      // 機首上げ時の後ろ向きの力の強さ
        public float tiltDownForce = 20f;      // 機首上げ時の下向きの力の強さ

        [Header("Lift / Stall")]
        public float liftPower = 10f;          // 基本の揚力
        public float stallSpeed = 40f;         // 失速し始める速度[m/s]
        public float stallExtraGravity = 2f;   // 失速時の重力倍率
        public float stallNoseDropTorque = 20f;// 失速時に機首を下げるトルク

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
            // 推力（手動スロットル）
            rb.AddRelativeForce(Vector3.forward * thrust * throttle * forceMult);

            // 操舵トルク
            rb.AddRelativeTorque(
                new Vector3(
                    turnTorque.x * pitch,
                    turnTorque.y * yaw,
                    -turnTorque.z * roll
                ) * forceMult,
                ForceMode.Force
            );

            // ★ 機首の角度による前後加速 ★
            {
                // 上向き：+、下向き：- の値になる
                float nosePitch = Vector3.Dot(transform.forward, Vector3.up);

                // ほぼ水平なら無視
                if (Mathf.Abs(nosePitch) > pitchDeadZone)
                {
                    // nosePitch > 0 (上向き) → 後ろ向き加速
                    // nosePitch < 0 (下向き) → 前向き加速
                    float extraForwardAccel = -nosePitch * pitchAccel;

                    rb.AddRelativeForce(
                        Vector3.forward * extraForwardAccel,
                        ForceMode.Acceleration
                    );
                }
            }

            // 抗力
            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);
        }

    }
}
