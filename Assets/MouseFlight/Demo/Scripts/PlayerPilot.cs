using UnityEngine;
using UnityEngine.InputSystem;

namespace MFlight.Demo
{
    /// <summary>
    /// キーボード / パッド入力を Plane に渡すプレイヤーパイロット
    /// </summary>
    public class PlayerPilot : PlanePilot
    {
        [Header("Input Actions")]
        public InputActionReference pitchAction;    // W/S or Stick Y
        public InputActionReference yawAction;      // A/D or Stick X
        public InputActionReference rollAction;     // Q/E or Shoulder
        public InputActionReference throttleAction; // 上矢印/下矢印など、-1〜1で変化させる用

        [Header("Throttle Settings")]
        public float throttleChangeSpeed = 0.5f; // 1秒あたりどれだけ変化するか

        private float _throttleCurrent = 0f;

        private void OnEnable()
        {
            pitchAction?.action.Enable();
            yawAction?.action.Enable();
            rollAction?.action.Enable();
            throttleAction?.action.Enable();
        }

        private void OnDisable()
        {
            pitchAction?.action.Disable();
            yawAction?.action.Disable();
            rollAction?.action.Disable();
            throttleAction?.action.Disable();
        }

        public override void GetInputs(
            out float pitch,
            out float yaw,
            out float roll,
            out float throttle)
        {
            // -1〜1 のアナログ入力を想定
            pitch = pitchAction != null ? pitchAction.action.ReadValue<float>() : 0f;
            yaw = yawAction != null ? yawAction.action.ReadValue<float>() : 0f;
            roll = rollAction != null ? rollAction.action.ReadValue<float>() : 0f;

            float throttleInput = throttleAction != null
                ? throttleAction.action.ReadValue<float>() // -1〜1
                : 0f;

            // 入力に応じてスロットル値を積分
            _throttleCurrent += throttleInput * throttleChangeSpeed * Time.deltaTime;
            _throttleCurrent = Mathf.Clamp01(_throttleCurrent);

            throttle = _throttleCurrent;
        }
    }
}
