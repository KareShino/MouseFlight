using UnityEngine;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class Plane : MonoBehaviour
    {
        [Header("Pilot")]
        [SerializeField]
        private PlanePilot _pilot;   // インスペクタで PlayerPilot / AIPilot を渡す

        [Header("Physics")]
        public float thrust = 100f;
        public Vector3 turnTorque = new Vector3(90f, 25f, 45f);
        public float forceMult = 1000f;
        public float drag = 0.01f;
        public float throttleSpeed = 0.5f; // スロットルの上がり下がり速度

        [Header("Pseudo Gravity (前後だけ)")]
        [Tooltip("機首上下による疑似的な加減速の強さ")]
        public float pseudoGravityStrength = 50f;

        [Header("Debug (Read Only)")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(0f, 1f)] public float throttle;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // 保険：未設定なら同じ GameObject から探す
            if (_pilot == null)
            {
                _pilot = GetComponent<PlanePilot>();
            }

            if (_pilot == null)
            {
                Debug.LogWarning($"{name}: PlanePilot がアサインされていません。入力はゼロのままです。");
            }
        }

        private void Update()
        {
            // パイロットから入力をもらう
            if (_pilot != null)
            {
                _pilot.GetInputs(out pitch, out yaw, out roll, out throttle);
            }
            else
            {
                pitch = yaw = roll = 0f;
                // throttle はそのままでもいいし、徐々に0に寄せても良い
            }
        }

        private void FixedUpdate()
        {
            // ★ここは「今まで使っていた Plane の物理処理」を
            //    pitch / yaw / roll / throttle を使ってそのまま移植するだけでOK

            // 例）めちゃざっくりサンプル：
            // ドラッグ
            _rb.linearVelocity -= _rb.linearVelocity * drag * Time.fixedDeltaTime;

            // 推力
            Vector3 thrustForce = transform.forward * (throttle * thrust * forceMult);
            _rb.AddForce(thrustForce, ForceMode.Force);

            // 疑似重力（ピッチに応じて前後方向の加減速を入れる例）
            float pseudoAccel = pitch * pseudoGravityStrength;
            _rb.AddForce(transform.forward * pseudoAccel, ForceMode.Force);

            // 回転トルク
            Vector3 torqueInput = new Vector3(
                turnTorque.x * pitch,
                turnTorque.y * yaw,
                turnTorque.z * roll
            );

            _rb.AddRelativeTorque(torqueInput * forceMult * Time.fixedDeltaTime, ForceMode.Force);
        }
    }
}
