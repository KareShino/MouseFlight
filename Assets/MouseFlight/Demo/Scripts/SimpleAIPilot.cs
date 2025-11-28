using UnityEngine;

namespace MFlight.Demo
{
    /// <summary>
    /// ごく簡単な AI パイロット。
    /// target に向かって機首を向けつつ、目標速度を維持しようとする。
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class SimpleAIPilot : BasePlanePilot
    {
        [Header("Target")]
        public Transform target;

        [Header("Steering")]
        public float pitchGain = 2f;
        public float yawGain = 2f;
        [Tooltip("ヨーに応じてどれくらいロールさせるか")]
        public float rollFromYawFactor = 1.0f;

        [Header("Speed Control")]
        public float desiredSpeed = 200f;
        public float throttleGain = 0.5f;

        private float _pitch;
        private float _yaw;
        private float _roll;
        private float _throttleInput;

        public override float Pitch => _pitch;
        public override float Yaw => _yaw;
        public override float Roll => _roll;
        public override float ThrottleInput => _throttleInput;

        public override void TickPilot()
        {
            if (plane == null || target == null)
            {
                _pitch = _yaw = _roll = _throttleInput = 0f;
                return;
            }

            // ターゲット方向（ローカル座標系）
            Vector3 toTargetWorld = (target.position - plane.transform.position).normalized;
            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            // 左右（x）でヨー、上下（y）でピッチ
            _yaw = Mathf.Clamp(toTargetLocal.x * yawGain, -1f, 1f);
            _pitch = Mathf.Clamp(toTargetLocal.y * pitchGain, -1f, 1f);

            // ヨー方向にロールを傾ける（バンクターン用）
            _roll = Mathf.Clamp(-_yaw * rollFromYawFactor, -1f, 1f);

            // スピード制御 → throttle の増減指示を出す
            float currentSpeed = plane.Speed;
            if (desiredSpeed <= 0.1f)
            {
                _throttleInput = 0f;
            }
            else
            {
                float speedError = desiredSpeed - currentSpeed;
                float t = (speedError / desiredSpeed) * throttleGain;
                _throttleInput = Mathf.Clamp(t, -1f, 1f);
            }
        }
    }
}
