using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePath のウェイポイント列に沿って飛行機をなぞらせる AI パイロット。
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class RacePathFollowerPilot : BasePlanePilot
    {
        [Header("Path")]
        [Tooltip("なぞりたい RacePath")]
        public RacePath path;

        [Tooltip("いまの位置から、この距離だけ先を狙う（メートル単位）")]
        public float lookAheadDistance = 80f;

        [Header("Steering (pitch / yaw)")]
        public float pitchGain = 2f;
        public float yawInputGain = 0.5f;
        [Tooltip("符号調整用。逆に動くときは -1 にする")]
        public float pitchSign = -1f;
        [Tooltip("符号調整用。逆に動くときは -1 にする")]
        public float yawSign = 1f;

        [Header("Bank control (roll)")]
        [Tooltip("曲がり具合（yawError）から目標バンク角を決める係数")]
        public float bankFromYawGain = 1.0f;

        [Tooltip("目標とする最大バンク角（度）")]
        public float maxBankAngle = 45f;

        [Tooltip("バンク角のズレをどれだけロール入力に変換するか（deg → input）")]
        public float bankControlGain = 0.02f;

        [Tooltip("ロール方向が逆のときは -1 にする")]
        public float rollInputSign = 1f;

        [Tooltip("ヨー入力に応じたロール量（バンクターン用）")]
        public float rollFromYawFactor = 0.8f;

        [Tooltip("出す入力の最大量（0〜1）")]
        [Range(0.1f, 1f)]
        public float maxInput = 0.7f;

        [Tooltip("この角度以下なら入力を 0 にする（デッドゾーン）")]
        [Range(0f, 10f)]
        public float deadZoneAngleDeg = 1.0f;

        [Header("Speed Control")]
        public float desiredSpeed = 200f;
        public float throttleGain = 0.5f;

        private int _lastClosestIndex = 0;

        private float _pitch;
        private float _yaw;
        private float _roll;
        private float _throttleInput;

        public override float Pitch => _pitch;
        public override float Yaw => _yaw;
        public override float Roll => _roll;
        public override float ThrottleInput => _throttleInput;

        public override void Initialize(Plane plane)
        {
            base.Initialize(plane);

            if (path != null)
            {
                // 念のためリビルドしておく
                path.RebuildPath();
            }
        }

        public override void TickPilot()
        {
            if (plane == null || path == null)
            {
                ZeroInput();
                return;
            }

            IReadOnlyList<RacePath.Waypoint> wps = path.Waypoints;
            if (wps == null || wps.Count < 2)
            {
                ZeroInput();
                return;
            }

            Vector3 planePos = plane.transform.position;
            int closestIndex = FindClosestWaypointIndex(planePos, wps);
            _lastClosestIndex = closestIndex;

            int targetIndex = GetTargetIndexByDistance(closestIndex, wps);
            Vector3 targetPos = wps[targetIndex].position;

            // ─────────────────────
            // 進行方向制御（ピッチ／ヨー／ロール）
            // ─────────────────────

            Vector3 toTargetWorld = (targetPos - planePos).normalized;
            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            // yawError > 0 : 右方向にターゲット
            float yawError = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z);   // −π～π
            float pitchError = Mathf.Atan2(toTargetLocal.y, toTargetLocal.z);   // −π～π

            // ========== 1. ピッチ：上下追従だけ ==========
            _pitch = Mathf.Clamp(pitchSign * pitchError * pitchGain, -1f, 1f);


            // ========== 2. ロール：左右はバンクで合わせる ==========

            // yawError が右側なら、右バンクを目指す（bankFromYawGain は調整用）
            float desiredBankDeg = Mathf.Clamp(
                yawError * Mathf.Rad2Deg * bankFromYawGain,
                -maxBankAngle,
                maxBankAngle
            );

            // 現在のバンク角（機体 up が world up からどれだけ傾いてるか）
            float currentBankDeg = Vector3.SignedAngle(
                plane.transform.up,  // 今の「上」
                Vector3.up,          // 世界の「上」
                plane.transform.forward  // 機体の進行方向を軸に測る
            );

            // 目標バンクとの差
            float bankErrorDeg = Mathf.DeltaAngle(currentBankDeg, desiredBankDeg);

            // バンク誤差 → ロール入力（deg → -1～1）
            float rollInput = Mathf.Clamp(bankErrorDeg * bankControlGain, -1f, 1f);

            // rollInputSign が 1 で逆向きだったら -1 にする
            _roll = rollInputSign * rollInput;

            // ─────────────────────
            // 速度制御 → throttleInput（増減指示）
            // ─────────────────────
            float speed = plane.Speed;
            if (desiredSpeed > 0.1f)
            {
                float speedError = desiredSpeed - speed;
                float t = (speedError / desiredSpeed) * throttleGain;
                _throttleInput = Mathf.Clamp(t, -1f, 1f);
            }
            else
            {
                _throttleInput = 0f;
            }
        }

        private void ZeroInput()
        {
            _pitch = _yaw = _roll = _throttleInput = 0f;
        }

        /// <summary>最も近いウェイポイントのインデックスを返す。</summary>
        private int FindClosestWaypointIndex(Vector3 pos, IReadOnlyList<RacePath.Waypoint> wps)
        {
            int count = wps.Count;
            int bestIndex = 0;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                float sqr = (pos - wps[i].position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// いちばん近い地点から、lookAheadDistance だけ先の距離を持つウェイポイントを探す。
        /// </summary>
        private int GetTargetIndexByDistance(int closestIndex, IReadOnlyList<RacePath.Waypoint> wps)
        {
            int count = wps.Count;
            if (count == 0) return 0;

            float currentDist = wps[closestIndex].distance;
            float targetDist = currentDist + Mathf.Max(0f, lookAheadDistance);

            if (path.loop)
            {
                float totalLength = wps[count - 1].distance;
                if (totalLength <= 0.01f) return closestIndex;

                // ループなので距離を一周で丸める
                targetDist = Mathf.Repeat(targetDist, totalLength);

                int idx = closestIndex;
                for (int n = 0; n < count; n++)
                {
                    idx = (closestIndex + n) % count;
                    if (wps[idx].distance >= targetDist)
                        return idx;
                }

                return closestIndex;
            }
            else
            {
                float totalLength = wps[count - 1].distance;
                targetDist = Mathf.Min(targetDist, totalLength);

                for (int i = closestIndex; i < count; i++)
                {
                    if (wps[i].distance >= targetDist)
                        return i;
                }

                return count - 1;
            }
        }
    }
}