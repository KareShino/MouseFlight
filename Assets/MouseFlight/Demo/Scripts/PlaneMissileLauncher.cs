using UnityEngine;

namespace MFlight.Demo
{
    public class PlaneMissileLauncher : MonoBehaviour
    {
        [Header("Missile")]
        public HomingMissile missilePrefab;
        public Transform muzzle;
        public float fireInterval = 1f;
        public int maxAmmo = 4;

        [SerializeField] private Plane ownerPlane;

        private float _cooldown;
        private int _currentAmmo;

        private void Awake()
        {
            if (ownerPlane == null)
            {
                ownerPlane = GetComponentInParent<Plane>();
            }
            _currentAmmo = maxAmmo;
        }

        private void Update()
        {
            if (_cooldown > 0f)
            {
                _cooldown -= Time.deltaTime;
                if (_cooldown < 0f) _cooldown = 0f;
            }
        }

        public void FireAt(Transform target)
        {
            if (missilePrefab == null) return;
            if (target == null) return;
            if (_cooldown > 0f) return;
            if (_currentAmmo <= 0) return;

            _cooldown = fireInterval;
            _currentAmmo--;

            Transform spawnPoint = muzzle != null ? muzzle : transform;

            var missile = Instantiate(
                missilePrefab,
                spawnPoint.position,
                spawnPoint.rotation
            );

            missile.SetTarget(target);
            missile.SetOwner(ownerPlane);
        }

        public void ReloadAll()
        {
            _currentAmmo = maxAmmo;
        }
    }
}
