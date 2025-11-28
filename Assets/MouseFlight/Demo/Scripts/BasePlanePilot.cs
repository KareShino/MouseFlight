using UnityEngine;

namespace MFlight.Demo
{
    /// <summary>
    /// Plane に入力を与える基底クラス。プレイヤー操作 / AI 操作などをここから派生させる。
    /// </summary>
    public abstract class BasePlanePilot : MonoBehaviour
    {
        /// <summary>操作対象の Plane。</summary>
        protected Plane plane;

        /// <summary>ピッチ入力（-1 ～ 1）。</summary>
        public abstract float Pitch { get; }

        /// <summary>ヨー入力（-1 ～ 1）。</summary>
        public abstract float Yaw { get; }

        /// <summary>ロール入力（-1 ～ 1）。</summary>
        public abstract float Roll { get; }

        /// <summary>スロットル入力（-1 ～ 1 の増減指示）。</summary>
        public abstract float ThrottleInput { get; }

        /// <summary>Plane 初期化時に呼ばれる。</summary>
        public virtual void Initialize(Plane plane)
        {
            this.plane = plane;
        }

        /// <summary>毎フレーム呼ばれる。内部で入力計算などを行う。</summary>
        public abstract void TickPilot();
    }
}
