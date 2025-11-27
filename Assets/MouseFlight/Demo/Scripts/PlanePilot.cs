using UnityEngine;

namespace MFlight.Demo
{
    /// <summary>
    /// 機体に渡す操作入力（-1〜1）を提供する「パイロット」基底クラス
    /// </summary>
    public abstract class PlanePilot : MonoBehaviour
    {
        /// <param name="pitch">機首上下 -1〜1</param>
        /// <param name="yaw">ヨー -1〜1</param>
        /// <param name="roll">ロール -1〜1</param>
        /// <param name="throttle">スロットル 0〜1</param>
        public abstract void GetInputs(
            out float pitch,
            out float yaw,
            out float roll,
            out float throttle
        );
    }
}
