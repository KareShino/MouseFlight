using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePath のウェイポイント列に沿って飛行機をなぞらせる AI パイロット。
    /// - 最近傍ウェイポイントから lookAheadDistance 先を「目標」とする
    /// - さらに「直近のウェイポイント」と「その次のウェイポイント」の中間も意識して補正
    /// - 曲がりは基本バンクターン（ロールで横倒し → ピッチで曲がる）
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

        [Header("Multi-point Targeting")]
        [Tooltip("直近とその次のウェイポイントも意識してターゲットを補正するか")]
        public bool useNearAndNextWaypoints = true;

        [Tooltip("直近〜次のウェイポイント（セグメント）の影響の強さ (0=無視, 1=ほぼそこを向く)")]
        [Range(0f, 1f)]
        public float nearSegmentWeight = 0.6f;

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

        [Header("Speed Control")]
        [Tooltip("目標速度（m/s）")]
        public float desiredSpeed = 200f;

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
            // 最近傍ウェイポイントの決定（前フレーム近傍だけ検索）
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
            // lookAheadDistance 分だけ先のターゲットインデックス
            // ─────────────────────
            int targetIndex = GetTargetIndexByDistance(closestIndex, wps);

            // ─────────────────────
            // 「直近 & その次」のセグメントと、「先のターゲット」をブレンドして最終ターゲットを決める
            // ─────────────────────
            Vector3 finalTargetPos = wps[targetIndex].position;

            if (useNearAndNextWaypoints)
            {
                int count = wps.Count;

                // 直近の次のウェイポイント（前方）
                int nextIndex;
                if (path.loop)
                    nextIndex = (closestIndex + 1) % count;
                else
                    nextIndex = Mathf.Min(closestIndex + 1, count - 1);

                Vector3 p0 = wps[closestIndex].position;
                Vector3 p1 = wps[nextIndex].position;

                // セグメント上の中間点（「直近とその次の間」を狙う）
                Vector3 segmentMid = Vector3.Lerp(p0, p1, 0.5f);

                // 「先の方（lookAhead）」と「直近セグメント」のブレンド
                // nearSegmentWeight が 1 に近いほど「直近〜次」に強く吸い寄せられる
                finalTargetPos = Vector3.Lerp(
                    finalTargetPos,   // 遠いターゲット
                    segmentMid,       // 直近セグメント
                    nearSegmentWeight
                );
            }

            // ─────────────────────
            // 進行方向制御（ピッチ／ヨー／ロール）
            // ─────────────────────
            Vector3 toTargetWorld = (finalTargetPos - planePos).normalized;
            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            // ローカル空間での方向誤差（ラジアン）
            float yawError = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z);   // 左右
            float pitchError = Mathf.Atan2(toTargetLocal.y, toTargetLocal.z); // 上下

            float deadRad = deadZoneAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(yawError) < deadRad) yawError = 0f;
            if (Mathf.Abs(pitchError) < deadRad) pitchError = 0f;

            // ========== 1. 目標バンク角（ロールで横倒しにする） ==========

            // yawError が大きいほどバンクも大きく
            float desiredBankDeg = Mathf.Clamp(
                yawError * Mathf.Rad2Deg * bankFromYawGain,
                -maxBankAngle,
                maxBankAngle
            );

            // 現在のバンク角（機体 up が world up からどれだけ傾いているか）
            float currentBankDeg = Vector3.SignedAngle(
                plane.transform.up,      // 今の「上」
                Vector3.up,              // 世界の「上」
                plane.transform.forward  // 機体の進行方向を軸に測る
            );

            float bankErrorDeg = Mathf.DeltaAngle(currentBankDeg, desiredBankDeg);
            if (Mathf.Abs(bankErrorDeg) < deadZoneAngleDeg)
                bankErrorDeg = 0f;

            // バンク誤差からのロール入力
            float rollFromBank = bankErrorDeg * bankControlGain;

            // yawError から直接ロールに足す分（お好みで）
            float rollFromYaw = yawError * Mathf.Rad2Deg * rollFromYawFactor * 0.01f;

            float rollInput = rollFromBank + rollFromYaw;
            rollInput = Mathf.Clamp(rollInput, -maxInput, maxInput);
            _roll = rollInputSign * rollInput;

            // ========== 2. 急カーブ時はピッチを強める（バンク角に応じて） ==========

            // ベースのピッチ（ターゲット方向への誤差に基づく）
            float basePitch = pitchSign * pitchError * pitchGain;

            // どれくらい「横倒し」かを見る（目標バンク角の絶対値で判定）
            float bankAbs = Mathf.Abs(desiredBankDeg);

            float t = 0f;
            if (maxBankAngle > sharpTurnBankThreshold)
            {
                t = Mathf.InverseLerp(sharpTurnBankThreshold, maxBankAngle, bankAbs);
                t = Mathf.Clamp01(t);
            }

            // t=0 → 倍率 1, t=1 → 倍率 sharpTurnPitchMultiplier
            float pitchMul = Mathf.Lerp(1f, sharpTurnPitchMultiplier, t);

            _pitch = Mathf.Clamp(basePitch * pitchMul, -maxInput, maxInput);

            // ========== 3. Yaw はあくまで補助（使いたければ small 値） ==========

            _yaw = Mathf.Clamp(
                yawSign * yawError * yawInputGain,
                -maxInput,
                maxInput
            );

            // ─────────────────────
            // 速度制御 → throttleInput（増減指示）
            // ─────────────────────
            float speed = plane.Speed;
            if (desiredSpeed > 0.1f)
            {
                float speedError = desiredSpeed - speed;
                float tSpeed = (speedError / desiredSpeed) * throttleGain;
                _throttleInput = Mathf.Clamp(tSpeed, -1f, 1f);
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
            float ahead = Mathf.Max(0f, lookAheadDistance);

            float totalLength = wps[count - 1].distance;
            if (totalLength <= 0.01f) return closestIndex;

            if (path.loop)
            {
                float targetDist = currentDist + ahead;

                // 1周以内で収まる場合：普通に「今のインデックスから前方」を探す
                if (targetDist <= totalLength)
                {
                    for (int i = closestIndex; i < count; i++)
                    {
                        if (wps[i].distance >= targetDist)
                            return i;
                    }

                    // 念のため
                    return count - 1;
                }
                else
                {
                    // 1周を超えた分だけ先 → スタート付近にターゲットが来る
                    float wrappedTarget = targetDist - totalLength;

                    // スタート（0）から探す
                    for (int i = 0; i < count; i++)
                    {
                        if (wps[i].distance >= wrappedTarget)
                            return i;
                    }

                    // 念のため
                    return 0;
                }
            }
            else
            {
                // 非ループは今のままでもほぼOK（端でクランプするだけ）
                float targetDist = Mathf.Min(currentDist + ahead, totalLength);

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
