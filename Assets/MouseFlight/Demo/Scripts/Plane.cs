using UnityEngine;
using System.Collections;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class Plane : MonoBehaviour
    {
        [Header("Pilot")]
        [Tooltip("BasePlanePilot を継承したコンポーネントを割り当てる")]
        [SerializeField] private BasePlanePilot pilot;

        [Header("Stats")]
        [Tooltip("飛行機のステータス（ScriptableObject）")]
        [SerializeField] private PlaneStats planeStats;

        [Header("Crash Settings")]
        public bool isCrashed = false;
        public float crashTorque = 500f; // スピンさせる力
        public float crashDrag = 0.2f;   // 空気抵抗増やして落ちやすく

        private Vector3 lastCrashPosition;
        private Quaternion lastCrashRotation;
        [SerializeField] private float respawnDelay = 3f;

        private float defaultLinearDamping;   // もともとの減衰を保存

        [Header("Physics (Runtime View)")]
        public float thrust = 100f;
        public Vector3 turnTorque = new Vector3(90f, 25f, 45f);
        public float forceMult = 1000f;
        public float drag = 0.01f;
        public float throttleSpeed = 0.5f;

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

        public PlaneStats Stats => planeStats;

        private void Reset()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // ScriptableObject から値を反映
            if (planeStats != null)
            {
                ApplyStats(planeStats);
                throttle = Mathf.Clamp01(planeStats.initialThrottle);
            }
            else
            {
                throttle = minThrottle;
            }

            // ★ もともとの linearDamping を記録しておく
            defaultLinearDamping = rb.linearDamping;

            // 初期スロットル
            if (planeStats != null)
            {
                throttle = Mathf.Clamp01(planeStats.initialThrottle);
            }
            else
            {
                throttle = minThrottle;
            }

            if (pilot != null)
            {
                pilot.Initialize(this);
            }
            else
            {
                Debug.LogWarning($"{name}: Plane - BasePlanePilot が設定されていません。");
            }
        }

        /// <summary>
        /// ScriptableObject からパラメータをコピー。
        /// プレハブ複製時や難易度変更時に差し替えてもOK。
        /// </summary>
        public void ApplyStats(PlaneStats stats)
        {
            if (stats == null) return;

            planeStats = stats;
            thrust = stats.thrust;
            turnTorque = stats.turnTorque;
            forceMult = stats.forceMult;
            drag = stats.drag;
            throttleSpeed = stats.throttleSpeed;
            pseudoGravityStrength = stats.pseudoGravityStrength;
            minThrottle = stats.minThrottle;
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

            // minThrottle ～ 1 に徐々に近づける
            throttle = Mathf.Clamp(
                throttle + tInput * throttleSpeed * Time.deltaTime,
                minThrottle,
                1f
            );
        }

        private void FixedUpdate()
        {
            if (rb == null) return;

            if (isCrashed)
            {
                // 操作無効
                pitch = yaw = roll = 0f;
                throttle = 0f;

                // スピン継続させたいならここでトルクを足し続ける
                rb.AddTorque(transform.right * crashTorque, ForceMode.Force);

                // 空気抵抗はクラッシュ用の値のまま（復帰時に戻す）
                rb.linearDamping = crashDrag;

                return;
            }

            // --- 以下、通常飛行処理 ---
            rb.AddRelativeForce(
                Vector3.forward * thrust * throttle * forceMult,
                ForceMode.Force
            );

            float pitchFactor = -transform.forward.y;
            rb.AddRelativeForce(
                Vector3.forward * pitchFactor * pseudoGravityStrength * forceMult,
                ForceMode.Force
            );

            rb.AddRelativeTorque(
                new Vector3(
                    turnTorque.x * pitch,
                    turnTorque.y * yaw,
                    -turnTorque.z * roll
                ) * forceMult,
                ForceMode.Force
            );

            float bank = transform.right.y;
            float bankTurnStrength = 0.6f;
            rb.AddRelativeTorque(
                Vector3.up * -bank * turnTorque.y * bankTurnStrength * forceMult,
                ForceMode.Force
            );

            rb.AddForce(-rb.linearVelocity * drag, ForceMode.Force);

            speed = rb.linearVelocity.magnitude;
        }

        public void Crash()
        {
            if (isCrashed) return;

            isCrashed = true;

            // リスポーン用に現在位置と向きを保存
            lastCrashPosition = transform.position;
            lastCrashRotation = transform.rotation;

            // スロットルと入力はゼロに
            throttle = 0f;
            pitch = yaw = roll = 0f;

            // 推進力は変えなくてもいいが、変えたいならここで 0 にしてもOK
            // thrust = 0f;

            // 派手なスピン＆吹っ飛び
            Vector3 spin = new Vector3(
                0f,
                0f,
                Random.Range(1.5f, 2.5f)
            );
            rb.AddTorque(spin * crashTorque, ForceMode.Impulse);
            rb.AddForce(Random.insideUnitSphere * (rb.mass * 20f), ForceMode.Impulse);

            // クラッシュ中の空気抵抗アップ
            rb.linearDamping = crashDrag;

            // 一定時間後にリスポーン
            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            // ★ ここが重要：速度と回転速度をゼロにする
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // ★ 減衰も元に戻す
            rb.linearDamping = defaultLinearDamping;

            // 位置と向きを復元（またはコース上の安全な向きにしてもいい）
            transform.position = lastCrashPosition;
            transform.rotation = lastCrashRotation;

            // thrust / throttle を復帰
            if (planeStats != null)
            {
                ApplyStats(planeStats);
                throttle = Mathf.Clamp01(planeStats.initialThrottle);
            }
            else
            {
                throttle = minThrottle;
            }

            isCrashed = false;
        }

    }
}
