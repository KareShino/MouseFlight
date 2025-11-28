using UnityEngine;

namespace MFlight.Demo
{
    [CreateAssetMenu(
        menuName = "MFlight/Plane/AI/RacePathFollower Settings",
        fileName = "RacePathFollowerSettings_Default"
    )]
    public class RacePathFollowerPilotSettings : ScriptableObject
    {
        [Header("Path / LookAhead")]
        [Tooltip("いまの位置から、この距離だけ先を狙う（メートル単位）")]
        public float lookAheadDistance = 80f;

        [Header("Waypoint Search")]
        [Min(0)] public int searchBackwardCount = 5;
        [Min(1)] public int searchForwardCount = 30;

        [Header("Multi-point Targeting")]
        public bool useNearAndNextWaypoints = true;

        [Range(0f, 1f)]
        [Tooltip("直近〜次のウェイポイント（セグメント）の影響の強さ")]
        public float nearSegmentWeight = 0.6f;

        [Header("Steering (pitch / yaw)")]
        [Tooltip("ピッチ誤差（rad）→ 入力への倍率")]
        public float pitchGain = 2f;

        [Tooltip("ヨー誤差（rad）→ 入力への倍率")]
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

        [Tooltip("バンク角のズレ（deg）→ ロール入力への変換係数")]
        public float bankControlGain = 0.02f;

        [Tooltip("ロール方向が逆のときは -1 にする")]
        public float rollInputSign = 1f;

        [Tooltip("ヨー誤差から直接ロール入力にも足す係数")]
        public float rollFromYawFactor = 0.8f;

        [Header("Input Limits")]
        [Range(0.1f, 1f)]
        [Tooltip("出す入力の最大量（0〜1）")]
        public float maxInput = 0.7f;

        [Range(0f, 10f)]
        [Tooltip("この角度以下なら入力を 0 にする（デッドゾーン, deg）")]
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
    }
}
