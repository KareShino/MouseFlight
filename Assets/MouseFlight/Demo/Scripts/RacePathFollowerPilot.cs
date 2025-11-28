using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// RacePath のウェイポイント列に沿って飛行機をなぞらせる AI パイロット。
    /// 数値パラメータは ScriptableObject (RacePathFollowerPilotSettings) で管理。
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class RacePathFollowerPilot : BasePlanePilot
    {
        [Header("Path")]
        [Tooltip("なぞりたい RacePath")]
        public RacePath path;

        [Header("AI Settings")]
        [Tooltip("この AI 機体のステータス（難易度などを含む）")]
        public RacePathFollowerPilotSettings settings;

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
            if (plane == null || path == null || settings == null)
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

            // ───────────── 最近傍ウェイポイント ─────────────
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
                    settings.searchBackwardCount,
                    settings.searchForwardCount,
                    path.loop
                );
            }

            _lastClosestIndex = closestIndex;

            // ───────────── lookAheadDistance 先のターゲット ─────────────
            int targetIndex = GetTargetIndexByDistance(
                closestIndex,
                wps,
                settings.lookAheadDistance
            );

            // ───────────── 「直近セグメント」とブレンドしたターゲット ─────────────
            Vector3 finalTargetPos = wps[targetIndex].position;

            if (settings.useNearAndNextWaypoints)
            {
                int count = wps.Count;
                int nextIndex;
                if (path.loop)
                    nextIndex = (closestIndex + 1) % count;
                else
                    nextIndex = Mathf.Min(closestIndex + 1, count - 1);

                Vector3 p0 = wps[closestIndex].position;
                Vector3 p1 = wps[nextIndex].position;
                Vector3 segmentMid = Vector3.Lerp(p0, p1, 0.5f);

                finalTargetPos = Vector3.Lerp(
                    finalTargetPos,
                    segmentMid,
                    settings.nearSegmentWeight
                );
            }

            // ───────────── 進行方向制御（ピッチ／ヨー／ロール） ─────────────
            Vector3 toTargetWorld = (finalTargetPos - planePos).normalized;
            Vector3 toTargetLocal = plane.transform.InverseTransformDirection(toTargetWorld);

            float yawError = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z);   // 左右
            float pitchError = Mathf.Atan2(toTargetLocal.y, toTargetLocal.z);   // 上下

            float deadRad = settings.deadZoneAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(yawError) < deadRad) yawError = 0f;
            if (Mathf.Abs(pitchError) < deadRad) pitchError = 0f;

            // 1. 目標バンク角
            float desiredBankDeg = Mathf.Clamp(
                yawError * Mathf.Rad2Deg * settings.bankFromYawGain,
                -settings.maxBankAngle,
                settings.maxBankAngle
            );

            float currentBankDeg = Vector3.SignedAngle(
                plane.transform.up,
                Vector3.up,
                plane.transform.forward
            );

            float bankErrorDeg = Mathf.DeltaAngle(currentBankDeg, desiredBankDeg);
            if (Mathf.Abs(bankErrorDeg) < settings.deadZoneAngleDeg)
                bankErrorDeg = 0f;

            float rollFromBank = bankErrorDeg * settings.bankControlGain;
            float rollFromYaw = yawError * Mathf.Rad2Deg * settings.rollFromYawFactor * 0.01f;

            float rollInput = rollFromBank + rollFromYaw;
            rollInput = Mathf.Clamp(rollInput, -settings.maxInput, settings.maxInput);
            _roll = settings.rollInputSign * rollInput;

            // 2. バンク角に応じてピッチ強化
            float basePitch = settings.pitchSign * pitchError * settings.pitchGain;

            float bankAbs = Mathf.Abs(desiredBankDeg);
            float t = 0f;
            if (settings.maxBankAngle > settings.sharpTurnBankThreshold)
            {
                t = Mathf.InverseLerp(
                    settings.sharpTurnBankThreshold,
                    settings.maxBankAngle,
                    bankAbs
                );
                t = Mathf.Clamp01(t);
            }

            float pitchMul = Mathf.Lerp(1f, settings.sharpTurnPitchMultiplier, t);
            _pitch = Mathf.Clamp(basePitch * pitchMul, -settings.maxInput, settings.maxInput);

            // 3. ヨーは補助
            _yaw = Mathf.Clamp(
                settings.yawSign * yawError * settings.yawInputGain,
                -settings.maxInput,
                settings.maxInput
            );

            // ───────────── 速度制御 → throttleInput ─────────────
            float speed = plane.Speed;
            if (settings.desiredSpeed > 0.1f)
            {
                float speedError = settings.desiredSpeed - speed;
                float tSpeed = (speedError / settings.desiredSpeed) * settings.throttleGain;
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

        private int GetTargetIndexByDistance(
            int closestIndex,
            IReadOnlyList<RacePath.Waypoint> wps,
            float lookAheadDistance
        )
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

                if (targetDist <= totalLength)
                {
                    for (int i = closestIndex; i < count; i++)
                    {
                        if (wps[i].distance >= targetDist)
                            return i;
                    }
                    return count - 1;
                }
                else
                {
                    float wrappedTarget = targetDist - totalLength;

                    for (int i = 0; i < count; i++)
                    {
                        if (wps[i].distance >= wrappedTarget)
                            return i;
                    }
                    return 0;
                }
            }
            else
            {
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
