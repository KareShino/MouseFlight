using UnityEngine;

namespace MFlight.Demo
{
    /// <summary>
    /// 自動ロックオン＋ターゲット変更キー用のロックオン管理。
    /// - ターゲットがいない時だけ自動で最適な対象を取得
    /// - 一度ロックしたら、射程外 or 破壊 or ターゲット変更まで維持
    /// </summary>
    [RequireComponent(typeof(Plane))]
    public class LockOnSystem : MonoBehaviour
    {
        [Header("Lock-On Settings")]
        [Tooltip("ロック維持・取得の最大距離")]
        public float maxLockDistance = 1500f;

        [Tooltip("自動取得時の視野角（度）")]
        [Range(0f, 90f)] public float autoLockAngle = 30f;

        [Tooltip("ロック候補を更新する間隔（秒）")]
        public float updateInterval = 0.2f;

        [Tooltip("ロック対象にできるレイヤー")]
        public LayerMask targetLayerMask = ~0;

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
                UpdateLock();
            }
        }

        /// <summary>
        /// 自動ロックオン＆ロック維持の判定。
        /// </summary>
        private void UpdateLock()
        {
            // すでにロックしている場合：射程外になってないかだけ見る
            if (CurrentTarget != null)
            {
                if (!IsValidTarget(CurrentTarget))
                {
                    CurrentTarget = null;
                }
                else
                {
                    float dist = Vector3.Distance(
                        _selfPlane.transform.position,
                        CurrentTarget.transform.position
                    );

                    if (dist > maxLockDistance)
                    {
                        CurrentTarget = null;
                    }
                }
            }

            // ロック対象がいなくなったら、自動で新規取得
            if (CurrentTarget == null)
            {
                CurrentTarget = FindBestTarget(autoLockAngle);
            }
        }

        /// <summary>
        /// 「ターゲット変更キー」から呼ぶ。方向は +1 で次、-1 で前。
        /// </summary>
        public void CycleTarget(int direction = 1)
        {
            if (direction == 0) direction = 1;

            // 一旦「ロック候補」を全部集める（視野角は広めでも可）
            Plane[] candidates = FindCandidates(angleLimitDeg: 70f);

            if (candidates.Length == 0)
            {
                CurrentTarget = null;
                return;
            }

            // 現在のターゲットが候補にいなければ「最適なやつ」を新規取得
            int currentIndex = -1;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == CurrentTarget)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                CurrentTarget = FindBestInList(candidates);
                return;
            }

            // 次 or 前に回す
            int nextIndex = (currentIndex + direction) % candidates.Length;
            if (nextIndex < 0) nextIndex += candidates.Length;

            CurrentTarget = candidates[nextIndex];
        }

        private bool IsValidTarget(Plane p)
        {
            if (p == null) return false;

            // レイヤー判定
            if (((1 << p.gameObject.layer) & targetLayerMask) == 0)
                return false;

            return true;
        }

        /// <summary>
        /// 現在のforwardから angleLimitDeg 以内＆距離制限以内の候補を集める。
        /// </summary>
        private Plane[] FindCandidates(float angleLimitDeg)
        {
            var list = new System.Collections.Generic.List<Plane>();

            Vector3 myPos = _selfPlane.transform.position;
            Vector3 myForward = _selfPlane.transform.forward;

            Plane[] planes = FindObjectsOfType<Plane>();
            foreach (var p in planes)
            {
                if (p == _selfPlane) continue;
                if (!IsValidTarget(p)) continue;

                Vector3 toTarget = p.transform.position - myPos;
                float distance = toTarget.magnitude;
                if (distance > maxLockDistance) continue;

                Vector3 dir = toTarget.normalized;
                float dot = Vector3.Dot(myForward, dir);
                if (dot <= 0f) continue; // 後ろは候補外

                float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                if (angle > angleLimitDeg) continue;

                list.Add(p);
            }

            return list.ToArray();
        }

        /// <summary>
        /// angleLimitDeg 以内の候補から「一番狙ってそうな」Planeを返す。
        /// </summary>
        private Plane FindBestTarget(float angleLimitDeg)
        {
            Plane[] candidates = FindCandidates(angleLimitDeg);
            if (candidates.Length == 0) return null;
            return FindBestInList(candidates);
        }

        /// <summary>
        /// 候補リストからスコア最大のものを返す。
        /// </summary>
        private Plane FindBestInList(Plane[] candidates)
        {
            if (candidates == null || candidates.Length == 0) return null;

            Vector3 myPos = _selfPlane.transform.position;
            Vector3 myForward = _selfPlane.transform.forward;

            Plane best = null;
            float bestScore = 0f;

            foreach (var p in candidates)
            {
                Vector3 toTarget = p.transform.position - myPos;
                float distance = toTarget.magnitude;
                Vector3 dir = toTarget.normalized;
                float dot = Mathf.Clamp(Vector3.Dot(myForward, dir), -1f, 1f);

                float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;

                float angleFactor = Mathf.InverseLerp(70f, 0f, angle);       // 正面ほど高い
                float distFactor = Mathf.InverseLerp(maxLockDistance, 0f, distance); // 近いほど高い

                float score = angleFactor * 0.7f + distFactor * 0.3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }
    }
}
