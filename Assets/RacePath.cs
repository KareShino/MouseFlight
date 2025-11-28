using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ControlPoints（チェックポイント）を Catmull-Rom スプラインでつないで、
/// 滑らかな Waypoints（位置＋距離＋向き）を生成するコンポーネント。
/// 必要なら LineRenderer で可視化する。
/// </summary>
[ExecuteAlways]
public class RacePath : MonoBehaviour
{
    [Header("Control Points (通過順のチェックポイント)")]
    [SerializeField]
    private List<Transform> controlPoints = new List<Transform>();

    [Header("Path Settings")]
    [Min(2)]
    public int subdivisionsPerSegment = 10; // 1区間を何分割するか

    public bool loop = false;               // 周回コースなら true

    [Header("Line Renderer (任意)")]
    [SerializeField]
    private LineRenderer lineRenderer;

    [System.Serializable]
    public struct Waypoint
    {
        /// <summary>ワールド座標</summary>
        public Vector3 position;

        /// <summary>スタートからの累積距離</summary>
        public float distance;

        /// <summary>進行方向を表す向き（rotation * Vector3.forward が進行方向）</summary>
        public Quaternion rotation;
    }

    /// <summary>生成されたウェイポイント列</summary>
    public IReadOnlyList<Waypoint> Waypoints => _waypoints;
    private readonly List<Waypoint> _waypoints = new List<Waypoint>();

    // ───────────────────────────────
    // ライフサイクル
    // ───────────────────────────────

    private void OnValidate()
    {
        RebuildPath();
    }

    private void Start()
    {
        RebuildPath();
    }

    // ───────────────────────────────
    // メイン処理：パス再生成
    // ───────────────────────────────

    /// <summary>
    /// controlPoints から Waypoints を生成し直す。
    /// </summary>
    public void RebuildPath()
    {
        _waypoints.Clear();

        if (controlPoints == null || controlPoints.Count < 2)
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            return;
        }

        int count = controlPoints.Count;

        Vector3 prevPos = Vector3.zero;
        bool hasPrev = false;
        float totalDist = 0f;

        int segmentCount = loop ? count : count - 1;

        // まず「位置＋距離」だけ作る
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = GetControlPointPos(i - 1);
            Vector3 p1 = GetControlPointPos(i);
            Vector3 p2 = GetControlPointPos(i + 1);
            Vector3 p3 = GetControlPointPos(i + 2);

            for (int s = 0; s <= subdivisionsPerSegment; s++)
            {
                // 区間の先頭は前の区間の末尾と同じなのでスキップ
                if (i > 0 && s == 0) continue;

                float t = (float)s / subdivisionsPerSegment;
                Vector3 pos = CatmullRom(p0, p1, p2, p3, t);

                if (hasPrev)
                {
                    totalDist += Vector3.Distance(prevPos, pos);
                }
                else
                {
                    hasPrev = true;
                }

                Waypoint wp = new Waypoint
                {
                    position = pos,
                    distance = totalDist,
                    rotation = Quaternion.identity // とりあえず後で埋める
                };

                _waypoints.Add(wp);
                prevPos = pos;
            }
        }

        // 次に、「前後の点」から進行方向ベクトルを求めて rotation を埋める
        ComputeWaypointRotations();

        // デバッグ用可視化（任意）
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = _waypoints.Count;
            for (int i = 0; i < _waypoints.Count; i++)
            {
                lineRenderer.SetPosition(i, _waypoints[i].position);
            }
        }
    }

    // ───────────────────────────────
    // 向きの計算
    // ───────────────────────────────

    /// <summary>
    /// Waypoints の position 列から進行方向を推定して rotation を埋める。
    /// 各点について「前の点」と「次の点」を見ることで、滑らかな向きを求める。
    /// </summary>
    private void ComputeWaypointRotations()
    {
        int n = _waypoints.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            Vector3 prevPos;
            Vector3 nextPos;

            if (loop)
            {
                int prevIndex = (i - 1 + n) % n;
                int nextIndex = (i + 1) % n;
                prevPos = _waypoints[prevIndex].position;
                nextPos = _waypoints[nextIndex].position;
            }
            else
            {
                int prevIndex = Mathf.Max(i - 1, 0);
                int nextIndex = Mathf.Min(i + 1, n - 1);
                prevPos = _waypoints[prevIndex].position;
                nextPos = _waypoints[nextIndex].position;
            }

            Vector3 dir = nextPos - prevPos;
            if (dir.sqrMagnitude < 1e-6f)
            {
                // ほぼ同じ位置の場合は適当な向き（前のものがあれば流用）
                dir = Vector3.forward;
            }
            dir.Normalize();

            Waypoint wp = _waypoints[i];
            wp.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _waypoints[i] = wp;
        }
    }

    // ───────────────────────────────
    // 補助関数
    // ───────────────────────────────

    private Vector3 GetControlPointPos(int index)
    {
        int count = controlPoints.Count;

        if (loop)
        {
            index = (index % count + count) % count; // 負インデックス対応
        }
        else
        {
            index = Mathf.Clamp(index, 0, count - 1);
        }

        Transform t = controlPoints[index];
        return t != null ? t.position : Vector3.zero;
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        // 標準的な Catmull-Rom
        return 0.5f *
               ((2f * p1) +
               (-p0 + p2) * t +
               (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
               (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
