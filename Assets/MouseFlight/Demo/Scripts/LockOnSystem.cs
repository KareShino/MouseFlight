using UnityEngine;
using System.Collections.Generic;

namespace MFlight.Demo
{
    /// <summary>
    /// 自機の前方にいる「ロック対象になりそうな Plane」を自動選択する。
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class LockOnSystem : MonoBehaviour
    {
        [Header("Lock-On Settings")]
        [Tooltip("ロックする最大距離")]
        public float maxLockDistance = 1500f;

        [Tooltip("ロック可能な視野角（度）")]
        [Range(0f, 90f)] public float maxLockAngle = 30f;

        [Tooltip("ロックの更新間隔（秒）")]
        public float updateInterval = 0.1f;

        [Tooltip("自分と同じ陣営などを除外したい場合の LayerMask")]
        public LayerMask targetLayerMask = ~0; // デフォルトは全部

        /// <summary>現在ロック中のターゲット。</summary>
        public Plane CurrentTarget { get; private set; }

        private Plane _selfPlane;
        private float _timer;

        private void Awake()
        {
            _selfPlane = GetComponent<Plane>();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = updateInterval;
                UpdateLockTarget();
            }
        }

        private void UpdateLockTarget()
        {
            Plane best = null;
            float bestScore = 0.0f;

            Vector3 myPos = _selfPlane.transform.position;
            Vector3 myForward = _selfPlane.transform.forward;

            Plane[] planes = FindObjectsOfType<Plane>();
            foreach (var p in planes)
            {
                if (p == _selfPlane) continue;

                // Layer でフィルタしたい場合
                if (((1 << p.gameObject.layer) & targetLayerMask) == 0)
                    continue;

                Vector3 toTarget = p.transform.position - myPos;
                float distance = toTarget.magnitude;
                if (distance > maxLockDistance) continue;

                Vector3 dir = toTarget.normalized;
                float dot = Vector3.Dot(myForward, dir); // 前方 = 1, 真横 = 0, 後ろ = -1
                if (dot <= 0f) continue; // 後ろはロックしない

                float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                if (angle > maxLockAngle) continue;

                // 「一番狙ってそう」：正面に近い＋近距離ほど高スコア
                float angleFactor = Mathf.InverseLerp(maxLockAngle, 0f, angle); // 0〜1, 0:ギリギリ,1:ド真ん中
                float distFactor = Mathf.InverseLerp(maxLockDistance, 0f, distance); // 0:遠い,1:近い

                float score = angleFactor * 0.7f + distFactor * 0.3f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            CurrentTarget = best;
        }
    }
}
