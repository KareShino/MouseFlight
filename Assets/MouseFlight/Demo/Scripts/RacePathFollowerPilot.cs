using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePath の「線」をなぞる AI パイロット。
    /// - 最近傍ウェイポイント付近の線分に射影して「線として」捉える
    /// - 向きは Waypoint.rotation （コースの進行方向）に合わせる
    /// - 横ズレが大きいときは、バンク＋ヨー＋減速でライン復帰を優先
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class RacePathFollowerPilot : BasePlanePilot
    {
        [Header("Path")]
        [Tooltip("なぞりたい RacePath")]
        public RacePath path;

        [Tooltip("いまの距離から、この距離だけ先を狙う（メートル単位）")]
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

        [Header("Bank Turn Behaviour")]
        [Tooltip("何度以上を『急カーブ』とみなすか（バンク角の絶対値）")]
        public float sharpTurnBankThreshold = 30f;

        [Tooltip("急カーブ時にピッチ入力を何倍まで強めるか")]
        public float sharpTurnPitchMultiplier = 2.0f;

        [Header("Line Following (lateral offset)")]
        [Tooltip("線から横方向にズレたときのヨー補正の強さ（m → rad 換算係数）")]
        public float lateralYawGain = 0.05f;

        [Tooltip("横ズレとして扱う最大距離（それ以上は飽和）")]
        public float maxLateralErrorMeters = 50f;

        [Tooltip("この横ズレ以上で『かなりずれている』とみなして減速を強める")]
        public float bigLateralErrorMeters = 30f;

        [Header("Speed Control")]
        [Tooltip("直線〜ゆるいカーブでの巡航速度（m/s）")]
        public float straightSpeed = 220f;

        [Tooltip("急カーブ or 大きな横ズレ時まで落とす最低速度（m/s）")]
        public float minSpeedOnSharpTurn = 140f;

        [Tooltip("このバンク角以上で最小速度になる（deg）")]
        public float maxTurnForMinSpeed = 60f;

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

            IReadOnlyList<RacePath.Waypoint> wps = path.Waypoints;
            if (wps == null || wps.Count < 2)
            {
                ZeroInput();
                return;
            }

            Vector3 planePos = plane.transform.position;

            // ─────────────────────
            // 最近傍ウェイポイント付近を探索して「今いる区間」を決める
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

            // 次のウェイポイント（線分の終点）
            int nextIndex = GetNextIndex(closestIndex, wps.Count, path.loop);

            Vector3 wpA = wps[closestIndex].position;
            Vector3 wpB = wps[nextIndex].position;

            // 線分 AB に自機を射影 → 「線としての最近傍点」を取る
            Vector3 ab = wpB - wpA;
            float abSqrMag = ab.sqrMagnitude;
            float tSeg = 0f;
            if (abSqrMag > 0.0001f)
            {
                tSeg = Mathf.Clamp01(Vector3.Dot(planePos - wpA, ab) / abSqrMag);
            }
            Vector3 closestPointOnLine = wpA + ab * tSeg;

            // 横ズレ量（右＋ / 左−）を計算（機体ローカル右方向で測る）
            Vector3 offsetWorld = planePos - closestPointOnLine;
            float lateralOffset = Vector3.Dot(offsetWorld, plane.transform.right); // m

            // ─────────────────────
            // 距離ベースで lookAheadDistance 先のターゲットインデックスを決める
            // ─────────────────────
            int targetIndex = GetTargetIndexByDistance(closestIndex, wps);
            RacePath.Waypoint targetWp = wps[targetIndex];

            // 線としての「進行方向」は Waypoint.rotation の forward を使う
            Vector3 desiredForwardWorld = targetWp.rotation * Vector3.forward;
            desiredForwardWorld.Normalize();

            // 機体ローカルに変換して、向きの誤差を取る
            Vector3 desiredForwardLocal = plane.transform.InverseTransformDirection(desiredForwardWorld);

            float yawErrorDir = Mathf.Atan2(desiredForwardLocal.x, desiredForwardLocal.z);
            float pitchErrorDir = Mathf.Atan2(desiredForwardLocal.y, desiredForwardLocal.z);

            // ─────────────────────
            // 横ズレからのヨー補正（線に戻すための成分）
            // ─────────────────────
            float lateral = Mathf.Clamp(lateralOffset, -maxLateralErrorMeters, maxLateralErrorMeters);

            // 右にズレていれば左へ向ける（符号反転）
            float yawFromLateral = -lateral * lateralYawGain; // meters → radians くらいのイメージ

            // 合成ヨー誤差
            float yawError = yawErrorDir + yawFromLateral;
            float pitchError = pitchErrorDir;

            float deadRad = deadZoneAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(yawError) < deadRad) yawError = 0f;
            if (Mathf.Abs(pitchError) < deadRad) pitchError = 0f;

            // ========== 1. 目標バンク角（バンクターン） ==========

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

            // ========== 2. 急カーブ時はピッチを強める（バンク角に応じて） ==========

            float basePitch = pitchSign * pitchError * pitchGain;

            float bankAbs = Mathf.Abs(desiredBankDeg);
            float tBank = 0f;
            if (maxBankAngle > sharpTurnBankThreshold)
            {
                tBank = Mathf.InverseLerp(sharpTurnBankThreshold, maxBankAngle, bankAbs);
                tBank = Mathf.Clamp01(tBank);
            }

            float pitchMul = Mathf.Lerp(1f, sharpTurnPitchMultiplier, tBank);
            _pitch = Mathf.Clamp(basePitch * pitchMul, -maxInput, maxInput);

            // ========== 3. Yaw は向き＋横ズレ補正の結果を小さめに反映 ==========
            _yaw = Mathf.Clamp(
                yawSign * yawError * yawInputGain,
                -maxInput,
                maxInput
            );

            // ─────────────────────
            // 速度制御（急カーブ or 大きな横ズレなら減速）
            // ─────────────────────
            float speed = plane.Speed;

            // バンクと横ズレの両方から「どれだけ危ない旋回か」を見る
            float tTurn = 0f;
            if (maxTurnForMinSpeed > 0.01f)
            {
                tTurn = Mathf.InverseLerp(0f, maxTurnForMinSpeed, bankAbs);
            }

            float lateralAbs = Mathf.Abs(lateralOffset);
            float tLat = 0f;
            if (bigLateralErrorMeters > 0.01f)
            {
                tLat = Mathf.InverseLerp(0f, bigLateralErrorMeters, lateralAbs);
            }

            // バンクと横ズレのうち「きつい方」を採用
            float tSlow = Mathf.Clamp01(Mathf.Max(tTurn, tLat));

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
            {
                return (index + 1) % count;
            }
            else
            {
                return Mathf.Min(index + 1, count - 1);
            }
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
