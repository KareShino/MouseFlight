using UnityEngine;

namespace MFlight.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    public class HomingMissile : MonoBehaviour
    {
        [Header("Missile Settings")]
        public float speed = 300f;
        [Tooltip("1秒あたりの最大旋回角 (deg/s)")]
        public float turnRateDegPerSec = 90f;
        public float lifeTime = 10f;

        [Header("爆発などはここで処理してもよい")]
        public GameObject explosionPrefab;
        public float damage = 100f;

        private Rigidbody _rb;
        private Transform _target;
        private float _timer;

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            _timer = lifeTime;
            // 初速は前方
            _rb.linearVelocity = transform.forward * speed;
        }

        private void FixedUpdate()
        {
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (_target == null)
            {
                // ターゲットを失ったらまっすぐ飛ぶ
                _rb.linearVelocity = transform.forward * speed;
                return;
            }

            Vector3 toTarget = (_target.position - transform.position).normalized;
            Vector3 currentDir = _rb.linearVelocity.sqrMagnitude > 0.01f
                ? _rb.linearVelocity.normalized
                : transform.forward;

            float maxRadiansDelta = turnRateDegPerSec * Mathf.Deg2Rad * Time.fixedDeltaTime;
            Vector3 newDir = Vector3.RotateTowards(currentDir, toTarget, maxRadiansDelta, 0f);

            _rb.linearVelocity = newDir * speed;
            transform.rotation = Quaternion.LookRotation(newDir, Vector3.up);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // ここでダメージ処理などをする
            if (explosionPrefab != null)
            {
                Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            }

            // ざっくり Plane にダメージを与える例
            var plane = collision.collider.GetComponentInParent<Plane>();
            if (plane != null)
            {
                // HP システムを別で作っていれば、そこを呼ぶ
                // plane.GetComponent<PlaneHealth>()?.ApplyDamage(damage);
            }

            Destroy(gameObject);
        }
    }
}
