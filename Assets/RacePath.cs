using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// チェックポイント（Transform）をスプラインでつなぎ、
/// ウェイポイント列と向きを自動生成する。
/// ついでに LineRenderer で線も描画。
/// </summary>
[ExecuteAlways]
public class RacePath : MonoBehaviour
{
    [Header("Control Points (通過順のチェックポイント)")]
    [SerializeField] private List<Transform> controlPoints = new List<Transform>();

    [Header("Path Settings")]
    [Min(2)] public int subdivisionsPerSegment = 10; // 1区間を何分割するか
    public bool loop = false;                        // 周回コースなら true

    [Header("Line Renderer (任意)")]
    [SerializeField] private LineRenderer lineRenderer;

    [System.Serializable]
    public struct Waypoint
    {
        public Vector3 position;
        public Quaternion rotation; // 進行方向に向いた回転
        public float distance;      // スタートからの累積距離
    }

    public List<Waypoint> Waypoints { get; private set; } = new List<Waypoint>();

    // エディタで値を変更したときも自動で再計算
    private void OnValidate()
    {
        RebuildPath();
    }

    private void Start()
    {
        RebuildPath();
    }

    /// <summary>
    /// 外部から呼びたいとき用
    /// </summary>
    public void RebuildPath()
    {
        Waypoints.Clear();

        if (controlPoints == null || controlPoints.Count < 2)
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            return;
        }

        int count = controlPoints.Count;

        Vector3 prevPos = Vector3.zero;
        float totalDist = 0f;
        bool hasPrev = false;

        // 各「区間」ごとにスプラインをサンプリング
        int segmentCount = loop ? count : count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            // Catmull-Rom 用の4点を取る
            Vector3 p0 = GetControlPoint(i - 1);
            Vector3 p1 = GetControlPoint(i);
            Vector3 p2 = GetControlPoint(i + 1);
            Vector3 p3 = GetControlPoint(i + 2);

            // subdivisionsPerSegment 回に分割してサンプリング
            for (int s = 0; s <= subdivisionsPerSegment; s++)
            {
                // セグメント境界での重複を避けるため、最初だけ0を含める
                if (i > 0 && s == 0) continue;

                float t = (float)s / subdivisionsPerSegment;
                Vector3 pos = CatmullRom(p0, p1, p2, p3, t);

                Vector3 dir;
                if (!hasPrev)
                {
                    // 最初だけ、次のサンプル方向を後で計算するので一旦保留
                    prevPos = pos;
                    hasPrev = true;
                    continue;
                }
                else
                {
                    dir = (pos - prevPos);
                }

                float segDist = dir.magnitude;
                if (segDist > 0.0001f)
                {
                    dir /= segDist;
                }
                else
                {
                    // ほぼ同一点なら前回の向きを流用
                    dir = Waypoints.Count > 0
                        ? Waypoints[Waypoints.Count - 1].rotation * Vector3.forward
                        : Vector3.forward;
                    segDist = 0f;
                }

                totalDist += segDist;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

                Waypoint wp = new Waypoint
                {
                    position = pos,
                    rotation = rot,
                    distance = totalDist
                };
                Waypoints.Add(wp);

                prevPos = pos;
            }
        }

        // LineRenderer に反映
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = Waypoints.Count;
            for (int i = 0; i < Waypoints.Count; i++)
            {
                lineRenderer.SetPosition(i, Waypoints[i].position);
            }
        }

        // ついでに「元のチェックポイントの角度合わせ」もしておく
        AutoAlignCheckpoints();
    }

    private Vector3 GetControlPoint(int index)
    {
        int count = controlPoints.Count;
        if (loop)
        {
            index = (index % count + count) % count; // ループ
            return controlPoints[index].position;
        }
        else
        {
            index = Mathf.Clamp(index, 0, count - 1); // 端は固定
            return controlPoints[index].position;
        }
    }

    /// <summary>
    /// Catmull-Rom スプライン
    /// t: 0〜1
    /// </summary>
    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f *
               ((2f * p1) +
               (-p0 + p2) * t +
               (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
               (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>
    /// 各チェックポイントの回転を「道の向き」に合わせる。
    /// （リングの向きを自動で合わせたいとき用）
    /// </summary>
    private void AutoAlignCheckpoints()
    {
        int count = controlPoints.Count;
        if (count < 2 || Waypoints.Count < 2) return;

        for (int i = 0; i < count; i++)
        {
            Transform cp = controlPoints[i];
            if (cp == null) continue;

            // 近いウェイポイントを探す（雑に最近傍）
            int closestIndex = 0;
            float closestSqr = float.MaxValue;
            Vector3 cpPos = cp.position;
            for (int w = 0; w < Waypoints.Count; w++)
            {
                float d2 = (Waypoints[w].position - cpPos).sqrMagnitude;
                if (d2 < closestSqr)
                {
                    closestSqr = d2;
                    closestIndex = w;
                }
            }

            Quaternion rot = Waypoints[closestIndex].rotation;
            cp.rotation = rot;
        }
    }
}
