using UnityEngine;

namespace MFlight.Demo
{
    [CreateAssetMenu(menuName = "MFlight/Skill/TurnBoost")]
    public class SkillTurnBoost : PlaneSkill
    {
        public float torqueMultiplier = 1.5f;

        private Vector3 originalTorque;

        public override void Activate(Plane plane)
        {
            originalTorque = plane.turnTorque;
            plane.turnTorque *= torqueMultiplier;
        }

        public override void Deactivate(Plane plane)
        {
            plane.turnTorque = originalTorque;
        }
    }
}
