using UnityEngine;

namespace MFlight.Demo
{
    [CreateAssetMenu(
        menuName = "MFlight/Plane/Plane Stats",
        fileName = "PlaneStats_Default"
    )]
    public class PlaneStats : ScriptableObject
    {
        [Header("Physics")]
        public float thrust = 100f;
        public Vector3 turnTorque = new Vector3(90f, 25f, 45f);
        public float forceMult = 1000f;
        public float drag = 0.01f;

        [Header("Throttle")]
        [Tooltip("スロットルの上がり下がり速度")]
        public float throttleSpeed = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("最低スロットル")]
        public float minThrottle = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("スポーン直後の初期スロットル")]
        public float initialThrottle = 0.4f;

        [Header("Pseudo Gravity (前後だけ)")]
        [Tooltip("機首上下による疑似的な加減速の強さ")]
        public float pseudoGravityStrength = 50f;
    }
}
