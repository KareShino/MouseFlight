using UnityEngine;
using UnityEngine.InputSystem;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class Plane : MonoBehaviour
    {
        [Header("Debug")]
        [Range(-1f, 1f)] public float pitch;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float roll;
        [Range(0f, 1f)] public float throttle;

        [SerializeField] float speed;

        [Header("Input Actions")]
        public InputActionReference pitchAction;
        public InputActionReference yawAction;
        public InputActionReference rollAction;
        public InputActionReference throttleAction;

        [SerializeField] float throttleSpeed = 0.5f; // スロットルの上がり下がり速度

        [Header("Physics")]
        public float thrust = 100f;
        public Vector3 turnTorque = new Vector3(90f, 25f, 45f);
        public float forceMult = 1000f;
        public float drag = 0.01f;

        [Header("Pseudo Gravity (前後だけ)")]
        [Tooltip("機首上下による疑似的な加減速の強さ")]
        public float pseudoGravityStrength = 50f;

        private Vector2 rollVector;

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
            // 入力読み取り
            pitch = Mathf.Clamp(pitchAction.action.ReadValue<Vector2>().y, -1f, 1f);
            yaw = Mathf.Clamp(yawAction.action.ReadValue<Vector2>().x, -1f, 1f);
            rollVector = rollAction.action.ReadValue<Vector2>();

            // スロットル入力（-1 ～ 1）
            float tInput = throttleAction.action.ReadValue<float>();
            Debug.Log("Throttle Input: " + tInput);

            // ★ここで 0～1 に徐々に近づける
            throttle = Mathf.Clamp01(throttle + tInput * throttleSpeed * Time.deltaTime);
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

            // ③ 回転トルク
            rb.AddRelativeTorque(      
                new Vector3(
                    turnTorque.x * pitch,
                    turnTorque.y * yaw,
                    0f
                ) * forceMult,
                ForceMode.Force
            );

            float rollAngle = Mathf.Atan2(rollVector.y, rollVector.x) * Mathf.Rad2Deg - 90f;

            // 入力がないならロール角の更新はしない
            if (rollVector.sqrMagnitude > 0.0001f)
            {
                Vector3 e = transform.rotation.eulerAngles;

                // 現在のZ角
                float currentZ = e.z;

                // 徐々にrollAngleへ向ける (rollFollowSpeed は調整用係数)
                float rollFollowSpeed = 5f; // お好みで
                float newZ = Mathf.LerpAngle(currentZ, rollAngle, Time.deltaTime * rollFollowSpeed);

                e.z = newZ;
                transform.rotation = Quaternion.Euler(e);
            }




            // ③.5 バンクターン（ロール角による自然な旋回）
            float bank = transform.right.y; 
            // 右翼が下がると bank < 0 → 右旋回したい
            // 左翼が下がると bank > 0 → 左旋回したい

            float bankTurnStrength = 0.6f; // 調整用パラメータ（0.3〜1.0が扱いやすい目安）

            rb.AddRelativeTorque(Vector3.up * -bank * turnTorque.y * bankTurnStrength * forceMult, ForceMode.Force);


            // ④ 空気抵抗（速度に比例した減速）
            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);

            speed = rb.linearVelocity.magnitude;
        }
    }
}
