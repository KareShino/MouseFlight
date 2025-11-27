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
        public float throttleSpeed = 0.5f; // スロットルの上がり下がり速度

        [Header("Pseudo Gravity (前後だけ)")]
        [Tooltip("機首上下による疑似的な加減速の強さ")]
        public float pseudoGravityStrength = 50f;

        [Header("Throttle")]
        [Range(0f, 1f)]
        public float minThrottle = 0.2f;   // ★ 最低スロットル

        [Header("Debug")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(0f, 1f)] public float throttle;

        [SerializeField] float speed;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            throttle = minThrottle; // ★ 初期値
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
            // 入力読み取り
            pitch = Mathf.Clamp(pitchAction.action.ReadValue<Vector2>().y, -1f, 1f);
            yaw = Mathf.Clamp(yawAction.action.ReadValue<Vector2>().x, -1f, 1f);
            roll = Mathf.Clamp(rollAction.action.ReadValue<Vector2>().x, -1f, 1f);

            // スロットル入力（-1 ～ 1）
            float tInput = throttleAction.action.ReadValue<float>();

            // ★ここで minThrottle ～ 1 に徐々に近づける
            throttle = Mathf.Clamp(
                throttle + tInput * throttleSpeed * Time.deltaTime,
                minThrottle,
                1f
            );

        }


        private void FixedUpdate()
        {
            // ① エンジン推力（スロットル）
            rb.AddRelativeForce(Vector3.forward * thrust * throttle * forceMult, ForceMode.Force);

            // ② 機首上下による疑似重力の前後加速
            // forward.y < 0 … 機首下げ → -forward.y は正 → 前に押す
            // forward.y > 0 … 機首上げ → -forward.y は負 → 後ろに押す（減速）
            float pitchFactor = -transform.forward.y; // -1 〜 1
            rb.AddRelativeForce(
                Vector3.forward * pitchFactor * pseudoGravityStrength * forceMult,
                ForceMode.Force
            );

            // ③ 回転トルク（入力）
            rb.AddRelativeTorque(
                new Vector3(
                    turnTorque.x * pitch,
                    turnTorque.y * yaw,
                    -turnTorque.z * roll
                ) * forceMult,
                ForceMode.Force
            );

            // ③.5 バンクターン（ロール角による自然な曲がり）
            float bank = transform.right.y;
            float bankTurnStrength = 0.6f;
            rb.AddRelativeTorque(Vector3.up * -bank * turnTorque.y * bankTurnStrength * forceMult, ForceMode.Force);


            // ④ 空気抵抗（速度に比例した減速）
            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);

            speed = rb.linearVelocity.magnitude;
        }
    }
}
