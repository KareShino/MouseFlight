using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePathに沿って飛ぶAIパイロット（改良版）。
    /// - Pure Pursuitベースの経路追従を拡張し、速度と曲率に応じた動的lookAhead距離を採用
    /// - ターゲットへの角度誤差＋横ズレに基づき、Yaw/Pitchをデッドゾーン付きPI制御で滑らかに調整
    /// - 急カーブ時や大きなズレ発生時には減速し、ライン復帰後には再加速する速度制御を組み込み
    /// （Roll/Yaw操作の符号反転にも注意して制御）
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class RacePathFollowerPilot : BasePlanePilot
    {
        [Header("Path")]
        [Tooltip("なぞりたい RacePath")]
        public RacePath path;

        [Header("Dynamic LookAhead (速度・曲率連動)")]
        [Tooltip("最低先読み距離 (速度が低い時や急カーブ時に適用)")]
        public float minLookAheadDistance = 40f;
        [Tooltip("最高先読み距離 (高速時や直線区間で適用)")]
        public float maxLookAheadDistance = 120f;

        [Header("Waypoint Search")]
        [Tooltip("前フレームのインデックスから後ろに何個まで見るか")]
        [Min(0)] public int searchBackwardCount = 5;
        [Tooltip("前フレームのインデックスから前に何個まで見るか")]
        [Min(1)] public int searchForwardCount = 30;

        [Header("Steering (pitch / yaw)")]
        [Tooltip("ピッチ誤差（rad）→ 入力への比例係数")]
        public float pitchGain = 2f;
        [Tooltip("ヨー誤差（rad）→ 入力への比例係数（バンクターンだけにしたい場合は0）")]
        public float yawInputGain = 0.3f;
        [Tooltip("ピッチ入力方向の符号調整（逆挙動の場合-1）")]
        public float pitchSign = -1f;
        [Tooltip("ヨー入力方向の符号調整（逆挙動の場合-1）")]
        public float yawSign = 1f;
        [Tooltip("ヨー誤差の積分項ゲイン（PI制御用）")]
        public float yawIntegralGain = 0.1f;
        [Tooltip("ピッチ誤差の積分項ゲイン（PI制御用）")]
        public float pitchIntegralGain = 0.1f;

        [Header("Bank control (roll)")]
        [Tooltip("yaw誤差から目標バンク角を決める係数")]
        public float bankFromYawGain = 1.0f;
        [Tooltip("目標とする最大バンク角（度）")]
        public float maxBankAngle = 60f;
        [Tooltip("バンク角のズレ（deg）→ ロール入力への比例係数")]
        public float bankControlGain = 0.02f;
        [Tooltip("ロール入力方向の符号調整（逆挙動の場合-1）")]
        public float rollInputSign = 1f;
        [Tooltip("ヨー誤差から直接ロール入力に加算する係数（バンクターン補助）")]
        public float rollFromYawFactor = 0.8f;

        [Header("Input Limits")]
        [Tooltip("操作入力の最大値（0〜1）")]
        [Range(0.1f, 1f)] public float maxInput = 0.7f;
        [Tooltip("この角度以下の誤差は入力ゼロにする（デッドゾーン角度, deg）")]
        [Range(0f, 10f)] public float deadZoneAngleDeg = 1.0f;

        [Header("Sharp Turn Behaviour")]
        [Tooltip("何度以上のyaw誤差を『急カーブ』とみなすか (deg)")]
        public float sharpTurnAngleDeg = 20f;
        [Tooltip("急カーブ時にピッチ入力を最大何倍まで強めるか")]
        public float sharpTurnPitchMultiplier = 2.0f;

        [Header("Line Following (lateral offset)")]
        [Tooltip("ラインから横にズレたときのヨー補正の強さ（1mあたりの角度ラジアン）")]
        public float lateralYawGain = 0.02f;
        [Tooltip("横ズレとして扱う最大距離（それ以上は補正角を飽和）")]
        public float maxLateralErrorMeters = 80f;
        [Tooltip("この横ズレ以上で『大幅にずれている』とみなし減速を強化")]
        public float bigLateralErrorMeters = 40f;

        [Header("Speed Control")]
        [Tooltip("直線〜緩いカーブでの巡航速度 (m/s)")]
        public float straightSpeed = 220f;
        [Tooltip("急カーブ/大きなズレ時まで減速する最低速度 (m/s)")]
        public float minSpeedOnSharpTurn = 140f;
        [Tooltip("ターゲットへの角度がこの値 (deg) 以上なら最小速度まで減速する")]
        public float maxTurnAngleForMinSpeed = 60f;
        [Tooltip("速度誤差 → スロットル入力への係数")]
        public float throttleGain = 0.5f;

        // 内部状態（前回の最寄りWaypoint）
        private int _lastClosestIndex = 0;
        private bool _hasLastClosest = false;
        // 内部状態（PI制御用の積分項）
        private float _yawIntegral = 0f;
        private float _pitchIntegral = 0f;

        // 出力すべき操作入力値（BasePlanePilotの抽象プロパティ実装）
        private float _pitch, _yaw, _roll, _throttleInput;
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
            // 状態リセット
            _hasLastClosest = false;
            _lastClosestIndex = 0;
            _yawIntegral = 0f;
            _pitchIntegral = 0f;
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

            // === 1. 最寄りWaypointの検索（逆走防止の範囲内で探索） ===
            int closestIndex;
            if (!_hasLastClosest)
            {
                // 初回のみ全体から最近傍を検索
                closestIndex = FindClosestWaypointIndexGlobal(planePos, wps);
                _hasLastClosest = true;
            }
            else
            {
                // 前フレームの近傍のみから最近傍WPを探索（行き過ぎや逆走を防ぐ）
                closestIndex = FindClosestWaypointIndexAround(
                    planePos, wps,
                    _lastClosestIndex,
                    searchBackwardCount,
                    searchForwardCount,
                    path.loop
                );
            }
            _lastClosestIndex = closestIndex;

            // === 2. 現在の区間上での「ライン上最近傍点」と横ズレ距離を計算 ===
            int nextIndex = GetNextIndex(closestIndex, wps.Count, path.loop);
            Vector3 wpA = wps[closestIndex].position;
            Vector3 wpB = wps[nextIndex].position;
            // 機体位置を現在のセグメントAB上に射影し、ライン上最近点を求める
            Vector3 AB = wpB - wpA;
            float ABlen2 = AB.sqrMagnitude;
            float tSeg = 0f;
            if (ABlen2 > 1e-4f)
            {
                tSeg = Mathf.Clamp01(Vector3.Dot(planePos - wpA, AB) / ABlen2);
            }
            Vector3 closestPointOnLine = wpA + AB * tSeg;
            // 横方向のずれ量（右ずれを+、左ずれを-とする）
            Vector3 offsetWorld = planePos - closestPointOnLine;
            float lateralOffset = Vector3.Dot(offsetWorld, plane.transform.right);

            // === 3. 動的lookAhead距離に基づくターゲットWaypointの決定 ===
            // 現在の累積距離
            float currentDist = wps[closestIndex].distance + Vector3.Distance(wpA, closestPointOnLine);
            // 機体速度を正規化（minSpeed～straightSpeed の範囲0～1）
            float speed = plane.Speed;
            float speedNorm = 0f;
            if (straightSpeed > minSpeedOnSharpTurn)
            {
                speedNorm = Mathf.Clamp01((speed - minSpeedOnSharpTurn) / (straightSpeed - minSpeedOnSharpTurn));
            }
            // yaw方向の角度誤差（deg）を算出（後で曲率による補正に使用）
            //   注: yawErrorDirは機体の進行方向とターゲット方向との水平面上の角度差
            Vector3 toForward = (wpB - planePos).normalized;
            float yawErrorDirDeg = 0f;
            if (toForward.sqrMagnitude > 1e-4f)
            {
                // 水平面での進行方向偏差角
                Vector3 toFwdLocal = plane.transform.InverseTransformDirection(toForward);
                yawErrorDirDeg = Mathf.Atan2(toFwdLocal.x, toFwdLocal.z) * Mathf.Rad2Deg;
                yawErrorDirDeg = Mathf.Abs(yawErrorDirDeg);
            }
            // 曲率に応じたlookAhead短縮率を算出（sharpTurnAngleDeg以上の角度で徐々に短縮）
            float tCurv = 0f;
            if (yawErrorDirDeg > sharpTurnAngleDeg)
            {
                float maxAngle = maxTurnAngleForMinSpeed;
                // 補正対象の最大角度（maxTurnAngleForMinSpeedを流用）
                if (maxAngle < sharpTurnAngleDeg) maxAngle = sharpTurnAngleDeg;
                tCurv = Mathf.Clamp01((yawErrorDirDeg - sharpTurnAngleDeg) / (maxAngle - sharpTurnAngleDeg));
            }
            // 速度による動的lookAhead距離（線形補間）
            float lookAheadDist = Mathf.Lerp(minLookAheadDistance, maxLookAheadDistance, speedNorm);
            // 曲率による短縮を適用
            lookAheadDist = Mathf.Lerp(lookAheadDist, minLookAheadDistance, tCurv);

            // ターゲットとする累積距離
            float targetDist = currentDist + Mathf.Max(0f, lookAheadDist);
            // Waypointリスト内で、累積距離がtargetDist以上となるインデックスを探す
            int targetIndex = closestIndex;
            if (path.loop)
            {
                float totalLength = wps[wps.Count - 1].distance;
                if (totalLength > 0.01f)
                {
                    // 距離がコース全長を超えた場合ループ
                    targetDist = Mathf.Repeat(targetDist, totalLength);
                    for (int n = 0; n < wps.Count; n++)
                    {
                        int idx = (closestIndex + n) % wps.Count;
                        if (wps[idx].distance >= targetDist)
                        {
                            targetIndex = idx;
                            break;
                        }
                    }
                }
            }
            else
            {
                float totalLength = wps[wps.Count - 1].distance;
                targetDist = Mathf.Min(targetDist, totalLength);
                for (int i = closestIndex; i < wps.Count; i++)
                {
                    if (wps[i].distance >= targetDist)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }
            Vector3 targetPos = wps[targetIndex].position;

            // ターゲット方向ベクトル（ワールド座標）
            Vector3 toTargetWorld = targetPos - planePos;
            if (toTargetWorld.sqrMagnitude < 1e-4f)
            {
                // ターゲットがほぼ現在位置と同じ場合、安全のため操作ゼロ
                ZeroInput();
                return;
            }
            toTargetWorld.Normalize();
            // ローカル座標系でのターゲット方向
            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            // 水平(yaw)と鉛直(pitch)方向の角度誤差を計算（rad）
            float yawErrorDir = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z);    // 左(+)右(-)方向のズレ
            float pitchErrorDir = Mathf.Atan2(toTargetLocal.y, toTargetLocal.z);  // 上(+)下(-)方向のズレ

            // 横ズレ量に応じた追加のヨー誤差補正（ラインへ戻るためのヨーヨー操作）
            float lateralClamped = Mathf.Clamp(lateralOffset, -maxLateralErrorMeters, maxLateralErrorMeters);
            float yawFromLateral = -lateralClamped * lateralYawGain;  // 右にズレている場合左向き(+yaw)補正
            // 合成したヨー誤差（方向誤差 + 横ズレ補正）
            float yawError = yawErrorDir + yawFromLateral;
            float pitchError = pitchErrorDir;

            // デッドゾーン処理：誤差が小さい場合はゼロ扱い
            float deadRad = deadZoneAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(yawError) < deadRad) yawError = 0f;
            if (Mathf.Abs(pitchError) < deadRad) pitchError = 0f;

            // === 4. ロール制御（バンクターンで方向調整） ===
            // yaw誤差に比例した目標バンク角を決定
            float desiredBankDeg = Mathf.Clamp(yawError * Mathf.Rad2Deg * bankFromYawGain, -maxBankAngle, maxBankAngle);
            // 現在の機体バンク角（機体上方向とワールド上方向のなす角度）
            float currentBankDeg = Vector3.SignedAngle(
                plane.transform.up,
                Vector3.up,
                plane.transform.forward
            );
            // バンク角の偏差
            float bankErrorDeg = Mathf.DeltaAngle(currentBankDeg, desiredBankDeg);
            if (Mathf.Abs(bankErrorDeg) < deadZoneAngleDeg)
                bankErrorDeg = 0f;
            // バンク角ズレに対するロール入力（比例制御）
            float rollFromBank = bankErrorDeg * bankControlGain;
            // ヨー誤差に応じたロール入力補助（バンク不足時の補完）
            float rollFromYaw = yawError * Mathf.Rad2Deg * rollFromYawFactor * 0.01f;
            // 合成ロール入力
            float rollInput = rollFromBank + rollFromYaw;
            rollInput = Mathf.Clamp(rollInput, -maxInput, maxInput);
            _roll = rollInputSign * rollInput;

            // === 5. ピッチ制御（急カーブ時は増強） ===
            // PI制御: ピッチ比例項 + 積分項
            _pitchIntegral += pitchError * Time.deltaTime;
            float pitchP = pitchError * pitchGain;
            float pitchI = _pitchIntegral * pitchIntegralGain;
            // 基本のピッチ入力値
            float basePitchInput = pitchP + pitchI;
            // 急カーブ時はピッチ入力を増幅する係数を適用
            float yawDegAbs = Mathf.Abs(yawErrorDir * Mathf.Rad2Deg);  // ターゲット方向への水平角度（参考値）
            float tSharp = 0f;
            if (maxTurnAngleForMinSpeed > sharpTurnAngleDeg)
            {
                tSharp = Mathf.InverseLerp(sharpTurnAngleDeg, maxTurnAngleForMinSpeed, yawDegAbs);
                tSharp = Mathf.Clamp01(tSharp);
            }
            float pitchMultiplier = Mathf.Lerp(1f, sharpTurnPitchMultiplier, tSharp);
            // 計算したピッチ入力に符号補正と増幅を適用し、制限内にクランプ
            _pitch = Mathf.Clamp(pitchSign * basePitchInput * pitchMultiplier, -maxInput, maxInput);

            // === 6. ヨー制御（方向誤差＋横ズレ補正の合成） ===
            // PI制御: ヨー比例項 + 積分項
            _yawIntegral += yawError * Time.deltaTime;
            float yawP = yawError * yawInputGain;
            float yawI = _yawIntegral * yawIntegralGain;
            // 計算したヨー入力に符号補正を適用し、制限内にクランプ
            float yawControl = yawP + yawI;
            _yaw = Mathf.Clamp(yawSign * yawControl, -maxInput, maxInput);

            // === 7. 速度制御（急ターンや大きなズレでは減速） ===
            // ターンの鋭さに応じた減速係数tTurnを算出（0〜1）
            float tTurn = 0f;
            float yawDegAbsDir = Mathf.Abs(yawErrorDir * Mathf.Rad2Deg);
            if (maxTurnAngleForMinSpeed > 0.01f)
            {
                tTurn = Mathf.InverseLerp(0f, maxTurnAngleForMinSpeed, yawDegAbsDir);
                tTurn = Mathf.Clamp01(tTurn);
            }
            // 横ズレ量に応じた減速係数tLatを算出（0〜1）
            float latAbs = Mathf.Abs(lateralOffset);
            float tLat = 0f;
            if (bigLateralErrorMeters > 0.01f)
            {
                tLat = Mathf.InverseLerp(0f, bigLateralErrorMeters, latAbs);
                tLat = Mathf.Clamp01(tLat);
            }
            // 急カーブまたは大きな横ズレほど強く減速する
            float tSlow = Mathf.Max(tTurn, tLat);
            float targetSpeed = Mathf.Lerp(straightSpeed, minSpeedOnSharpTurn, tSlow);
            if (targetSpeed < 0.1f) targetSpeed = 0.1f;
            // 現在速度との差からスロットル入力を決定（比例制御）
            float speedError = targetSpeed - speed;
            float throttleCtrl = (speedError / targetSpeed) * throttleGain;
            _throttleInput = Mathf.Clamp(throttleCtrl, -1f, 1f);
        }

        /// <summary>機体操作入力をすべてゼロにリセット</summary>
        private void ZeroInput()
        {
            _pitch = _yaw = _roll = _throttleInput = 0f;
        }

        /// <summary>次のウェイポイントのインデックスを取得（ループ対応）</summary>
        private int GetNextIndex(int index, int count, bool loop)
        {
            if (loop) return (index + 1) % count;
            else return Mathf.Min(index + 1, count - 1);
        }

        /// <summary>全ウェイポイントから最も近いインデックスを返す（初期化用）</summary>
        private int FindClosestWaypointIndexGlobal(Vector3 pos, IReadOnlyList<RacePath.Waypoint> wps)
        {
            int count = wps.Count;
            int bestIndex = 0;
            float bestSqrDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float sqr = (pos - wps[i].position).sqrMagnitude;
                if (sqr < bestSqrDist)
                {
                    bestSqrDist = sqr;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// 前フレーム近傍のみから最も近いWPを探す（逆走や振り返りを抑制）
        /// </summary>
        private int FindClosestWaypointIndexAround(
            Vector3 pos, IReadOnlyList<RacePath.Waypoint> wps,
            int centerIndex, int backCount, int forwardCount, bool loop)
        {
            int count = wps.Count;
            if (count == 0) return 0;
            centerIndex = Mathf.Clamp(centerIndex, 0, count - 1);

            int bestIndex = centerIndex;
            float bestSqrDist = (pos - wps[bestIndex].position).sqrMagnitude;
            // 近傍のインデックス範囲を決定
            int startIndex = centerIndex;
            int endIndex = centerIndex;
            if (loop)
            {
                // ループコースの場合は前後にwrap-aroundして探索
                for (int i = 1; i <= backCount; i++)
                {
                    int idx = (centerIndex - i + count) % count;
                    float sqr = (pos - wps[idx].position).sqrMagnitude;
                    if (sqr < bestSqrDist)
                    {
                        bestSqrDist = sqr;
                        bestIndex = idx;
                    }
                }
                for (int i = 1; i <= forwardCount; i++)
                {
                    int idx = (centerIndex + i) % count;
                    float sqr = (pos - wps[idx].position).sqrMagnitude;
                    if (sqr < bestSqrDist)
                    {
                        bestSqrDist = sqr;
                        bestIndex = idx;
                    }
                }
            }
            else
            {
                // 非ループの場合は範囲内のみ探索
                startIndex = Mathf.Max(0, centerIndex - backCount);
                endIndex = Mathf.Min(count - 1, centerIndex + forwardCount);
                for (int idx = startIndex; idx <= endIndex; idx++)
                {
                    float sqr = (pos - wps[idx].position).sqrMagnitude;
                    if (sqr < bestSqrDist)
                    {
                        bestSqrDist = sqr;
                        bestIndex = idx;
                    }
                }
            }
            return bestIndex;
        }
    }
}
