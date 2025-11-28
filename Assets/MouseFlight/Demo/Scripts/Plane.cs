using UnityEngine;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class Plane : MonoBehaviour
    {
        [Header("Pilot")]
        [Tooltip("BasePlanePilot を継承したコンポーネントを割り当てる")]
        [SerializeField] private BasePlanePilot pilot;

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
        public float minThrottle = 0.2f;   // 最低スロットル

        [Header("Debug")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(0f, 1f)] public float throttle;

        [SerializeField] private float speed;

        private Rigidbody rb;

        /// <summary>現在の速度（m/s 想定）。</summary>
        public float Speed => speed;

        /// <summary>内部 Rigidbody の参照（AI などで使いたい場合用）。</summary>
        public Rigidbody Rigidbody => rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            throttle = minThrottle; // 初期値

            if (pilot != null)
            {
                pilot.Initialize(this);
            }
            else
            {
                Debug.LogWarning($"{name}: Plane - BasePlanePilot が設定されていません。");
            }
        }

        private void Update()
        {
            if (pilot == null) return;

            // パイロット側で入力を更新
            pilot.TickPilot();

            // 入力を取得してクランプ
            pitch = Mathf.Clamp(pilot.Pitch, -1f, 1f);
            yaw = Mathf.Clamp(pilot.Yaw, -1f, 1f);
            roll = Mathf.Clamp(pilot.Roll, -1f, 1f);

            float tInput = Mathf.Clamp(pilot.ThrottleInput, -1f, 1f);

            // ★ここで minThrottle ～ 1 に徐々に近づける（元のロジック踏襲）
            throttle = Mathf.Clamp(
                throttle + tInput * throttleSpeed * Time.deltaTime,
                minThrottle,
                1f
            );
        }

        private void FixedUpdate()
        {
            if (rb == null) return;

            // ① エンジン推力（スロットル）
            rb.AddRelativeForce(
                Vector3.forward * thrust * throttle * forceMult,
                ForceMode.Force
            );

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
            rb.AddRelativeTorque(
                Vector3.up * -bank * turnTorque.y * bankTurnStrength * forceMult,
                ForceMode.Force
            );

            // ④ 空気抵抗（速度に比例した減速）
            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);

            speed = rb.linearVelocity.magnitude;
        }
    }
}
