using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePath に沿って飛ぶ AI パイロット。
    /// - いちばん近い Waypoint から距離ベースで lookAhead 先をターゲットにする
    /// - ターゲット「位置」に機体の向きを合わせる（シンプル方式）
    /// - 急カーブはロールで横倒し → ピッチ強めで曲がる
    /// - ラインから大きくズレそうなら減速もする
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class RacePathFollowerPilot : BasePlanePilot
    {
        [Header("Path")]
        [Tooltip("なぞりたい RacePath")]
        public RacePath path;

        [Tooltip("いまの位置から、この距離だけ先を狙う（メートル単位）")]
        public float lookAheadDistance = 80f;

        [Header("Waypoint Search")]
        [Tooltip("前フレームのインデックスから後ろに何個までを見るか")]
        [Min(0)] public int searchBackwardCount = 5;

        [Tooltip("前フレームのインデックスから前に何個までを見るか")]
        [Min(1)] public int searchForwardCount = 30;

        [Header("Steering (pitch / yaw)")]
        [Tooltip("ピッチ誤差（rad）→ 入力への倍率")]
        public float pitchGain = 2f;

        [Tooltip("ヨー誤差（rad）→ 入力への倍率（バンクターンだけにしたいなら 0 に）")]
        public float yawInputGain = 0.3f;

        [Tooltip("符号調整用。逆に動くときは -1 にする")]
        public float pitchSign = -1f;

        [Tooltip("符号調整用。逆に動くときは -1 にする")]
        public float yawSign = 1f;

        [Header("Bank control (roll)")]
        [Tooltip("yawError から目標バンク角を決める係数")]
        public float bankFromYawGain = 1.0f;

        [Tooltip("目標とする最大バンク角（度）")]
        public float maxBankAngle = 60f;

        [Tooltip("バンク角のズレ（deg）をどれだけロール入力に変換するか")]
        public float bankControlGain = 0.02f;

        [Tooltip("ロール方向が逆のときは -1 にする")]
        public float rollInputSign = 1f;

        [Tooltip("ヨー誤差から直接ロール入力にも足す係数（バンクターン補助）")]
        public float rollFromYawFactor = 0.8f;

        [Header("Input Limits")]
        [Tooltip("出す入力の最大量（0〜1）")]
        [Range(0.1f, 1f)]
        public float maxInput = 0.7f;

        [Tooltip("この角度以下なら入力を 0 にする（デッドゾーン, deg）")]
        [Range(0f, 10f)]
        public float deadZoneAngleDeg = 1.0f;

        [Header("Sharp Turn Behaviour")]
        [Tooltip("何度以上（ターゲットへの角度）を『急カーブ』とみなすか（deg, yaw 基準）")]
        public float sharpTurnAngleDeg = 20f;

        [Tooltip("急カーブ時にピッチ入力を何倍まで強めるか")]
        public float sharpTurnPitchMultiplier = 2.0f;

        [Header("Line Following (lateral offset)")]
        [Tooltip("ラインから横にズレたときのヨー補正の強さ（m → rad の係数）")]
        public float lateralYawGain = 0.02f;

        [Tooltip("横ズレとして扱う最大距離（それ以上は飽和）")]
        public float maxLateralErrorMeters = 80f;

        [Tooltip("この横ズレ以上で『かなりずれている』とみなして減速を強める")]
        public float bigLateralErrorMeters = 40f;

        [Header("Speed Control")]
        [Tooltip("直線〜ゆるいカーブでの巡航速度（m/s）")]
        public float straightSpeed = 220f;

        [Tooltip("急カーブ or 大きな横ズレ時まで落とす最低速度（m/s）")]
        public float minSpeedOnSharpTurn = 140f;

        [Tooltip("ターゲットへの角度がこの値（deg）以上で最小速度扱いにする")]
        public float maxTurnAngleForMinSpeed = 60f;

        [Tooltip("速度誤差→スロットル入力への係数")]
        public float throttleGain = 0.5f;

        // 内部状態
        private int _lastClosestIndex = 0;
        private bool _hasLastClosest = false;

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
                path.RebuildPath();
            }

            _hasLastClosest = false;
            _lastClosestIndex = 0;
            ZeroInput();
        }

        public override void TickPilot()
        {
            if (plane == null || path == null)
            {
                ZeroInput();
                return;
            }

            var wps = path.Waypoints;
            if (wps == null || wps.Count < 2)
            {
                ZeroInput();
                return;
            }

            Vector3 planePos = plane.transform.position;

            // ─────────────────────
            // 最近傍ウェイポイントの決定（前フレーム近傍だけ探索）
            // ─────────────────────
            int closestIndex;
            if (!_hasLastClosest)
            {
                closestIndex = FindClosestWaypointIndexGlobal(planePos, wps);
                _hasLastClosest = true;
            }
            else
            {
                closestIndex = FindClosestWaypointIndexAround(
                    planePos,
                    wps,
                    _lastClosestIndex,
                    searchBackwardCount,
                    searchForwardCount,
                    path.loop
                );
            }

            _lastClosestIndex = closestIndex;

            // ─────────────────────
            // いまいる区間の線分に射影して「ライン上の最近傍点」と横ズレを求める
            // ─────────────────────
            int nextIndex = GetNextIndex(closestIndex, wps.Count, path.loop);
            Vector3 wpA = wps[closestIndex].position;
            Vector3 wpB = wps[nextIndex].position;

            Vector3 ab = wpB - wpA;
            float abSqrMag = ab.sqrMagnitude;
            float tSeg = 0f;
            if (abSqrMag > 0.0001f)
            {
                tSeg = Mathf.Clamp01(Vector3.Dot(planePos - wpA, ab) / abSqrMag);
            }
            Vector3 closestPointOnLine = wpA + ab * tSeg;

            Vector3 offsetWorld = planePos - closestPointOnLine;
            float lateralOffset = Vector3.Dot(offsetWorld, plane.transform.right); // 右＋左−（m）

            // ─────────────────────
            // 距離ベースの lookAhead ターゲット位置を決める
            // ─────────────────────
            int targetIndex = GetTargetIndexByDistance(closestIndex, wps);
            Vector3 targetPos = wps[targetIndex].position;

            // ターゲット方向（位置ベース）をローカル空間で見る
            Vector3 toTargetWorld = (targetPos - planePos);
            if (toTargetWorld.sqrMagnitude < 1e-4f)
            {
                ZeroInput();
                return;
            }
            toTargetWorld.Normalize();

            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            float yawErrorDir = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z); // 左右
            float pitchErrorDir = Mathf.Atan2(toTargetLocal.y, toTargetLocal.z); // 上下

            // 横ズレからのヨー補正（線に戻すため）
            float lateralClamped = Mathf.Clamp(lateralOffset, -maxLateralErrorMeters, maxLateralErrorMeters);
            float yawFromLateral = -lateralClamped * lateralYawGain; // 右にズレてたら左向き

            float yawError = yawErrorDir + yawFromLateral;
            float pitchError = pitchErrorDir;

            float deadRad = deadZoneAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(yawError) < deadRad) yawError = 0f;
            if (Mathf.Abs(pitchError) < deadRad) pitchError = 0f;

            // ========== 1. ロール（バンクターン） ==========

            // yaw 誤差が大きいほどバンクを深くする
            float desiredBankDeg = Mathf.Clamp(
                yawError * Mathf.Rad2Deg * bankFromYawGain,
                -maxBankAngle,
                maxBankAngle
            );

            float currentBankDeg = Vector3.SignedAngle(
                plane.transform.up,
                Vector3.up,
                plane.transform.forward
            );

            float bankErrorDeg = Mathf.DeltaAngle(currentBankDeg, desiredBankDeg);
            if (Mathf.Abs(bankErrorDeg) < deadZoneAngleDeg)
                bankErrorDeg = 0f;

            float rollFromBank = bankErrorDeg * bankControlGain;
            float rollFromYaw = yawError * Mathf.Rad2Deg * rollFromYawFactor * 0.01f;

            float rollInput = rollFromBank + rollFromYaw;
            rollInput = Mathf.Clamp(rollInput, -maxInput, maxInput);
            _roll = rollInputSign * rollInput;

            // ========== 2. ピッチ（急カーブ時に強める） ==========

            float basePitch = pitchSign * pitchError * pitchGain;

            float yawDegAbs = Mathf.Abs(yawErrorDir * Mathf.Rad2Deg); // ターゲットへの角度ベース
            float tSharp = 0f;
            if (maxTurnAngleForMinSpeed > sharpTurnAngleDeg)
            {
                tSharp = Mathf.InverseLerp(sharpTurnAngleDeg, maxTurnAngleForMinSpeed, yawDegAbs);
                tSharp = Mathf.Clamp01(tSharp);
            }

            float pitchMul = Mathf.Lerp(1f, sharpTurnPitchMultiplier, tSharp);
            _pitch = Mathf.Clamp(basePitch * pitchMul, -maxInput, maxInput);

            // ========== 3. ヨー（方向＋横ズレ補正の合成、控えめ） ==========
            _yaw = Mathf.Clamp(
                yawSign * yawError * yawInputGain,
                -maxInput,
                maxInput
            );

            // ========== 4. 速度制御（ターンのきつさ＋横ズレで減速） ==========

            float speed = plane.Speed;

            // ターンのきつさから減速度合い
            float tTurn = 0f;
            if (maxTurnAngleForMinSpeed > 0.01f)
            {
                tTurn = Mathf.InverseLerp(0f, maxTurnAngleForMinSpeed, yawDegAbs);
                tTurn = Mathf.Clamp01(tTurn);
            }

            // 横ズレからも減速度合い
            float latAbs = Mathf.Abs(lateralOffset);
            float tLat = 0f;
            if (bigLateralErrorMeters > 0.01f)
            {
                tLat = Mathf.InverseLerp(0f, bigLateralErrorMeters, latAbs);
                tLat = Mathf.Clamp01(tLat);
            }

            // きついターン or 大きな横ズレ → 強めに減速
            float tSlow = Mathf.Max(tTurn, tLat);

            float targetSpeed = Mathf.Lerp(straightSpeed, minSpeedOnSharpTurn, tSlow);
            if (targetSpeed < 0.1f) targetSpeed = 0.1f;

            float speedError = targetSpeed - speed;
            float tSpeed = (speedError / targetSpeed) * throttleGain;

            _throttleInput = Mathf.Clamp(tSpeed, -1f, 1f);
        }

        private void ZeroInput()
        {
            _pitch = _yaw = _roll = _throttleInput = 0f;
        }

        private int GetNextIndex(int index, int count, bool loop)
        {
            if (loop)
                return (index + 1) % count;
            else
                return Mathf.Min(index + 1, count - 1);
        }

        /// <summary>全ウェイポイントから最も近いインデックスを返す（初期化用）。</summary>
        private int FindClosestWaypointIndexGlobal(Vector3 pos, IReadOnlyList<RacePath.Waypoint> wps)
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
        /// 前フレームのインデックス近辺だけを見て、最も近いウェイポイントを返す。
        /// これにより「逆走」や「行ったり来たり」を抑える。
        /// </summary>
        private int FindClosestWaypointIndexAround(
            Vector3 pos,
            IReadOnlyList<RacePath.Waypoint> wps,
            int centerIndex,
            int backCount,
            int forwardCount,
            bool loop
        )
        {
            int count = wps.Count;
            if (count == 0) return 0;

            centerIndex = Mathf.Clamp(centerIndex, 0, count - 1);

            int bestIndex = centerIndex;
            float bestSqr = float.MaxValue;

            if (loop)
            {
                int minOffset = -Mathf.Abs(backCount);
                int maxOffset = Mathf.Max(1, forwardCount);

                for (int offset = minOffset; offset <= maxOffset; offset++)
                {
                    int idx = centerIndex + offset;
                    if (idx < 0) idx += count;
                    else if (idx >= count) idx -= count;

                    float sqr = (pos - wps[idx].position).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        bestIndex = idx;
                    }
                }
            }
            else
            {
                int start = Mathf.Max(0, centerIndex - Mathf.Abs(backCount));
                int end = Mathf.Min(count - 1, centerIndex + Mathf.Max(1, forwardCount));

                for (int i = start; i <= end; i++)
                {
                    float sqr = (pos - wps[i].position).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        bestIndex = i;
                    }
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
