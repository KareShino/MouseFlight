using UnityEngine;

namespace MFlight.Demo
{
    [CreateAssetMenu(
        menuName = "MFlight/Skill/Speed Boost",
        fileName = "Skill_SpeedBoost"
    )]
    public class SkillSpeedBoost : PlaneSkill
    {
        [Header("Speed Boost Settings")]
        public float thrustMultiplier = 1.5f;
        public float minThrottleBoost = 0.8f;  // ó·: 0.8 Ç‹Ç≈íÍè„Ç∞

        private float originalThrust;
        private float originalMinThrottle;

        public override void Activate(Plane plane)
        {
            if (plane == null) return;

            originalThrust = plane.thrust;
            originalMinThrottle = plane.minThrottle;

            plane.thrust = originalThrust * thrustMultiplier;
            plane.minThrottle = Mathf.Max(originalMinThrottle, minThrottleBoost);
        }

        public override void Deactivate(Plane plane)
        {
            if (plane == null) return;

            plane.thrust = originalThrust;
            plane.minThrottle = originalMinThrottle;
        }
    }
}
