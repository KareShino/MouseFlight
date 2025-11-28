using UnityEngine;

namespace MFlight.Demo
{
    public abstract class PlaneSkill : ScriptableObject
    {
        public string skillName;
        public float duration = 3f;

        public abstract void Activate(MFlight.Demo.Plane plane);
        public abstract void Deactivate(MFlight.Demo.Plane plane);
    }
}
