using UnityEngine;

namespace MFlight.Demo
{
    [CreateAssetMenu(
        menuName = "MFlight/Skill/Emergency Brake",
        fileName = "Skill_EmergencyBrake"
    )]
    public class SkillEmergencyBrake : PlaneSkill
    {
        [Header("Emergency Brake")]
        public float dragMultiplier = 4.0f;              // 抵抗大
        public float decelerationStrength = 25f;        // 強制減速
        public float throttleSpeedMultiplier = 0.1f;    // スロットル反応遅延

        private float originalDrag;
        private float originalThrottleSpeed;

        public override void Activate(Plane plane)
        {
            if (plane == null) return;

            originalDrag = plane.drag;
            originalThrottleSpeed = plane.throttleSpeed;

            // 抵抗アップ
            plane.drag = originalDrag * dragMultiplier;
            plane.throttleSpeed = originalThrottleSpeed * throttleSpeedMultiplier;

            // 強制減速開始
            plane.StartCoroutine(BrakeRoutine(plane));
        }

        public override void Deactivate(Plane plane)
        {
            if (plane == null) return;

            plane.drag = originalDrag;
            plane.throttleSpeed = originalThrottleSpeed;
        }

        private System.Collections.IEnumerator BrakeRoutine(Plane plane)
        {
            Rigidbody rb = plane.Rigidbody;

            float currentDuration = duration;

            while (currentDuration > 0f)
            {
                currentDuration -= Time.deltaTime;

                // ブレーキ力＝速度の逆方向へ強制的な力を加える
                Vector3 v = rb.linearVelocity;
                rb.AddForce(-v.normalized * decelerationStrength * rb.mass, ForceMode.Force);

                yield return null;
            }
        }
    }
}
